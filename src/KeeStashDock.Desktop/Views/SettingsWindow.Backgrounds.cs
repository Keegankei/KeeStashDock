using System.IO;
using System.Windows;
using System.Windows.Controls;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Storage;

namespace KeeStashDock.Desktop.Views;

public partial class SettingsWindow
{
    internal bool IsSettingsBackgroundSelected =>
        (BackgroundTargetCombo.SelectedItem as ComboBoxItem)?.Tag as string == "settings";

    private void OnBackgroundTargetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _settings is null) return;
        RefreshBackgroundControls();
    }

    private void RefreshBackgroundControls()
    {
        if (_settings is null || BackgroundOptions is null) return;
        var saved = _settings.Current;
        var appearance = ThemeManager.CreatePanelAppearanceSettings(saved);
        bool settingsPage = IsSettingsBackgroundSelected;
        string? path = settingsPage ? appearance.SettingsBackgroundImagePath : appearance.BackgroundImagePath;
        bool custom = settingsPage ? saved.ThemeUseCustomSettingsBackground : saved.ThemeUseCustomBackground;
        string? personalPath = settingsPage ? saved.SettingsBackgroundImagePath : saved.BackgroundImagePath;
        bool loading = _loading;
        _loading = true;
        try
        {
            BgPathText.Text = !custom && !string.IsNullOrEmpty(path)
                ? ThemeManager.ActiveScheme?.Manifest.Name + " / " + Path.GetFileName(path)
                : path ?? "—";
            BgOpacitySlider.Minimum = settingsPage ? 0 : 20;
            BgOpacitySlider.Value = (settingsPage ? appearance.SettingsBackgroundOverlayOpacity : appearance.BackgroundOverlayOpacity) * 100;
            BgOpacityValue.Text = $"{(int)Math.Round(BgOpacitySlider.Value)}%";
            BackgroundOptions.Visibility = string.IsNullOrWhiteSpace(path) ? Visibility.Collapsed : Visibility.Visible;
            RestoreBackgroundButton.Visibility = !custom && !string.IsNullOrWhiteSpace(personalPath)
                ? Visibility.Visible : Visibility.Collapsed;
            UseThemeBackgroundButton.Visibility = custom && ThemeManager.ActiveScheme is not null
                ? Visibility.Visible : Visibility.Collapsed;
            QuickPanelFrostOptions.Visibility = settingsPage ? Visibility.Collapsed : Visibility.Visible;
            SettingsBackgroundOptions.Visibility = settingsPage ? Visibility.Visible : Visibility.Collapsed;
            FrostRadiusRow.Visibility = FrostCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _loading = loading; }
    }

    // Validate before changing preferences so a failed choice keeps the previous artwork.
    internal void SetSelectedBackground(string? path)
    {
        if (path is not null) ThemeManager.ValidateImage(path);
        ApplySelectedBackground(path);
    }

    private void ApplySelectedBackground(string? path) => CommitBackgroundChange(cfg =>
    {
        if (IsSettingsBackgroundSelected)
        {
            cfg.SettingsBackgroundImagePath = path;
            cfg.ThemeUseCustomSettingsBackground = true;
        }
        else
        {
            cfg.BackgroundImagePath = path;
            cfg.ThemeUseCustomBackground = true;
        }
    });

    private void TrySetSelectedBackground(string path)
    {
        try { ThemeManager.ValidateImage(path); }
        catch (Exception ex) when (ThemePackageStore.IsPackageFailure(ex))
        {
            ShowBackgroundError("Settings.Bg.InvalidImage");
            return;
        }
        TryBackgroundChange(() => ApplySelectedBackground(path));
    }

    internal void UseThemeBackground() => CommitBackgroundChange(cfg =>
    {
        if (IsSettingsBackgroundSelected) cfg.ThemeUseCustomSettingsBackground = false;
        else cfg.ThemeUseCustomBackground = false;
    });

    internal void CommitBackgroundChange(Action<AppSettings> update)
    {
        var cfg = _settings.Current;
        var previous = ThemeAppearanceSnapshot.Capture(cfg);
        string? panelImage = cfg.BackgroundImagePath;
        try
        {
            update(cfg);
            _settings.Save();
        }
        catch (Exception ex) when (ThemePackageStore.IsPackageFailure(ex))
        {
            previous.Restore(cfg);
            cfg.BackgroundImagePath = panelImage;
            ReloadAppearanceControls();
            RefreshSurfaceOpacity();
            throw;
        }
        RefreshBackgroundControls();
    }

    private void TryBackgroundChange(Action change)
    {
        try { change(); ShowBackgroundError(null); }
        catch (Exception ex) when (ThemePackageStore.IsPackageFailure(ex)) { ShowBackgroundError("Theme.SaveFailed"); }
    }

    private void ShowBackgroundError(string? key)
    {
        BackgroundStatusText.Text = key is null ? "" : LocalizationService.Instance[key];
        BackgroundStatusText.Visibility = key is null ? Visibility.Collapsed : Visibility.Visible;
        if (key is not null) BackgroundStatusText.BringIntoView();
    }

    private static void CustomizeSettingsAppearance(AppSettings cfg)
    {
        var appearance = ThemeManager.CreatePanelAppearanceSettings(cfg);
        cfg.SettingsSurfaceOpacity = appearance.SettingsSurfaceOpacity;
        cfg.SettingsFrostedGlassEnabled = appearance.SettingsFrostedGlassEnabled;
        cfg.SettingsFrostedGlassRadius = appearance.SettingsFrostedGlassRadius;
        cfg.SettingsAppearanceCustomized = true;
    }

    private void OnUseThemeBackgroundClick(object sender, RoutedEventArgs e) => TryBackgroundChange(UseThemeBackground);
}
