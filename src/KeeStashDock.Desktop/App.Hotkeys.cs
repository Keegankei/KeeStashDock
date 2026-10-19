using System.Windows.Threading;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Infrastructure.Storage;

namespace KeeStashDock.Desktop;

public partial class App
{
    private readonly HotkeyConflictNotifications _hotkeyNotifications = new();
    private bool _hotkeyNotificationQueued;

    private void InitializeHotkeys(SettingsStore settings)
    {
        _hotkey = new HotkeyManager();
        _screenshotHotkey = new HotkeyManager();
        _hotkey.RegistrationChanged += () => ObserveHotkeyStatus(HotkeyAction.Panel, _hotkey);
        _screenshotHotkey.RegistrationChanged += () => ObserveHotkeyStatus(HotkeyAction.Screenshot, _screenshotHotkey);
        _hotkey.Initialize(settings.Current.HotkeyModifiers, settings.Current.HotkeyVirtualKey);
        if (settings.Current.ScreenshotHotkeyEnabled)
            _screenshotHotkey.Initialize(settings.Current.ScreenshotHotkeyModifiers, settings.Current.ScreenshotHotkeyVirtualKey);
        else _screenshotHotkey.Initialize(0, 0);
        _hotkey.HotkeyPressed += TogglePanel;
        _screenshotHotkey.HotkeyPressed += CaptureScreenshot;
    }

    private void ObserveHotkeyStatus(HotkeyAction action, HotkeyManager manager)
    {
        _hotkeyNotifications.Observe(action, manager.RegistrationStatus);
        if (manager.RegistrationStatus.HasFailure)
            Log?.Warn($"{action} hotkey could not be registered; previous binding retained if available.");
        if (_hotkeyNotificationQueued || _exitPending || Dispatcher.HasShutdownStarted) return;
        _hotkeyNotificationQueued = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _hotkeyNotificationQueued = false;
            var failures = _hotkeyNotifications.TakePending();
            if (_exitPending || failures.Count == 0) return;
            var loc = LocalizationService.Instance;
            string details = string.Join("\n", failures.Select(failure =>
                $"{loc[$"Hotkeys.Action.{failure.Action}"]}: {failure.Status.FailedAttempt}"));
            _tray.ShowActionableWarning(loc["Hotkeys.Notification.Title"],
                details + "\n\n" + loc["Hotkeys.Notification.OpenSettings"], () =>
                {
                    ShowSettings();
                    _settingsWin?.ShowHotkeys();
                });
        }), DispatcherPriority.Background);
    }
}
