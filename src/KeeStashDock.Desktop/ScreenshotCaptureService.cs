using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
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

/// <summary>
/// Freezes the desktop before showing one selector per monitor. All selection
/// coordinates stay in physical pixels; a monitor's WPF scale is used only to draw.
/// The caller must hide its own windows before invoking this UI-thread service.
/// </summary>
public sealed class ScreenshotCaptureService
{
    private bool _isCapturing;

    public async Task<CapturedScreenshot?> CaptureAsync(CancellationToken cancellationToken = default)
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
            var result = new TaskCompletionSource<Drawing.Rectangle?>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool overlaysReady = false;
            Drawing.Point? start = null;
            Drawing.Rectangle selection = Drawing.Rectangle.Empty;
            void Cancel() => result.TrySetResult(null);
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
            void Begin(Drawing.Point point) { start = point; Move(point); }
            void Finish(Drawing.Point point)
            {
                Move(point);
                if (selection.Width >= 2 && selection.Height >= 2)
                    result.TrySetResult(selection);
                else
                {
                    start = null;
                    foreach (var window in windows) window.ShowSelection(Drawing.Rectangle.Empty);
                }
            }

            string instruction = LocalizationService.Instance["Screenshot.CaptureRegionHint"];
            foreach (var monitor in monitors)
            {
                var crop = new CroppedBitmap(snapshot, new Int32Rect(
                    monitor.X - desktop.X, monitor.Y - desktop.Y, monitor.Width, monitor.Height));
                crop.Freeze();
                windows.Add(new CaptureOverlay(monitor, crop, instruction, Begin, Move, Finish, Cancel, CheckActivation));
            }
            using var registration = cancellationToken.Register(() =>
                Application.Current.Dispatcher.BeginInvoke(Cancel));
            foreach (var window in windows) window.Show();
            if (CaptureNative.GetCursorPos(out var cursor))
                windows.FirstOrDefault(w => w.ScreenBounds.Contains(cursor.X, cursor.Y))?.Activate();
            overlaysReady = true;

            Drawing.Rectangle? selected = await result.Task;
            cancellationToken.ThrowIfCancellationRequested();
            if (selected is not { } bounds) return null;
            if (bounds.Width > 16384 || bounds.Height > 16384 || (long)bounds.Width * bounds.Height > 40_000_000)
                throw new InvalidOperationException("The selected image is too large to save.");
            return EncodeCrop(snapshot, new Int32Rect(bounds.X - desktop.X, bounds.Y - desktop.Y,
                bounds.Width, bounds.Height));
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

    private sealed class CaptureOverlay : Window
    {
        private readonly SelectionSurface _surface;
        private readonly Action<Drawing.Point> _begin, _move, _finish;
        private readonly Action _cancel;
        public Drawing.Rectangle ScreenBounds { get; }

        public CaptureOverlay(Drawing.Rectangle bounds, BitmapSource image, string instruction,
            Action<Drawing.Point> begin, Action<Drawing.Point> move, Action<Drawing.Point> finish, Action cancel,
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
            Content = _surface = new SelectionSurface(bounds, image, instruction);
            SourceInitialized += (_, _) => Place();
            Loaded += (_, _) => { Place(); Focus(); };
            Deactivated += (_, _) => checkActivation();
            PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; _cancel(); } };
            MouseRightButtonDown += (_, e) => { e.Handled = true; _cancel(); };
            MouseLeftButtonDown += (_, e) =>
            {
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
    public static Drawing.Rectangle Clamp(Drawing.Point start, Drawing.Point end, Drawing.Rectangle desktop)
    {
        int left = Math.Clamp(Math.Min(start.X, end.X), desktop.Left, desktop.Right);
        int top = Math.Clamp(Math.Min(start.Y, end.Y), desktop.Top, desktop.Bottom);
        int right = Math.Clamp(Math.Max(start.X, end.X), desktop.Left, desktop.Right);
        int bottom = Math.Clamp(Math.Max(start.Y, end.Y), desktop.Top, desktop.Bottom);
        return Drawing.Rectangle.FromLTRB(left, top, right, bottom);
    }
}
