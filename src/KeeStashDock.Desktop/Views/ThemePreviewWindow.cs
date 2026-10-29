using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Storage;

namespace KeeStashDock.Desktop.Views;

/// <summary>A reversible live preview. Only explicit Apply persists the appearance-field whitelist.</summary>
public sealed partial class ThemePreviewWindow : Window
{
    private readonly ThemeScheme? _scheme;
    private readonly SettingsStore _settings;
    private readonly bool _installOnly;
    private bool? _stacked;
    private ComboBox _mode => PreviewModeCombo;
    private CheckBox _bubble => PreviewBubbleCheck;
    private TextBlock _error => PreviewError;
    public bool InstallAsCopy => PreviewInstallCopyCheck.IsChecked == true;

    public ThemePreviewWindow(ThemeScheme? scheme, SettingsStore settings, bool installOnly = false)
    {
        _scheme = scheme; _settings = settings; _installOnly = installOnly;
        InitializeComponent();
        FitWindowToWorkArea();
        _mode.SelectedIndex = settings.Current.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        _bubble.Visibility = installOnly ? Visibility.Collapsed : Visibility.Visible;
        PreviewInstallCopyCheck.Visibility = installOnly ? Visibility.Visible : Visibility.Collapsed;
        RefreshLanguage();
        _mode.SelectionChanged += OnModeChanged;
        ThemeManager.Changed += RefreshPreviewVisuals;
        LocalizationService.Instance.LanguageChanged += RefreshLanguage;
        Loaded += (_, _) => FitWindowToWorkArea();
        Closed += (_, _) =>
        {
            ThemeManager.Changed -= RefreshPreviewVisuals;
            LocalizationService.Instance.LanguageChanged -= RefreshLanguage;
            ThemeManager.EndPreview();
        };
        ThemeManager.TrackWindowChrome(this);
        UpdatePreview();
    }

    private void RefreshLanguage()
    {
        var loc = LocalizationService.Instance;
        loc.ApplyFlowDirection(this);
        PreviewName.Text = _scheme?.Manifest.Name ?? loc["Theme.Default"];
        PreviewDescription.Text = _scheme is null ? loc["Theme.DefaultHint"] : $"v{_scheme.Manifest.Version}  ·  {loc["Theme.Complete"]}";
        PreviewHint.Text = loc[_installOnly ? "Theme.ImportPreviewHint" : "Theme.PreviewHint"];
        PreviewApplyButton.Content = loc[_installOnly ? "Theme.Install" : "Theme.Apply"];
    }

    private void FitWindowToWorkArea()
    {
        Rect work = SystemParameters.WorkArea;
        nint handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle != 0)
        {
            var bounds = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            work = new Rect(0, 0, bounds.Width / dpi.DpiScaleX, bounds.Height / dpi.DpiScaleY);
        }
        MinWidth = Math.Min(420, work.Width);
        MinHeight = Math.Min(460, work.Height);
        Width = Math.Min(Width, work.Width);
        Height = Math.Min(Height, work.Height);
    }

    private void OnModeChanged(object sender, SelectionChangedEventArgs args)
    {
        if (ReferenceEquals(args.Source, _mode)) UpdatePreview();
    }

    private void UpdatePreview()
    {
        string mode = (_mode.SelectedItem as ComboBoxItem)?.Tag as string ?? "System";
        ThemeManager.Preview(_scheme, mode);
    }

    private void RefreshPreviewVisuals()
    {
        // A palette notification never starts another preview. It also handles
        // system-theme and high-contrast changes while the dialog remains open.
        PreviewImage.Source = ThemeManager.GetBackgroundImage("workspace");
        PreviewImageFrame.Visibility = PreviewImage.Source is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnCardsSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (PreviewCards is null || PreviewSampleCard is null || PreviewOptionsCard is null) return;
        bool stacked = args.NewSize.Width < 600;
        if (_stacked == stacked) return;
        _stacked = stacked;
        PreviewCards.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(PreviewSampleCard, stacked ? 0 : 1);
        Grid.SetRow(PreviewSampleCard, stacked ? 1 : 0);
        PreviewOptionsCard.Margin = stacked ? new Thickness(0) : new Thickness(0, 0, 9, 0);
        PreviewSampleCard.Margin = stacked ? new Thickness(0, 16, 0, 0) : new Thickness(9, 0, 0, 0);
    }

    private void OnImageSizeChanged(object sender, SizeChangedEventArgs args)
        => PreviewImage.Clip = new RectangleGeometry(new Rect(new Point(), args.NewSize), 15, 15);

    private void OnCancelClick(object sender, RoutedEventArgs args) => DialogResult = false;
    private void OnApplyClick(object sender, RoutedEventArgs args) => ApplySelection();
    private void ApplySelection()
    {
        if (_installOnly) { DialogResult = true; return; }
        string mode = (_mode.SelectedItem as ComboBoxItem)?.Tag as string ?? "System";
        ThemeManager.Preview(_scheme, mode);
        var old = ThemeAppearanceSnapshot.Capture(_settings.Current);
        var cfg = _settings.Current;
        cfg.ThemePresetId = _scheme?.Key ?? ""; cfg.Theme = mode;
        cfg.ThemeUseCustomBackground = false; cfg.ThemeUseCustomSettingsBackground = false; cfg.ThemeAppearanceCustomized = false;
        cfg.SettingsBackgroundOverlayCustomized = false;
        cfg.SettingsAppearanceCustomized = false;
        ThemeVariant? variant = ThemeManager.IsDark ? _scheme?.Dark : _scheme?.Light;
        cfg.CardOpacity = variant?.CardOpacity ?? 0.9;
        cfg.SettingsSurfaceOpacity = variant?.SettingsSurfaceOpacity ?? 1;
        cfg.SettingsFrostedGlassEnabled = variant?.SettingsFrostedGlassEnabled ?? false;
        cfg.SettingsFrostedGlassRadius = variant?.SettingsFrostedGlassRadius ?? 8;
        ThemeBackground? settingsArtwork = null;
        if (variant?.HideSettingsBackground != true && variant is not null &&
            !variant.Backgrounds.TryGetValue("settings", out settingsArtwork))
            variant.Backgrounds.TryGetValue("workspace", out settingsArtwork);
        cfg.SettingsBackgroundOverlayOpacity = settingsArtwork?.OverlayOpacity ?? .2;
        cfg.PanelTextColorMode = variant?.PanelTextColorMode ?? "Auto";
        cfg.PanelTextCustomColor = variant?.PanelTextCustomColor ?? "#FFFFFF";
        if (variant?.Backgrounds.TryGetValue("quickPanel", out ThemeBackground? background) == true)
        {
            cfg.BackgroundOverlayOpacity = background.OverlayOpacity;
            cfg.FrostedGlassEnabled = background.BlurRadius > 0; cfg.FrostedGlassRadius = Math.Max(1, background.BlurRadius);
        }
        else { cfg.BackgroundOverlayOpacity = 0.2; cfg.FrostedGlassEnabled = false; cfg.FrostedGlassRadius = 4; }
        if (_bubble.IsChecked == true)
        {
            cfg.SelectionBubbleStyle = variant?.BubbleStyle ?? "Default";
            cfg.BubbleOpacity = variant?.BubbleOpacity ?? 1;
        }
        try { _settings.Save(); ThemeManager.EndPreview(); DialogResult = true; }
        catch (Exception ex) when (ThemePackageStore.IsPackageFailure(ex))
        {
            old.Restore(cfg); ThemeManager.EndPreview();
            _error.Text = LocalizationService.Instance["Theme.SaveFailed"];
            _error.Visibility = Visibility.Visible;
        }
    }
}

