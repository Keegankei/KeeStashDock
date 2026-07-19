using Microsoft.Data.Sqlite;
using KeeStashDock.Infrastructure.Database;
using KeeStashDock.Infrastructure.Logging;

namespace KeeStashDock.Infrastructure.Storage;

/// <summary>
/// Daily database backup + manual export/import (the product's only
/// "sync" story is deliberately local: an export package the user moves
/// between machines by hand).
///
/// Backups use SQLite's online backup API (via BackupDatabase) instead of a
/// raw file copy, so they are consistent even while WAL is active.
/// </summary>
public sealed class BackupService
{
    private readonly Db _db;
    private readonly FileLogger _log;

    public BackupService(Db db, FileLogger log) { _db = db; _log = log; }

    /// <summary>Scheduled automatic backup: creates a new snapshot when the
    /// NEWEST existing one is older than <paramref name="intervalHours"/>,
    /// then prunes beyond <paramref name="keepCount"/>. Timestamped to the
    /// minute so sub-daily intervals produce distinct files; the age check
    /// uses file write time, so the old date-only names keep counting.</summary>
    public void RunAuto(int keepCount, int intervalHours)
    {
        try
        {
            intervalHours = Math.Clamp(intervalHours, 1, 24 * 30);
            var existing = Directory.GetFiles(PortablePaths.BackupsDir,
                                              "keestashdock_*.db");
            DateTime newest = existing.Length == 0
                ? DateTime.MinValue
                : existing.Max(File.GetLastWriteTime);
            if ((DateTime.Now - newest).TotalHours < intervalHours) return;

            BackupTo(Path.Combine(PortablePaths.BackupsDir,
                $"keestashdock_{DateTime.Now:yyyyMMdd_HHmm}.db"));

            var old = Directory.GetFiles(PortablePaths.BackupsDir, "keestashdock_*.db")
                               .OrderByDescending(File.GetLastWriteTime)
                               .Skip(Math.Max(1, keepCount));
            foreach (string f in old) File.Delete(f);
        }
        catch (Exception ex)
        {
            _log.Error("Automatic backup failed", ex);   // never fatal
        }
    }

    /// <summary>Consistent online backup of the live database to <paramref name="targetFile"/>.</summary>
    public void BackupTo(string targetFile)
    {
        using var source = _db.Open();
        using var dest = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = targetFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString());
        dest.Open();
        source.BackupDatabase(dest);
        _log.Info($"Database backed up to {targetFile}");
    }

    /// <summary>Manual "export data package": a consistent DB snapshot the user
    /// can carry to another machine and import there.</summary>
    public void ExportPackage(string targetFile) => BackupTo(targetFile);

    /// <summary>Import replaces the live database. The caller (Settings page)
    /// must restart data services afterwards; a safety backup of the current
    /// database is taken first so import is always reversible.</summary>
    public void ImportPackage(string sourceFile)
    {
        string safety = Path.Combine(PortablePaths.BackupsDir,
            $"pre_import_{DateTime.Now:yyyyMMdd_HHmmss}.db");
        BackupTo(safety);

        SqliteConnection.ClearAllPools();            // release file handles
        File.Copy(sourceFile, PortablePaths.DatabaseFile, overwrite: true);
        // Remove stale WAL side files belonging to the replaced database.
        foreach (string suffix in new[] { "-wal", "-shm" })
        {
            string side = PortablePaths.DatabaseFile + suffix;
            if (File.Exists(side)) File.Delete(side);
        }
        _log.Info($"Database imported from {sourceFile} (safety copy: {safety})");
    }
}
