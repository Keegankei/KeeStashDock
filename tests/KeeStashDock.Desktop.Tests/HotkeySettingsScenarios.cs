using System.Windows;
using System.Windows.Controls;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Desktop.Views;
using KeeStashDock.Infrastructure.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

internal static class HotkeySettingsScenarios
{
    internal static void Verify(SettingsWindow window, SettingsStore settings,
        HotkeyManager panel, HotkeyManager screenshot)
    {
        var cfg = settings.Current;
        var panelStatus = (TextBlock)window.FindName("HotkeyStatus");
        var screenshotStatus = (TextBlock)window.FindName("ScreenshotHotkeyStatus");
        var enabled = (CheckBox)window.FindName("ScreenshotHotkeyCheck");
        var originalPanel = new HotkeyCombination(cfg.HotkeyModifiers, cfg.HotkeyVirtualKey);
        var originalScreenshot = new HotkeyCombination(cfg.ScreenshotHotkeyModifiers, cfg.ScreenshotHotkeyVirtualKey);

        panel.Disable();
        Assert.IsFalse(panel.Rebind(originalScreenshot.Modifiers, originalScreenshot.VirtualKey));
        Assert.AreEqual(Visibility.Visible, panelStatus.Visibility);
        Assert.AreEqual(panel.RegistrationStatus.Describe(), panelStatus.Text,
            "A panel registration failure must remain visible in Settings after its toast disappears.");
        Assert.Contains(originalScreenshot.ToString(), panelStatus.Text);
        Assert.IsTrue(window.TryApplyHotkey(false, originalPanel.Modifiers, originalPanel.VirtualKey));
        Assert.AreEqual(panel.RegistrationStatus.Describe(), panelStatus.Text);
        Assert.IsFalse(panel.RegistrationStatus.HasFailure);

        screenshot.Disable();
        Assert.IsFalse(screenshot.Rebind(originalPanel.Modifiers, originalPanel.VirtualKey));
        Assert.AreEqual(screenshot.RegistrationStatus.Describe(), screenshotStatus.Text);
        enabled.IsChecked = false;
        Assert.AreEqual(LocalizationService.Instance["Hotkeys.Status.Disabled"], screenshotStatus.Text);
        Assert.IsFalse(screenshot.RegistrationStatus.HasFailure);
        enabled.IsChecked = true;
        Assert.IsTrue(screenshot.IsRegistered);
        Assert.AreEqual(screenshot.RegistrationStatus.Describe(), screenshotStatus.Text);

        Assert.IsFalse(window.TryApplyHotkey(false, originalScreenshot.Modifiers, originalScreenshot.VirtualKey));
        Assert.Contains(originalScreenshot.ToString(), panelStatus.Text);
        Assert.Contains(originalPanel.ToString(), panelStatus.Text,
            "A failed rebind must name the attempted shortcut and the previous shortcut that remains active.");
        Assert.AreEqual(originalPanel.VirtualKey, cfg.HotkeyVirtualKey);
        Assert.IsTrue(panel.IsRegistered);
        Assert.IsTrue(screenshot.IsRegistered);
        Assert.IsTrue(window.TryApplyHotkey(false, originalPanel.Modifiers, originalPanel.VirtualKey));
        Assert.AreEqual(panel.RegistrationStatus.Describe(), panelStatus.Text);
    }
}
