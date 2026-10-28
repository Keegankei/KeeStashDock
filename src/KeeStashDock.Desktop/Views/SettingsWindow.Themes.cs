using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Storage;
using Microsoft.Win32;

namespace KeeStashDock.Desktop.Views;

public partial class SettingsWindow
{
    private bool _themeOperation;
    private CancellationTokenSource? _themeCancellation;
    private CancellationTokenSource? _themeCatalogCancellation;
    private IReadOnlyList<ThemeScheme>? _themeCatalog;
    private int _themeCatalogGeneration;
    private bool _themeWindowClosed;
    internal Task ThemeCatalogRefresh { get; private set; } = Task.CompletedTask;

    private void PopulateThemes(bool refresh = false, string? selectedKey = null)
    {
        if (_themeWindowClosed) return;
        ApplyThemeChoices(_themeCatalog ?? (ThemeManager.ActiveScheme is { } active ? [active] : []), selectedKey);
        if (!refresh && (_themeCatalog is not null || _themeCatalogCancellation is not null)) return;
        CancelThemeCatalog();
        var cancellation = _themeCatalogCancellation = new CancellationTokenSource();
        int generation = _themeCatalogGeneration;
        // Capture the store before dispatching: the application/test host may
        // change its active theme root while this validation is in progress.
        ThemePackageStore store = ThemeManager.Store;
        ThemeCatalogRefresh = RefreshThemeCatalogAsync(store, cancellation, generation, selectedKey);
    }

    private async Task RefreshThemeCatalogAsync(ThemePackageStore store, CancellationTokenSource cancellation,
        int generation, string? selectedKey)
    {
        CancellationToken token = cancellation.Token;
        try
        {
            IReadOnlyList<ThemeScheme> schemes = await Task.Run(() => store.Discover(token), token);
            if (_themeWindowClosed || token.IsCancellationRequested || generation != _themeCatalogGeneration) return;
            // Keep verified definitions for this window. Language switches and
            // ordinary appearance changes must not decode all installed images.
            _themeCatalog = schemes;
            ApplyThemeChoices(schemes, selectedKey ?? (ThemePresetCombo.SelectedItem as ThemeChoice)?.Key);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ThemePackageStore.IsPackageFailure(ex))
        {
            if (!_themeWindowClosed && generation == _themeCatalogGeneration)
                ThemePresetStatus.Text = LocalizationService.Instance["Theme.Unavailable"];
        }
        finally
        {
            if (ReferenceEquals(_themeCatalogCancellation, cancellation)) _themeCatalogCancellation = null;
            cancellation.Dispose();
        }
    }

    private void ApplyThemeChoices(IReadOnlyList<ThemeScheme> schemes, string? selectedKey = null)
    {
        var choices = new List<ThemeChoice> { new("", LocalizationService.Instance["Theme.Default"], null) };
        choices.AddRange(schemes.Select(scheme => new ThemeChoice(scheme.Key, scheme.Manifest.Name + " · v" + scheme.Manifest.Version, scheme)));
        ThemePresetCombo.ItemsSource = choices;
        ThemePresetCombo.SelectedItem = choices.FirstOrDefault(choice => choice.Key == (selectedKey ?? _settings.Current.ThemePresetId)) ?? choices[0];
        UpdateThemeStatus();
    }

    private void CancelThemeCatalog()
    {
        _themeCatalogGeneration++;
        _themeCatalogCancellation?.Cancel();
        _themeCatalogCancellation = null;
    }

