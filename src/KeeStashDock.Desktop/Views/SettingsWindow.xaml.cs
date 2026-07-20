using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using KeeStashDock.Desktop.Interop;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Infrastructure.Storage;

namespace KeeStashDock.Desktop.Views;

/// <summary>
/// Settings code-behind. Deliberately not MVVM: this page is a thin bridge
/// between controls and SettingsStore/system helpers with no reusable logic,
/// so direct handlers keep it shorter and clearer than a view model would.
/// Every change is persisted immediately (no OK/Apply button).
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsStore _settings;
    private readonly BackupService _backup;
    private readonly DatabaseImportService _databaseImport;
    private readonly IApplicationRestarter _restarter;
    private readonly HotkeyManager _hotkey;
    private bool _loading = true;   // suppress handlers during initial population

    public SettingsWindow(
        SettingsStore settings,
        BackupService backup,
        DatabaseImportService databaseImport,
        IApplicationRestarter restarter,
        HotkeyManager hotkey)
    {
        InitializeComponent();
        _settings = settings;
        _backup = backup;
        _databaseImport = databaseImport;
        _restarter = restarter;
        _hotkey = hotkey;

        Closing += (s, e) => { e.Cancel = true; Hide(); };
        ThemeManager.TrackWindowChrome(this);   // dark title bar follows theme
        LocalizationService.Instance.LanguageChanged +=
            () => LocalizationService.Instance.ApplyFlowDirection(this);
        LocalizationService.Instance.ApplyFlowDirection(this);

        PopulateFromSettings();
        _loading = false;
    }

    public void ShowAndActivate() { Show(); Activate(); }

    private void PopulateFromSettings()
    {
        var cfg = _settings.Current;

        // Language list: discovered from disk, shown by native name.
        var langs = LocalizationService.Instance.DiscoverLanguages();
        LangCombo.ItemsSource = langs;
        LangCombo.SelectedItem = langs.FirstOrDefault(l => l.Tag == cfg.Language)
                                 ?? langs.FirstOrDefault(l => l.Tag == "en-US");

        HotkeyBox.Text = FormatHotkey(cfg.HotkeyModifiers, cfg.HotkeyVirtualKey);
        AutoStartCheck.IsChecked = AutoStartHelper.IsEnabled();
        CollapseDelayBox.Text = cfg.CollapseDelaySeconds.ToString("0.#",
            System.Globalization.CultureInfo.InvariantCulture);
        ExpandDelayBox.Text = cfg.ExpandDelaySeconds.ToString("0.#",
            System.Globalization.CultureInfo.InvariantCulture);
        ThemeCombo.SelectedIndex = cfg.Theme switch
        { "Light" => 1, "Dark" => 2, _ => 0 };

        BgPathText.Text = cfg.BackgroundImagePath ?? "—";
        BgOpacitySlider.Value = Math.Clamp(cfg.BackgroundOverlayOpacity, 0.2, 1.0) * 100;
        BgOpacityValue.Text = $"{(int)BgOpacitySlider.Value}%";
        FrostCheck.IsChecked = cfg.FrostedGlassEnabled;
        FrostSlider.Value = Math.Clamp(cfg.FrostedGlassRadius, 1, 18);
        FrostValue.Text = $"{(int)FrostSlider.Value}px";
        GlowCheck.IsChecked = cfg.EdgeGlowEnabled;
        GlowSlider.Value = Math.Clamp(cfg.EdgeGlowStrength, 4, 30);
        GlowValue.Text = $"{(int)GlowSlider.Value}px";
        GlowBrightSlider.Value = Math.Clamp(cfg.EdgeGlowBrightness, 0.1, 1.0) * 100;
        GlowBrightValue.Text = $"{(int)GlowBrightSlider.Value}%";
        CardOpacitySlider.Value = Math.Clamp(cfg.CardOpacity, 0.1, 1.0) * 100;
        CardOpacityValue.Text = $"{(int)CardOpacitySlider.Value}%";
        // Retired Light/Dark values from older settings alias to the
        // presets they were folded into (same mapping the panel applies).
        string mode = cfg.PanelTextColorMode switch
        { "Light" => "Ivory", "Dark" => "Graphite", _ => cfg.PanelTextColorMode };
        TextColorCombo.SelectedIndex = 0;   // fallback: Auto
        for (int i = 0; i < TextColorCombo.Items.Count; i++)
            if (TextColorCombo.Items[i] is ComboBoxItem it &&
                (string?)it.Tag == mode)
            { TextColorCombo.SelectedIndex = i; break; }
        CustomColorBox.Text = cfg.PanelTextCustomColor;
        RefreshCustomColorUi();
        SelectionPopupCheck.IsChecked = cfg.SelectionPopupEnabled;
        SelectionActionCombo.SelectedIndex =
            cfg.SelectionAction == "Translate" ? 1 : 0;
        BubbleStyleCombo.SelectedIndex = 1;   // fallback: FollowTheme
        for (int i = 0; i < BubbleStyleCombo.Items.Count; i++)
            if (BubbleStyleCombo.Items[i] is ComboBoxItem bs &&
                (string?)bs.Tag == cfg.SelectionBubbleStyle)
            { BubbleStyleCombo.SelectedIndex = i; break; }
        TranslateEngineCombo.SelectedIndex = cfg.TranslationEngine switch
        { "Bing" => 1, "Lingva" => 2, _ => 0 };
        BubbleGlowCheck.IsChecked = cfg.BubbleTextGlow;
        BubbleShadowCheck.IsChecked = cfg.BubbleShadow;
        BubbleOpacitySlider.Value = Math.Round(
            Math.Clamp(cfg.BubbleOpacity, 0.5, 1.0) * 100);
        BubbleOpacityValue.Text = $"{BubbleOpacitySlider.Value:0}%";
        ClipboardWatchCheck.IsChecked = cfg.ClipboardWatchEnabled;
        DirectPasteCheck.IsChecked = cfg.DirectPasteEnabled;
        AutoBackupCheck.IsChecked = cfg.AutoBackupEnabled;
        AutoBackupIntervalCombo.SelectedIndex = cfg.AutoBackupIntervalHours switch
        { 6 => 0, 12 => 1, 48 => 3, 168 => 4, _ => 2 };
        ApiKeyBox.Text = cfg.TranslationApiKey;
        AutoBackupIntervalCombo.IsEnabled = cfg.AutoBackupEnabled;
        AutoBackupIntervalCombo.Opacity = cfg.AutoBackupEnabled ? 1.0 : 0.4;
        RefreshSelectionActionUi();

        string ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
        AboutText.Text = $"KeeStashDock v{ver}\n© 2026 KeeganKei\nData: {PortablePaths.DataRoot}"
            + (PortablePaths.IsAppDataFallback
               ? $"\n{LocalizationService.Instance["Settings.About.FallbackNote"]}" : "");
    }

    // ---------------------------------------------------------- language

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (LangCombo.SelectedItem is not LocalizationService.LanguageInfo lang) return;
        LocalizationService.Instance.SetLanguage(lang.Tag);   // whole UI hot-switches here
        _settings.Current.Language = lang.Tag;
        _settings.Save();
        PopulateAboutOnly();
    }

    private void OnReloadLocaleClick(object sender, RoutedEventArgs e)
        => LocalizationService.Instance.Reload();   // translator proof-reading loop

    private void PopulateAboutOnly()
    {
        string ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
        AboutText.Text = $"KeeStashDock v{ver}\n© 2026 KeeganKei\nData: {PortablePaths.DataRoot}";
    }

    // ------------------------------------------------------------ hotkey

    /// <summary>Hotkey recorder: focus the box, press the desired combination.
    /// Requires at least one modifier; Rebind failure (combination owned by
    /// another app) reverts and shows the conflict warning - the promised
    /// "conflict detection" behavior.</summary>
    private void OnHotkeyRecord(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
               or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;

        uint mods = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= NativeMethods.MOD_CONTROL;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= NativeMethods.MOD_ALT;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= NativeMethods.MOD_SHIFT;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= NativeMethods.MOD_WIN;
        if (mods == 0) return;   // bare keys are rejected (design: e.g. F2 hijacks rename)

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        bool ok = _hotkey.Rebind(mods, vk);
        HotkeyStatus.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
        if (ok)
        {
            _settings.Current.HotkeyModifiers = mods;
            _settings.Current.HotkeyVirtualKey = vk;
            _settings.Save();
            HotkeyBox.Text = FormatHotkey(mods, vk);
        }
        else
        {
            // Re-register the previous working combination.
            _hotkey.Rebind(_settings.Current.HotkeyModifiers,
                           _settings.Current.HotkeyVirtualKey);
        }
    }

    private static string FormatHotkey(uint mods, uint vk)
    {
        var parts = new List<string>(4);
        if ((mods & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((mods & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
        if ((mods & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((mods & NativeMethods.MOD_WIN) != 0) parts.Add("Win");
        parts.Add(KeyInterop.KeyFromVirtualKey((int)vk).ToString());
        return string.Join(" + ", parts);
    }

    // ---------------------------------------------------------- behavior

    private void OnBehaviorChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        AutoStartHelper.SetEnabled(AutoStartCheck.IsChecked == true);
        _settings.Current.SelectionPopupEnabled = SelectionPopupCheck.IsChecked == true;
        RefreshSelectionActionUi();
        _settings.Save();
    }

    /// <summary>Commits the bubble action (Capture/Translate). The bubble
    /// itself relabels live via SettingsStore.Changed.</summary>
    private void OnSelectionActionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _settings.Current.SelectionAction =
            (SelectionActionCombo.SelectedItem as ComboBoxItem)?.Tag as string
            ?? "Capture";
        _settings.Save();
        RefreshSelectionActionUi();   // engine row follows the action
    }

    /// <summary>Bubble effect toggles (glow / shadow). The bubbles restyle
    /// live via SettingsStore.Changed - no restart, next bubble wears it.</summary>
    private void OnBubbleFxChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.Current.BubbleTextGlow = BubbleGlowCheck.IsChecked == true;
        _settings.Current.BubbleShadow = BubbleShadowCheck.IsChecked == true;
        _settings.Save();
    }

    private void OnBubbleOpacityChanged(object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        // Fires DURING InitializeComponent: parsing Minimum="50" coerces the
        // value 0 -> 50 and raises ValueChanged while the sibling value
        // label (declared later in XAML) is still null. Guard both.
        if (BubbleOpacityValue is not null)
            BubbleOpacityValue.Text = $"{e.NewValue:0}%";
        if (_loading || _settings is null) return;
        _settings.Current.BubbleOpacity = e.NewValue / 100.0;
        _settings.Save();
    }

    /// <summary>Clipboard collection + direct paste. The watcher (a
    /// message-only window) reacts live to SettingsStore.Changed, so the
    /// single switch doubles as an instant pause.</summary>
    private void OnCollectionChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.Current.ClipboardWatchEnabled = ClipboardWatchCheck.IsChecked == true;
        _settings.Current.DirectPasteEnabled = DirectPasteCheck.IsChecked == true;
        _settings.Save();
    }

    private void OnAutoBackupChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.Current.AutoBackupEnabled = AutoBackupCheck.IsChecked == true;
        if (int.TryParse((AutoBackupIntervalCombo.SelectedItem as ComboBoxItem)?.Tag
                as string, out int hours))
            _settings.Current.AutoBackupIntervalHours = hours;
        AutoBackupIntervalCombo.IsEnabled = AutoBackupCheck.IsChecked == true;
        AutoBackupIntervalCombo.Opacity =
            AutoBackupCheck.IsChecked == true ? 1.0 : 0.4;
        _settings.Save();
    }

    private void OnApiKeyChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        _settings.Current.TranslationApiKey = ApiKeyBox.Text;
        _settings.Save();
    }

    private void OnTranslateEngineChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _settings.Current.TranslationEngine =
            (TranslateEngineCombo.SelectedItem as ComboBoxItem)?.Tag as string
            ?? "Google";
        _settings.Save();
        RefreshSelectionActionUi();   // the API-key row follows the engine
    }

    /// <summary>Commits the bubble dress; bubble/toasts restyle live via
    /// SettingsStore.Changed.</summary>
    private void OnBubbleStyleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _settings.Current.SelectionBubbleStyle =
            (BubbleStyleCombo.SelectedItem as ComboBoxItem)?.Tag as string
            ?? "FollowTheme";
        _settings.Save();
    }

    /// <summary>The action/style combos only mean anything while the bubble
    /// is on; lock and dim them (same treatment as the custom color row).</summary>
    private void RefreshSelectionActionUi()
    {
        bool on = SelectionPopupCheck.IsChecked == true;
        SelectionActionCombo.IsEnabled = on;
        SelectionActionCombo.Opacity = on ? 1.0 : 0.4;
        BubbleStyleCombo.IsEnabled = on;
        BubbleStyleCombo.Opacity = on ? 1.0 : 0.4;
        // The engine row only EXISTS while the action is Translate; it is
        // additionally locked with the bubble toggle like its siblings.
        bool translate = (SelectionActionCombo.SelectedItem as ComboBoxItem)?.Tag
            as string == "Translate";
        TranslateEngineRow.Visibility =
            translate ? Visibility.Visible : Visibility.Collapsed;
        TranslateEngineCombo.IsEnabled = on;
        TranslateEngineCombo.Opacity = on ? 1.0 : 0.4;
        // API key: only exists for engines that need one (DeepL), and only
        // while the action is Translate.
        bool deepl = (TranslateEngineCombo.SelectedItem as ComboBoxItem)?.Tag
            as string == "DeepL";
        ApiKeyRow.Visibility = translate && deepl
            ? Visibility.Visible : Visibility.Collapsed;
        ApiKeyBox.IsEnabled = on;
        ApiKeyBox.Opacity = on ? 1.0 : 0.4;
        BubbleGlowCheck.IsEnabled = on;
        BubbleGlowCheck.Opacity = on ? 1.0 : 0.4;
        BubbleShadowCheck.IsEnabled = on;
        BubbleShadowCheck.Opacity = on ? 1.0 : 0.4;
        BubbleOpacitySlider.IsEnabled = on;
        BubbleOpacitySlider.Opacity = on ? 1.0 : 0.4;
    }

    /// <summary>Opens the user guide (localized; follows the app language,
    /// which itself defaults to the system language on first run).</summary>
    private void OnHelpClick(object sender, RoutedEventArgs e)
        => HelpWindow.ShowGuide(this);

    /// <summary>Restart: schedule a delayed relaunch via a hidden cmd, THEN
    /// shut down. Order matters - launching the exe directly first would hit
    /// the single-instance mutex while this process is still alive, and the
    /// new instance would just wake the panel and exit.</summary>
    private void OnRestartClick(object sender, RoutedEventArgs e)
    {
        if (_restarter.TryRestart())
        {
            Application.Current.Shutdown();
        }
        else
        {
            MessageBox.Show(
                this,
                "KeeStashDock could not restart automatically. Please exit and start it again.",
                "KeeStashDock",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>Panel timing fields: parse (invariant OR current culture, so
    /// both "1.5" and "1,5" work), clamp to 0.5-60 s collapse / 0.1-10 s
    /// expand, persist. The panel reads settings live on every tick, so no
    /// push notification is needed.</summary>
    private void OnDelayChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var cfg = _settings.Current;

        if (TryParseSeconds(CollapseDelayBox.Text, out double c))
            cfg.CollapseDelaySeconds = Math.Clamp(c, 0.5, 60);
        if (TryParseSeconds(ExpandDelayBox.Text, out double x))
            cfg.ExpandDelaySeconds = Math.Clamp(x, 0.1, 10);
        _settings.Save();

        // Reflect the clamped values back into the boxes.
        CollapseDelayBox.Text = cfg.CollapseDelaySeconds.ToString("0.#",
            System.Globalization.CultureInfo.InvariantCulture);
        ExpandDelayBox.Text = cfg.ExpandDelaySeconds.ToString("0.#",
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private void OnDelayKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnDelayChanged(sender, e);
    }

    private void OnCustomColorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnCustomColorChanged(sender, e);
    }

    private static bool TryParseSeconds(string text, out double value)
        => double.TryParse(text, System.Globalization.NumberStyles.Float,
               System.Globalization.CultureInfo.InvariantCulture, out value)
           || double.TryParse(text, out value);

    private void OnTextColorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _settings.Current.PanelTextColorMode =
            (TextColorCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Auto";
        _settings.Save();   // panel hot-applies via SettingsStore.Changed
        RefreshCustomColorUi();
    }

    /// <summary>Commits the custom hex color. Invalid input reverts to the
    /// last saved value instead of poisoning the settings file - the panel
    /// would fall back to white anyway, but the box should tell the truth.</summary>
    private void OnCustomColorChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        string text = CustomColorBox.Text.Trim();
        if (!text.StartsWith('#')) text = "#" + text;
        try
        {
            System.Windows.Media.ColorConverter.ConvertFromString(text);
            _settings.Current.PanelTextCustomColor = text;
            _settings.Save();
        }
        catch
        {
            CustomColorBox.Text = _settings.Current.PanelTextCustomColor;
        }
        RefreshCustomColorUi();
    }

    /// <summary>Opens the standard Windows color dialog (full RGB picker;
    /// WinForms is already in the process for the tray icon, so this costs
    /// nothing new). OK writes the hex box, settings, and swatch in one go.</summary>
    private void OnPickColorClick(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.ColorDialog
        { FullOpen = true, AnyColor = true };
        try
        {
            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                .ConvertFromString(_settings.Current.PanelTextCustomColor);
            dlg.Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
        }
        catch { /* keep the dialog's default */ }

        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        string hex = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
        _settings.Current.PanelTextCustomColor = hex;
        CustomColorBox.Text = hex;
        _settings.Save();
        RefreshCustomColorUi();
    }

    /// <summary>Hex box + swatch only matter in Custom mode; the swatch
    /// always previews the color that would apply.</summary>
    private void RefreshCustomColorUi()
    {
        bool custom = (TextColorCombo.SelectedItem as ComboBoxItem)?.Tag
            as string == "Custom";
        CustomColorRow.IsEnabled = custom;      // locks the hex box
        CustomColorRow.Opacity = custom ? 1.0 : 0.4;   // and reads as locked
        try
        {
            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                .ConvertFromString(_settings.Current.PanelTextCustomColor);
            CustomColorSwatch.Background = new System.Windows.Media.SolidColorBrush(c);
        }
        catch
        {
            CustomColorSwatch.Background = System.Windows.Media.Brushes.White;
        }
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        string theme = (ThemeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "System";
        _settings.Current.Theme = theme;
        _settings.Save();
        ThemeManager.Apply(theme);
    }

    // --------------------------------------------------------- background

    private void OnPickBgClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.webp",
        };
        if (dlg.ShowDialog(this) != true) return;
        _settings.Current.BackgroundImagePath = dlg.FileName;
        _settings.Save();
        BgPathText.Text = dlg.FileName;
    }

    private void OnClearBgClick(object sender, RoutedEventArgs e)
    {
        _settings.Current.BackgroundImagePath = null;
        _settings.Save();
        BgPathText.Text = "—";
    }

    private void OnBgOpacityChanged(object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _settings.Current.BackgroundOverlayOpacity = e.NewValue / 100.0;
        _settings.Save();
        BgOpacityValue.Text = $"{(int)e.NewValue}%";
    }

    /// <summary>Frost + glow controls share one handler; SettingsStore.Changed
    /// hot-applies the result to the panel and workbench immediately.</summary>
    private void OnEffectsChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var cfg = _settings.Current;
        cfg.FrostedGlassEnabled = FrostCheck.IsChecked == true;
        cfg.FrostedGlassRadius = FrostSlider.Value;
        cfg.EdgeGlowEnabled = GlowCheck.IsChecked == true;
        cfg.EdgeGlowStrength = GlowSlider.Value;
        cfg.EdgeGlowBrightness = GlowBrightSlider.Value / 100.0;
        cfg.CardOpacity = CardOpacitySlider.Value / 100.0;
        _settings.Save();
        FrostValue.Text = $"{(int)FrostSlider.Value}px";
        GlowValue.Text = $"{(int)GlowSlider.Value}px";
        GlowBrightValue.Text = $"{(int)GlowBrightSlider.Value}%";
        CardOpacityValue.Text = $"{(int)CardOpacitySlider.Value}%";
    }

    // -------------------------------------------------------------- data

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            FileName = $"KeeStashDock_export_{DateTime.Now:yyyyMMdd}.ksdpkg",
            Filter = "KeeStashDock package (*.ksdpkg)|*.ksdpkg",
        };
        if (dlg.ShowDialog(this) == true)
            _backup.ExportPackage(dlg.FileName);
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        { Filter = "KeeStashDock package (*.ksdpkg)|*.ksdpkg|SQLite database (*.db)|*.db" };
        if (dlg.ShowDialog(this) != true) return;

        var loc = LocalizationService.Instance;
        if (MessageBox.Show(this, loc["Settings.Backup.Import.Confirm"], loc["Common.Confirm"],
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        IsEnabled = false;
        try
        {
            await _databaseImport.ImportAsync(dlg.FileName);
            Application.Current.Shutdown();
        }
        catch (DatabaseImportException exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "KeeStashDock",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            if (!_databaseImport.AreOperationsDisabled)
            {
                IsEnabled = true;
            }
        }
    }
}
