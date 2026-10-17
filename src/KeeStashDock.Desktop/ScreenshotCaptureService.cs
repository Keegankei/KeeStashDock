using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KeeStashDock.Desktop.Localization;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace KeeStashDock.Desktop;

public sealed record CapturedScreenshot(byte[] PngBytes, int PixelWidth, int PixelHeight);
public enum ScreenshotAction { Save, ExtractText, ExtractAndTranslate }
public sealed record ScreenshotCaptureResult(CapturedScreenshot Image, ScreenshotAction Action, int AnchorX, int AnchorY);

/// <summary>
/// Freezes the desktop before showing one selector per monitor. All selection
/// coordinates stay in physical pixels; a monitor's WPF scale is used only to draw.
/// The caller must hide its own windows before invoking this UI-thread service.
/// </summary>
public sealed class ScreenshotCaptureService
{
    private bool _isCapturing;

    public async Task<ScreenshotCaptureResult?> CaptureAsync(CancellationToken cancellationToken = default)
    {
        Application.Current.Dispatcher.VerifyAccess();
        if (_isCapturing) return null;
        cancellationToken.ThrowIfCancellationRequested();
        _isCapturing = true;
        var windows = new List<CaptureOverlay>();
        try
        {
            var monitors = Forms.Screen.AllScreens.Select(s => s.Bounds).ToArray();
            Drawing.Rectangle desktop = monitors.Aggregate(Drawing.Rectangle.Union);
            if (desktop.Width <= 0 || desktop.Height <= 0 ||
                (long)desktop.Width * desktop.Height > 80_000_000)
                throw new InvalidOperationException("The virtual desktop is too large to capture.");

            BitmapSource snapshot = CaptureDesktop(desktop);
            var result = new TaskCompletionSource<(Drawing.Rectangle Bounds, ScreenshotAction Action, Drawing.Point Anchor)?>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool overlaysReady = false;
            Drawing.Point? start = null;
            Drawing.Rectangle selection = Drawing.Rectangle.Empty;
            Drawing.Point anchor = Drawing.Point.Empty;
            void Cancel() => result.TrySetResult(null);
            void Choose(ScreenshotAction action)
            {
                if (start is null && selection.Width >= 2 && selection.Height >= 2)
                    result.TrySetResult((selection, action, anchor));
            }
            void CheckActivation() => Application.Current.Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    // Showing/activating another monitor's selector temporarily deactivates
                    // its sibling. Inspect the settled group, never one window in isolation.
                    if (overlaysReady && !result.Task.IsCompleted && !windows.Any(window => window.IsActive))
                        Cancel();
                }));
            void Move(Drawing.Point point)
            {
                if (start is not { } origin) return;
                selection = ScreenshotSelection.Clamp(origin, point, desktop);
                foreach (var window in windows) window.ShowSelection(selection);
            }
            void Begin(Drawing.Point point)
            {
                foreach (var window in windows) window.HideActions();
                start = point;
                Move(point);
            }
            void Finish(Drawing.Point point)
            {
                Move(point);
                start = null;
                if (selection.Width >= 2 && selection.Height >= 2)
                {
                    anchor = point;
                    var owner = windows.FirstOrDefault(window => window.ScreenBounds.Contains(point))
                        ?? windows.OrderByDescending(window =>
                        {
                            var overlap = Drawing.Rectangle.Intersect(window.ScreenBounds, selection);
                            return (long)overlap.Width * overlap.Height;
                        }).First();
                    owner.ShowActions(selection);
                }
                else
                {
                    foreach (var window in windows) window.ShowSelection(Drawing.Rectangle.Empty);
                }
            }

            string instruction = LocalizationService.Instance["Screenshot.ActionHint"];
            foreach (var monitor in monitors)
            {
                var crop = new CroppedBitmap(snapshot, new Int32Rect(
                    monitor.X - desktop.X, monitor.Y - desktop.Y, monitor.Width, monitor.Height));
                crop.Freeze();
                windows.Add(new CaptureOverlay(monitor, crop, instruction, Begin, Move, Finish, Choose, Cancel, CheckActivation));
            }
            using var registration = cancellationToken.Register(() =>
                Application.Current.Dispatcher.BeginInvoke(Cancel));
            foreach (var window in windows) window.Show();
            if (CaptureNative.GetCursorPos(out var cursor))
                windows.FirstOrDefault(w => w.ScreenBounds.Contains(cursor.X, cursor.Y))?.Activate();
            overlaysReady = true;

            var selected = await result.Task;
            cancellationToken.ThrowIfCancellationRequested();
            if (selected is not { } chosen) return null;
            var bounds = chosen.Bounds;
            if (bounds.Width > 16384 || bounds.Height > 16384 || (long)bounds.Width * bounds.Height > 40_000_000)
                throw new InvalidOperationException("The selected image is too large to save.");
            return new ScreenshotCaptureResult(EncodeCrop(snapshot, new Int32Rect(bounds.X - desktop.X, bounds.Y - desktop.Y,
                bounds.Width, bounds.Height)), chosen.Action, chosen.Anchor.X, chosen.Anchor.Y);
        }
        finally
        {
            // A release after completion cannot start another selection; close only our overlays.
            foreach (var window in windows) window.Close();
            _isCapturing = false;
        }
    }

    private static BitmapSource CaptureDesktop(Drawing.Rectangle bounds)
    {
        using var bitmap = new Drawing.Bitmap(bounds.Width, bounds.Height, Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(bounds.Location, Drawing.Point.Empty, bounds.Size, Drawing.CopyPixelOperation.SourceCopy);
        nint handle = bitmap.GetHbitmap();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(handle, nint.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally { CaptureNative.DeleteObject(handle); }
    }

    internal static CapturedScreenshot EncodeCrop(BitmapSource source, Int32Rect bounds)
    {
        var crop = new CroppedBitmap(source, bounds);
        // Force RGB, including on indexed/gray test inputs. Persistence accepts PNG RGB(A).
        var rgb = new FormatConvertedBitmap(crop, PixelFormats.Bgr24, null, 0);
        var encoder = new PngBitmapEncoder { Interlace = PngInterlaceOption.Off };
        encoder.Frames.Add(BitmapFrame.Create(rgb));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return new CapturedScreenshot(stream.ToArray(), bounds.Width, bounds.Height);
    }

    internal sealed class CaptureOverlay : Window
    {
        private readonly SelectionSurface _surface;
        private readonly Action<Drawing.Point> _begin, _move, _finish;
        private readonly Action _cancel;
        private readonly Border _toolbar;
        private readonly Canvas _actions;
        private readonly Button _save;
        public Drawing.Rectangle ScreenBounds { get; }

        public CaptureOverlay(Drawing.Rectangle bounds, BitmapSource image, string instruction,
            Action<Drawing.Point> begin, Action<Drawing.Point> move, Action<Drawing.Point> finish,
            Action<ScreenshotAction> choose, Action cancel,
            Action checkActivation)
        {
            ScreenBounds = bounds;
            _begin = begin; _move = move; _finish = finish; _cancel = cancel;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Cursor = Cursors.Cross;
            Background = Brushes.Black;
            FlowDirection = FlowDirection.LeftToRight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            var root = new Grid();
            root.Children.Add(_surface = new SelectionSurface(bounds, image, instruction));
            root.Children.Add(_actions = new Canvas());
            var buttons = new WrapPanel
            {
                FlowDirection = LocalizationService.Instance.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight
            };
            Button AddAction(string key, Action action)
            {
                string label = LocalizationService.Instance[key];
                var button = new Button
                {
                    Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap },
                    Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(3),
                    MaxWidth = 210, Cursor = Cursors.Arrow, MinHeight = 32
                };
                AutomationProperties.SetName(button, label);
                button.Click += (_, e) => { e.Handled = true; action(); };
                buttons.Children.Add(button);
                return button;
            }
            _save = AddAction("Screenshot.ActionSave", () => choose(ScreenshotAction.Save));
            AddAction("Screenshot.ActionExtract", () => choose(ScreenshotAction.ExtractText));
            AddAction("Screenshot.ActionTranslate", () => choose(ScreenshotAction.ExtractAndTranslate));
            AddAction("Common.Cancel", cancel);
            _toolbar = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(28, 28, 34)), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(5), Visibility = Visibility.Collapsed,
                Child = new ScrollViewer { Content = buttons, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }
            };
            _actions.Children.Add(_toolbar);
            Content = root;
            SourceInitialized += (_, _) => Place();
            Loaded += (_, _) => { Place(); Focus(); };
            Deactivated += (_, _) => checkActivation();
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) { e.Handled = true; _cancel(); }
                else if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    if (!ScreenshotSelection.InvokeFocusedAction(_toolbar, Keyboard.FocusedElement))
                        choose(ScreenshotAction.Save);
                }
            };
            MouseRightButtonDown += (_, e) => { e.Handled = true; _cancel(); };
            MouseLeftButtonDown += (_, e) =>
            {
                if (_toolbar.IsMouseOver) return;
                e.Handled = true;
                if (CaptureNative.GetCursorPos(out var p)) { CaptureMouse(); _begin(new(p.X, p.Y)); }
            };
            MouseMove += (_, _) =>
            {
                if (IsMouseCaptured && CaptureNative.GetCursorPos(out var p)) _move(new(p.X, p.Y));
            };
            MouseLeftButtonUp += (_, e) =>
            {
                if (!IsMouseCaptured) return;
                e.Handled = true;
                if (CaptureNative.GetCursorPos(out var p)) _finish(new(p.X, p.Y));
                ReleaseMouseCapture();
            };
            Closed += (_, _) => { ReleaseMouseCapture(); _cancel(); Content = null; };
        }

        private void Place()
        {
            if (!CaptureNative.SetWindowPos(new WindowInteropHelper(this).Handle, new nint(-1),
                ScreenBounds.X, ScreenBounds.Y, ScreenBounds.Width, ScreenBounds.Height, 0x0010))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        public void ShowSelection(Drawing.Rectangle selected) => _surface.ShowSelection(selected);

        public void HideActions() => _toolbar.Visibility = Visibility.Collapsed;

        public void ShowActions(Drawing.Rectangle selected)
        {
            // Only the toolbar uses DIPs; the selection and monitor rectangle stay in pixels.
            double scaleX = _surface.ActualWidth / ScreenBounds.Width;
            double scaleY = _surface.ActualHeight / ScreenBounds.Height;
            double availableWidth = Math.Max(1, _surface.ActualWidth - 16);
            double availableHeight = Math.Max(1, _surface.ActualHeight - 16);
            _toolbar.MaxWidth = Math.Min(760, availableWidth);
            _toolbar.MaxHeight = availableHeight;
            _toolbar.Visibility = Visibility.Visible;
            _toolbar.Measure(new Size(_toolbar.MaxWidth, _toolbar.MaxHeight));
            Rect local = new((selected.Left - ScreenBounds.Left) * scaleX,
                (selected.Top - ScreenBounds.Top) * scaleY, selected.Width * scaleX, selected.Height * scaleY);
            Point placement = ScreenshotSelection.PlaceToolbar(local, _toolbar.DesiredSize,
                new Size(_surface.ActualWidth, _surface.ActualHeight));
            Canvas.SetLeft(_toolbar, placement.X);
            Canvas.SetTop(_toolbar, placement.Y);
            Activate();
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => _save.Focus());
        }
    }

    internal sealed class SelectionSurface(Drawing.Rectangle bounds, BitmapSource image, string instruction) : FrameworkElement
    {
        private Drawing.Rectangle _selection;
        public void ShowSelection(Drawing.Rectangle selected) { _selection = selected; InvalidateVisual(); }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (ActualWidth <= 0 || ActualHeight <= 0) return;
            var full = new Rect(0, 0, ActualWidth, ActualHeight);
            dc.DrawImage(image, full);
            var shade = new GeometryGroup { FillRule = FillRule.EvenOdd };
            shade.Children.Add(new RectangleGeometry(full));
            var clipped = Drawing.Rectangle.Intersect(bounds, _selection);
            if (clipped.Width > 0 && clipped.Height > 0)
            {
                var selected = new Rect((clipped.X - bounds.X) * ActualWidth / bounds.Width,
                    (clipped.Y - bounds.Y) * ActualHeight / bounds.Height,
                    clipped.Width * ActualWidth / bounds.Width, clipped.Height * ActualHeight / bounds.Height);
                shade.Children.Add(new RectangleGeometry(selected));
                dc.DrawRectangle(null, new Pen(Brushes.DeepSkyBlue, 2), selected);
            }
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(115, 0, 0, 0)), null, shade);
            var text = new FormattedText(instruction, CultureInfo.CurrentUICulture,
                LocalizationService.Instance.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 14, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip)
            { MaxTextWidth = Math.Max(1, ActualWidth - 48) };
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(230, 28, 28, 28)), null,
                new Rect(16, 16, Math.Min(text.Width + 24, ActualWidth - 32), text.Height + 20), 6, 6);
            dc.DrawText(text, new Point(28, 26));
        }
    }

    private static class CaptureNative
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
        [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(
            nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
        [DllImport("gdi32.dll")] internal static extern bool DeleteObject(nint handle);
    }
}

