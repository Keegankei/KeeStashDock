using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using KeeStashDock.Desktop.Localization;

namespace KeeStashDock.Desktop.Views;

public partial class SettingsWindow
{
    private CancellationTokenSource? _folderCancellation;
    private Task _folderOperation = Task.CompletedTask;

    private void RefreshFolderPaths()
    {
        BackupPathText.Text = _backup.BackupsDirectory;
        OcrPathText.Text = GetFolderPath("Ocr");
        OfflinePathText.Text = GetFolderPath("Offline");
        DataPathText.Text = _displayDataRoot;
        RefreshFolderAvailability();
    }

    private string GetFolderPath(string kind) => kind switch
    {
        "Backup" => _backup.BackupsDirectory,
        "Data" => _displayDataRoot,
        "Ocr" => ComponentFolder(ComponentKind.Ocr),
        "Offline" => ComponentFolder(ComponentKind.OfflineTranslation),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private string ComponentFolder(ComponentKind kind)
    {
        if (_componentService is not null)
        {
            _componentService.TryGetInstallDirectory(kind, out string path);
            return path;
        }
        return kind == ComponentKind.Ocr
            ? _settings.Current.OcrInstallDirectory ?? Path.Combine(_displayDataRoot, "Components")
            : _settings.Current.OfflineTranslationInstallDirectory ?? Path.Combine(_displayDataRoot, "OfflineTranslation");
    }

    private void RefreshFolderAvailability()
    {
        if (BackupPathActions is null) return;
        bool idle = _folderCancellation is null && _componentCancellation is null && !_shutdownPending;
        BackupPathActions.IsEnabled = idle && !_backupUiBusy;
        OcrPathActions.IsEnabled = OfflinePathActions.IsEnabled = idle && _componentService is not null;
    }

    private async void OnChooseFolderClick(object sender, RoutedEventArgs e)
    {
        string kind = (string)((Button)sender).Tag;
        string initial = GetFolderPath(kind);
        var dialog = new OpenFolderDialog
        {
            Title = LocalizationService.Instance["Settings.Paths." + kind],
            InitialDirectory = Path.IsPathFullyQualified(initial) && Directory.Exists(initial)
                ? initial : _displayDataRoot,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) == true) await ChangeFolderAsync(kind, dialog.FolderName);
    }

    private async void OnResetFolderClick(object sender, RoutedEventArgs e)
        => await ChangeFolderAsync((string)((Button)sender).Tag, null);

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string path = GetFolderPath((string)((Button)sender).Tag);
            if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("The folder must be absolute.");
            if (((Button)sender).Tag is "Ocr" or "Offline" &&
                !ComponentInstallService.TryNormalizeInstallDirectory(path, out _))
                throw new ArgumentException("The component folder is invalid.");
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException)
        {
            FolderStatus.Text = LocalizationService.Instance["Settings.Paths.Error"];
        }
    }

    internal Task ChangeFolderAsync(string kind, string? path)
    {
        if (_folderCancellation is not null) return _folderOperation;
        if (_componentCancellation is not null || _shutdownPending || _backupUiBusy)
        {
            FolderStatus.Text = LocalizationService.Instance["Settings.Paths.Busy"];
            return Task.CompletedTask;
        }
        _folderCancellation = new CancellationTokenSource();
        RefreshComponents();
        FolderStatus.Text = LocalizationService.Instance["Settings.Paths.Changing"];
        return _folderOperation = CompleteFolderChangeAsync(kind, path, _folderCancellation);
    }

    private async Task CompleteFolderChangeAsync(string kind, string? path, CancellationTokenSource request)
    {
        try
        {
            if (kind == "Backup")
            {
                string? previousSetting = _settings.Current.BackupDirectory;
                string previousPath = _backup.BackupsDirectory;
                await _backup.SetBackupsDirectoryAsync(path, request.Token);
                _settings.Current.BackupDirectory = string.IsNullOrWhiteSpace(path) ? null : _backup.BackupsDirectory;
                try { _settings.Save(); }
                catch
                {
                    _settings.Current.BackupDirectory = previousSetting;
                    await _backup.SetBackupsDirectoryAsync(previousPath);
                    throw;
                }
                RefreshBackupStatus();
            }
            else if (kind is "Ocr" or "Offline" && _componentService is not null)
            {
                ComponentInstallResult result = await _componentService.ChangeInstallDirectoryAsync(
                    kind == "Ocr" ? ComponentKind.Ocr : ComponentKind.OfflineTranslation, path, request.Token);
                if (!result.IsSuccess)
                {
                    FolderStatus.Text = LocalizationService.Instance[$"Components.Failure.{result.Failure}"];
                    if (!string.IsNullOrWhiteSpace(result.Detail))
                        FolderStatus.Text += " " + result.Detail[..Math.Min(result.Detail.Length, 320)];
                    return;
                }
                RefreshComponents();
            }
            else throw new ArgumentException("Unknown or unavailable folder setting.", nameof(kind));
            FolderStatus.Text = LocalizationService.Instance["Settings.Paths.Changed"];
        }
        catch (OperationCanceledException)
        {
            FolderStatus.Text = string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            App.Log?.Warn($"Folder change failed: {exception.GetType().Name}");
            FolderStatus.Text = LocalizationService.Instance["Settings.Paths.Error"];
        }
        finally
        {
            _folderCancellation = null;
            request.Dispose();
            RefreshFolderPaths();
            RefreshComponents();
        }
    }
}
