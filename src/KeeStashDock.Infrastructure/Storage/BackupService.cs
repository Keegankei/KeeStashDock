using System.Text.Json;
using System.Text.RegularExpressions;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Database;
using KeeStashDock.Infrastructure.Logging;
using Microsoft.Data.Sqlite;

namespace KeeStashDock.Infrastructure.Storage;

/// <summary>Validated SQLite snapshots. Retention only removes owned automatic
/// snapshots, preserving exports and import/update recovery copies.</summary>
public sealed partial class BackupService
{
    private readonly Db _db;
    private readonly FileLogger _log;
    private readonly TimeProvider _clock;
    private readonly DatabaseMaintenanceCoordinator? _maintenance;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _statusGate = new();
    private readonly string _statusFile;
    private BackupStatus _status = new();

    public BackupService(Db db, FileLogger log, TimeProvider? clock = null,
        DatabaseMaintenanceCoordinator? maintenance = null)
    {
        _db = db;
        _log = log;
        _clock = clock ?? TimeProvider.System;
        _maintenance = maintenance;
        BackupsDirectory = Path.Combine(Path.GetDirectoryName(_db.DatabaseFile)
            ?? throw new InvalidOperationException("Database path has no parent directory."), "Backups");
        _statusFile = Path.Combine(BackupsDirectory, "backup-status.json");
        LoadStatus();
    }

    public string BackupsDirectory { get; }
    public BackupStatus Status { get { lock (_statusGate) return _status; } }
    /// <summary>Raised on the changing worker. UI subscribers must dispatch.</summary>
    public event Action? StatusChanged;

