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

    public Db() : this(PortablePaths.DatabaseFile)
    {
    }

    /// <summary>Creates a connection factory for an explicit database path.
    /// Production uses <see cref="PortablePaths.DatabaseFile"/>; tests use an
    /// isolated temporary path so they never touch portable user data.</summary>
    public Db(string databaseFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseFile);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databaseFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
        }.ToString();
    }

    /// <summary>Opens a pooled connection with all pragmas applied.</summary>
    public SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous  = NORMAL;
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 3000;
            """;
        cmd.ExecuteNonQuery();
        return conn;
    }
}
