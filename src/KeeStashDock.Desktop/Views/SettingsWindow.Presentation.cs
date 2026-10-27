using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using KeeStashDock.Desktop.Localization;

namespace KeeStashDock.Desktop.Views;

public partial class SettingsWindow
{
    private static readonly string[] SettingsPages = ["General", "Translation", "Appearance", "Components", "Data", "About"];

    private void ConfigurePresentation()
    {
        SettingsVersionText.Text = "v" + Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);
        SelectSettingsPage("General");
        RefreshConditionalOptions();
        RefreshFolderPaths();
        ApplySettingsContrast();
        void OnContrastChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SystemParameters.HighContrast) && !Dispatcher.HasShutdownStarted)
                Dispatcher.Invoke(ApplySettingsContrast);
        }
        SystemParameters.StaticPropertyChanged += OnContrastChanged;
        Closed += (_, _) => SystemParameters.StaticPropertyChanged -= OnContrastChanged;
        LocalizationService.Instance.LanguageChanged += () =>
        {
            RefreshPageHeading();
            RefreshFolderPaths();
            RefreshTranslationTargets();
            PopulateAboutOnly();
        };
        // Keep the full shell on small/high-DPI work areas; each page scrolls independently.
        Loaded += (_, _) =>
        {
            Rect work = SystemParameters.WorkArea;
            MinWidth = Math.Min(760, work.Width);
            MinHeight = Math.Min(560, work.Height);
            Width = Math.Min(Width, work.Width);
            Height = Math.Min(Height, work.Height);
        };
    }

    private void ApplySettingsContrast()
    {
        (string Key, object Brush)[] colors =
        [
            ("B.WindowBg", SystemColors.WindowBrush), ("B.CardBg", SystemColors.WindowBrush),
            ("B.CardHover", SystemColors.ControlBrush), ("B.InputBg", SystemColors.WindowBrush),
            ("B.TextPrimary", SystemColors.WindowTextBrush), ("B.TextMuted", SystemColors.WindowTextBrush),
            ("B.InputText", SystemColors.WindowTextBrush), ("B.Border", SystemColors.WindowTextBrush),
            ("B.Accent", SystemColors.HighlightBrush), ("B.AccentSoft", SystemColors.ControlBrush),
            ("B.OnAccent", SystemColors.HighlightTextBrush), ("B.Danger", SystemColors.WindowTextBrush),
        ];
        foreach (var color in colors)
        {
            if (SystemParameters.HighContrast) Resources[color.Key] = color.Brush;
            else Resources.Remove(color.Key);
        }
        RefreshSurfaceOpacity();
    }

    internal void SelectSettingsPage(string page)
    {
        var item = SettingsNavigation.Items.OfType<ListBoxItem>().Single(i => (string)i.Tag == page);
        SettingsNavigation.SelectedItem = item;
        RefreshPageHeading();
    }

    private void OnSettingsNavigationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, SettingsNavigation)) return;
        RefreshPageHeading();
    }

    private void RefreshPageHeading()
    {
        if (SettingsPageTitle is null || SettingsNavigation.SelectedItem is not ListBoxItem item) return;
        string selected = (string)item.Tag;
        foreach (string page in SettingsPages)
            if (FindName(page + "Page") is FrameworkElement content)
                content.Visibility = page == selected ? Visibility.Visible : Visibility.Collapsed;
        SettingsPageTitle.Text = LocalizationService.Instance["Settings.Nav." + selected];
        SettingsPageDescription.Text = LocalizationService.Instance["Settings.Intro." + selected];
    }

    internal void RefreshConditionalOptions()
    {
        if (_settings is null || BackgroundOptions is null) return;
        BackgroundOptions.Visibility = string.IsNullOrWhiteSpace(ThemeManager.CreatePanelAppearanceSettings(_settings.Current).BackgroundImagePath)
            ? Visibility.Collapsed : Visibility.Visible;
        RestoreBackgroundButton.Visibility = !_settings.Current.ThemeUseCustomBackground && !string.IsNullOrWhiteSpace(_settings.Current.BackgroundImagePath)
            ? Visibility.Visible : Visibility.Collapsed;
        FrostRadiusRow.Visibility = FrostCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        BackupScheduleOptions.Visibility = AutoBackupCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnResultOptionsChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.Current.TranslationResultShowTargetSelector = ResultTargetCheck.IsChecked == true;
        _settings.Current.TranslationResultShowSavePanel = ResultSaveCheck.IsChecked == true;
        _settings.Save();
    }
}
