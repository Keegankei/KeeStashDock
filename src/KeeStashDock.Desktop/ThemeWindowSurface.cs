using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using KeeStashDock.Domain.Entities;

namespace KeeStashDock.Desktop;

/// <summary>Shared frozen artwork behind ordinary windows; content and captured images remain unmodified.</summary>
public static class ThemeWindowSurface
{
    private static bool _registered;
    private static readonly ConditionalWeakTable<Window, Surface> Surfaces = new();
    private static readonly DependencyProperty SettingsBlurRadiusProperty = DependencyProperty.RegisterAttached(
        "SettingsBlurRadius", typeof(double), typeof(ThemeWindowSurface),
        new PropertyMetadata(double.NaN, (sender, _) =>
        {
            if (sender is Window window && Surfaces.TryGetValue(window, out Surface? surface)) surface.ApplyBlur();
        }));

    /// <summary>Changes only the Settings artwork layer; text and controls never receive a blur effect.</summary>
    internal static void SetSettingsFrost(Window window, bool enabled, double radius)
        => window.SetValue(SettingsBlurRadiusProperty, enabled && double.IsFinite(radius) ? Math.Clamp(radius, 1, 18) : double.NaN);

    public static void Register()
    {
        if (_registered) return;
        _registered = true;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is not Window window) return;
                string name = window.GetType().Name;
                if (name is "QuickPanelWindow" or "SelectionPopupWindow" or "TranslationResultWindow" or "PanelShadowWindow" or "CaptureOverlay" ||
                    name.Contains("Screenshot", StringComparison.Ordinal) || name.Contains("Region", StringComparison.Ordinal)) return;
                if (window.GetType() == typeof(Window) && window.AllowsTransparency && !window.ShowActivated) return;
                Track(window, name is "SettingsWindow" or "WorkbenchWindow" ? "workspace" : "smallWindow");
            }), true);
    }

    public static void Track(Window window, string role)
    {
        if (Surfaces.TryGetValue(window, out _) || window.Content is not UIElement content) return;
        window.Content = null;
        var surface = new Surface(window, role, content);
        Surfaces.Add(window, surface);
    }

    private sealed class Surface
    {
        private readonly Window _window;
        private readonly string _role;
        private readonly Grid _art = new() { IsHitTestVisible = false, ClipToBounds = true };
        // A background brush has no intrinsic layout size: artwork must never
        // enlarge a SizeToContent dialog or detached note to its pixel dimensions.
        private readonly Border _image = new() { Name = "ThemeBackgroundImage" };
        private ImageBrush? _imageBrush;
        private ThemeBackground? _background;
        private readonly double _cornerRadius;
        private readonly Border _scrim = new();

        internal Surface(Window window, string role, UIElement content)
        {
            _window = window;
            _role = role;
            _cornerRadius = window.AllowsTransparency && content is Border border ? border.CornerRadius.TopLeft : 0;
            _art.Children.Add(_image); _art.Children.Add(_scrim);
            _art.SizeChanged += (_, _) => UpdateCrop();
            var root = new Grid(); root.Children.Add(_art); root.Children.Add(content);
            window.Content = root;
            ThemeManager.Changed += Apply;
            window.Closed += (_, _) => ThemeManager.Changed -= Apply;
            Apply();
        }

        private void Apply()
        {
            ThemeManager.ApplyWindowChrome(_window);
            var variant = ThemeManager.ActiveVariant;
            if (variant?.Backgrounds.TryGetValue(_role, out var background) != true || background is null)
            { _art.Visibility = Visibility.Collapsed; _image.Background = null; _imageBrush = null; _background = null; _image.Effect = null; return; }
            BitmapSource? source = ThemeManager.GetBackgroundImage(_role);
            _background = background;
            _imageBrush = source is null ? null : new ImageBrush(source) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.RelativeToBoundingBox };
            _image.Background = _imageBrush;
            _art.Visibility = source is null ? Visibility.Collapsed : Visibility.Visible;
            _image.Opacity = background.Opacity;
            ApplyBlur();
            _scrim.Background = (Brush)Application.Current.FindResource("B.WindowBg");
            _scrim.Opacity = background.OverlayOpacity;
            UpdateCrop();
        }

        internal void ApplyBlur()
        {
            double custom = (double)_window.GetValue(SettingsBlurRadiusProperty);
            double radius = SystemParameters.HighContrast || _background is null ? 0
                : double.IsNaN(custom) ? _background.BlurRadius : custom;
            if (radius <= 0) { _image.Effect = null; return; }
            if (_image.Effect is BlurEffect current && current.Radius == radius) return;
            var blur = new BlurEffect { Radius = radius, RenderingBias = RenderingBias.Performance };
            blur.Freeze();
            _image.Effect = blur;
        }

        private void UpdateCrop()
        {
            if (_cornerRadius > 0 && _art.ActualWidth > 0 && _art.ActualHeight > 0)
                _art.Clip = new RectangleGeometry(new Rect(0, 0, _art.ActualWidth, _art.ActualHeight), _cornerRadius, _cornerRadius);
            if (_imageBrush?.ImageSource is not BitmapSource image || _background is null || _art.ActualWidth <= 0 || _art.ActualHeight <= 0) return;
            double sourceRatio = image.PixelWidth / (double)image.PixelHeight, targetRatio = _art.ActualWidth / _art.ActualHeight;
            double width = Math.Min(1, targetRatio / sourceRatio), height = Math.Min(1, sourceRatio / targetRatio);
            _imageBrush.Viewbox = new Rect((1 - width) * _background.FocalX, (1 - height) * _background.FocalY, width, height);
        }
    }
}
