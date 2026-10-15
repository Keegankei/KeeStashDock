using System.ComponentModel;
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
    private readonly DispatcherTimer _depthPointerTimer = new() { Interval = TimeSpan.FromMilliseconds(32) };
    private MotionValue? _parallaxX, _parallaxY, _highlightOpacity, _yaw, _pitch, _offsetX, _offsetY;
    private DispatcherOperation? _pendingDepthShadow;
    private bool _depthClosed, _depthApproached, _nativeDepthMove;
    private int _depthPopupCount;
    private bool? _lastFloating, _lastHighContrast, _lastMotion, _lastFollowPointer;
    private readonly Func<Point?>? _perspectivePointer;

    internal bool IsDepthPointerPolling => _depthPointerTimer.IsEnabled;

    private void InitializeDepth()
    {
        _panelShadow = new PanelShadowWindow(this);
        _parallaxX = new(_backgroundParallax, TranslateTransform.XProperty);
        _parallaxY = new(_backgroundParallax, TranslateTransform.YProperty);
        _highlightOpacity = new(_depthHighlightBrush, Brush.OpacityProperty);
        _yaw = new(SpatialSurface, PerspectiveSurface.YawProperty);
        _pitch = new(SpatialSurface, PerspectiveSurface.PitchProperty);
        _offsetX = new(SpatialSurface, PerspectiveSurface.OffsetXProperty);
        _offsetY = new(SpatialSurface, PerspectiveSurface.OffsetYProperty);
        DepthHighlight.Background = _depthHighlightBrush;
        SpatialSurface.ProjectionChanged += OnPerspectiveChanged;
        _depthPointerTimer.Tick += OnDepthPointerTick;
        SizeChanged += OnDepthSizeChanged;
        MouseMove += OnDepthPointerMove;
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
            SetValue(FloatingDepthEnabledProperty, motion);
            Resources["Panel.CardShadowStrength"] = motion ? 0.22 : 0.14;
            Resources["Panel.ShadowVisibility"] = highContrast ? Visibility.Hidden : Visibility.Visible;
            _backgroundScale.ScaleX = _backgroundScale.ScaleY = motion ? 1.02 : 1;
        }
        RefreshPerspectiveMode();
        _panelShadow.UpdateAppearance(!highContrast, motion, _depthApproached);
        _highlightOpacity?.To(motion ? _depthApproached ? 1 : 0.55 : 0, motion);
        ApplyBackgroundDepth();
        UpdatePerspectivePointer();
    }

    private void RefreshPerspectiveMode()
    {
        bool enabled = FloatingDepthEnabled && _state == PanelState.Expanded;
        if (SpatialSurface.IsPerspectiveEnabled != enabled)
        {
            ResetDepthMotion();
            SpatialSurface.IsPerspectiveEnabled = enabled;
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
    private void OnDepthPointerMove(object sender, MouseEventArgs e) => UpdatePerspectivePointer();

    internal void UpdatePerspectivePointer()
    {
        if (_depthClosed || !IsVisible || !SpatialSurface.IsPerspectiveEnabled)
        {
            _depthPointerTimer.Stop();
            return;
        }
        if (IsDepthInteractionActive())
        {
            FreezePerspectiveMotion();
            return;
        }
        Point? point = _perspectivePointer is not null ? _perspectivePointer() : ReadPerspectivePointer();
        PerspectivePose pointerPose = point is Point cursor ? PerspectivePose.FromPointer(cursor, RenderSize) : default;
        // Fixed perspective still samples nearby movement for card feedback;
        // its panel angle is independent of that pointer position.
        PerspectivePose pose = _settings.Current.FloatingPanelFollowPointer ? pointerPose : PerspectivePose.Fixed;
        if (pointerPose.Nearby) _depthPointerTimer.Start();
        else _depthPointerTimer.Stop();
        _yaw?.To(pose.Yaw);
        _pitch?.To(pose.Pitch);
        _offsetX?.To(pose.OffsetX);
        _offsetY?.To(pose.OffsetY);
        _parallaxX?.To(pose.OffsetX * 0.5 / Math.Max(1, RootBorder.ActualWidth));
        _parallaxY?.To(pose.OffsetY * 0.5 / Math.Max(1, RootBorder.ActualHeight));
        FloatingCardMotion.UpdateProximity(CardList, point, this);
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
        _yaw?.Freeze(); _pitch?.Freeze(); _offsetX?.Freeze(); _offsetY?.Freeze();
        _parallaxX?.Freeze(); _parallaxY?.Freeze();
        FloatingCardMotion.FreezeProximity(CardList);
        _depthPointerTimer.Stop();
    }

    private void OnDepthPress(object sender, MouseButtonEventArgs e) => FreezePerspectiveMotion();
    private void OnDepthRelease(object sender, MouseButtonEventArgs e) => ResumePerspectiveAfterInput();
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
        if (!_depthClosed) Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(UpdatePerspectivePointer));
    }

    private void OnPerspectiveChanged(object? sender, EventArgs e)
    {
        if (_depthClosed || _pendingDepthShadow is not null) return;
        // Four animated values can change together. Rebuild the hollow outline
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
        _panelShadow?.UpdateAppearance(!SystemParameters.HighContrast, FloatingDepthEnabled, approached);
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
        _yaw?.To(0, false); _pitch?.To(0, false); _offsetX?.To(0, false); _offsetY?.To(0, false);
        _parallaxX?.To(0, false); _parallaxY?.To(0, false); _highlightOpacity?.To(0, false);
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
        Dispatcher.Invoke(() => { if (!_depthClosed) ApplyBackground(); });
    }
    private void OnPanelThemeChanged()
    {
        if (_depthClosed) return;
        _visualSig = null;
        ApplyBackground();
    }

    private bool TryGetNativeBounds(out PixelRect bounds)
        => GetWindowRect(new WindowInteropHelper(this).Handle, out bounds);

    [StructLayout(LayoutKind.Sequential)]
    private struct PixelRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint handle, out PixelRect bounds);
}
