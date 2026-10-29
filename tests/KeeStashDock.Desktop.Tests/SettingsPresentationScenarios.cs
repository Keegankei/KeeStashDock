using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Desktop.Views;
using KeeStashDock.Infrastructure.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

internal static class SettingsPresentationScenarios
{
    internal static void Verify(SettingsWindow window, SettingsStore settings, string settingsFile)
    {
        VerifyNavigation(window, settings);
        FontSizeSettingsScenarios.Verify(window, settings, settingsFile);
        VerifyResultOptions(window, settingsFile);
        VerifySelectionDependencies(window, settings);
        VerifyHotkeyDependencies(window, settings);
        VerifyAppearanceDependencies(window, settings);
        VerifyBackupDependencies(window, settings);
        VerifyBackupFolder(window, settings, settingsFile);
        window.SelectSettingsPage("General");
    }

    private static void VerifyNavigation(SettingsWindow window, SettingsStore settings)
    {
        string[] pages = ["General", "Translation", "Appearance", "Components", "Data", "About"];
        int writes = 0;
        void Changed() => writes++;
        settings.Changed += Changed;
        try
        {
            foreach (string selected in pages)
            {
                window.SelectSettingsPage(selected);
                Assert.AreEqual(selected, ((ListBoxItem)Named<ListBox>(window, "SettingsNavigation").SelectedItem).Tag);
                foreach (string page in pages)
                    Assert.AreEqual(page == selected ? Visibility.Visible : Visibility.Collapsed,
                        Named<ScrollViewer>(window, page + "Page").Visibility,
                        "Category navigation must show exactly one page without losing the other controls.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(Named<TextBlock>(window, "SettingsPageTitle").Text));
            }
            Assert.AreEqual(0, writes, "Browsing categories must not overwrite user preferences.");
        }
        finally { settings.Changed -= Changed; }
    }

    private static void VerifyResultOptions(SettingsWindow window, string settingsFile)
    {
        window.SelectSettingsPage("Translation");
        var target = Named<CheckBox>(window, "ResultTargetCheck");
        var save = Named<CheckBox>(window, "ResultSaveCheck");
        target.IsChecked = true;
        save.IsChecked = true;
        target.IsChecked = false;
        AssertSavedResultOptions(settingsFile, false, true);
        target.IsChecked = true;
        save.IsChecked = false;
        AssertSavedResultOptions(settingsFile, true, false);
        target.IsChecked = false;
        AssertSavedResultOptions(settingsFile, false, false);
        target.IsChecked = true;
        save.IsChecked = true;
        AssertSavedResultOptions(settingsFile, true, true);
    }

    private static void AssertSavedResultOptions(string settingsFile, bool target, bool save)
    {
        var reloaded = new SettingsStore(settingsFile);
        reloaded.Load();
        Assert.AreEqual(target, reloaded.Current.TranslationResultShowTargetSelector);
        Assert.AreEqual(save, reloaded.Current.TranslationResultShowSavePanel);
    }

    private static void VerifySelectionDependencies(SettingsWindow window, SettingsStore settings)
    {
        var enabled = Named<CheckBox>(window, "SelectionPopupCheck");
        enabled.IsChecked = true;
        Select(window, "SelectionActionCombo", "Both");
        enabled.IsChecked = false;
        Assert.AreEqual(Visibility.Collapsed, Named<FrameworkElement>(window, "SelectionActionRow").Visibility);
        Assert.IsFalse(Named<ComboBox>(window, "SelectionActionCombo").IsEnabled);
        Assert.AreEqual("Both", settings.Current.SelectionAction);
        foreach (string name in new[] { "TranslateEngineCombo", "TranslationTargetCombo", "TranslationTestButton",
            "BubbleStyleCombo", "BubbleOpacitySlider" })
            Assert.IsTrue(Named<UIElement>(window, name).IsEnabled,
                "Screenshot translation and shared result appearance must remain available without selection monitoring.");
        enabled.IsChecked = true;
        Assert.AreEqual(Visibility.Visible, Named<FrameworkElement>(window, "SelectionActionRow").Visibility);
        Assert.AreEqual("Both", ((ComboBoxItem)Named<ComboBox>(window, "SelectionActionCombo").SelectedItem).Tag);
    }

    private static void VerifyHotkeyDependencies(SettingsWindow window, SettingsStore settings)
    {
        var enabled = Named<CheckBox>(window, "ScreenshotHotkeyCheck");
        enabled.IsChecked = false;
        Assert.AreEqual(Visibility.Collapsed, Named<FrameworkElement>(window, "ScreenshotHotkeyEditor").Visibility);
        uint modifiers = settings.Current.ScreenshotHotkeyModifiers;
        uint key = settings.Current.ScreenshotHotkeyVirtualKey;
        Assert.IsFalse(window.TryApplyHotkey(true, settings.Current.HotkeyModifiers, settings.Current.HotkeyVirtualKey));
        Assert.AreEqual(Visibility.Visible, Named<FrameworkElement>(window, "ScreenshotHotkeyEditor").Visibility,
            "A failed shortcut must expose its repair controls even when the shortcut is disabled.");
        Assert.AreEqual(key, settings.Current.ScreenshotHotkeyVirtualKey);
        Assert.IsTrue(window.TryApplyHotkey(true, modifiers, key));
        Assert.AreEqual(Visibility.Collapsed, Named<FrameworkElement>(window, "ScreenshotHotkeyEditor").Visibility);
        enabled.IsChecked = true;
        Assert.AreEqual(Visibility.Visible, Named<FrameworkElement>(window, "ScreenshotHotkeyEditor").Visibility);
    }

    private static void VerifyAppearanceDependencies(SettingsWindow window, SettingsStore settings)
    {
        window.SelectSettingsPage("Appearance");
        Select(window, "PanelModeCombo", "Classic");
        Assert.AreEqual(Visibility.Collapsed, Named<FrameworkElement>(window, "FloatingMotionRow").Visibility);
        Assert.AreEqual(Visibility.Collapsed, Named<FrameworkElement>(window, "FloatingMotionHint").Visibility);
        Assert.AreEqual(Visibility.Collapsed, Named<FrameworkElement>(window, "InspectionGesturePanel").Visibility);
        Select(window, "PanelModeCombo", "Floating");
        Select(window, "FloatingMotionCombo", "Follow");
        Select(window, "InspectionGestureCombo", "Custom");
        Select(window, "InspectionModifiersCombo", "Control+Shift");
        Select(window, "InspectionButtonCombo", "XButton1");
        Assert.AreEqual(Visibility.Visible, Named<FrameworkElement>(window, "FloatingMotionRow").Visibility);
        Assert.AreEqual(Visibility.Visible, Named<FrameworkElement>(window, "InspectionGesturePanel").Visibility);
        Assert.AreEqual(Visibility.Visible, Named<FrameworkElement>(window, "InspectionCustomRow").Visibility);
        Select(window, "FloatingMotionCombo", "Fixed");
        Assert.AreEqual(Visibility.Collapsed, Named<FrameworkElement>(window, "InspectionGesturePanel").Visibility);
        Select(window, "PanelModeCombo", "Classic");
        Select(window, "PanelModeCombo", "Floating");
        Assert.IsFalse(settings.Current.FloatingPanelFollowPointer);
        Assert.AreEqual(Visibility.Collapsed, Named<FrameworkElement>(window, "InspectionGesturePanel").Visibility);
        Select(window, "FloatingMotionCombo", "Follow");
        Assert.AreEqual(Visibility.Visible, Named<FrameworkElement>(window, "InspectionCustomRow").Visibility);
        Select(window, "InspectionGestureCombo", "Right");
        Assert.AreEqual(Visibility.Collapsed, Named<FrameworkElement>(window, "InspectionCustomRow").Visibility);
        Assert.AreEqual("Control+Shift", settings.Current.FloatingInspectionModifiers);
        Assert.AreEqual("XButton1", settings.Current.FloatingInspectionButton);

        Select(window, "TextColorCombo", "Custom");
        var color = Named<TextBox>(window, "CustomColorBox");
        color.Text = "#4267AA";
        color.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        Select(window, "TextColorCombo", "Auto");
        Assert.AreEqual(Visibility.Collapsed, Named<FrameworkElement>(window, "CustomColorContainer").Visibility);
        Assert.AreEqual("#4267AA", settings.Current.PanelTextCustomColor);
        Select(window, "TextColorCombo", "Custom");
        Assert.AreEqual(Visibility.Visible, Named<FrameworkElement>(window, "CustomColorContainer").Visibility);
        Assert.AreEqual("#4267AA", color.Text);

        string? previousBackground = settings.Current.BackgroundImagePath;
        try
        {
            settings.Current.BackgroundImagePath = null;
            window.RefreshConditionalOptions();
            Assert.AreEqual(Visibility.Collapsed, Named<FrameworkElement>(window, "BackgroundOptions").Visibility);
            settings.Current.BackgroundImagePath = Path.Combine(Path.GetTempPath(), "synthetic-settings-background.png");
            window.RefreshConditionalOptions();
            Assert.AreEqual(Visibility.Visible, Named<FrameworkElement>(window, "BackgroundOptions").Visibility);
            var frost = Named<CheckBox>(window, "FrostCheck");
            frost.IsChecked = true;
            Named<Slider>(window, "FrostSlider").Value = 11;
            Assert.AreEqual(Visibility.Visible, Named<FrameworkElement>(window, "FrostRadiusRow").Visibility);
            frost.IsChecked = false;
            Assert.AreEqual(Visibility.Collapsed, Named<FrameworkElement>(window, "FrostRadiusRow").Visibility);
            Assert.AreEqual(11d, settings.Current.FrostedGlassRadius);
            frost.IsChecked = true;
            settings.Current.BackgroundImagePath = null;
            window.RefreshConditionalOptions();
            Assert.AreEqual(Visibility.Collapsed, Named<FrameworkElement>(window, "BackgroundOptions").Visibility);
            Assert.IsTrue(settings.Current.FrostedGlassEnabled);
            Assert.AreEqual(11d, settings.Current.FrostedGlassRadius,
                "Hiding background options must preserve the user's blur preference.");
        }
        finally
        {
            settings.Current.BackgroundImagePath = previousBackground;
            settings.Save();
            window.RefreshConditionalOptions();
        }
    }

    private static void VerifyBackupDependencies(SettingsWindow window, SettingsStore settings)
    {
        window.SelectSettingsPage("Data");
        var enabled = Named<CheckBox>(window, "AutoBackupCheck");
        enabled.IsChecked = true;
        Select(window, "AutoBackupIntervalCombo", "30");
        Select(window, "BackupKeepCountCombo", "30");
        Assert.AreEqual(Visibility.Visible, Named<FrameworkElement>(window, "BackupScheduleOptions").Visibility);
        enabled.IsChecked = false;
        Assert.AreEqual(Visibility.Collapsed, Named<FrameworkElement>(window, "BackupScheduleOptions").Visibility);
        Assert.AreEqual(30, settings.Current.AutoBackupIntervalMinutes);
        Assert.AreEqual(30, settings.Current.BackupKeepCount);
        Assert.IsTrue(Named<Button>(window, "BackupNowButton").IsEnabled);
        Assert.AreEqual(Visibility.Visible, Named<TextBlock>(window, "BackupStatusText").Visibility);
        Assert.AreEqual(Visibility.Visible, Named<ListBox>(window, "BackupSnapshotList").Visibility);
        enabled.IsChecked = true;
        Assert.AreEqual("30", ((ComboBoxItem)Named<ComboBox>(window, "AutoBackupIntervalCombo").SelectedItem).Tag);
        Assert.AreEqual("30", ((ComboBoxItem)Named<ComboBox>(window, "BackupKeepCountCombo").SelectedItem).Tag);
    }

    private static void Select(SettingsWindow window, string name, string tag)
    {
        var combo = Named<ComboBox>(window, name);
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().Single(item => (string?)item.Tag == tag);
    }

    private static void VerifyBackupFolder(SettingsWindow window, SettingsStore settings, string settingsFile)
    {
        window.SelectSettingsPage("Data");
        string defaultDirectory = Named<TextBox>(window, "BackupPathText").Text;
        string customDirectory = Path.Combine(Path.GetDirectoryName(settingsFile)!, "chosen-backups");
        var list = Named<ListBox>(window, "BackupSnapshotList");
        var now = Named<Button>(window, "BackupNowButton");
        string CreateSnapshot()
        {
            var before = list.Items.OfType<BackupSnapshot>().Select(snapshot => snapshot.Path).ToHashSet();
            now.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => now.IsEnabled && list.Items.OfType<BackupSnapshot>().Any(snapshot => !before.Contains(snapshot.Path)));
            return list.Items.OfType<BackupSnapshot>().Single(snapshot => !before.Contains(snapshot.Path)).Path;
        }
        string originalSnapshot = CreateSnapshot();
        byte[] originalBytes = File.ReadAllBytes(originalSnapshot);
        PumpUntilCompleted(window.ChangeFolderAsync("Backup", customDirectory));
        Assert.AreEqual(customDirectory, settings.Current.BackupDirectory);
        Assert.AreEqual(customDirectory, Named<TextBox>(window, "BackupPathText").Text);
        Assert.Contains(customDirectory, Named<TextBlock>(window, "BackupStatusText").Text);
        Assert.AreEqual(0, list.Items.Count, "A new folder must not list snapshots from the previous location.");
        var saved = new SettingsStore(settingsFile);
        saved.Load();
        Assert.AreEqual(customDirectory, saved.Current.BackupDirectory);
        CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(originalSnapshot));
        string customSnapshot = CreateSnapshot();