    private void UpdateThemeStatus()
    {
        if (!Dispatcher.CheckAccess())
        { if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(UpdateThemeStatus)); return; }
        if (_themeOperation || ThemePresetStatus is null) return;
        ThemePresetStatus.Text = ThemeManager.LoadError is not null ? LocalizationService.Instance[ThemeManager.LoadError] :
            _settings.Current.ThemeAppearanceCustomized || _settings.Current.ThemeUseCustomBackground && !string.IsNullOrEmpty(_settings.Current.BackgroundImagePath)
                ? LocalizationService.Instance["Theme.Customized"] : LocalizationService.Instance["Theme.CompleteHint"];
    }

    private void OnThemeLanguageChanged()
    {
        if (!_themeOperation) PopulateThemes();
    }

    private void OnPreviewThemeClick(object sender, RoutedEventArgs e)
    {
        if (_themeOperation || ThemePresetCombo.SelectedItem is not ThemeChoice choice) return;
        var dialog = new ThemePreviewWindow(choice.Scheme, _settings) { Owner = this };
        dialog.ShowDialog();
        ReloadAppearanceControls();
    }

    private void OnResetThemeClick(object sender, RoutedEventArgs e)
    {
        if (_themeOperation) return;
        new ThemePreviewWindow(null, _settings) { Owner = this }.ShowDialog();
        ReloadAppearanceControls();
    }

    private void ReloadAppearanceControls()
    {
        bool loading = _loading; _loading = true;
        try { PopulateFromSettings(); PopulateThemes(); RefreshConditionalOptions(); }
        finally { _loading = loading; }
    }

    private async void OnImportThemeClick(object sender, RoutedEventArgs e)
    {
        if (_themeOperation) return;
        var file = new OpenFileDialog { Filter = "Theme package (*.zip)|*.zip", Title = LocalizationService.Instance["Theme.Import"] };
        if (file.ShowDialog(this) != true) return;
        _themeOperation = true; ThemePackageButtons.IsEnabled = false;
        _themeCancellation = new CancellationTokenSource();
        ThemePresetStatus.Text = LocalizationService.Instance["Theme.Validating"];
        try
        {
            using ThemePackageStore.StagedTheme staged = await Task.Run(() => ThemeManager.Store.StageImport(file.FileName, _themeCancellation.Token));
            if (!IsVisible) return;
            var preview = new ThemePreviewWindow(staged.Scheme, _settings, installOnly: true) { Owner = this };
            if (preview.ShowDialog() != true) return;
            ThemeScheme? existing = await Task.Run(() => ThemeManager.Store.Find("custom:" + staged.Scheme.Manifest.Id));
            if (!IsVisible) return;
            bool replace = existing is not null && !preview.InstallAsCopy;
            if (replace && existing!.Manifest.Version != staged.Scheme.Manifest.Version && MessageBox.Show(this,
                LocalizationService.Instance["Theme.ReplaceConfirm"], "KeeStashDock", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            bool installAsCopy = preview.InstallAsCopy;
            ThemeScheme installed = await Task.Run(() => ThemeManager.Store.Install(staged, replace, installAsCopy));
            ThemeManager.RefreshSchemes();
            _themeCatalog = (_themeCatalog ?? []).Where(scheme => scheme.Key != installed.Key).Append(installed).ToArray();
            PopulateThemes(refresh: true, selectedKey: installed.Key);
            ThemePresetStatus.Text = LocalizationService.Instance["Theme.Imported"];
        }
        catch (OperationCanceledException) { ThemePresetStatus.Text = LocalizationService.Instance["Theme.Cancelled"]; }
        catch (Exception ex) when (ThemePackageStore.IsPackageFailure(ex) || ex is InvalidOperationException)
        { ThemePresetStatus.Text = LocalizationService.Instance["Theme.ImportFailed"]; }
        finally { _themeOperation = false; ThemePackageButtons.IsEnabled = true; _themeCancellation.Dispose(); _themeCancellation = null; }
    }

    private async void OnExportThemeClick(object sender, RoutedEventArgs e)
    {
        if (_themeOperation) return;
        ThemeScheme? active = ThemeManager.ActiveScheme;
        var cfg = _settings.Current;
        string? background = cfg.ThemeUseCustomBackground ? cfg.BackgroundImagePath : null;
        if (!string.IsNullOrEmpty(background) && MessageBox.Show(this, LocalizationService.Instance["Theme.ExportBackgroundConfirm"],
                "KeeStashDock", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        var file = new SaveFileDialog { Filter = "Theme package (*.zip)|*.zip", FileName = (active?.Manifest.Id ?? "keestashdock-appearance") + "-theme.zip",
            Title = LocalizationService.Instance["Theme.Export"] };
        if (file.ShowDialog(this) != true) return;
        ThemeExportOverrides? appearance = active is null || cfg.ThemeAppearanceCustomized || !string.IsNullOrEmpty(background)
            ? new(cfg.CardOpacity, cfg.BubbleOpacity, cfg.PanelTextColorMode, cfg.PanelTextCustomColor,
                background, cfg.BackgroundOverlayOpacity, cfg.FrostedGlassEnabled ? cfg.FrostedGlassRadius : 0, true,
                cfg.SelectionBubbleStyle, cfg.ThemeUseCustomBackground, cfg.SettingsSurfaceOpacity,
                SettingsFrostedGlassEnabled: cfg.SettingsFrostedGlassEnabled, SettingsFrostedGlassRadius: cfg.SettingsFrostedGlassRadius) : null;
        _themeOperation = true; ThemePackageButtons.IsEnabled = false;
        try
        {
            if (active is null)
            {
                ThemeVariant light = ThemeManager.CreateDefaultVariant(false), dark = ThemeManager.CreateDefaultVariant(true);
                await Task.Run(() => ThemeManager.Store.ExportDefault(light, dark, file.FileName, appearance!));
            }
            else await Task.Run(() => ThemeManager.Store.Export(active, file.FileName, appearance));
            ThemePresetStatus.Text = LocalizationService.Instance["Theme.Exported"];
        }
        catch (Exception ex) when (ThemePackageStore.IsPackageFailure(ex) || ex is InvalidOperationException)
        { ThemePresetStatus.Text = LocalizationService.Instance["Theme.ExportFailed"]; }
        finally { _themeOperation = false; ThemePackageButtons.IsEnabled = true; }
    }

    private void OnOpenThemesClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(ThemeManager.Store.Root);
            Process.Start(new ProcessStartInfo(ThemeManager.Store.Root) { UseShellExecute = true });
        }
        catch (Exception ex) when (ThemePackageStore.IsPackageFailure(ex) || ex is System.ComponentModel.Win32Exception)
        { ThemePresetStatus.Text = LocalizationService.Instance["Theme.FolderFailed"]; }
    }

    private void OnRestoreBackgroundClick(object sender, RoutedEventArgs e)
    {
        _settings.Current.ThemeUseCustomBackground = true;
        _settings.Current.ThemeAppearanceCustomized = true;
        _settings.Save(); ReloadAppearanceControls();
    }

    private sealed record ThemeChoice(string Key, string Name, ThemeScheme? Scheme);
}
