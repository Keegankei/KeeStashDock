using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Infrastructure.Storage;

namespace KeeStashDock.Desktop.Views;

public partial class SettingsWindow
{
    private bool _backupUiBusy;

    private void ConfigureBackups()
    {
        _backup.StatusChanged += () =>
        {
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(new Action(() => { if (IsVisible) RefreshBackupStatus(); }));
        };
        IsVisibleChanged += (_, _) => { if (IsVisible) RefreshBackupStatus(); };
        LocalizationService.Instance.LanguageChanged += () =>
        {
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(new Action(RefreshBackupStatus));
        };
    }

    private void PopulateBackupSettings()
    {
        AutoBackupCheck.IsChecked = _settings.Current.AutoBackupEnabled;
        SelectBackupOption(AutoBackupIntervalCombo, _settings.Current.AutoBackupIntervalMinutes,
            $"{_settings.Current.AutoBackupIntervalMinutes} min");
        SelectBackupOption(BackupKeepCountCombo, _settings.Current.BackupKeepCount,
            _settings.Current.BackupKeepCount.ToString(CultureInfo.CurrentCulture));
        AutoBackupIntervalCombo.IsEnabled = _settings.Current.AutoBackupEnabled;
        AutoBackupIntervalCombo.Opacity = _settings.Current.AutoBackupEnabled ? 1.0 : 0.4;
        RefreshBackupStatus();
    }

    private static void SelectBackupOption(ComboBox combo, int value, string label)
    {
        string tag = value.ToString(CultureInfo.InvariantCulture);
        ComboBoxItem? option = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string?)item.Tag == tag);
        if (option is null)
        {
            option = new ComboBoxItem { Tag = tag, Content = label };
            combo.Items.Add(option);
        }
        combo.SelectedItem = option;
    }

    private void OnBackupKeepCountChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (int.TryParse((BackupKeepCountCombo.SelectedItem as ComboBoxItem)?.Tag as string, out int count))
        {
            _settings.Current.BackupKeepCount = count;
            _settings.Save();
            RefreshBackupStatus();
        }
    }

    private void RefreshBackupStatus()
    {
        var loc = LocalizationService.Instance;
        BackupStatus status = _backup.Status;
        IReadOnlyList<BackupSnapshot> snapshots;
        try { snapshots = _backup.ListSnapshots(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            BackupStatusText.Text = loc["Settings.Backup.Error.Storage"];
            BackupSnapshotList.ItemsSource = null;
            RestoreBackupButton.IsEnabled = false;
            return;
        }
        string? selected = (BackupSnapshotList.SelectedItem as BackupSnapshot)?.Path;
        BackupSnapshotList.ItemsSource = snapshots;
        BackupSnapshotList.SelectedItem = snapshots.FirstOrDefault(snapshot => snapshot.Path == selected);
        string Date(DateTimeOffset? utc) => utc?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "—";
        string state = loc["Settings.Backup.State." + status.State];
        string next = _settings.Current.AutoBackupEnabled ? Date(status.NextCheckUtc) : loc["Settings.Backup.Disabled"];
        BackupStatusText.Text = string.Format(CultureInfo.CurrentCulture, loc["Settings.Backup.StatusSummary"],
            state, Date(status.LastSuccessUtc), Date(status.LastAttemptUtc), Date(status.LastCheckUtc),
            next, snapshots.Count, _backup.BackupsDirectory);
        if (status.ErrorCode is not null)
            BackupStatusText.Text += "\n" + loc["Settings.Backup.Error." + status.ErrorCode];
        int keep = _settings.Current.BackupKeepCount;
        int minutes = _settings.Current.AutoBackupIntervalMinutes;
        string estimatedMb = snapshots.FirstOrDefault() is { } latest
            ? (latest.SizeBytes * (double)keep / 1024 / 1024).ToString("0.0", CultureInfo.CurrentCulture) : "—";
        BackupRetentionText.Text = string.Format(CultureInfo.CurrentCulture, loc["Settings.Backup.RetentionSummary"],
            keep, minutes, ((keep - 1) * minutes / 60d).ToString("0.##", CultureInfo.CurrentCulture), estimatedMb)
            + "\n" + loc["Settings.Backup.FullSnapshotHint"];
        BackupNowButton.IsEnabled = !_backupUiBusy && status.State != BackupRunState.Running && !_databaseImport.AreOperationsDisabled;
        RestoreBackupButton.IsEnabled = !_backupUiBusy && status.State != BackupRunState.Running
            && BackupSnapshotList.SelectedItem is BackupSnapshot && !_databaseImport.AreOperationsDisabled;
    }

    private async void OnBackupNowClick(object sender, RoutedEventArgs e)
    {
        if (_backupUiBusy) return;
        _backupUiBusy = true;
        RefreshBackupStatus();
        try
        {
            BackupAttemptResult result = await _backup.BackUpNowAsync();
            if (result.Outcome is BackupAttemptOutcome.Busy or BackupAttemptOutcome.Deferred)
                MessageBox.Show(this, LocalizationService.Instance["Settings.Backup.DeferredNotice"], "KeeStashDock",
                    MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally { _backupUiBusy = false; RefreshBackupStatus(); }
    }

    private void OnOpenBackupFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_backup.BackupsDirectory);
            Process.Start(new ProcessStartInfo(_backup.BackupsDirectory) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, LocalizationService.Instance["Settings.Backup.Error.Storage"], "KeeStashDock",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnRefreshBackupsClick(object sender, RoutedEventArgs e) => RefreshBackupStatus();
    private void OnBackupSelectionChanged(object sender, SelectionChangedEventArgs e)
        => RestoreBackupButton.IsEnabled = !_backupUiBusy && _backup.Status.State != BackupRunState.Running
            && BackupSnapshotList.SelectedItem is BackupSnapshot && !_databaseImport.AreOperationsDisabled;

    private async void OnRestoreBackupClick(object sender, RoutedEventArgs e)
    {
        if (BackupSnapshotList.SelectedItem is BackupSnapshot snapshot)
            await RestoreDatabaseAsync(snapshot.Path);
    }
}
