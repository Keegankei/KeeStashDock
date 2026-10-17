using System.IO;
using System.Windows;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Desktop.Views;
using KeeStashDock.Infrastructure.Storage;
using KeeStashDock.Core.Services;
using KeeStashDock.Infrastructure.Updates;
using Microsoft.Extensions.DependencyInjection;

namespace KeeStashDock.Desktop;

public partial class App
{
    private UpdateWindow? _updateWindow;

    private void ShowUpdates()
    {
        if (_exitPending) return;
        if (_updateWindow is null)
        {
            _updateWindow = new UpdateWindow(RestartForUpdateAsync);
            _updateWindow.Closed += (_, _) => _updateWindow = null;
        }
        _updateWindow.Show(); _updateWindow.Activate();
    }

    private async Task<bool> RestartForUpdateAsync(StagedUpdate staged)
    {
        if (_exitPending) return false;
        _exitPending = true;
        var disabledWindows = new List<Window>();
        bool pausedCollection = false;
        bool launched = false;
        try
        {
            if (!await PrepareForShutdownAsync()) return false;
            foreach (Window window in Windows)
                if (window.IsEnabled) { disabledWindows.Add(window); window.IsEnabled = false; }
            _clipboardWatcher?.Dispose();
            _selectionWatcherController.Stop();
            pausedCollection = true;
            _panel?.FlushDraft();
            var components = new ComponentInstallService();
            if (components.GetStatus(ComponentKind.OfflineTranslation) == ComponentStatus.Ready)
            {
                var stopped = await components.StopOfflineAsync(_shutdownCancellation.Token);
                if (!stopped.IsSuccess)
                {
                    MessageBox.Show(LocalizationService.Instance["Updates.ComponentFailed"], "KeeStashDock");
                    return false;
                }
            }
            _services.GetRequiredService<SettingsStore>().Save();
            string snapshot = Path.Combine(PortablePaths.BackupsDir, "BeforeUpdate-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
            string? recovery = await Task.Run(() =>
            {
                Directory.CreateDirectory(snapshot);
                _services.GetRequiredService<BackupService>().BackupTo(Path.Combine(snapshot, Path.GetFileName(PortablePaths.DatabaseFile)));
                UpdateDataSnapshot.CopyNonDatabaseFiles(PortablePaths.DataRoot, snapshot, Path.GetFileName(PortablePaths.DatabaseFile));
                return DesktopUpdateCoordinator.Launch(staged, AppContext.BaseDirectory, out string directory) ? directory : null;
            });
            if (recovery is null) return false;
            launched = true;
            Log?.Info("Update staged; recovery directory: " + recovery);
            _shutdownCancellation.Cancel();
            Shutdown();
            return true;
        }
        finally
        {
            if (!launched)
            {
                foreach (var window in disabledWindows) window.IsEnabled = true;
                if (pausedCollection)
                {
                    var settings = _services.GetRequiredService<SettingsStore>();
                    _clipboardWatcher = new ClipboardWatcher(settings, _services.GetRequiredService<SnippetService>());
                    ApplySelectionPopupSetting();
                }
            }
            _exitPending = false;
        }
    }
}

/// <summary>Back up settings, drafts and user assets without copying a live SQLite file or recursing into backups.</summary>
internal static class UpdateDataSnapshot
{
    internal static void CopyNonDatabaseFiles(string dataDirectory, string snapshotDirectory, string databaseFileName)
    {
        string root = Path.GetFullPath(dataDirectory);
        string snapshot = Path.GetFullPath(snapshotDirectory);
        UpdatePathPolicy.EnsureNoReparsePoints(root);
        UpdatePathPolicy.EnsureNoReparsePoints(snapshot);
        string relative = Path.GetRelativePath(root, snapshot).Replace('\\', '/');
        if (relative == "." || (!relative.StartsWith("../", StringComparison.Ordinal) &&
            !Path.IsPathRooted(relative) && !relative.StartsWith("Backups/", StringComparison.OrdinalIgnoreCase)))
            throw new IOException("A data snapshot inside the active data directory must be under Backups.");
        CopyDirectory(root, snapshot, true);

        void CopyDirectory(string source, string target, bool topLevel)
        {
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The data backup cannot traverse linked directories.");
            foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos())
            {
                if (topLevel && (entry.Name.Equals("Backups", StringComparison.OrdinalIgnoreCase) ||
                    entry.Name.Equals("Logs", StringComparison.OrdinalIgnoreCase) ||
                    entry.Name.Equals(databaseFileName, StringComparison.OrdinalIgnoreCase) ||
                    entry.Name.StartsWith(databaseFileName + "-", StringComparison.OrdinalIgnoreCase))) continue;
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("The data backup cannot traverse linked files.");
                string destination = Path.Combine(target, entry.Name);
                if (entry is DirectoryInfo) { Directory.CreateDirectory(destination); CopyDirectory(entry.FullName, destination, false); }
                else File.Copy(entry.FullName, destination, false);
            }
        }
    }
}
