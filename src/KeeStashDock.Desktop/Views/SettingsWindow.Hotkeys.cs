using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KeeStashDock.Desktop.Interop;
using KeeStashDock.Desktop.Localization;

namespace KeeStashDock.Desktop.Views;

public partial class SettingsWindow
{
    private HotkeyRegistrationStatus? _panelHotkeyRejection;
    private HotkeyRegistrationStatus? _screenshotHotkeyRejection;

    private void ConfigureHotkeyFeedback()
    {
        _hotkey.RegistrationChanged += () => { _panelHotkeyRejection = null; RefreshHotkeyStatuses(); };
        if (_screenshotHotkey is not null)
            _screenshotHotkey.RegistrationChanged += () => { _screenshotHotkeyRejection = null; RefreshHotkeyStatuses(); };
        LocalizationService.Instance.LanguageChanged += RefreshHotkeyStatuses;
    }

    private void PopulateHotkeys()
    {
        var cfg = _settings.Current;
        HotkeyBox.Text = FormatHotkey(cfg.HotkeyModifiers, cfg.HotkeyVirtualKey);
        ScreenshotHotkeyCheck.IsChecked = cfg.ScreenshotHotkeyEnabled;
        ScreenshotHotkeyCheck.IsEnabled = _screenshotHotkey is not null;
        ScreenshotHotkeyBox.Text = FormatHotkey(cfg.ScreenshotHotkeyModifiers, cfg.ScreenshotHotkeyVirtualKey);
        ScreenshotHotkeyBox.IsEnabled = _screenshotHotkey is not null;
        RefreshHotkeyStatuses();
    }

    private void RefreshHotkeyStatuses()
    {
        SetHotkeyStatus(HotkeyStatus, _panelHotkeyRejection ?? _hotkey.RegistrationStatus);
        var screenshot = _screenshotHotkeyRejection ?? _screenshotHotkey?.RegistrationStatus ?? new();
        SetHotkeyStatus(ScreenshotHotkeyStatus, screenshot);
        if (!_settings.Current.ScreenshotHotkeyEnabled && screenshot.HasFailure)
            ScreenshotHotkeyStatus.Text = LocalizationService.Instance["Hotkeys.Status.Disabled"] + " " + ScreenshotHotkeyStatus.Text;
        ScreenshotHotkeyEditor.Visibility = _settings.Current.ScreenshotHotkeyEnabled || screenshot.HasFailure
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void SetHotkeyStatus(TextBlock text, HotkeyRegistrationStatus status)
    {
        text.Text = status.Describe();
        text.SetResourceReference(TextBlock.ForegroundProperty, status.HasFailure ? "B.Danger" : "B.TextMuted");
    }

    public void ShowHotkeys()
    {
        SelectSettingsPage("General");
        ShowAndActivate();
        Dispatcher.BeginInvoke(new Action(() => { HotkeysSection.BringIntoView(); HotkeyBox.Focus(); }));
    }

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
            return false;
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
        if (screenshot) _screenshotHotkeyRejection = null;
        else _panelHotkeyRejection = null;
        bool ok = valid && !overlaps && manager is not null &&
            (screenshot && !cfg.ScreenshotHotkeyEnabled || manager.Rebind(mods, vk));
        if (!ok)
        {
            var rejected = new HotkeyRegistrationStatus(manager?.RegistrationStatus.Active, new(mods, vk));
            if (screenshot) _screenshotHotkeyRejection = rejected;
            else _panelHotkeyRejection = rejected;
            RefreshHotkeyStatuses();
            return false; // Rebind preserves the previous working registration.
        }
        if (screenshot)
        {
            if (!cfg.ScreenshotHotkeyEnabled) manager!.Disable();
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
        RefreshHotkeyStatuses();
        _settings.Save();
        return true;
    }

    private void OnScreenshotHotkeyEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var cfg = _settings.Current;
        bool enabled = ScreenshotHotkeyCheck.IsChecked == true;
        _screenshotHotkeyRejection = null;
        bool conflict = enabled &&
            (cfg.ScreenshotHotkeyModifiers == cfg.HotkeyModifiers && cfg.ScreenshotHotkeyVirtualKey == cfg.HotkeyVirtualKey ||
             _screenshotHotkey?.Initialize(cfg.ScreenshotHotkeyModifiers, cfg.ScreenshotHotkeyVirtualKey) != true);
        if (conflict)
        {
            _loading = true;
            ScreenshotHotkeyCheck.IsChecked = cfg.ScreenshotHotkeyEnabled;
            _loading = false;
            _screenshotHotkeyRejection = new(_screenshotHotkey?.RegistrationStatus.Active,
                new(cfg.ScreenshotHotkeyModifiers, cfg.ScreenshotHotkeyVirtualKey));
            RefreshHotkeyStatuses();
            return;
        }
        if (!enabled) _screenshotHotkey?.Disable();
        cfg.ScreenshotHotkeyEnabled = enabled;
        RefreshHotkeyStatuses();
        _settings.Save();
    }

    private static string FormatHotkey(uint mods, uint vk) => new HotkeyCombination(mods, vk).ToString();
}