        PumpUntilCompleted(window.ChangeFolderAsync("Backup", "relative-folder"));
        Assert.AreEqual(customDirectory, settings.Current.BackupDirectory);
        Assert.AreEqual(customDirectory, Named<TextBox>(window, "BackupPathText").Text);
        Assert.AreEqual(LocalizationService.Instance["Settings.Paths.Error"], Named<TextBlock>(window, "FolderStatus").Text);
        Assert.IsTrue(Named<WrapPanel>(window, "BackupPathActions").IsEnabled);
        saved.Load();
        Assert.AreEqual(customDirectory, saved.Current.BackupDirectory);
        Assert.IsTrue(list.Items.OfType<BackupSnapshot>().Any(snapshot => snapshot.Path == customSnapshot));

        PumpUntilCompleted(window.ChangeFolderAsync("Backup", null));
        Assert.IsNull(settings.Current.BackupDirectory);
        saved.Load();
        Assert.IsNull(saved.Current.BackupDirectory);
        Assert.AreEqual(defaultDirectory, Named<TextBox>(window, "BackupPathText").Text);
        Assert.Contains(defaultDirectory, Named<TextBlock>(window, "BackupStatusText").Text);
        Assert.IsTrue(list.Items.OfType<BackupSnapshot>().Any(snapshot => snapshot.Path == originalSnapshot));
        CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(originalSnapshot));
        Assert.IsTrue(File.Exists(customSnapshot), "Resetting the folder must preserve backups made in the custom location.");
    }

    private static void PumpUntilCompleted(Task task)
    {
        PumpUntil(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    private static void PumpUntil(Func<bool> completed)
    {
        if (completed()) return;
        var frame = new DispatcherFrame();
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (completed() || DateTime.UtcNow >= deadline) frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Assert.IsTrue(completed(), "The isolated backup folder operation did not finish.");
    }

    private static T Named<T>(SettingsWindow window, string name) where T : DependencyObject
    {
        object? element = window.FindName(name);
        Assert.IsInstanceOfType<T>(element, $"Settings control '{name}' was not found.");
        return (T)element;
    }
}
