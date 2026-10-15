using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace KeeStashDock.Desktop.Views;

public partial class QuickPanelWindow
{
    private PanelDockSide _edgeDockSide;
    private Drawing.Rectangle? _edgeRestoreBounds;
    private DateTime? _edgeIdleSince;
    private bool _changingEdgeBounds;
    internal bool IsEdgeDockHidden { get; private set; }

    private void InitializeEdgeDock()
    {
        EdgeDockHandle.MouseEnter += (_, _) => RevealEdgeDock();
        EdgeDockHandle.MouseLeftButtonDown += (_, e) => { RevealEdgeDock(); e.Handled = true; };
    }

    private void ApplyEdgeDockSettings()
    {
        if (!_settings.Current.PanelEdgeAutoHideEnabled)
        {
            RevealEdgeDock();
            _edgeDockSide = PanelDockSide.None;
            _edgeIdleSince = null;
        }
        else if (IsVisible && !IsEdgeDockHidden) UpdateEdgeDockAfterMove();
    }

    internal void UpdateEdgeDockAfterMove()
    {
        if (_changingEdgeBounds || IsEdgeDockHidden || !_settings.Current.PanelEdgeAutoHideEnabled ||
            !TryGetNativeBounds(out var bounds)) return;
        Drawing.Rectangle normal = Drawing.Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        Drawing.Rectangle work = Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
        _edgeDockSide = EdgeDockGeometry.Detect(normal, work, VisualTreeHelper.GetDpi(this).DpiScaleX);
        _edgeRestoreBounds = _edgeDockSide == PanelDockSide.None ? null : normal;
        _edgeIdleSince = null;
    }

    /// <summary>Returns true when edge docking owns the idle transition.</summary>
    private bool TickEdgeDock()
    {
        if (!_settings.Current.PanelEdgeAutoHideEnabled || _edgeDockSide == PanelDockSide.None) return false;
        if (IsEdgeDockHidden)
        {
            if (_vm.IsLocked || IsKeyboardFocusWithin || CursorNearWindow(4)) RevealEdgeDock();
            return true;
        }
        bool engaged = IsMouseOver || IsKeyboardFocusWithin || CursorNearWindow(ProximityDip) ||
            _vm.IsLocked || _nativeDepthMove || _depthPopupCount > 0 || _inspectionActive || _inspectionPending || Mouse.Captured is not null ||
            Mouse.LeftButton == MouseButtonState.Pressed || Mouse.RightButton == MouseButtonState.Pressed ||
            Mouse.MiddleButton == MouseButtonState.Pressed || Mouse.XButton1 == MouseButtonState.Pressed ||
            Mouse.XButton2 == MouseButtonState.Pressed;
        if (engaged) { _edgeIdleSince = null; return true; }
        _edgeIdleSince ??= DateTime.UtcNow;
        if ((DateTime.UtcNow - _edgeIdleSince.Value).TotalSeconds >= Math.Max(0.5, _settings.Current.CollapseDelaySeconds))
            RetractEdgeDock();
        return true;
    }

    internal void RetractEdgeDock()
    {
        if (IsEdgeDockHidden || _edgeDockSide == PanelDockSide.None || _vm.IsLocked ||
            IsKeyboardFocusWithin || _nativeDepthMove || _inspectionActive || _inspectionPending || Mouse.Captured is not null ||
            !TryGetNativeBounds(out var bounds)) return;
        _edgeRestoreBounds = Drawing.Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        Drawing.Rectangle work = Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        Drawing.Rectangle handle = EdgeDockGeometry.HandleBounds(_edgeRestoreBounds.Value, work, _edgeDockSide, scale);
        IsEdgeDockHidden = true;
        ResetDepthMotion();
        SpatialSurface.IsPerspectiveEnabled = false;
        SpatialSurface.Visibility = Visibility.Collapsed;
        EdgeDockHandle.Visibility = Visibility.Visible;
        _panelShadow?.UpdateAppearance(false, false, false);
        _changingEdgeBounds = true;
        try
        {
            ResizeMode = ResizeMode.NoResize;
            MinWidth = MinHeight = 1;
            MaxWidth = MaxHeight = double.PositiveInfinity;
            Width = handle.Width / scale;
            Height = handle.Height / scale;
            UpdateLayout();
            MoveEdgeWindow(handle);
        }
        finally { _changingEdgeBounds = false; }
        ResetTimers();
    }

    internal void RevealEdgeDock()
    {
        if (!IsEdgeDockHidden) return;
        Drawing.Rectangle work = Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
        Drawing.Rectangle? normal = _edgeRestoreBounds;
        IsEdgeDockHidden = false;
        EdgeDockHandle.Visibility = Visibility.Collapsed;
        SpatialSurface.Visibility = Visibility.Visible;
        _changingEdgeBounds = true;
        try
        {
            ApplyStateVisuals();
            if (normal is Drawing.Rectangle rectangle)
            {
                // Retain the current DPI-derived dimensions after layout, while
                // anchoring to this monitor's work area rather than the primary.
                UpdateLayout();
                if (TryGetNativeBounds(out var actual))
                    rectangle.Size = new Drawing.Size(actual.Right - actual.Left, actual.Bottom - actual.Top);
                MoveEdgeWindow(EdgeDockGeometry.RestoreBounds(rectangle, work, _edgeDockSide));
            }
        }
        finally { _changingEdgeBounds = false; }
        ApplyDepthSettings();
        _edgeIdleSince = null;
        ResetTimers();
    }

    private void ClearEdgeDockBeforeShow()
    {
        RevealEdgeDock();
        _edgeIdleSince = null;
    }

    private bool TryApplyDevicePosition()
    {
        if (_settings.Current.PanelDevicePosX is not int x || _settings.Current.PanelDevicePosY is not int y) return false;
        new WindowInteropHelper(this).EnsureHandle();
        if (!TryGetNativeBounds(out var bounds)) return false;
        Drawing.Rectangle work = Forms.Screen.FromPoint(new Drawing.Point(x, y)).WorkingArea;
        MoveEdgeWindow(EdgeDockGeometry.Clamp(new Drawing.Rectangle(x, y,
            bounds.Right - bounds.Left, bounds.Bottom - bounds.Top), work));
        // Moving to another monitor can change DPI and therefore native size.
        UpdateLayout();
        ClampPanelToCurrentWorkArea();
        return true;
    }

    private void ClampPanelToCurrentWorkArea()
    {
        if (IsEdgeDockHidden || !TryGetNativeBounds(out var bounds)) return;
        Drawing.Rectangle work = Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
        Drawing.Rectangle current = Drawing.Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        MoveEdgeWindow(EdgeDockGeometry.RestoreBounds(current, work, _edgeDockSide));
    }

    private void MoveEdgeWindow(Drawing.Rectangle bounds)
        => SetEdgeWindowPos(new WindowInteropHelper(this).Handle, nint.Zero,
            bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0004 | 0x0010);

    [DllImport("user32.dll", EntryPoint = "SetWindowPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetEdgeWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
