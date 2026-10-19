using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace KeeStashDock.Desktop.Views;

/// <summary>
/// Decoration in a separate owned window: the panel's native rectangle remains
/// its visible rectangle, including the 44-DIP collapsed bar. The layered shadow
/// window is click-through even across processes, never activates, and contains
/// no pixels inside the owner silhouette.
/// </summary>
internal sealed class PanelShadowWindow : Window, IDisposable
{
    private const int PaddingDip = 32;
    private const int GwlExStyle = -20;
    private const long Transparent = 0x20, ToolWindow = 0x80, NoActivate = 0x08000000;
    private readonly Window _panel;
    private readonly SurfaceShadow _surface;
    private readonly double _cornerRadius;
    private readonly DependencyPropertyDescriptor _opacityDescriptor;
    private HwndSource? _source;
    private bool _disposed;
    private bool _enabled = true;
    private DispatcherOperation? _pendingShow;
    private bool _syncingBounds;
    private Geometry? _projectedOutline;
    private Geometry? _appliedOutline;
    private double _appliedScaleX = double.NaN, _appliedScaleY = double.NaN;
    private bool _floating;

    internal PanelShadowWindow(Window panel, double radius = 12)
    {
        _panel = panel;
        _cornerRadius = radius;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowActivated = false;
        ShowInTaskbar = false;
        Focusable = false;
        IsHitTestVisible = false;
        Topmost = panel.Topmost;
        _surface = new SurfaceShadow
        {
            Margin = new Thickness(PaddingDip), CornerRadius = radius,
            Spread = 24, ShadowOffset = 4, Strength = 0.22,
        };
        _surface.SetResourceReference(SurfaceShadow.DepthBrushProperty, "B.Border");
        Content = _surface;
        SourceInitialized += OnSourceInitialized;
        panel.LocationChanged += OnGeometryChanged;
        panel.SizeChanged += OnSizeChanged;
        panel.IsVisibleChanged += OnVisibilityChanged;
        panel.Loaded += OnPanelLoaded;
        panel.Closed += OnPanelClosed;
        panel.DpiChanged += OnDpiChanged;
        DpiChanged += OnDpiChanged;
        _opacityDescriptor = DependencyPropertyDescriptor.FromProperty(OpacityProperty, typeof(Window));
        _opacityDescriptor.AddValueChanged(panel, OnOpacityChanged);
        Closed += (_, _) => Detach();
    }

    internal void UpdateAppearance(bool enabled, bool floating, bool emphasized)
    {
        if (_disposed) return;
        _enabled = enabled;
        _floating = floating;
        UpdateDepth(VisualTreeHelper.GetDpi(_panel), VisualTreeHelper.GetDpi(this));
        _surface.Strength = floating ? 0.30 : 0.21;
        // Only decoration changes on approach: no control moves away from the pointer.
        _surface.Opacity = floating && !emphasized ? 0.78 : 1;
        SyncVisibility();
    }

    /// <summary>Updates a projected panel silhouette in owner-local DIPs.
    /// Null restores the ordinary rounded rectangle used by Classic and notes.</summary>
    internal void UpdateProjectedOutline(Geometry? outline)
    {
        if (_disposed) return;
        _projectedOutline = outline is null || outline.IsFrozen ? outline : (Geometry)outline.GetCurrentValueAsFrozen();
        ApplyProjectedOutline(VisualTreeHelper.GetDpi(_panel), VisualTreeHelper.GetDpi(this));
    }

    private void ApplyProjectedOutline(DpiScale ownerDpi, DpiScale shadowDpi)
    {
        double scaleX = ownerDpi.DpiScaleX / shadowDpi.DpiScaleX;
        double scaleY = ownerDpi.DpiScaleY / shadowDpi.DpiScaleY;
        if (ReferenceEquals(_appliedOutline, _projectedOutline) &&
            _appliedScaleX == scaleX && _appliedScaleY == scaleY) return;
        _appliedOutline = _projectedOutline;
        _appliedScaleX = scaleX;
        _appliedScaleY = scaleY;
        _surface.Outline = ScaleProjectedOutline(_projectedOutline, scaleX, scaleY);
    }

    private void UpdateDepth(DpiScale ownerDpi, DpiScale shadowDpi)
        => _surface.Depth = _floating ? 2.5 * ownerDpi.DpiScaleY / shadowDpi.DpiScaleY : 0;

    internal static Geometry? ScaleProjectedOutline(Geometry? outline, double scaleX, double scaleY)
    {
        if (outline is null) return null;
        // The usual same-monitor path already receives a frozen silhouette.
        // Share it with the shadow instead of copying all its points twice.
        if (scaleX == 1 && scaleY == 1)
            return outline.IsFrozen ? outline : (Geometry)outline.GetCurrentValueAsFrozen();
        Geometry result = outline.CloneCurrentValue();
        Matrix transform = result.Transform.Value;
        transform.Scale(scaleX, scaleY);
        result.Transform = new MatrixTransform(transform);
        result.Freeze();
        return result;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // A native handle can be initialized before this window's visual tree.
        nint hwnd = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(hwnd);
        SetWindowLongPtr(hwnd, GwlExStyle,
            new nint(GetWindowLongPtr(hwnd, GwlExStyle).ToInt64() | Transparent | ToolWindow | NoActivate));
        _source?.AddHook(WndProc);
    }

