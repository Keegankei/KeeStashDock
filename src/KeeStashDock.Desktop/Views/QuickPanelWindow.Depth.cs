using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Controls.Primitives;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace KeeStashDock.Desktop.Views;

public partial class QuickPanelWindow
{
    public static readonly DependencyProperty FloatingDepthEnabledProperty = DependencyProperty.Register(
        nameof(FloatingDepthEnabled), typeof(bool), typeof(QuickPanelWindow), new PropertyMetadata(false));
    public bool FloatingDepthEnabled => (bool)GetValue(FloatingDepthEnabledProperty);

    private PanelShadowWindow? _panelShadow;
    private readonly TranslateTransform _backgroundParallax = new();
    private readonly ScaleTransform _backgroundScale = new(1, 1, 0.5, 0.5);
    private readonly LinearGradientBrush _depthHighlightBrush = new(
        Color.FromArgb(18, 255, 255, 255), Colors.Transparent, 90) { Opacity = 0 };
    private readonly DispatcherTimer _depthPointerTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private MotionValue? _highlightOpacity;
    private Point? _lastDepthPointer;
    private bool _cardProximityDirty = true;
    private long _lastDepthMovement;
    private DispatcherOperation? _pendingDepthShadow;
    private bool _depthClosed, _depthApproached, _nativeDepthMove;
    private int _depthPopupCount;
    private bool? _lastFloating, _lastHighContrast, _lastMotion, _lastFollowPointer;
    private readonly Func<Point?>? _perspectivePointer;

    internal bool IsDepthPointerPolling => _depthPointerTimer.IsEnabled;

