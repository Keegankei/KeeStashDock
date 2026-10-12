using System.Globalization;
using KeeStashDock.Infrastructure.Database;
using KeeStashDock.Infrastructure.Logging;
using KeeStashDock.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace KeeStashDock.Tests;

[TestClass]
[DoNotParallelize]
public sealed class DatabaseDependencyTests
{
    private static readonly Version MinimumSqliteVersion = new(3, 50, 2);
    private static readonly string TempParent = Path.Combine(
        Path.GetTempPath(),
        "KeeStashDock.Tests");

    private string _tempRoot = null!;

    [TestInitialize]
    public void Initialize()
    {
        Directory.CreateDirectory(TempParent);
        _tempRoot = Path.Combine(TempParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (!Directory.Exists(_tempRoot))
        {
            return;
        }

        string parent = Path.GetFullPath(TempParent)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string target = Path.GetFullPath(_tempRoot);
        Assert.IsTrue(
            target.StartsWith(parent, StringComparison.OrdinalIgnoreCase),
            $"Refusing to delete a test directory outside {parent}: {target}");
        Directory.Delete(target, recursive: true);
    }

    [TestMethod]
    public void RuntimeSqliteVersion_IsAtLeastSecureMinimum()
    {
        using FileLogger logger = CreateLogger();
        Db database = CreateMigratedDatabase(logger);
        using SqliteConnection connection = database.Open();

        string versionText = Convert.ToString(
            ExecuteScalar(connection, "SELECT sqlite_version();"),
            CultureInfo.InvariantCulture) ?? string.Empty;
        Version actualVersion = Version.Parse(versionText);

        Assert.IsTrue(
            actualVersion.CompareTo(MinimumSqliteVersion) >= 0,
            $"SQLite {actualVersion} is older than required {MinimumSqliteVersion}.");
    }

    [TestMethod]
    public void Migration_EnablesWalFts5AndForeignKeys()
    {
        using FileLogger logger = CreateLogger();
        Db database = CreateMigratedDatabase(logger);
        using SqliteConnection connection = database.Open();

        Assert.AreEqual(
            "wal",
            Convert.ToString(
                ExecuteScalar(connection, "PRAGMA journal_mode;"),
                CultureInfo.InvariantCulture)?.ToLowerInvariant());
        Assert.AreEqual(1L, Convert.ToInt64(
            ExecuteScalar(connection, "PRAGMA foreign_keys;"),
            CultureInfo.InvariantCulture));
        Assert.AreEqual((long)MigrationRunner.LatestSupportedVersion, Convert.ToInt64(
            ExecuteScalar(connection, "SELECT version FROM schema_version;"),
            CultureInfo.InvariantCulture));

        InsertSnippet(connection, "secure clipboard storage", "hash-secure");
        Assert.AreEqual(1L, Convert.ToInt64(
            ExecuteScalar(
                connection,
                "SELECT COUNT(*) FROM snippets_fts WHERE snippets_fts MATCH 'secure';"),
            CultureInfo.InvariantCulture));

        using SqliteCommand invalidForeignKey = connection.CreateCommand();
        invalidForeignKey.CommandText = """
            INSERT INTO snippets (
                content, content_hash, category_id, created_at, updated_at)
            VALUES (
                'invalid foreign key', 'hash-invalid-fk', 999999, $now, $now);
            """;
        invalidForeignKey.Parameters.AddWithValue(
            "$now",
            DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));

        try
        {
            invalidForeignKey.ExecuteNonQuery();
            Assert.Fail("Expected SQLite to reject an invalid category foreign key.");
        }
        catch (SqliteException exception)
        {
            Assert.AreEqual(19, exception.SqliteErrorCode);
        }
    }

    [TestMethod]
    public void OnlineBackup_PreservesMigratedSchemaAndFtsData()
    {
        using FileLogger logger = CreateLogger();
        Db database = CreateMigratedDatabase(logger);
        using (SqliteConnection connection = database.Open())
        {
            InsertSnippet(connection, "backup verification token", "hash-backup");
        }

        string backupPath = Path.Combine(_tempRoot, "backup.db");
        var backup = new BackupService(database, logger);
        backup.BackupTo(backupPath);

        using var backupConnection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = backupPath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());
        backupConnection.Open();

        Assert.AreEqual(
            "ok",
            Convert.ToString(
                ExecuteScalar(backupConnection, "PRAGMA quick_check;"),
                CultureInfo.InvariantCulture));
        Assert.AreEqual((long)MigrationRunner.LatestSupportedVersion, Convert.ToInt64(
            ExecuteScalar(backupConnection, "SELECT version FROM schema_version;"),
            CultureInfo.InvariantCulture));
        Assert.AreEqual(1L, Convert.ToInt64(
            ExecuteScalar(
                backupConnection,
                "SELECT COUNT(*) FROM snippets_fts WHERE snippets_fts MATCH 'verification';"),
            CultureInfo.InvariantCulture));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("-wal")]
    [DataRow("-shm")]
    [DataRow("-journal")]
    public void BackupRejectsActiveDatabaseAndJournalDestinationsWithoutChangingData(string suffix)
    {
        using FileLogger logger = CreateLogger();
        Db database = CreateMigratedDatabase(logger);
        using (SqliteConnection connection = database.Open())
            InsertSnippet(connection, "keep active data", "hash-keep");
        var backup = new BackupService(database, logger);

        Assert.ThrowsExactly<IOException>(() => backup.BackupTo(database.DatabaseFile + suffix));

        using SqliteConnection check = database.Open();
        Assert.AreEqual("keep active data", ExecuteScalar(check, "SELECT content FROM snippets"));
        Assert.AreEqual("ok", ExecuteScalar(check, "PRAGMA quick_check"));
    }

    private Db CreateMigratedDatabase(FileLogger logger)
    {
        var database = new Db(Path.Combine(_tempRoot, "source.db"));
        new MigrationRunner(database, logger).Run();
        return database;
    }

    private FileLogger CreateLogger()
    {
        string logs = Path.Combine(_tempRoot, "Logs");
        Directory.CreateDirectory(logs);
        return new FileLogger(logs);
    }

    private static object? ExecuteScalar(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static void InsertSnippet(
        SqliteConnection connection,
        string content,
        string contentHash)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO snippets (content, content_hash, created_at, updated_at)
            VALUES ($content, $hash, $now, $now);
            """;
        command.Parameters.AddWithValue("$content", content);
        command.Parameters.AddWithValue("$hash", contentHash);
        command.Parameters.AddWithValue(
            "$now",
            DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }
}
