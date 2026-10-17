using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using KeeStashDock.Desktop.Interop;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Infrastructure.Storage;
using KeeStashDock.Core.Services;
using KeeStashDock.Domain.Entities;

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
    private readonly HotkeyManager? _screenshotHotkey;
    private readonly Func<Task<bool>> _prepareForShutdown;
    private bool _shutdownPending;
    private readonly string _displayDataRoot;
    private readonly bool _isAppDataFallback;
    private bool _loading = true;   // suppress handlers during initial population
    private bool _updatingTranslationCredentials;
    private CancellationTokenSource? _translationTestCancellation;
    private readonly Func<string, AppSettings, CancellationToken, Task<TranslationResult>> _translate;

    public SettingsWindow(
        SettingsStore settings,
        BackupService backup,
        DatabaseImportService databaseImport,
        IApplicationRestarter restarter,
        HotkeyManager hotkey,
        HotkeyManager? screenshotHotkey = null,
        Func<Task<bool>>? prepareForShutdown = null)
        : this(settings, backup, databaseImport, restarter, hotkey,
            PortablePaths.DataRoot, PortablePaths.IsAppDataFallback, screenshotHotkey: screenshotHotkey,
            prepareForShutdown: prepareForShutdown)
    {
    }

    internal SettingsWindow(
        SettingsStore settings,
        BackupService backup,
        DatabaseImportService databaseImport,
        IApplicationRestarter restarter,
        HotkeyManager hotkey,
        string displayDataRoot,
        bool isAppDataFallback,
        Func<string, AppSettings, CancellationToken, Task<TranslationResult>>? translate = null,
        HotkeyManager? screenshotHotkey = null,
        Func<Task<bool>>? prepareForShutdown = null,
        ComponentInstallerActions? components = null)
    {
        InitializeComponent();
        _settings = settings;
        _backup = backup;
        _databaseImport = databaseImport;
        _restarter = restarter;
        _hotkey = hotkey;
        _screenshotHotkey = screenshotHotkey;
        _prepareForShutdown = prepareForShutdown ?? (() => Task.FromResult(true));
        _displayDataRoot = displayDataRoot;
        _isAppDataFallback = isAppDataFallback;
        _translate = translate ?? TranslationService.TranslateDetailedAsync;

        ConfigureComponents(components);
        Closing += (s, e) => { e.Cancel = true; CancelTranslationTest(); _componentCancellation?.Cancel(); Hide(); };
        IsVisibleChanged += (_, _) => { if (!IsVisible) CancelTranslationTest(); };
        ThemeManager.TrackWindowChrome(this);   // dark title bar follows theme
        LocalizationService.Instance.LanguageChanged +=
            () => { LocalizationService.Instance.ApplyFlowDirection(this); RefreshComponents(); };
        LocalizationService.Instance.ApplyFlowDirection(this);

        PopulateFromSettings();
        _loading = false;
    }

    public event Action? CheckUpdatesRequested;

    public void ShowAndActivate() { Show(); Activate(); }

    public void ShowComponents()
    {
        ShowAndActivate();
        Dispatcher.BeginInvoke(new Action(() => ComponentsSection.BringIntoView()));
    }

    private void OnCheckUpdatesClick(object sender, RoutedEventArgs e)
        => CheckUpdatesRequested?.Invoke();

    private void PopulateFromSettings()
    {
        var cfg = _settings.Current;

        // Language list: discovered from disk, shown by native name.
        var langs = LocalizationService.Instance.DiscoverLanguages();
        LangCombo.ItemsSource = langs;
        LangCombo.SelectedItem = langs.FirstOrDefault(l => l.Tag == cfg.Language)
                                 ?? langs.FirstOrDefault(l => l.Tag == "en-US");

        HotkeyBox.Text = FormatHotkey(cfg.HotkeyModifiers, cfg.HotkeyVirtualKey);
        ScreenshotHotkeyCheck.IsChecked = cfg.ScreenshotHotkeyEnabled;
        ScreenshotHotkeyCheck.IsEnabled = _screenshotHotkey is not null;
        ScreenshotHotkeyBox.Text = FormatHotkey(cfg.ScreenshotHotkeyModifiers, cfg.ScreenshotHotkeyVirtualKey);
        ScreenshotHotkeyBox.IsEnabled = _screenshotHotkey is not null;
        ScreenshotHotkeyStatus.Visibility = cfg.ScreenshotHotkeyEnabled && _screenshotHotkey is { IsRegistered: false }
            ? Visibility.Visible : Visibility.Collapsed;
        AutoStartCheck.IsChecked = AutoStartHelper.IsEnabled();
        CollapseDelayBox.Text = cfg.CollapseDelaySeconds.ToString("0.#",
            System.Globalization.CultureInfo.InvariantCulture);
        ExpandDelayBox.Text = cfg.ExpandDelaySeconds.ToString("0.#",
            System.Globalization.CultureInfo.InvariantCulture);
        ThemeCombo.SelectedIndex = cfg.Theme switch
        { "Light" => 1, "Dark" => 2, _ => 0 };
        PanelModeCombo.SelectedIndex = cfg.FloatingPanelEnabled ? 1 : 0;
        FloatingMotionCombo.SelectedIndex = cfg.FloatingPanelFollowPointer ? 0 : 1;
        FloatingMotionCombo.IsEnabled = cfg.FloatingPanelEnabled;
        PanelEdgeAutoHideCheck.IsChecked = cfg.PanelEdgeAutoHideEnabled;
        SelectInspectionOption(InspectionGestureCombo, cfg.FloatingInspectionGesture, "Right");
        SelectInspectionOption(InspectionButtonCombo, cfg.FloatingInspectionButton, "Left");
        SelectInspectionOption(InspectionModifiersCombo, cfg.FloatingInspectionModifiers, "Alt");
        RefreshInspectionGestureControls();

        BgPathText.Text = cfg.BackgroundImagePath ?? "—";
        BgOpacitySlider.Value = Math.Clamp(cfg.BackgroundOverlayOpacity, 0.2, 1.0) * 100;
        BgOpacityValue.Text = $"{(int)BgOpacitySlider.Value}%";
        FrostCheck.IsChecked = cfg.FrostedGlassEnabled;
        FrostSlider.Value = Math.Clamp(cfg.FrostedGlassRadius, 1, 18);
        FrostValue.Text = $"{(int)FrostSlider.Value}px";
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
            cfg.SelectionAction switch { "Translate" => 1, "Both" => 2, _ => 0 };
        BubbleStyleCombo.SelectedIndex = 0;   // fallback: Default
        for (int i = 0; i < BubbleStyleCombo.Items.Count; i++)
            if (BubbleStyleCombo.Items[i] is ComboBoxItem bs &&
                (string?)bs.Tag == cfg.SelectionBubbleStyle)
            { BubbleStyleCombo.SelectedIndex = i; break; }
        TranslateEngineCombo.SelectedItem = TranslateEngineCombo.Items
            .OfType<ComboBoxItem>().FirstOrDefault(item => (string?)item.Tag == cfg.TranslationEngine);
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
        RefreshTranslationCredentials();
        AutoBackupIntervalCombo.IsEnabled = cfg.AutoBackupEnabled;
        AutoBackupIntervalCombo.Opacity = cfg.AutoBackupEnabled ? 1.0 : 0.4;
        RefreshSelectionActionUi();

        string ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
        AboutText.Text = $"KeeStashDock v{ver}\n© 2026 KeeganKei\nData: {_displayDataRoot}"
            + (_isAppDataFallback
               ? $"\n{LocalizationService.Instance["Settings.About.FallbackNote"]}" : "");
    }

    // ---------------------------------------------------------- language

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (LangCombo.SelectedItem is not LocalizationService.LanguageInfo lang) return;
        CancelTranslationTest();
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
        AboutText.Text = $"KeeStashDock v{ver}\n© 2026 KeeganKei\nData: {_displayDataRoot}";
    }

    // ------------------------------------------------------------ hotkey

    /// <summary>Record a modified key combination without stealing normal typing keys.</summary>
    private void OnHotkeyRecord(object sender, KeyEventArgs e)
    {
        if (TryReadHotkey(e, out uint mods, out uint vk))
            TryApplyHotkey(screenshot: false, mods, vk);
    }

    private void OnScreenshotHotkeyRecord(object sender, KeyEventArgs e)
    {
        if (TryReadHotkey(e, out uint mods, out uint vk))
            TryApplyHotkey(screenshot: true, mods, vk);
    }

    private static bool TryReadHotkey(KeyEventArgs e, out uint mods, out uint vk)
    {
        mods = vk = 0;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Tab && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift)
            return false; // Keep keyboard navigation available.
        e.Handled = true;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.ImeProcessed or Key.DeadCharProcessed)
            return false;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= NativeMethods.MOD_CONTROL;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= NativeMethods.MOD_ALT;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= NativeMethods.MOD_SHIFT;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= NativeMethods.MOD_WIN;
        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        return mods != 0 && vk is > 0 and <= 0xfe;
    }

    internal bool TryApplyHotkey(bool screenshot, uint mods, uint vk)
    {
        var cfg = _settings.Current;
        bool overlaps = screenshot
            ? mods == cfg.HotkeyModifiers && vk == cfg.HotkeyVirtualKey
            : cfg.ScreenshotHotkeyEnabled && mods == cfg.ScreenshotHotkeyModifiers && vk == cfg.ScreenshotHotkeyVirtualKey;
        HotkeyManager? manager = screenshot ? _screenshotHotkey : _hotkey;
        bool valid = mods != 0 && (mods & ~0x0fu) == 0 && vk is > 0 and <= 0xfe;
        bool ok = valid && !overlaps && manager is not null &&
            (screenshot && !cfg.ScreenshotHotkeyEnabled || manager.Rebind(mods, vk));
        (screenshot ? ScreenshotHotkeyStatus : HotkeyStatus).Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
        if (!ok) return false; // Rebind preserves the previous working registration.
        if (screenshot)
        {
            cfg.ScreenshotHotkeyModifiers = mods;
            cfg.ScreenshotHotkeyVirtualKey = vk;
            ScreenshotHotkeyBox.Text = FormatHotkey(mods, vk);
        }
        else
        {
            cfg.HotkeyModifiers = mods;
            cfg.HotkeyVirtualKey = vk;
            HotkeyBox.Text = FormatHotkey(mods, vk);
        }
        _settings.Save();
        return true;
    }

    private void OnScreenshotHotkeyEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var cfg = _settings.Current;
        bool enabled = ScreenshotHotkeyCheck.IsChecked == true;
        bool conflict = enabled &&
            (cfg.ScreenshotHotkeyModifiers == cfg.HotkeyModifiers && cfg.ScreenshotHotkeyVirtualKey == cfg.HotkeyVirtualKey ||
             _screenshotHotkey?.Initialize(cfg.ScreenshotHotkeyModifiers, cfg.ScreenshotHotkeyVirtualKey) != true);
        if (conflict)
        {
            _loading = true;
            ScreenshotHotkeyCheck.IsChecked = cfg.ScreenshotHotkeyEnabled;
            _loading = false;
            ScreenshotHotkeyStatus.Visibility = Visibility.Visible;
            return;
        }
        if (!enabled) _screenshotHotkey?.Disable();
        cfg.ScreenshotHotkeyEnabled = enabled;
        ScreenshotHotkeyBox.IsEnabled = _screenshotHotkey is not null;
        ScreenshotHotkeyStatus.Visibility = Visibility.Collapsed;
        _settings.Save();
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

    private void OnAutoStartChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        // Only this explicit checkbox action may update the Windows startup entry.
        bool enabled = AutoStartCheck.IsChecked == true;
        AutoStartHelper.SetEnabled(enabled);
        _settings.Current.AutoStart = enabled;
        _settings.Save();
    }

    private void OnBehaviorChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        CancelTranslationTest();
        _settings.Current.SelectionPopupEnabled = SelectionPopupCheck.IsChecked == true;
        RefreshSelectionActionUi();
        _settings.Save();
    }

    /// <summary>Commits the bubble action (Capture/Translate). The bubble
    /// itself relabels live via SettingsStore.Changed.</summary>
    private void OnSelectionActionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        CancelTranslationTest();
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
        if (_loading || _updatingTranslationCredentials) return;
        CancelTranslationTest();
        string? engine = (TranslateEngineCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        if (engine == "Baidu")
            _settings.Current.BaiduTranslationApiKey = ApiKeyBox.Text;
        else if (engine == "DeepL")
            _settings.Current.TranslationApiKey = ApiKeyBox.Text;
        else
            return;
        _settings.Save();
    }

    private void OnTranslationAppIdChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _updatingTranslationCredentials) return;
        CancelTranslationTest();
        _settings.Current.TranslationAppId = TranslationAppIdBox.Text;
        _settings.Save();
    }

    private void RefreshTranslationCredentials()
    {
        _updatingTranslationCredentials = true;
        try
        {
            ApiKeyBox.Text = _settings.Current.TranslationEngine switch
            {
                "Baidu" => _settings.Current.BaiduTranslationApiKey,
                "DeepL" => _settings.Current.TranslationApiKey,
                _ => string.Empty,
            };
            TranslationAppIdBox.Text = _settings.Current.TranslationAppId;
            AliyunAccessKeyIdBox.Text = _settings.Current.AliyunAccessKeyId;
            AliyunAccessKeySecretBox.Password = _settings.Current.AliyunAccessKeySecret;
            OfflineEndpointBox.Text = _settings.Current.OfflineTranslationEndpoint;
        }
        finally { _updatingTranslationCredentials = false; }
    }

    private void OnTranslateEngineChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        CancelTranslationTest();
        _settings.Current.TranslationEngine =
            (TranslateEngineCombo.SelectedItem as ComboBoxItem)?.Tag as string
            ?? AppSettings.DefaultTranslationEngine;
        _settings.Save();
        RefreshTranslationCredentials();
        RefreshSelectionActionUi();   // the API-key row follows the engine
    }

    private void OnAliyunCredentialsChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _updatingTranslationCredentials) return;
        CancelTranslationTest();
        _settings.Current.AliyunAccessKeyId = AliyunAccessKeyIdBox.Text;
        _settings.Save();
    }

    private void OnAliyunSecretChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || _updatingTranslationCredentials) return;
        CancelTranslationTest();
        _settings.Current.AliyunAccessKeySecret = AliyunAccessKeySecretBox.Password;
        _settings.Save();
    }

    private void OnOfflineEndpointChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _updatingTranslationCredentials) return;
        CancelTranslationTest();
        _settings.Current.OfflineTranslationEndpoint = OfflineEndpointBox.Text.Trim();
        _settings.Save();
    }

    private async void OnTranslationTestClick(object sender, RoutedEventArgs e)
        => await TestTranslationAsync();

    internal async Task TestTranslationAsync()
    {
        CancelTranslationTest();
        using var request = new CancellationTokenSource();
        _translationTestCancellation = request;
        TranslationTestButton.IsEnabled = false;
        TranslationTestStatus.Text = LocalizationService.Instance["Settings.TranslationTesting"];
        try
        {
            // A fixed public sample exercises the provider without reading the clipboard.
            bool useChineseSample = _settings.Current.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase)
                || (_settings.Current.Language == "zh-TW" && _settings.Current.TranslationEngine == "Aliyun");
            string sample = useChineseSample
                ? "\u8FD9\u662F\u4E00\u4E2A\u7FFB\u8BD1\u6D4B\u8BD5\u3002" : "This is a translation test.";
            TranslationResult result = await _translate(sample, _settings.Current, request.Token);
            if (request.IsCancellationRequested ||
                !ReferenceEquals(_translationTestCancellation, request)) return;
            TranslationTestStatus.Text = result.IsSuccess
                ? LocalizationService.Instance["Settings.TranslationTestSuccess"]
                : LocalizationService.Instance[TranslationService.FailureMessageKey(result)];
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            // Editing settings or closing the window invalidates the test result.
        }
        finally
        {
            if (ReferenceEquals(_translationTestCancellation, request))
            {
                _translationTestCancellation = null;
                RefreshSelectionActionUi();
            }
        }
    }

    private void CancelTranslationTest()
    {
        _translationTestCancellation?.Cancel();
        _translationTestCancellation = null;
        TranslationTestStatus.Text = string.Empty;
        TranslationTestButton.IsEnabled = SelectionPopupCheck.IsChecked == true;
    }

    /// <summary>Commits the bubble dress; bubble/toasts restyle live via
    /// SettingsStore.Changed.</summary>
    private void OnBubbleStyleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _settings.Current.SelectionBubbleStyle =
            (BubbleStyleCombo.SelectedItem as ComboBoxItem)?.Tag as string
            ?? AppSettings.DefaultSelectionBubbleStyle;
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
        // Screenshot translation also uses these providers, even with the
        // selection bubble disabled or in capture-only mode.
        const bool translate = true;
        TranslateEngineRow.Visibility =
            translate ? Visibility.Visible : Visibility.Collapsed;
        TranslateEngineCombo.IsEnabled = true;
        TranslateEngineCombo.Opacity = 1;
        string? engine = (TranslateEngineCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        TranslateEngineHint.Visibility = translate && engine is not ("Aliyun" or "Offline")
            ? Visibility.Visible : Visibility.Collapsed;
        AliyunSettingsPanel.Visibility = translate && engine == "Aliyun"
            ? Visibility.Visible : Visibility.Collapsed;
        OfflineSettingsPanel.Visibility = translate && engine == "Offline"
            ? Visibility.Visible : Visibility.Collapsed;
        AliyunSettingsPanel.IsEnabled = true;
        OfflineSettingsPanel.IsEnabled = true;
        TranslationTestPanel.Visibility = translate ? Visibility.Visible : Visibility.Collapsed;
        TranslationTestButton.IsEnabled = _translationTestCancellation is null;
        ApiKeyRow.Visibility = translate && engine is "DeepL" or "Baidu"
            ? Visibility.Visible : Visibility.Collapsed;
        TranslationAppIdRow.Visibility = translate && engine == "Baidu"
            ? Visibility.Visible : Visibility.Collapsed;
        TranslationAppIdBox.IsEnabled = true;
        TranslationAppIdBox.Opacity = 1;
        ApiKeyBox.IsEnabled = true;
        ApiKeyBox.Opacity = 1;
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

    /// <summary>Start a replacement that waits for this process to exit before
    /// claiming the single-instance mutex, then shut down the current instance.</summary>
    private async void OnRestartClick(object sender, RoutedEventArgs e)
    {
        if (_shutdownPending) return;
        _shutdownPending = true;
        IsEnabled = false;
        try
        {
            // Save/cancel editor drafts and stop OCR before scheduling a relaunch.
            if (!await _prepareForShutdown()) return;
            if (_restarter.TryRestart()) Application.Current.Shutdown();
            else
                MessageBox.Show(this,
                    "KeeStashDock could not restart automatically. Please exit and start it again.",
                    "KeeStashDock", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _shutdownPending = false;
            if (!_databaseImport.AreOperationsDisabled) IsEnabled = true;
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

    private void OnPanelModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        string? mode = (PanelModeCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        if (mode is not ("Classic" or "Floating")) return;
        bool enabled = mode == "Floating";
        FloatingMotionCombo.IsEnabled = enabled;
        RefreshInspectionGestureControls();
        if (_settings.Current.FloatingPanelEnabled == enabled) return;
        _settings.Current.FloatingPanelEnabled = enabled;
        _settings.Save();
    }

    private void OnFloatingMotionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || !_settings.Current.FloatingPanelEnabled) return;
        string? mode = (FloatingMotionCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        if (mode is not ("Follow" or "Fixed")) return;
        bool follow = mode == "Follow";
        RefreshInspectionGestureControls();
        if (_settings.Current.FloatingPanelFollowPointer == follow) return;
        _settings.Current.FloatingPanelFollowPointer = follow;
        _settings.Save();
    }

    private static void SelectInspectionOption(ComboBox combo, string value, string fallback)
        => combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string?)item.Tag == value)
            ?? combo.Items.OfType<ComboBoxItem>().Single(item => (string?)item.Tag == fallback);

    private void RefreshInspectionGestureControls()
    {
        if (InspectionGesturePanel is null || InspectionCustomRow is null) return;
        bool enabled = (PanelModeCombo.SelectedItem as ComboBoxItem)?.Tag as string == "Floating" &&
            (FloatingMotionCombo.SelectedItem as ComboBoxItem)?.Tag as string == "Follow";
        InspectionGesturePanel.IsEnabled = enabled;
        InspectionCustomRow.Visibility = (InspectionGestureCombo.SelectedItem as ComboBoxItem)?.Tag as string == "Custom"
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnInspectionGestureChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshInspectionGestureControls();
        if (_loading || InspectionGestureCombo?.SelectedItem is not ComboBoxItem gesture ||
            InspectionButtonCombo?.SelectedItem is not ComboBoxItem button ||
            InspectionModifiersCombo?.SelectedItem is not ComboBoxItem modifiers) return;
        var cfg = _settings.Current;
        cfg.FloatingInspectionGesture = (string)gesture.Tag;
        cfg.FloatingInspectionButton = (string)button.Tag;
        cfg.FloatingInspectionModifiers = (string)modifiers.Tag;
        _settings.Save();
    }

    private void OnPanelEdgeAutoHideChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.Current.PanelEdgeAutoHideEnabled = PanelEdgeAutoHideCheck.IsChecked == true;
        _settings.Save();
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

    /// <summary>Frost and opacity controls share one handler;
    /// SettingsStore.Changed hot-applies the result to the panel.</summary>
    private void OnEffectsChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var cfg = _settings.Current;
        cfg.FrostedGlassEnabled = FrostCheck.IsChecked == true;
        cfg.FrostedGlassRadius = FrostSlider.Value;
        cfg.CardOpacity = CardOpacitySlider.Value / 100.0;
        _settings.Save();
        FrostValue.Text = $"{(int)FrostSlider.Value}px";
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
        if (dlg.ShowDialog(this) != true) return;
        if (!TryExportPackage(dlg.FileName))
            MessageBox.Show(this, LocalizationService.Instance["Settings.Backup.ExportFailed"], "KeeStashDock",
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    internal bool TryExportPackage(string fileName)
    {
        try { _backup.ExportPackage(fileName); return true; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or ArgumentException or Microsoft.Data.Sqlite.SqliteException)
        {
            App.Log?.Warn($"Database export failed: {exception.GetType().Name}");
            return false;
        }
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        if (_shutdownPending) return;
        _shutdownPending = true;
        try
        {
            var dlg = new OpenFileDialog
            { Filter = "KeeStashDock package (*.ksdpkg)|*.ksdpkg|SQLite database (*.db)|*.db" };
            if (dlg.ShowDialog(this) != true) return;

            var loc = LocalizationService.Instance;
            if (MessageBox.Show(this, loc["Settings.Backup.Import.Confirm"], loc["Common.Confirm"],
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            IsEnabled = false;
            // This must finish before ImportAsync can replace the active database.
            if (!await _prepareForShutdown()) return;
            await _databaseImport.ImportAsync(dlg.FileName);
            Application.Current.Shutdown();
        }
        catch (DatabaseImportException exception)
        {
            MessageBox.Show(this, exception.Message, "KeeStashDock",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _shutdownPending = false;
            if (!_databaseImport.AreOperationsDisabled) IsEnabled = true;
        }
    }

}
