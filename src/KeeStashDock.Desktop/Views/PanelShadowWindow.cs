using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

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
        Content = _surface;
        SourceInitialized += OnSourceInitialized;
        panel.LocationChanged += OnGeometryChanged;
        panel.SizeChanged += OnSizeChanged;
        panel.IsVisibleChanged += OnVisibilityChanged;
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
        _surface.Strength = floating ? 0.30 : 0.21;
        // Only decoration changes on approach: no control moves away from the pointer.
        _surface.Opacity = floating && !emphasized ? 0.78 : 1;
        SyncVisibility();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _source = (HwndSource)PresentationSource.FromVisual(this)!;
        nint hwnd = _source.Handle;
        SetWindowLongPtr(hwnd, GwlExStyle,
            new nint(GetWindowLongPtr(hwnd, GwlExStyle).ToInt64() | Transparent | ToolWindow | NoActivate));
        _source.AddHook(WndProc);
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
    private void OnOpacityChanged(object? sender, EventArgs e) { if (!_disposed) Opacity = _panel.Opacity; }
    private void OnPanelClosed(object? sender, EventArgs e) => Dispose();
    private void OnDpiChanged(object sender, DpiChangedEventArgs e) => SyncBounds();

    private void SyncVisibility()
    {
        if (_disposed) return;
        if (_panel.IsVisible && Owner is null) Owner = _panel;
        if (!_enabled || !_panel.IsVisible || _panel.WindowState == WindowState.Minimized)
        {
            Hide();
            return;
        }
        Opacity = _panel.Opacity;
        SyncBounds();
        if (!IsVisible) Show();
        SyncBounds();
    }

    private void SyncBounds()
    {
        if (_disposed || !_panel.IsVisible) return;
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
        _panel.Closed -= OnPanelClosed;
        _panel.DpiChanged -= OnDpiChanged;
        _opacityDescriptor.RemoveValueChanged(_panel, OnOpacityChanged);
        _source?.RemoveHook(WndProc);
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
