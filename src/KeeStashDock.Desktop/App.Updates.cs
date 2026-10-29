using System.IO;
using System.Text.RegularExpressions;
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
            var components = new ComponentInstallService(_services.GetRequiredService<SettingsStore>());
            if (components.GetStatus(ComponentKind.OfflineTranslation) == ComponentStatus.Ready)
            {
                var stopped = await components.StopOfflineAsync(_shutdownCancellation.Token);
                if (!stopped.IsSuccess)
                {
                    MessageBox.Show(LocalizationService.Instance["Updates.ComponentFailed"], "KeeStashDock");
                    return false;
                }
            }
            var settings = _services.GetRequiredService<SettingsStore>();
            settings.Save();
            var backup = _services.GetRequiredService<BackupService>();
            string snapshot = Path.Combine(PortablePaths.BackupsDir, "BeforeUpdate-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
            string? recovery = await Task.Run(() =>
            {
                Directory.CreateDirectory(snapshot);
                backup.BackupTo(Path.Combine(snapshot, Path.GetFileName(PortablePaths.DatabaseFile)));
                UpdateDataSnapshot.CopyNonDatabaseFiles(PortablePaths.DataRoot, snapshot, Path.GetFileName(PortablePaths.DatabaseFile),
                    backup.BackupsDirectory, settings.Current.OcrInstallDirectory, settings.Current.OfflineTranslationInstallDirectory);
                return DesktopUpdateCoordinator.Launch(staged, AppContext.BaseDirectory, out string directory,
                    PortablePaths.DatabaseFile) ? directory : null;
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
internal static partial class UpdateDataSnapshot
{
    internal static void CopyNonDatabaseFiles(string dataDirectory, string snapshotDirectory, string databaseFileName,
        string? backupDirectory = null, string? ocrDirectory = null, string? offlineDirectory = null)
    {
        string root = Path.GetFullPath(dataDirectory);
        string snapshot = Path.GetFullPath(snapshotDirectory);
        string? backups = string.IsNullOrWhiteSpace(backupDirectory) ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(backupDirectory));
        string? ocr = ComponentInstallService.TryNormalizeInstallDirectory(ocrDirectory, out string? ocrPath) ? ocrPath : null;
        string? offline = ComponentInstallService.TryNormalizeInstallDirectory(offlineDirectory, out string? offlinePath) ? offlinePath : null;
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
            bool backupFolder = SameDirectory(source, backups);
            bool ocrFolder = SameDirectory(source, ocr) && IsComponentDirectory(source, ComponentKind.Ocr);
            bool offlineFolder = SameDirectory(source, offline) && IsComponentDirectory(source, ComponentKind.OfflineTranslation);
            foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos())
            {
                if (topLevel && (entry.Name.Equals("Backups", StringComparison.OrdinalIgnoreCase) ||
                    entry.Name.Equals("Logs", StringComparison.OrdinalIgnoreCase) ||
                    entry.Name.Equals(databaseFileName, StringComparison.OrdinalIgnoreCase) ||
                    entry.Name.StartsWith(databaseFileName + "-", StringComparison.OrdinalIgnoreCase))) continue;
                // A custom backup folder can also contain ordinary user assets, or
                // even be Data itself. Exclude only known owned files, not the folder.
                if (backupFolder && entry is FileInfo && (OwnedBackupFile().IsMatch(entry.Name) ||
                    entry.Name.Equals("backup-status.json", StringComparison.OrdinalIgnoreCase) ||
                    entry.Name.Equals("backup-status.json.tmp", StringComparison.OrdinalIgnoreCase))) continue;
                if (ocrFolder && (entry is DirectoryInfo && (entry.Name.Equals("OcrModels", StringComparison.OrdinalIgnoreCase) ||
                    entry.Name.Equals("OcrEngine", StringComparison.OrdinalIgnoreCase)) ||
                    entry.Name.Equals(".keestashdock-component", StringComparison.OrdinalIgnoreCase))) continue;
                if (offlineFolder && IsOfflinePayload(entry)) continue;
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("The data backup cannot traverse linked files.");
                string destination = Path.Combine(target, entry.Name);
                if (entry is DirectoryInfo) { Directory.CreateDirectory(destination); CopyDirectory(entry.FullName, destination, false); }
                else File.Copy(entry.FullName, destination, false);
            }
        }
    }

    private static bool SameDirectory(string path, string? expected) => expected is not null &&
        Path.TrimEndingDirectorySeparator(path).Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static bool IsComponentDirectory(string directory, ComponentKind component)
    {
        string marker = Path.Combine(directory, ".keestashdock-component");
        if (File.Exists(marker))
        {
            UpdatePathPolicy.EnsureNoReparsePoints(marker);
            if (new FileInfo(marker).Length <= 64 && File.ReadAllText(marker) == component.ToString()) return true;
        }
        return component == ComponentKind.Ocr
            ? ComponentInstallService.HasOcrModels(Path.Combine(directory, "OcrModels")) &&
                ComponentInstallService.HasOcrEngine(Path.Combine(directory, "OcrEngine"))
            : File.Exists(Path.Combine(directory, "installed.json")) && File.Exists(Path.Combine(directory, "python", "pythonw.exe"));
    }

    private static bool IsOfflinePayload(FileSystemInfo entry)
    {
        string name = entry.Name.ToLowerInvariant();
        return entry is DirectoryInfo
            ? name is "python" or "packages" or "models" or "downloads"
            : name is "installed.json" or "service.json" or "service.stdout.log" or "service.stderr.log" or ".keestashdock-component";
    }

    [GeneratedRegex(@"^(?:keestashdock_(?:\d{8}(?:_\d{4})?|(?:auto|manual)_\d{8}_\d{6}_\d{3}_[a-f0-9]{32})|pre_(?:import|update)_\d{8}_\d{6}_\d{3}_[a-f0-9]{32})\.db$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OwnedBackupFile();
}
