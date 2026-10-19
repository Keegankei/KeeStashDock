using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using KeeStashDock.Core.Services;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Desktop.Views;
using KeeStashDock.Infrastructure.Database;
using KeeStashDock.Infrastructure.Logging;
using KeeStashDock.Infrastructure.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

internal static class BackupSettingsScenarios
{
    internal static void Verify(string isolatedRoot, FileLogger logger)
    {
        string root = Path.Combine(isolatedRoot, "backup-settings");
        Directory.CreateDirectory(root);
        string settingsFile = Path.Combine(root, "settings.json");
        File.WriteAllText(settingsFile, """{"autoBackupIntervalHours":3,"backupKeepCount":17}""");
        var settings = new SettingsStore(settingsFile);
        settings.Load();
        var maintenance = new DatabaseMaintenanceCoordinator();
        var database = new Db(Path.Combine(root, "library.db"), maintenance);
        new MigrationRunner(database, logger).Run();
        var backup = new BackupService(database, logger, maintenance: maintenance);
        // A valid baseline makes starting the real scheduler safe: no automatic
        // snapshot is due during this quick isolated UI scenario.
        Assert.AreEqual(BackupAttemptOutcome.Succeeded, backup.BackUpNowAsync().GetAwaiter().GetResult().Outcome);
        using var scheduler = new AutomaticBackupScheduler(backup, settings, maintenance);
        scheduler.Start();
        var restarter = new NoRestart();
        var importer = new DatabaseImportService(database, maintenance, backup,
            new DatabaseSchemaValidator(), new PhysicalDatabaseFileOperations(), restarter, logger);
        using var panelHotkey = new HotkeyManager((_, _, _) => true, _ => { });
        panelHotkey.Initialize(1, 0x51);
        int writes = 0;
        settings.Changed += () => writes++;
        string originalLanguage = LocalizationService.Instance.CurrentLanguage;
        LocalizationService.Instance.SetLanguage("en-US");
        var window = new SettingsWindow(settings, backup, importer, restarter, panelHotkey,
            displayDataRoot: root, isAppDataFallback: false);
        try
        {
            var interval = Named<ComboBox>(window, "AutoBackupIntervalCombo");
            var keep = Named<ComboBox>(window, "BackupKeepCountCombo");
            var status = Named<TextBlock>(window, "BackupStatusText");
            var list = Named<ListBox>(window, "BackupSnapshotList");
            var now = Named<Button>(window, "BackupNowButton");
            var restore = Named<Button>(window, "RestoreBackupButton");
            Assert.AreEqual("180", ((ComboBoxItem)interval.SelectedItem).Tag,
                "A valid non-preset legacy interval must remain selected.");
            Assert.AreEqual("17", ((ComboBoxItem)keep.SelectedItem).Tag);
            Assert.AreEqual(0, writes, "Opening backup settings must not replace custom preferences.");
            Assert.AreEqual(1, list.Items.Count);
            Assert.IsTrue(now.IsEnabled);
            Assert.IsFalse(restore.IsEnabled);
            Assert.IsTrue(status.Text.Contains(backup.BackupsDirectory, StringComparison.Ordinal));
            Assert.IsTrue(status.Text.Contains(backup.Status.NextCheckUtc!.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), StringComparison.Ordinal));

            interval.SelectedItem = interval.Items.OfType<ComboBoxItem>().Single(item => (string?)item.Tag == "15");
            keep.SelectedItem = keep.Items.OfType<ComboBoxItem>().Single(item => (string?)item.Tag == "100");
            Assert.AreEqual(15, settings.Current.AutoBackupIntervalMinutes);
            Assert.AreEqual(100, settings.Current.BackupKeepCount);
            var reloaded = new SettingsStore(settingsFile);
            reloaded.Load();
            Assert.AreEqual(15, reloaded.Current.AutoBackupIntervalMinutes);
            Assert.AreEqual(100, reloaded.Current.BackupKeepCount);
            Assert.AreEqual(backup.Status.LastSuccessUtc!.Value.AddMinutes(15), backup.Status.NextCheckUtc);
            Assert.IsTrue(Named<TextBlock>(window, "BackupRetentionText").Text.Contains("24.75", StringComparison.Ordinal));

            now.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.IsFalse(now.IsEnabled, "Manual snapshot work must disable repeated clicks while yielding to the UI.");
            PumpUntil(() => now.IsEnabled && list.Items.Count == 2);
            Assert.AreEqual(BackupRunState.Succeeded, backup.Status.State);
            Assert.IsTrue(status.Text.Contains(LocalizationService.Instance["Settings.Backup.State.Succeeded"], StringComparison.Ordinal));
            list.SelectedIndex = 0;
            Assert.IsTrue(restore.IsEnabled, "Selecting a known snapshot should expose the existing guarded restore action.");
            Assert.AreEqual(0, restarter.Attempts, "Inspecting snapshots must not restore data or restart the app.");

            Named<CheckBox>(window, "AutoBackupCheck").IsChecked = false;
            Assert.IsNull(backup.Status.NextCheckUtc);
            Assert.IsFalse(interval.IsEnabled);
            Assert.IsTrue(now.IsEnabled, "Manual backup remains available when automatic scheduling is off.");
            Assert.IsTrue(status.Text.Contains(LocalizationService.Instance["Settings.Backup.Disabled"], StringComparison.Ordinal));
        }
        finally
        {
            window.Closing += (_, args) => args.Cancel = false;
            window.Close();
            scheduler.StopAsync().GetAwaiter().GetResult();
            LocalizationService.Instance.SetLanguage(originalLanguage);
        }
    }

    private static T Named<T>(Window window, string name) where T : FrameworkElement
        => (T)window.FindName(name);

    private static void PumpUntil(Func<bool> condition)
    {
        var frame = new DispatcherFrame();
        DateTime deadline = DateTime.UtcNow.AddSeconds(8);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (condition() || DateTime.UtcNow >= deadline) frame.Continue = false; };
        timer.Start();
        try { if (!condition()) Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Assert.IsTrue(condition(), "Backup settings did not finish the asynchronous manual snapshot.");
    }

    private sealed class NoRestart : IApplicationRestarter
    {
        public int Attempts { get; private set; }
        public bool TryRestart() { Attempts++; throw new InvalidOperationException("This scenario must not restart."); }
    }
}