    private static nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == 0x0084) { handled = true; return new nint(-1); } // HTTRANSPARENT
        if (msg == 0x0021) { handled = true; return new nint(3); }  // MA_NOACTIVATE
        return nint.Zero;
    }

    private void OnGeometryChanged(object? sender, EventArgs e) => SyncBounds();
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => SyncBounds();
    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => SyncVisibility();
    private void OnPanelLoaded(object sender, RoutedEventArgs e) => SyncVisibility();
    private void OnOpacityChanged(object? sender, EventArgs e) { if (!_disposed) Opacity = _panel.Opacity; }
    private void OnPanelClosed(object? sender, EventArgs e) => Dispose();
    private void OnDpiChanged(object sender, DpiChangedEventArgs e) => SyncBounds();

    private void SyncVisibility()
    {
        if (_disposed) return;
        if (_panel.IsLoaded && _panel.IsVisible && Owner is null) Owner = _panel;
        if (!_enabled || !_panel.IsVisible || _panel.WindowState == WindowState.Minimized)
        {
            _pendingShow?.Abort();
            _pendingShow = null;
            if (IsVisible) Hide();
            return;
        }
        if (!IsVisible)
        {
            // IsVisibleChanged is raised inside the owner's first Show().
            // Showing a second HWND synchronously there re-enters WPF layout
            // while SizeToContent still has a placeholder native rectangle,
            // shrinking a 280-DIP sticky note to a one/two-pixel-wide sliver.
            // Wait for that layout to finish before creating the decoration.
            if (_pendingShow is null)
                _pendingShow = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    _pendingShow = null;
                    if (_disposed || !_enabled || !_panel.IsVisible || !_panel.IsLoaded ||
                        _panel.WindowState == WindowState.Minimized || _panel.ActualWidth <= 0 || _panel.ActualHeight <= 0) return;
                    if (Owner is null) Owner = _panel;
                    Opacity = _panel.Opacity;
                    SyncBounds();
                    Show();
                    SyncBounds();
                }));
            return;
        }
        Opacity = _panel.Opacity;
        SyncBounds();
    }

    private void SyncBounds()
    {
        if (_disposed || _syncingBounds || !_panel.IsVisible || !_panel.IsLoaded) return;
        _syncingBounds = true;
        try { SyncNativeBounds(); }
        finally { _syncingBounds = false; }
    }

    private void SyncNativeBounds()
    {
        nint ownerHandle = new WindowInteropHelper(_panel).Handle;
        if (!GetWindowRect(ownerHandle, out NativeRect rect)) return;
        // Native coordinates are essential on monitors left of the primary and
        // when the owner and decoration have just crossed a DPI boundary.
        DpiScale dpi = VisualTreeHelper.GetDpi(_panel);
        int padX = (int)Math.Ceiling(PaddingDip * dpi.DpiScaleX);
        int padY = (int)Math.Ceiling(PaddingDip * dpi.DpiScaleY);
        if (_source is null)
        {
            Width = (rect.Right - rect.Left + 2 * padX) / dpi.DpiScaleX;
            Height = (rect.Bottom - rect.Top + 2 * padY) / dpi.DpiScaleY;
            Left = (rect.Left - padX) / dpi.DpiScaleX;
            Top = (rect.Top - padY) / dpi.DpiScaleY;
        }
        else
        {
            SetWindowPos(_source.Handle, nint.Zero, rect.Left - padX, rect.Top - padY,
                rect.Right - rect.Left + 2 * padX, rect.Bottom - rect.Top + 2 * padY,
                0x0004 | 0x0010); // SWP_NOZORDER | SWP_NOACTIVATE
            DpiScale decorationDpi = VisualTreeHelper.GetDpi(this);
            // The shadow may straddle a monitor boundary sooner than its owner.
            // Match the hollow interior in pixels even if their DIPs differ.
            _surface.Margin = new Thickness(padX / decorationDpi.DpiScaleX, padY / decorationDpi.DpiScaleY,
                padX / decorationDpi.DpiScaleX, padY / decorationDpi.DpiScaleY);
            _surface.CornerRadius = _cornerRadius * dpi.DpiScaleX / decorationDpi.DpiScaleX;
            _surface.Spread = 24 * dpi.DpiScaleX / decorationDpi.DpiScaleX;
            _surface.ShadowOffset = 4 * dpi.DpiScaleY / decorationDpi.DpiScaleY;
            UpdateDepth(dpi, decorationDpi);
            ApplyProjectedOutline(dpi, decorationDpi);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Detach();
        Close();
    }

    private void Detach()
    {
        if (_disposed) return;
        _disposed = true;
        _panel.LocationChanged -= OnGeometryChanged;
        _panel.SizeChanged -= OnSizeChanged;
        _panel.IsVisibleChanged -= OnVisibilityChanged;
        _panel.Loaded -= OnPanelLoaded;
        _panel.Closed -= OnPanelClosed;
        _panel.DpiChanged -= OnDpiChanged;
        _opacityDescriptor.RemoveValueChanged(_panel, OnOpacityChanged);
        _source?.RemoveHook(WndProc);
        _pendingShow?.Abort();
        _pendingShow = null;
        _source = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
}
