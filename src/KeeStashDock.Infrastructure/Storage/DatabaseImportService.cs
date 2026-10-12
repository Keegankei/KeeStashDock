using Microsoft.Data.Sqlite;
using KeeStashDock.Infrastructure.Database;
using KeeStashDock.Infrastructure.Logging;

namespace KeeStashDock.Infrastructure.Storage;

/// <summary>
/// Validates, migrates, atomically installs, and if necessary rolls back a
/// database import while holding the process-wide maintenance lease.
/// </summary>
public sealed class DatabaseImportService
{
    private readonly Db _database;
    private readonly DatabaseMaintenanceCoordinator _maintenance;
    private readonly BackupService _backup;
    private readonly DatabaseSchemaValidator _validator;
    private readonly IDatabaseFileOperations _files;
    private readonly IApplicationRestarter _restarter;
    private readonly FileLogger _log;
    private readonly IDatabaseImportObserver? _observer;

    public DatabaseImportService(
        Db database,
        DatabaseMaintenanceCoordinator maintenance,
        BackupService backup,
        DatabaseSchemaValidator validator,
        IDatabaseFileOperations files,
        IApplicationRestarter restarter,
        FileLogger log,
        IDatabaseImportObserver? observer = null)
    {
        _database = database;
        _maintenance = maintenance;
        _backup = backup;
        _validator = validator;
        _files = files;
        _restarter = restarter;
        _log = log;
        _observer = observer;
    }

    public bool AreOperationsDisabled => _maintenance.AreOperationsDisabled;

    public Task<DatabaseImportResult> ImportAsync(
        string sourceFile,
        CancellationToken cancellationToken = default)
        => Task.Run(() => Import(sourceFile, cancellationToken), cancellationToken);

    public DatabaseImportResult Import(
        string sourceFile,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);

        string source = Path.GetFullPath(sourceFile);
        string active = Path.GetFullPath(_database.DatabaseFile);
        if (string.Equals(source, active, StringComparison.OrdinalIgnoreCase))
        {
            throw new DatabaseImportException(
                "The active KeeStashDock database cannot be imported into itself.");
        }

        if (!_files.Exists(source))
        {
            throw new DatabaseImportException("The selected database file does not exist.");
        }

        if (_files.GetLength(source) == 0)
        {
            throw new DatabaseImportException("The selected database file is empty.");
        }

        string dataDirectory = Path.GetDirectoryName(active)
            ?? throw new DatabaseImportException("The active database path has no parent directory.");
        _files.CreateDirectory(dataDirectory);

        string operationId = Guid.NewGuid().ToString("N");
        string staging = Path.Combine(dataDirectory, $".import-{operationId}.db");
        string rollback = Path.Combine(dataDirectory, $".rollback-{operationId}.db");
        string restoreStage = Path.Combine(dataDirectory, $".restore-{operationId}.db");
        string failedReplacement = Path.Combine(dataDirectory, $".failed-{operationId}.db");
        string safetyBackup = Path.Combine(
            _backup.BackupsDirectory,
            $"pre_import_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{operationId}.db");

        DatabaseMaintenanceLease? maintenanceLease = null;
        bool replaced = false;
        bool restartAttempted = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _files.SnapshotToNew(source, staging);
            _observer?.OnStage(DatabaseImportStage.AfterStaging);

            // Validate the recognizable legacy/current schema before any
            // migration is allowed to modify the staging copy.
            _validator.Validate(staging, requireCurrentUserVersion: false);
            new MigrationRunner(new Db(staging), _log).Run();
            SqliteConnection.ClearAllPools();
            DeleteSidecars(staging);
            DatabaseValidationResult imported = _validator.Validate(
                staging,
                requireCurrentUserVersion: true);

            EnsureSameVolume(staging, active);
            cancellationToken.ThrowIfCancellationRequested();
            maintenanceLease = _maintenance.EnterMaintenance(cancellationToken);

            if (!_files.Exists(active))
            {
                throw new DatabaseImportException(
                    "The active database disappeared before a safety backup could be created.");
            }