internal sealed record ThemeAppearanceSnapshot(string Id, string Mode, bool CustomBackground, bool Customized,
    double CardOpacity, string TextMode, string TextColor, double Overlay, bool Frost, double Blur,
    string BubbleStyle, double BubbleOpacity, double SettingsSurfaceOpacity,
    bool SettingsFrostedGlassEnabled, double SettingsFrostedGlassRadius,
    bool CustomSettingsBackground, string? SettingsBackgroundPath, double SettingsBackgroundOverlayOpacity,
    bool SettingsBackgroundOverlayCustomized, bool SettingsAppearanceCustomized)
{
    internal static ThemeAppearanceSnapshot Capture(AppSettings cfg) => new(cfg.ThemePresetId, cfg.Theme, cfg.ThemeUseCustomBackground,
        cfg.ThemeAppearanceCustomized, cfg.CardOpacity, cfg.PanelTextColorMode, cfg.PanelTextCustomColor, cfg.BackgroundOverlayOpacity,
        cfg.FrostedGlassEnabled, cfg.FrostedGlassRadius, cfg.SelectionBubbleStyle, cfg.BubbleOpacity,
        cfg.SettingsSurfaceOpacity, cfg.SettingsFrostedGlassEnabled, cfg.SettingsFrostedGlassRadius,
        cfg.ThemeUseCustomSettingsBackground, cfg.SettingsBackgroundImagePath, cfg.SettingsBackgroundOverlayOpacity,
        cfg.SettingsBackgroundOverlayCustomized, cfg.SettingsAppearanceCustomized);
    internal void Restore(AppSettings cfg)
    {
        cfg.ThemePresetId = Id; cfg.Theme = Mode; cfg.ThemeUseCustomBackground = CustomBackground; cfg.ThemeAppearanceCustomized = Customized;
        cfg.CardOpacity = CardOpacity; cfg.PanelTextColorMode = TextMode; cfg.PanelTextCustomColor = TextColor;
        cfg.SettingsSurfaceOpacity = SettingsSurfaceOpacity;
        cfg.SettingsFrostedGlassEnabled = SettingsFrostedGlassEnabled; cfg.SettingsFrostedGlassRadius = SettingsFrostedGlassRadius;
        cfg.ThemeUseCustomSettingsBackground = CustomSettingsBackground;
        cfg.SettingsBackgroundImagePath = SettingsBackgroundPath; cfg.SettingsBackgroundOverlayOpacity = SettingsBackgroundOverlayOpacity;
        cfg.SettingsBackgroundOverlayCustomized = SettingsBackgroundOverlayCustomized;
        cfg.SettingsAppearanceCustomized = SettingsAppearanceCustomized;
        cfg.BackgroundOverlayOpacity = Overlay; cfg.FrostedGlassEnabled = Frost; cfg.FrostedGlassRadius = Blur;
        cfg.SelectionBubbleStyle = BubbleStyle; cfg.BubbleOpacity = BubbleOpacity;
    }
}