internal static class ScreenshotSelection
{
    internal static bool InvokeFocusedAction(FrameworkElement toolbar, IInputElement? focused)
    {
        if (focused is not Button { IsEnabled: true } button || !toolbar.IsAncestorOf(button)) return false;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        return true;
    }

    internal static Point PlaceToolbar(Rect selection, Size toolbar, Size monitor)
    {
        const double margin = 8;
        double y = selection.Bottom + margin;
        if (y + toolbar.Height > monitor.Height - margin) y = selection.Top - toolbar.Height - margin;
        return new Point(
            Math.Clamp(selection.Left, margin, Math.Max(margin, monitor.Width - toolbar.Width - margin)),
            Math.Clamp(y, margin, Math.Max(margin, monitor.Height - toolbar.Height - margin)));
    }

    public static Drawing.Rectangle Clamp(Drawing.Point start, Drawing.Point end, Drawing.Rectangle desktop)
    {
        int left = Math.Clamp(Math.Min(start.X, end.X), desktop.Left, desktop.Right);
        int top = Math.Clamp(Math.Min(start.Y, end.Y), desktop.Top, desktop.Bottom);
        int right = Math.Clamp(Math.Max(start.X, end.X), desktop.Left, desktop.Right);
        int bottom = Math.Clamp(Math.Max(start.Y, end.Y), desktop.Top, desktop.Bottom);
        return Drawing.Rectangle.FromLTRB(left, top, right, bottom);
    }
}