    private void InitializeDepth()
    {
        _panelShadow = new PanelShadowWindow(this);
        _highlightOpacity = new(_depthHighlightBrush, Brush.OpacityProperty);
        DepthHighlight.Background = _depthHighlightBrush;
        SpatialSurface.ProjectionChanged += OnPerspectiveChanged;
        _depthPointerTimer.Tick += OnDepthPointerTick;
        SizeChanged += OnDepthSizeChanged;
        MouseMove += OnDepthPointerMove;
        PreviewMouseMove += OnInspectionMove;
        LostMouseCapture += OnInspectionCaptureLost;
        PreviewKeyDown += OnInspectionKeyDown;
        MouseLeave += OnDepthPointerMove;
        IsVisibleChanged += OnDepthVisibilityChanged;
        AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler(OnDepthPress), true);
        AddHandler(PreviewMouseUpEvent, new MouseButtonEventHandler(OnDepthRelease), true);
        QuickBox.GotKeyboardFocus += OnDepthEditorFocus;
        QuickBox.LostKeyboardFocus += OnDepthEditorFocus;
        foreach (ComboBox combo in ExpireRow.Children.OfType<ComboBox>())
        {
            combo.DropDownOpened += OnDepthDropDownOpened;
            combo.DropDownClosed += OnDepthDropDownClosed;
        }
        CardList.AddHandler(ScrollViewer.ScrollChangedEvent,
            new ScrollChangedEventHandler((_, _) => { _cardProximityDirty = true; WakeDepthSampler(); }));
        SystemParameters.StaticPropertyChanged += OnDepthSystemSettingsChanged;
        ApplyDepthSettings();
    }

    private void ApplyDepthSettings()
    {
        if (_depthClosed || _panelShadow is null) return;
        bool floating = _settings.Current.FloatingPanelEnabled;
        bool highContrast = SystemParameters.HighContrast;
        bool motion = floating && FloatingCardMotion.AnimationsAllowed;
        bool followPointer = _settings.Current.FloatingPanelFollowPointer;
        if (_lastFloating != floating || _lastHighContrast != highContrast || _lastMotion != motion ||
            _lastFollowPointer != followPointer)
        {
            ResetDepthMotion();
            _lastFloating = floating;
            _lastHighContrast = highContrast;
            _lastMotion = motion;
            _lastFollowPointer = followPointer;
            if (motion) SpatialSurface.SetPose(PerspectivePose.Fixed);
            SetValue(FloatingDepthEnabledProperty, motion);
            Resources["Panel.CardShadowStrength"] = motion ? 0.22 : 0.14;
            Resources["Panel.ShadowVisibility"] = highContrast ? Visibility.Hidden : Visibility.Visible;
            _backgroundScale.ScaleX = _backgroundScale.ScaleY = motion ? 1.02 : 1;
        }
        RefreshPerspectiveMode();
        _panelShadow.UpdateAppearance(!highContrast && !IsEdgeDockHidden, motion, _depthApproached);
        _highlightOpacity?.To(motion ? _depthApproached ? 1 : 0.55 : 0, motion);
        ApplyBackgroundDepth();
        UpdatePerspectivePointer();
    }

    private void RefreshPerspectiveMode()
    {
        bool enabled = FloatingDepthEnabled && _state == PanelState.Expanded && !IsEdgeDockHidden;
        if (SpatialSurface.IsPerspectiveEnabled != enabled)
        {
            ResetDepthMotion();
            SpatialSurface.IsPerspectiveEnabled = enabled;
            if (enabled) SpatialSurface.SetPose(PerspectivePose.Fixed);
        }
    }

    private void ApplyBackgroundDepth()
    {
        if (BgImage.Background is not ImageBrush image) return;
        var transforms = new TransformGroup();
        transforms.Children.Add(_backgroundScale);
        transforms.Children.Add(_backgroundParallax);
        image.RelativeTransform = transforms;
    }

    private void OnDepthPointerTick(object? sender, EventArgs e) => UpdatePerspectivePointer();
    private void OnDepthPointerMove(object sender, MouseEventArgs e) => WakeDepthSampler();

    private void WakeDepthSampler()
    {
        if (!_depthClosed && IsVisible && SpatialSurface.IsPerspectiveEnabled && !IsDepthInteractionActive())
            _depthPointerTimer.Start();
    }

    internal void UpdatePerspectivePointer()
    {
        if (_depthClosed || !IsVisible || !IsLoaded || !SpatialSurface.IsLoaded || !SpatialSurface.IsPerspectiveEnabled)
        {
            _depthPointerTimer.Stop();
            return;
        }
        if (_inspectionActive)
        {
            ApplyInspectionPose();
            return;
        }
        if (IsDepthInteractionActive())
        {
            FreezePerspectiveMotion();
            return;
        }
        // Pointer movement now affects cards only. The panel's angle changes
        // exclusively during the user's configured inspection drag.
        Point? point = _perspectivePointer is not null ? _perspectivePointer() : ReadPerspectivePointer();
        bool nearby = point is Point cursor && PerspectivePose.FromPointer(cursor, RenderSize).Nearby;
        bool moved = point != _lastDepthPointer;
        if (moved || _cardProximityDirty)
        {
            _lastDepthPointer = point;
            _cardProximityDirty = false;
            _lastDepthMovement = Stopwatch.GetTimestamp();
            FloatingCardMotion.UpdateProximity(CardList, point, this);
        }
        if (nearby && Stopwatch.GetElapsedTime(_lastDepthMovement).TotalMilliseconds < 200)
            _depthPointerTimer.Start();
        else _depthPointerTimer.Stop();
    }
    private Point? ReadPerspectivePointer()
    {
        if (!Interop.NativeMethods.GetCursorPos(out var cursor) || !TryGetNativeBounds(out var bounds)) return null;
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        // Use the stationary HWND so following the pointer cannot feed back
        // into the coordinate system from which the next pose is computed.
        return new Point((cursor.X - bounds.Left) / dpi.DpiScaleX,
            (cursor.Y - bounds.Top) / dpi.DpiScaleY);
    }

    private bool IsDepthInteractionActive()
        => _nativeDepthMove || _depthPopupCount > 0 || QuickBox.IsKeyboardFocusWithin ||
           Mouse.LeftButton == MouseButtonState.Pressed || Mouse.RightButton == MouseButtonState.Pressed ||
           Mouse.MiddleButton == MouseButtonState.Pressed || Mouse.XButton1 == MouseButtonState.Pressed ||
           Mouse.XButton2 == MouseButtonState.Pressed ||
           Mouse.Captured is not null;

    private void FreezePerspectiveMotion()
    {
        // Keep the displayed angle. Snapping flat here would move an already
        // hit-tested button or text selection under the pointer.


        FloatingCardMotion.FreezeProximity(CardList);
        _depthPointerTimer.Stop();
    }

    private void OnDepthPress(object sender, MouseButtonEventArgs e)
    {
        if (_inspectionActive)
        {
            // Its release may arrive after the inspection button is released.
            // Keep that extra press from becoming an unpaired card click.
            if (e.ChangedButton != _inspectionButton)
                _suppressedInspectionButtons |= 1 << (int)e.ChangedButton;
            e.Handled = true;
            return;
        }
        PrepareInspection(e);
        FreezePerspectiveMotion();
    }
    private void OnDepthRelease(object sender, MouseButtonEventArgs e)
    {
        if (_inspectionActive && e.ChangedButton != _inspectionButton)
        {
            _suppressedInspectionButtons &= ~(1 << (int)e.ChangedButton);
            e.Handled = true;
            return;
        }
        CompleteInspection(e);
        ResumePerspectiveAfterInput();
    }
    private void OnDepthEditorFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (QuickBox.IsKeyboardFocusWithin) FreezePerspectiveMotion();
        else ResumePerspectiveAfterInput();
    }
    private void OnDepthDropDownOpened(object? sender, EventArgs e)
    { _depthPopupCount++; FreezePerspectiveMotion(); }
    private void OnDepthDropDownClosed(object? sender, EventArgs e)
    { _depthPopupCount = Math.Max(0, _depthPopupCount - 1); ResumePerspectiveAfterInput(); }
    private void TrackDepthPopup(ContextMenu menu)
    {
        menu.Opened += (_, _) => { _depthPopupCount++; FreezePerspectiveMotion(); };
        menu.Closed += (_, _) => { _depthPopupCount = Math.Max(0, _depthPopupCount - 1); ResumePerspectiveAfterInput(); };
    }
    private void ResumePerspectiveAfterInput()
    {
        if (!_depthClosed)
        {
            _cardProximityDirty = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(UpdatePerspectivePointer));
        }
    }

    private void OnPerspectiveChanged(object? sender, EventArgs e)
    {
        _cardProximityDirty = true;
        if (_depthClosed || _pendingDepthShadow is not null) return;
        // Coalesce layout and pose changes. Rebuild the hollow outline
        // once per render pass, with no permanent rendering subscription.
        _pendingDepthShadow = Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            _pendingDepthShadow = null;
            if (!_depthClosed) _panelShadow?.UpdateProjectedOutline(SpatialSurface.IsPerspectiveEnabled
                ? SpatialSurface.ProjectedOutline() : null);
        }));
    }

    private void UpdateDepthApproach(bool approached)
    {
        // The existing slow state timer discovers movement outside our HWND;
        // only the nearby area enables faster, lightweight cursor sampling.
        UpdatePerspectivePointer();
        if (_depthApproached == approached) return;
        _depthApproached = approached;
        _panelShadow?.UpdateAppearance(!SystemParameters.HighContrast && !IsEdgeDockHidden, FloatingDepthEnabled, approached);
        _highlightOpacity?.To(FloatingDepthEnabled ? approached ? 1 : 0.55 : 0, FloatingDepthEnabled);
    }

    private void OnDepthSizeChanged(object sender, SizeChangedEventArgs e)
    {
        BackgroundLayer.Clip = new RectangleGeometry(
            new Rect(0, 0, Math.Max(0, ActualWidth - 2), Math.Max(0, ActualHeight - 2)), 11, 11);
        RefreshPerspectiveMode();
        UpdatePerspectivePointer();
    }

    private void OnDepthVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!(bool)e.NewValue) ResetDepthMotion();
        else UpdatePerspectivePointer();
    }

    private void OnDepthSystemSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_depthClosed || Dispatcher.HasShutdownStarted) return;
        Dispatcher.Invoke(() => { if (!_depthClosed) ApplyDepthSettings(); });
    }

    private void ResetDepthMotion()
    {
        _depthPointerTimer.Stop();
        _depthApproached = false;
        CancelInspection();
        _lastDepthPointer = null;
        _cardProximityDirty = true;
        SpatialSurface.SetPose(FloatingDepthEnabled ? PerspectivePose.Fixed : default);
        _backgroundParallax.X = _backgroundParallax.Y = 0; _highlightOpacity?.To(0, false);
        ResetCardMotion(CardList);
    }

    private static void ResetCardMotion(DependencyObject root)
    {
        FloatingCardMotion.Reset(root);
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            ResetCardMotion(VisualTreeHelper.GetChild(root, i));
    }

    private void DisposeDepth()
    {
        _depthClosed = true;
        ResetDepthMotion();
        _pendingDepthShadow?.Abort();
        _pendingDepthShadow = null;
        SpatialSurface.ProjectionChanged -= OnPerspectiveChanged;
        _depthPointerTimer.Tick -= OnDepthPointerTick;
        SystemParameters.StaticPropertyChanged -= OnDepthSystemSettingsChanged;
        _panelShadow?.Dispose();
        _panelShadow = null;
    }

    private void OnPanelLanguageChanged() => Localization.LocalizationService.Instance.ApplyFlowDirection(this);
    private void OnPanelSettingsChanged()
    {
        if (_depthClosed || Dispatcher.HasShutdownStarted) return;
        Dispatcher.Invoke(() => { if (!_depthClosed) { CancelInspection(); ApplyBackground(); ApplyEdgeDockSettings(); } });
    }
    private void OnPanelThemeChanged()
    {
        if (_depthClosed) return;
        _visualSig = null;
        ApplyBackground();
    }

    private void OnPanelTypographyChanged()
    {
        if (_depthClosed || Dispatcher.HasShutdownStarted) return;
        // The projected controls are live, so their text invalidates WPF's
        // visual cache naturally. Refresh geometry-dependent tint samples once.
        CancelInspection();
        SpatialSurface.InvalidateMeasure();
        _cardProximityDirty = true;
        ScheduleLocalTints(rebuild: true);
    }

    private bool TryGetNativeBounds(out PixelRect bounds)
        => GetWindowRect(new WindowInteropHelper(this).Handle, out bounds);

    [StructLayout(LayoutKind.Sequential)]
    private struct PixelRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint handle, out PixelRect bounds);
}
