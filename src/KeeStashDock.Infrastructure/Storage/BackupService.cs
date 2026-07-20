using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Database;
using KeeStashDock.Infrastructure.Logging;
using Microsoft.Data.Sqlite;

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
    private readonly string _backupsDirectory;

    public BackupService(Db db, FileLogger log)
    {
        _db = db;
        _log = log;
        _backupsDirectory = Path.Combine(
            Path.GetDirectoryName(_db.DatabaseFile)
                ?? throw new InvalidOperationException("Database path has no parent directory."),
            "Backups");
        Directory.CreateDirectory(_backupsDirectory);
    }

    internal string BackupsDirectory => _backupsDirectory;

    /// <summary>Scheduled automatic backup: creates a new snapshot when the
    /// NEWEST existing one is older than <paramref name="intervalHours"/>,
    /// then prunes beyond <paramref name="keepCount"/>. Timestamped to the
    /// minute so sub-daily intervals produce distinct files; the age check
    /// uses file write time, so the old date-only names keep counting.</summary>
    public void RunAuto(int keepCount, int intervalHours)
    {
        try
        {
            intervalHours = intervalHours <= 0
                ? AppSettings.DefaultAutoBackupIntervalHours
                : Math.Min(intervalHours, AppSettings.MaxAutoBackupIntervalHours);
            keepCount = keepCount <= 0
                ? AppSettings.DefaultBackupKeepCount
                : Math.Min(keepCount, AppSettings.MaxBackupKeepCount);
            var existing = Directory.GetFiles(_backupsDirectory,
                                              "keestashdock_*.db");
            DateTime newest = existing.Length == 0
                ? DateTime.MinValue
                : existing.Max(File.GetLastWriteTime);
            if ((DateTime.Now - newest).TotalHours < intervalHours) return;

            BackupTo(Path.Combine(_backupsDirectory,
                $"keestashdock_{DateTime.Now:yyyyMMdd_HHmm}.db"));

            var old = Directory.GetFiles(_backupsDirectory, "keestashdock_*.db")
                               .OrderByDescending(File.GetLastWriteTime)
                               .Skip(keepCount);
            foreach (string f in old) File.Delete(f);
        }
        catch (Exception ex)
        {
            _log.Error("Automatic backup failed", ex);   // never fatal
        }
    }

    /// <summary>Consistent online backup of the live database to <paramref name="targetFile"/>.</summary>
    public void BackupTo(string targetFile)
        => BackupCore(targetFile, _db.Open);

    internal void BackupToForMaintenance(
        string targetFile,
        DatabaseMaintenanceLease lease)
        => BackupCore(targetFile, () => _db.OpenForMaintenance(lease));

    private void BackupCore(
        string targetFile,
        Func<SqliteConnection> openSource)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(targetFile));
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        using SqliteConnection source = openSource();
        using var dest = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = targetFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        dest.Open();
        source.BackupDatabase(dest);
        _log.Info($"Database backed up to {targetFile}");
    }

    /// <summary>Manual "export data package": a consistent DB snapshot the user
    /// can carry to another machine and import there.</summary>
    public void ExportPackage(string targetFile) => BackupTo(targetFile);
}