    public async Task WaitForIdleAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        _operationGate.Release();
    }

    public IReadOnlyList<BackupSnapshot> ListSnapshots()
    {
        if (!Directory.Exists(BackupsDirectory)) return [];
        return Directory.EnumerateFiles(BackupsDirectory, "*.db")
            .Where(path => OwnedSnapshotName().IsMatch(Path.GetFileName(path)) ||
                Path.GetFileName(path).StartsWith("pre_import_", StringComparison.Ordinal) ||
                Path.GetFileName(path).StartsWith("pre_update_", StringComparison.Ordinal))
            .Select(path => new FileInfo(path)).Where(file => file.Exists)
            .Select(file => new BackupSnapshot(file.FullName,
                new DateTimeOffset(file.LastWriteTimeUtc), file.Length,
                OwnedAutomaticName().IsMatch(file.Name)))
            .OrderByDescending(snapshot => snapshot.CreatedUtc).ToArray();
    }

    public Task<BackupAttemptResult> BackUpNowAsync(CancellationToken cancellationToken = default)
        => CreateSnapshotAsync(false, AppSettings.DefaultBackupKeepCount, cancellationToken);

    public Task<BackupAttemptResult> CreateAutomaticSnapshotAsync(int keepCount,
        CancellationToken cancellationToken = default)
        => CreateSnapshotAsync(true, keepCount, cancellationToken);

    private async Task<BackupAttemptResult> CreateSnapshotAsync(bool automatic, int keepCount,
        CancellationToken cancellationToken)
    {
        // Never queue a duplicate manual backup behind an automatic snapshot.
        if (!await _operationGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return new(BackupAttemptOutcome.Busy);
        try
        {
            if (_maintenance is { CanStartOperation: false })
            {
                RecordDeferredCheck();
                return new(BackupAttemptOutcome.Deferred);
            }
            return await Task.Run(() => CreateSnapshot(automatic, keepCount, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        finally { _operationGate.Release(); }
    }

    private BackupAttemptResult CreateSnapshot(bool automatic, int keepCount, CancellationToken cancellationToken)
    {
        DateTimeOffset attempt = _clock.GetUtcNow();
        SetStatus(current => current with { State = BackupRunState.Running, LastAttemptUtc = attempt,
            LastCheckUtc = attempt, ErrorCode = null, ErrorSummary = null }, true);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = $"keestashdock_{(automatic ? "auto" : "manual")}_{attempt:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.db";
            string snapshot = Path.Combine(BackupsDirectory, name);
            BackupCore(snapshot, _db.Open, cancellationToken);
            DateTimeOffset completed = _clock.GetUtcNow();
            File.SetLastWriteTimeUtc(snapshot, completed.UtcDateTime);
            SetStatus(current => current with { State = BackupRunState.Succeeded, LastSuccessUtc = completed,
                LastSnapshotFileName = name, ErrorCode = null, ErrorSummary = null }, true);
            if (automatic)
            {
                PruneAutomaticSnapshots(keepCount, snapshot);
                // Refresh the list/count after pruning, not only before it.
                StatusChanged?.Invoke();
            }
            return new(BackupAttemptOutcome.Succeeded, snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetStatus(current => current with { State = BackupRunState.Cancelled, ErrorCode = null, ErrorSummary = null }, true);
            return new(BackupAttemptOutcome.Cancelled);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            SqliteException or InvalidOperationException or ArgumentException)
        {
            string code = exception is UnauthorizedAccessException ? "AccessDenied" :
                exception is SqliteException ? "Database" : "Storage";
            SetStatus(current => current with { State = BackupRunState.Failed, ErrorCode = code,
                ErrorSummary = exception.Message }, true);
            _log.Error("Database snapshot failed", exception);
            return new(BackupAttemptOutcome.Failed);
        }
    }

    public void SetNextCheck(DateTimeOffset? next)
    {
        SetStatus(current => current.NextCheckUtc == next ? current : current with { NextCheckUtc = next }, false);
    }

    public void RecordDeferredCheck()
        => SetStatus(current => current with { LastCheckUtc = _clock.GetUtcNow(), State = BackupRunState.Deferred }, false);

    /// <summary>A failed export leaves an existing destination intact.</summary>
    public void BackupTo(string targetFile)
    {
        _operationGate.Wait();
        try { BackupCore(targetFile, _db.Open); }
        finally { _operationGate.Release(); }
    }

    internal void BackupToForMaintenance(string targetFile, DatabaseMaintenanceLease lease)
        // Maintenance already owns the database gate; taking the backup gate
        // could deadlock against a snapshot waiting for that database gate.
        => BackupCore(targetFile, () => _db.OpenForMaintenance(lease));

    private void BackupCore(string targetFile, Func<SqliteConnection> openSource,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFile);
        targetFile = Path.GetFullPath(targetFile);
        string active = Path.GetFullPath(_db.DatabaseFile);
        if (new[] { active, active + "-wal", active + "-shm", active + "-journal" }
            .Contains(targetFile, StringComparer.OrdinalIgnoreCase))
            throw new IOException("The active database and its journal files cannot be used as a backup destination.");
        string directory = Path.GetDirectoryName(targetFile)!;
        Directory.CreateDirectory(directory);
        string staging = Path.Combine(directory, $".snapshot-{Guid.NewGuid():N}.tmp");
        try
        {
            using (SqliteConnection source = openSource())
            using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = staging, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString()))
            {
                cancellationToken.ThrowIfCancellationRequested();
                destination.Open();
                source.BackupDatabase(destination);
                using var check = destination.CreateCommand();
                check.CommandText = "PRAGMA quick_check;";
                if (!string.Equals(check.ExecuteScalar() as string, "ok", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("The database snapshot did not pass its integrity check.");
                // Publish a self-contained file even when the live DB uses WAL.
                check.CommandText = "PRAGMA journal_mode=DELETE;";
                check.ExecuteScalar();
            }
            cancellationToken.ThrowIfCancellationRequested();
            using (var flush = new FileStream(staging, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                flush.Flush(flushToDisk: true);
            if (File.Exists(targetFile)) File.Replace(staging, targetFile, null);
            else File.Move(staging, targetFile);
            _log.Info($"Database backed up to {targetFile}");
        }
        finally
        {
            foreach (string path in new[] { staging, staging + "-wal", staging + "-shm", staging + "-journal" })
            {
                try { File.Delete(path); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                { _log.Warn($"Could not remove unfinished snapshot: {exception.GetType().Name}"); }
            }
        }
    }

    private void PruneAutomaticSnapshots(int keepCount, string newest)
    {
        keepCount = keepCount <= 0 ? AppSettings.DefaultBackupKeepCount : Math.Min(keepCount, AppSettings.MaxBackupKeepCount);
        try
        {
            foreach (string path in Directory.EnumerateFiles(BackupsDirectory, "*.db")
                .Where(path => OwnedAutomaticName().IsMatch(Path.GetFileName(path)))
                .OrderByDescending(path => string.Equals(path, newest, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(File.GetLastWriteTimeUtc).Skip(keepCount))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetStatus(current => current with { ErrorCode = "Retention", ErrorSummary = exception.Message }, true);
            _log.Warn($"Backup retention could not finish: {exception.GetType().Name}");
        }
    }

    private void LoadStatus()
    {
        try
        {
            if (File.Exists(_statusFile))
                _status = JsonSerializer.Deserialize<BackupStatus>(File.ReadAllText(_statusFile)) ?? new();
            if (!Enum.IsDefined(_status.State)) _status = _status with { State = BackupRunState.Never };
            if (_status.ErrorCode is not (null or "AccessDenied" or "Database" or "Storage" or
                "StatusStorage" or "Interrupted" or "Retention"))
                _status = _status with { ErrorCode = null, ErrorSummary = null };
            if (_status.State == BackupRunState.Running)
                _status = _status with { State = BackupRunState.Failed, ErrorCode = "Interrupted",
                    ErrorSummary = "The previous backup did not finish." };
            _status = _status with { NextCheckUtc = null,
                LastAttemptUtc = _status.LastAttemptUtc?.ToUniversalTime(),
                LastSuccessUtc = _status.LastSuccessUtc?.ToUniversalTime(),
                LastCheckUtc = _status.LastCheckUtc?.ToUniversalTime() };
            if (_status.LastAttemptUtc is null && Directory.Exists(BackupsDirectory))
            {
                var newest = ListSnapshots().FirstOrDefault(snapshot => snapshot.IsAutomatic);
                if (newest is not null)
                    _status = _status with { State = BackupRunState.Succeeded,
                        LastSuccessUtc = newest.CreatedUtc, LastAttemptUtc = newest.CreatedUtc,
                        LastSnapshotFileName = Path.GetFileName(newest.Path) };
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        { _log.Warn($"Backup status could not be loaded: {exception.GetType().Name}"); }
    }

    private void SetStatus(Func<BackupStatus, BackupStatus> update, bool persist)
    {
        lock (_statusGate)
        {
            BackupStatus status = update(_status);
            if (ReferenceEquals(status, _status)) return;
            _status = status;
            if (persist)
            {
                string temporary = _statusFile + ".tmp";
                try
                {
                    Directory.CreateDirectory(BackupsDirectory);
                    File.WriteAllText(temporary, JsonSerializer.Serialize(status));
                    using (var flush = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                        flush.Flush(flushToDisk: true);
                    if (File.Exists(_statusFile)) File.Replace(temporary, _statusFile, null);
                    else File.Move(temporary, _statusFile);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    _status = _status with { ErrorCode = "StatusStorage",
                        ErrorSummary = "Backup status could not be saved. Check folder access and free space." };
                    _log.Warn($"Backup status could not be saved: {exception.GetType().Name}");
                }
            }
        }
        StatusChanged?.Invoke();
    }

    public void ExportPackage(string targetFile) => BackupTo(targetFile);

    [GeneratedRegex(@"^keestashdock_(?:\d{8}(?:_\d{4})?|auto_\d{8}_\d{6}_\d{3}_[a-f0-9]{32})\.db$", RegexOptions.CultureInvariant)]
    private static partial Regex OwnedAutomaticName();
    [GeneratedRegex(@"^keestashdock_(?:\d{8}(?:_\d{4})?|(?:auto|manual)_\d{8}_\d{6}_\d{3}_[a-f0-9]{32})\.db$", RegexOptions.CultureInvariant)]
    private static partial Regex OwnedSnapshotName();
}

public enum BackupRunState { Never, Running, Succeeded, Failed, Deferred, Cancelled }
public enum BackupAttemptOutcome { Succeeded, Failed, Busy, Deferred, Cancelled }
public sealed record BackupAttemptResult(BackupAttemptOutcome Outcome, string? SnapshotPath = null);
public sealed record BackupSnapshot(string Path, DateTimeOffset CreatedUtc, long SizeBytes, bool IsAutomatic)
{
    public string DisplayName => $"{CreatedUtc.LocalDateTime:g}   {SizeBytes / (1024d * 1024d):0.00} MB   {System.IO.Path.GetFileName(Path)}";
}
public sealed record BackupStatus
{
    public BackupRunState State { get; init; }
    public DateTimeOffset? LastSuccessUtc { get; init; }
    public DateTimeOffset? LastAttemptUtc { get; init; }
    public DateTimeOffset? LastCheckUtc { get; init; }
    public DateTimeOffset? NextCheckUtc { get; init; }
    public string? LastSnapshotFileName { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorSummary { get; init; }
}
