using Microsoft.Data.Sqlite;
using KeeStashDock.Infrastructure.Storage;

namespace KeeStashDock.Infrastructure.Database;

/// <summary>
/// Central SQLite connection factory.
///
/// Reliability decisions (non-functional baseline of the design doc):
/// * journal_mode = WAL  : committed data survives power loss; readers never
///   block the writer, which keeps the quick panel snappy during background
///   housekeeping.
/// * synchronous = NORMAL: the recommended pairing with WAL (fsync on
///   checkpoint, not on every commit) - a good durability/latency trade-off.
/// * foreign_keys = ON   : SQLite defaults to OFF; we rely on FK cascades.
/// * busy_timeout        : short waits instead of instant SQLITE_BUSY errors.
/// </summary>
public sealed class Db
{
    private readonly string _connectionString;
    private readonly DatabaseMaintenanceCoordinator _maintenance;

    public Db() : this(PortablePaths.DatabaseFile, new DatabaseMaintenanceCoordinator())
    {
    }

    public Db(DatabaseMaintenanceCoordinator maintenance)
        : this(PortablePaths.DatabaseFile, maintenance)
    {
    }

    /// <summary>Creates a connection factory for an explicit database path.
    /// Production uses <see cref="PortablePaths.DatabaseFile"/>; tests use an
    /// isolated temporary path so they never touch portable user data.</summary>
    public Db(string databaseFile)
        : this(databaseFile, new DatabaseMaintenanceCoordinator())
    {
    }

    public Db(string databaseFile, DatabaseMaintenanceCoordinator maintenance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseFile);
        ArgumentNullException.ThrowIfNull(maintenance);
        DatabaseFile = Path.GetFullPath(databaseFile);
        _maintenance = maintenance;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabaseFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
        }.ToString();
    }

    public string DatabaseFile { get; }

    /// <summary>Opens a pooled connection with all pragmas applied.</summary>
    public SqliteConnection Open()
    {
        IDisposable operation = _maintenance.EnterOperation();
        return OpenCore(operation);
    }

    internal SqliteConnection OpenForMaintenance(DatabaseMaintenanceLease lease)
    {
        _maintenance.VerifyLease(lease);
        return OpenCore(connectionLease: null);
    }

    private SqliteConnection OpenCore(IDisposable? connectionLease)
    {
        var connection = new SqliteConnection(_connectionString);
        if (connectionLease is not null)
        {
            connection.Disposed += (_, _) => connectionLease.Dispose();
        }

        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous  = NORMAL;
                PRAGMA foreign_keys = ON;
                PRAGMA busy_timeout = 3000;
                """;
            command.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            connectionLease?.Dispose();
            throw;
        }
    }
}
