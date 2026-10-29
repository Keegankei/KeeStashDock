using System.Windows;
using System.Windows.Media;

namespace KeeStashDock.Desktop.Views;

public partial class SettingsWindow
{
    private void ConfigureSurfaceOpacity()
    {
        _settings.Changed += RefreshSurfaceOpacity;
        ThemeManager.Changed += RefreshSurfaceOpacity;
        Closed += (_, _) =>
        {
            _settings.Changed -= RefreshSurfaceOpacity;
            ThemeManager.Changed -= RefreshSurfaceOpacity;
        };
        RefreshSurfaceOpacity();
    }

    private void RefreshSurfaceOpacity()
    {
        if (!Dispatcher.CheckAccess())
        {
            if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(RefreshSurfaceOpacity));
            return;
        }
        bool hasArtwork = !SystemParameters.HighContrast && ThemeManager.GetBackgroundImage("settings") is not null;
        SettingsSurfaceOpacitySlider.IsEnabled = hasArtwork;
        var appearance = ThemeManager.CreatePanelAppearanceSettings(_settings.Current);
        double selected = appearance.SettingsSurfaceOpacity;
        selected = double.IsFinite(selected) ? Math.Clamp(selected, .1, 1) : 1;
        bool loading = _loading;
        _loading = true;
        try
        {
            SettingsSurfaceOpacitySlider.Value = selected * 100;
            SettingsSurfaceOpacityValue.Text = $"{(int)Math.Round(selected * 100)}%";
            SettingsFrostCheck.IsChecked = appearance.SettingsFrostedGlassEnabled;
            SettingsFrostCheck.IsEnabled = hasArtwork;
            SettingsFrostSlider.Value = double.IsFinite(appearance.SettingsFrostedGlassRadius)
                ? Math.Clamp(appearance.SettingsFrostedGlassRadius, 1, 18) : 8;
            SettingsFrostValue.Text = $"{(int)Math.Round(SettingsFrostSlider.Value)}px";
            SettingsFrostRadiusRow.Visibility = hasArtwork && appearance.SettingsFrostedGlassEnabled
                ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _loading = loading; }
        double opacity = hasArtwork ? selected : 1;

        // Clone application brushes into this window's scope. Never fade the
        // control tree: text, icons, focus outlines and popup menus stay opaque.
        SetSurfaceBrush("B.SettingsSurfaceBg", "B.CardBg", opacity);
        SetSurfaceBrush("B.InputBg", "B.InputBg", opacity);
        ThemeWindowSurface.SetSettingsFrost(this, hasArtwork && appearance.SettingsFrostedGlassEnabled,
            SettingsFrostSlider.Value);
        RefreshBackgroundControls();
    }

    private void SetSurfaceBrush(string key, string sourceKey, double opacity)
    {
        var source = SystemParameters.HighContrast ? SystemColors.WindowBrush
            : (SolidColorBrush)Application.Current.FindResource(sourceKey);
        if (Resources[key] is SolidColorBrush current && current.Color == source.Color &&
            current.Opacity == source.Opacity * opacity) return;
        var brush = source.CloneCurrentValue();
        brush.Opacity *= opacity;
        brush.Freeze();
        Resources[key] = brush;
    }

    private void OnSettingsSurfaceOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        TryBackgroundChange(() => CommitBackgroundChange(cfg =>
        {
            CustomizeSettingsAppearance(cfg);
            cfg.SettingsSurfaceOpacity = e.NewValue / 100;
        }));
    }

    private void OnSettingsFrostChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool enabled = SettingsFrostCheck.IsChecked == true;
        double radius = SettingsFrostSlider.Value;
        TryBackgroundChange(() => CommitBackgroundChange(cfg =>
        {
            CustomizeSettingsAppearance(cfg);
            cfg.SettingsFrostedGlassEnabled = enabled;
            cfg.SettingsFrostedGlassRadius = radius;
        }));
    }
}