            _backup.BackupToForMaintenance(safetyBackup, maintenanceLease);
            _validator.Validate(safetyBackup, requireCurrentUserVersion: true);
            _observer?.OnStage(DatabaseImportStage.AfterSafetyBackup);

            // The exclusive lease proves all Db.Open() connections are
            // disposed. Clear pools before touching the old WAL/SHM files.
            SqliteConnection.ClearAllPools();
            DeleteSidecars(active);
            _observer?.OnStage(DatabaseImportStage.BeforeReplacement);

            _files.Replace(staging, active, rollback);
            replaced = true;
            _observer?.OnStage(DatabaseImportStage.AfterReplacement);
            _validator.Validate(active, requireCurrentUserVersion: true);

            _observer?.OnStage(DatabaseImportStage.BeforeRestart);
            maintenanceLease.DisableOperations();
            restartAttempted = true;
            if (!_restarter.TryRestart())
            {
                throw new InvalidOperationException(
                    "KeeStashDock could not start the replacement process.");
            }

            _log.Info(
                $"Database imported from {source} (safety backup: {safetyBackup})");
            return new DatabaseImportResult(safetyBackup, imported.SchemaVersion);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (replaced && maintenanceLease is not null)
            {
                try
                {
                    RestoreSafetyBackup(
                        safetyBackup,
                        active,
                        restoreStage,
                        failedReplacement);
                }
                catch (Exception restoreException)
                {
                    _log.Error("Database import rollback failed", restoreException);
                    maintenanceLease.DisableOperations();
                    throw new DatabaseImportException(
                        "Import failed and the automatic rollback also failed. " +
                        $"Exit KeeStashDock immediately. The verified safety backup is {safetyBackup}.",
                        new AggregateException(exception, restoreException));
                }
            }

            if (restartAttempted)
            {
                maintenanceLease?.DisableOperations();
                throw new DatabaseRestartRequiredException(
                    "Import could not complete the automatic restart. The original database " +
                    "was restored, but all database operations are disabled; exit " +
                    "KeeStashDock before continuing.",
                    exception);
            }

            if (exception is DatabaseImportException)
            {
                throw;
            }

            if (exception is DatabaseValidationException validation)
            {
                throw new DatabaseImportException(
                    validation.Message,
                    validation);
            }

            throw new DatabaseImportException(
                $"Database import failed before completion: {exception.Message}",
                exception);
        }
        finally
        {
            maintenanceLease?.Dispose();
            TryDeleteDatabaseFiles(staging);
            TryDeleteDatabaseFiles(rollback);
            TryDeleteDatabaseFiles(restoreStage);
            TryDeleteDatabaseFiles(failedReplacement);
        }
    }

    private void RestoreSafetyBackup(
        string safetyBackup,
        string active,
        string restoreStage,
        string failedReplacement)
    {
        SqliteConnection.ClearAllPools();
        DeleteSidecars(active);
        _files.CopyToNew(safetyBackup, restoreStage);
        EnsureSameVolume(restoreStage, active);
        _files.Replace(restoreStage, active, failedReplacement);
        DeleteSidecars(active);
        _validator.Validate(active, requireCurrentUserVersion: true);
        _log.Warn("Database import failed after replacement; safety backup restored.");
    }

    private static void EnsureSameVolume(string source, string destination)
    {
        string? sourceRoot = Path.GetPathRoot(Path.GetFullPath(source));
        string? destinationRoot = Path.GetPathRoot(Path.GetFullPath(destination));
        if (sourceRoot is null || destinationRoot is null ||
            !string.Equals(sourceRoot, destinationRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new DatabaseImportException(
                "Atomic database replacement requires staging and active files on the same volume.");
        }
    }

    private void DeleteSidecars(string databaseFile)
    {
        _files.DeleteIfExists(databaseFile + "-wal");
        _files.DeleteIfExists(databaseFile + "-shm");
    }

    private void TryDeleteDatabaseFiles(string databaseFile)
    {
        try
        {
            _files.DeleteIfExists(databaseFile);
            DeleteSidecars(databaseFile);
        }
        catch (Exception exception)
        {
            _log.Warn($"Could not clean temporary database file {databaseFile}: {exception.Message}");
        }
    }
}
