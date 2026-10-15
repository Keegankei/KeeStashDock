using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace KeeStashDock.Desktop.Views;

public partial class QuickPanelWindow
{
    private bool _inspectionPending, _inspectionActive;
    private int _suppressedInspectionButtons;
    private MouseButton _inspectionButton;
    private Point _inspectionOrigin, _inspectionPointer;
    private PerspectivePose _inspectionStart;

    internal bool IsInspecting => _inspectionActive;

    private void PrepareInspection(MouseButtonEventArgs e)
    {
        if (_inspectionPending) CancelInspection();
        _suppressedInspectionButtons &= ~(1 << (int)e.ChangedButton);
        if (!CanInspect || _nativeDepthMove || _depthPopupCount > 0 ||
            Mouse.Captured is not null ||
            !InspectionGesture.Matches(_settings.Current, e.ChangedButton, Keyboard.Modifiers)) return;
        DependencyObject? source = e.OriginalSource as DependencyObject;
        // Controls keep their normal presses, selection and scrolling. A card
        // body can be inspected with the configured gesture without tearing it.
        if (FindAncestor<ButtonBase>(source) is not null || FindAncestor<TextBoxBase>(source) is not null ||
            FindAncestor<ComboBox>(source) is not null || FindAncestor<ScrollBar>(source) is not null) return;
        _inspectionPending = true;
        _inspectionButton = e.ChangedButton;
        _inspectionOrigin = ReadPerspectivePointer() ?? e.GetPosition(this);
        // SubTree capture retains the normal right-click target while allowing
        // a drag that starts at an edge to cross outside the window safely.
        if (!Mouse.Capture(this, CaptureMode.SubTree)) { CancelInspection(); return; }
        e.Handled = true;
    }

    private bool CanInspect => !_depthClosed && IsVisible && SpatialSurface.IsPerspectiveEnabled &&
        _settings.Current.FloatingPanelFollowPointer;

    private void OnInspectionMove(object sender, MouseEventArgs e)
    {
        if (!_inspectionPending && !_inspectionActive) return;
        MouseButtonState state = _inspectionButton switch
        {
            MouseButton.Left => e.LeftButton, MouseButton.Right => e.RightButton,
            MouseButton.Middle => e.MiddleButton, MouseButton.XButton1 => e.XButton1,
            _ => e.XButton2
        };
        if (state != MouseButtonState.Pressed)
        {
            CancelInspection();
            return;
        }
        Point point = ReadPerspectivePointer() ?? e.GetPosition(this);
        if (!_inspectionActive)
        {
            Vector delta = point - _inspectionOrigin;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            if (!BeginInspection(_inspectionOrigin)) { CancelInspection(); return; }
            if (!Mouse.Capture(this, CaptureMode.Element)) { CancelInspection(); return; }
            SetCurrentValue(CursorProperty, Cursors.SizeAll);
            _pressItem = null;
            _pressContainer = null;
        }
        MoveInspection(point);
        e.Handled = true;
    }

    // These methods also drive the isolated UI/performance fixtures, without
    // moving or capturing the user's real mouse.
    internal bool BeginInspection(Point point, MouseButton button = MouseButton.Right)
    {
        if (!CanInspect || _nativeDepthMove || _depthPopupCount > 0) return false;
        if (!_inspectionPending) _inspectionButton = button;
        _suppressedInspectionButtons &= ~(1 << (int)_inspectionButton);
        FreezePerspectiveMotion();
        _inspectionActive = true;
        SpatialSurface.SetInspectionActive(true);
        _inspectionOrigin = _inspectionPointer = point;
        _inspectionStart = new(SpatialSurface.Yaw, SpatialSurface.Pitch, 0, 0, false);
        _depthPointerTimer.Start();
        return true;
    }

    internal void MoveInspection(Point point)
    {
        if (!_inspectionActive || !double.IsFinite(point.X) || !double.IsFinite(point.Y)) return;
        _inspectionPointer = point;
    }

    private void ApplyInspectionPose()
        => SpatialSurface.SetPose(InspectionGesture.DragPose(_inspectionStart,
            _inspectionPointer - _inspectionOrigin, RenderSize));

    internal void EndInspection()
    {
        if (_inspectionActive) ApplyInspectionPose();
        CancelInspection();
        // Keep the inspected angle, with no inertia or permanent frame loop.
        ResumePerspectiveAfterInput();
    }

    private void CompleteInspection(MouseButtonEventArgs e)
    {
        if ((_suppressedInspectionButtons & (1 << (int)e.ChangedButton)) != 0)
        {
            _suppressedInspectionButtons &= ~(1 << (int)e.ChangedButton);
            e.Handled = true;
            return;
        }
        if (!_inspectionPending && !_inspectionActive) return;
        if (e.ChangedButton != _inspectionButton) return;
        bool dragged = _inspectionActive;
        EndInspection();
        _suppressedInspectionButtons &= ~(1 << (int)e.ChangedButton);
        if (dragged) e.Handled = true; // Suppress this drag's context menu/copy.
    }

    private void CancelInspection()
    {
        bool active = _inspectionActive;
        if (active || _inspectionPending) _suppressedInspectionButtons |= 1 << (int)_inspectionButton;
        _inspectionPending = _inspectionActive = false;
        SpatialSurface.SetInspectionActive(false);
        if (active) _depthPointerTimer.Stop();
        if (ReferenceEquals(Mouse.Captured, this)) Mouse.Capture(null);
        ClearValue(CursorProperty);
    }

    private void OnInspectionCaptureLost(object sender, MouseEventArgs e)
    {
        if ((_inspectionActive || _inspectionPending) && !ReferenceEquals(Mouse.Captured, this)) CancelInspection();
    }

    private void OnInspectionKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || (!_inspectionActive && !_inspectionPending)) return;
        CancelInspection();
        e.Handled = true;
    }
}
