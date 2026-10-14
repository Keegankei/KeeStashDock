using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace KeeStashDock.Desktop.Views;

public partial class QuickPanelWindow
{
    public static readonly DependencyProperty FloatingDepthEnabledProperty = DependencyProperty.Register(
        nameof(FloatingDepthEnabled), typeof(bool), typeof(QuickPanelWindow), new PropertyMetadata(false));
    public bool FloatingDepthEnabled => (bool)GetValue(FloatingDepthEnabledProperty);
    public static readonly DependencyProperty FloatingStyleEnabledProperty = DependencyProperty.Register(
        nameof(FloatingStyleEnabled), typeof(bool), typeof(QuickPanelWindow), new PropertyMetadata(false));
    public bool FloatingStyleEnabled => (bool)GetValue(FloatingStyleEnabledProperty);

    private PanelShadowWindow? _panelShadow;
    private readonly TranslateTransform _backgroundParallax = new();
    private readonly ScaleTransform _backgroundScale = new(1, 1, 0.5, 0.5);
    private readonly LinearGradientBrush _depthHighlightBrush = new(
        Color.FromArgb(18, 255, 255, 255), Colors.Transparent, 90) { Opacity = 0 };
    private MotionValue? _parallaxX, _parallaxY, _highlightOpacity;
    private bool _depthClosed, _depthApproached;
    private bool? _lastFloating, _lastHighContrast, _lastMotion;

    private void InitializeDepth()
    {
        _panelShadow = new PanelShadowWindow(this);
        _parallaxX = new(_backgroundParallax, TranslateTransform.XProperty);
        _parallaxY = new(_backgroundParallax, TranslateTransform.YProperty);
        _highlightOpacity = new(_depthHighlightBrush, Brush.OpacityProperty);
        DepthHighlight.Background = _depthHighlightBrush;
        SizeChanged += OnDepthSizeChanged;
        MouseMove += OnDepthPointerMove;
        MouseLeave += OnDepthPointerLeave;
        IsVisibleChanged += OnDepthVisibilityChanged;
        SystemParameters.StaticPropertyChanged += OnDepthSystemSettingsChanged;
        ApplyDepthSettings();
    }

    private void ApplyDepthSettings()
    {
        if (_depthClosed || _panelShadow is null) return;
        bool floating = _settings.Current.FloatingPanelEnabled;
        bool highContrast = SystemParameters.HighContrast;
        bool motion = floating && FloatingCardMotion.AnimationsAllowed;
        if (_lastFloating != floating || _lastHighContrast != highContrast || _lastMotion != motion)
        {
            ResetDepthMotion();
            _lastFloating = floating;
            _lastHighContrast = highContrast;
            _lastMotion = motion;
            SetValue(FloatingDepthEnabledProperty, motion);
            bool styled = floating && !highContrast;
            SetValue(FloatingStyleEnabledProperty, styled);
            Resources["Panel.CornerRadius"] = new CornerRadius(styled ? 2 : 12);
            Resources["Panel.InnerRadius"] = new CornerRadius(styled ? 1 : 11);
            Resources["Panel.CardMargin"] = styled ? new Thickness(12, 9, 12, 9) : new Thickness(8, 7, 8, 7);
            Resources["Panel.CardFrontMargin"] = styled ? new Thickness(0, 0, 5, 5) : new Thickness(0);
            Resources["Panel.CardShadowSpread"] = styled ? 7d : 4d;
            Resources["Panel.CardShadowStrength"] = styled ? 0.28 : 0.14;
            Resources["Panel.ShadowVisibility"] = highContrast ? Visibility.Hidden : Visibility.Visible;
            _backgroundScale.ScaleX = _backgroundScale.ScaleY = motion ? 1.02 : 1;
            _highlightOpacity?.To(floating && !highContrast ? 0.55 : 0, animate: false);
            UpdateBackgroundClip();
        }
        _panelShadow.UpdateAppearance(!highContrast, floating, _depthApproached);
        _highlightOpacity?.To(floating && !highContrast ? _depthApproached ? 1 : 0.55 : 0, motion);
        ApplyBackgroundDepth();
    }

    private void ApplyBackgroundDepth()
    {
        if (BgImage.Background is not ImageBrush image) return;
        var transforms = new TransformGroup();
        transforms.Children.Add(_backgroundScale);
        transforms.Children.Add(_backgroundParallax);
        image.RelativeTransform = transforms;
    }

    private void OnDepthPointerMove(object sender, MouseEventArgs e)
    {
        if (!FloatingDepthEnabled || !IsVisible || Mouse.LeftButton == MouseButtonState.Pressed) return;
        Point point = e.GetPosition(RootBorder);
        // Relative brush coordinates retain the rounded surface clip and crop.
        // At most 1.5 DIPs: text contrast sampling and controls remain stationary.
        _parallaxX?.To((point.X / Math.Max(1, RootBorder.ActualWidth) - 0.5) * 3 / Math.Max(1, RootBorder.ActualWidth));
        _parallaxY?.To((point.Y / Math.Max(1, RootBorder.ActualHeight) - 0.5) * 3 / Math.Max(1, RootBorder.ActualHeight));
    }

    private void OnDepthPointerLeave(object sender, MouseEventArgs e)
    {
        _parallaxX?.To(0, FloatingDepthEnabled);
        _parallaxY?.To(0, FloatingDepthEnabled);
    }

    private void UpdateDepthApproach(bool approached)
    {
        if (_depthApproached == approached) return;
        _depthApproached = approached;
        _panelShadow?.UpdateAppearance(!SystemParameters.HighContrast,
            _settings.Current.FloatingPanelEnabled, approached);
        double target = _settings.Current.FloatingPanelEnabled && !SystemParameters.HighContrast
            ? approached ? 1 : 0.55 : 0;
        _highlightOpacity?.To(target, FloatingDepthEnabled);
    }

    private void OnDepthSizeChanged(object sender, SizeChangedEventArgs e)
        => UpdateBackgroundClip();

    private void UpdateBackgroundClip()
    {
        double radius = FloatingStyleEnabled ? 1 : 11;
        BackgroundLayer.Clip = new RectangleGeometry(
            new Rect(0, 0, Math.Max(0, ActualWidth - 2), Math.Max(0, ActualHeight - 2)), radius, radius);
    }

    private void OnDepthVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!(bool)e.NewValue) ResetDepthMotion();
    }

    private void OnDepthSystemSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_depthClosed || Dispatcher.HasShutdownStarted) return;
        Dispatcher.Invoke(() => { if (!_depthClosed) ApplyDepthSettings(); });
    }

    private void ResetDepthMotion()
    {
        _depthApproached = false;
        _parallaxX?.To(0, animate: false);
        _parallaxY?.To(0, animate: false);
        _highlightOpacity?.To(0, animate: false);
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

    // Existing resize/approach behavior uses the real native rectangle. Scaling
    // Left/Top alone is incorrect for mixed-DPI and negative-coordinate monitors.
    private bool TryGetNativeBounds(out PixelRect bounds)
        => GetWindowRect(new WindowInteropHelper(this).Handle, out bounds);

    [StructLayout(LayoutKind.Sequential)]
    private struct PixelRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint handle, out PixelRect bounds);
}
