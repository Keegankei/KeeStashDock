using System.Globalization;
using System.Text;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Database;
using KeeStashDock.Infrastructure.Logging;
using KeeStashDock.Infrastructure.Repositories;
using KeeStashDock.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace KeeStashDock.Tests;

[TestClass]
[DoNotParallelize]
public sealed class DatabaseImportTests
{
    private static readonly string TempParent = Path.Combine(
        Path.GetTempPath(),
        "KeeStashDock.Tests",
        "DatabaseImport");

    private string _root = null!;
    private FileLogger _logger = null!;

    [TestInitialize]
    public void Initialize()
    {
        Directory.CreateDirectory(TempParent);
        _root = Path.Combine(TempParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        string logs = Path.Combine(_root, "Logs");
        Directory.CreateDirectory(logs);
        _logger = new FileLogger(logs);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _logger.Dispose();
        SqliteConnection.ClearAllPools();
        if (!Directory.Exists(_root))
        {
            return;
        }

        string parent = Path.GetFullPath(TempParent)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string target = Path.GetFullPath(_root);
        Assert.IsTrue(target.StartsWith(parent, StringComparison.OrdinalIgnoreCase));
        Directory.Delete(target, recursive: true);
    }

    [TestMethod]
    public void CurrentDatabaseImport_ReplacesDataAndRejectsOldObjectWrites()
    {
        ImportContext context = CreateContext();
        CreateDatabase(context.ActiveFile, "original");
        string source = Path.Combine(_root, "current.db");
        CreateDatabase(source, "imported");

        var oldRepository = new SnippetRepository(context.Database);
        Snippet oldObject = oldRepository.GetAllLive().Single();

        DatabaseImportResult result = context.Service.Import(source);

        Assert.AreEqual("imported", ReadOnlyMarker(context.ActiveFile));
        Assert.AreEqual(MigrationRunner.LatestSupportedVersion, result.ImportedSchemaVersion);
        Assert.IsTrue(File.Exists(result.SafetyBackupFile));
        Assert.IsTrue(context.Restarter.WasCalled);
        Assert.IsTrue(context.Coordinator.AreOperationsDisabled);
        Assert.ThrowsExactly<DatabaseOperationsDisabledException>(
            () => oldRepository.UpdateContent(oldObject, DateTime.UtcNow));
        Assert.AreEqual("imported", ReadOnlyMarker(context.ActiveFile));
        AssertNoImportTemporaryFiles();
    }

    [TestMethod]
    public void LegacyUserVersionZero_IsMigratedBeforeImport()
    {
        ImportContext context = CreateContext();
        CreateDatabase(context.ActiveFile, "original");
        string source = Path.Combine(_root, "legacy.db");
        CreateDatabase(source, "legacy");
        Execute(source, "PRAGMA user_version = 0");

        context.Service.Import(source);

        Assert.AreEqual("legacy", ReadOnlyMarker(context.ActiveFile));
        Assert.AreEqual(
            MigrationRunner.LatestSupportedVersion,
            ReadOnlyInt(context.ActiveFile, "PRAGMA user_version"));
        AssertNoImportTemporaryFiles();
    }

    [TestMethod]
    public void LegacyGroupNames_AreRepairedOnStagingWithoutMergingGroupsOrChangingTheSource()
    {
        ImportContext context = CreateContext();
        CreateDatabase(context.ActiveFile, "original");
        string source = Path.Combine(_root, "legacy-groups.db");
        CreateDatabase(source, "legacy content");
        Execute(source, """
            DROP TRIGGER trg_category_name_insert;
            DROP TRIGGER trg_category_name_update;
            DROP INDEX idx_categories_name_key;
            ALTER TABLE categories DROP COLUMN name_key;
            UPDATE schema_version SET version = 2;
            PRAGMA user_version = 2;
            INSERT INTO categories(id, name) VALUES
                (10, ' Team '), (20, 'team'), (30, 'Team (2)'), (40, '  '), (50, 'Group');
            UPDATE snippets SET category_id = 20;
            """);

        DatabaseImportResult result = context.Service.Import(source);

        Assert.AreEqual(MigrationRunner.LatestSupportedVersion, result.ImportedSchemaVersion);
        Assert.AreEqual(5, ReadOnlyInt(context.ActiveFile, "SELECT COUNT(*) FROM categories"));
        Assert.AreEqual("Team", ReadOnlyText(context.ActiveFile, "SELECT name FROM categories WHERE id = 10"));
        Assert.AreEqual("team (3)", ReadOnlyText(context.ActiveFile, "SELECT name FROM categories WHERE id = 20"));
        Assert.AreEqual("Team (2)", ReadOnlyText(context.ActiveFile, "SELECT name FROM categories WHERE id = 30"));
        Assert.AreEqual("Group (2)", ReadOnlyText(context.ActiveFile, "SELECT name FROM categories WHERE id = 40"));
        Assert.AreEqual("Group", ReadOnlyText(context.ActiveFile, "SELECT name FROM categories WHERE id = 50"));
        Assert.AreEqual(20, ReadOnlyInt(context.ActiveFile, "SELECT category_id FROM snippets"));
        Assert.AreEqual("legacy content", ReadOnlyMarker(context.ActiveFile));
        Assert.AreEqual(2, ReadOnlyInt(source, "SELECT version FROM schema_version"));
        Assert.AreEqual(" Team ", ReadOnlyText(source, "SELECT name FROM categories WHERE id = 10"));
        Assert.AreEqual("original", ReadOnlyMarker(result.SafetyBackupFile));
        AssertNoImportTemporaryFiles();
    }

    [TestMethod]
    public void SameVolumeMoveFallback_ImportsWithoutOverwriteCopy()
    {
        ImportContext context = CreateContext(
            files: new PhysicalDatabaseFileOperations(forceMoveFallback: true));
        CreateDatabase(context.ActiveFile, "original");
        string source = Path.Combine(_root, "fallback.db");
        CreateDatabase(source, "fallback");

        context.Service.Import(source);

        Assert.AreEqual("fallback", ReadOnlyMarker(context.ActiveFile));
        Assert.IsTrue(context.Coordinator.AreOperationsDisabled);
        AssertNoImportTemporaryFiles();
    }

    [TestMethod]
    [DataRow(InvalidFileKind.Empty)]
    [DataRow(InvalidFileKind.RandomText)]
    [DataRow(InvalidFileKind.TruncatedSqlite)]
    [DataRow(InvalidFileKind.CorruptedSqlite)]
    public void InvalidFiles_AreRejectedBeforeReplacement(InvalidFileKind kind)
    {
        ImportContext context = CreateContext();
        CreateDatabase(context.ActiveFile, "original");
        string source = Path.Combine(_root, $"invalid-{kind}.db");
        CreateInvalidFile(source, kind);

        Assert.ThrowsExactly<DatabaseImportException>(
            () => context.Service.Import(source));

        Assert.AreEqual("original", ReadOnlyMarker(context.ActiveFile));
        Assert.IsFalse(context.Restarter.WasCalled);
        Assert.IsFalse(context.Coordinator.AreOperationsDisabled);
        AssertNoImportTemporaryFiles();
    }

    [TestMethod]
    [DataRow(InvalidSchemaKind.MissingTable)]
    [DataRow(InvalidSchemaKind.MissingColumn)]
    [DataRow(InvalidSchemaKind.WrongFtsDefinition)]
    [DataRow(InvalidSchemaKind.FutureVersion)]
    [DataRow(InvalidSchemaKind.MissingGroupNameIndex)]
    [DataRow(InvalidSchemaKind.WrongGroupNameIndex)]
    [DataRow(InvalidSchemaKind.InvalidGroupNameKey)]
    public void InvalidSchemas_AreRejectedBeforeReplacement(InvalidSchemaKind kind)
    {
        ImportContext context = CreateContext();
        CreateDatabase(context.ActiveFile, "original");
        string source = Path.Combine(_root, $"schema-{kind}.db");
        CreateDatabase(source, "candidate");
        DamageSchema(source, kind);

        DatabaseImportException exception = Assert.ThrowsExactly<DatabaseImportException>(
            () => context.Service.Import(source));

        if (kind == InvalidSchemaKind.FutureVersion)
        {
            StringAssert.Contains(exception.Message, "newer KeeStashDock version");
        }

        Assert.AreEqual("original", ReadOnlyMarker(context.ActiveFile));
        Assert.IsFalse(context.Restarter.WasCalled);
        AssertNoImportTemporaryFiles();
    }

    [TestMethod]
    public void ActiveDatabasePath_CannotBeImportedIntoItself()
    {
        ImportContext context = CreateContext();
        CreateDatabase(context.ActiveFile, "original");

        Assert.ThrowsExactly<DatabaseImportException>(
            () => context.Service.Import(context.ActiveFile));

        Assert.AreEqual("original", ReadOnlyMarker(context.ActiveFile));
        Assert.IsFalse(context.Restarter.WasCalled);
        AssertNoImportTemporaryFiles();
    }

    [TestMethod]
    public void CancellationAfterStaging_CleansTemporaryFilesWithoutReplacement()
    {
        using var cancellation = new CancellationTokenSource();
        var observer = new CallbackObserver(stage =>
        {
            if (stage == DatabaseImportStage.AfterStaging)
            {
                cancellation.Cancel();
            }
        });
        ImportContext context = CreateContext(observer: observer);
        CreateDatabase(context.ActiveFile, "original");
        string source = Path.Combine(_root, "cancel.db");
        CreateDatabase(source, "candidate");

        Assert.ThrowsExactly<OperationCanceledException>(
            () => context.Service.Import(source, cancellation.Token));

        Assert.AreEqual("original", ReadOnlyMarker(context.ActiveFile));
        Assert.IsFalse(context.Restarter.WasCalled);
        Assert.IsFalse(context.Coordinator.AreOperationsDisabled);
        AssertNoImportTemporaryFiles();
    }

    [TestMethod]
    [DataRow(DatabaseImportStage.AfterSafetyBackup)]
    [DataRow(DatabaseImportStage.BeforeReplacement)]
    [DataRow(DatabaseImportStage.AfterReplacement)]
    [DataRow(DatabaseImportStage.BeforeRestart)]
    public void InjectedFailures_KeepOriginalDatabase(
        DatabaseImportStage failureStage)
    {
        var observer = new CallbackObserver(stage =>
        {
            if (stage == failureStage)
            {
                throw new IOException($"Injected failure at {stage}");
            }
        });
        ImportContext context = CreateContext(observer: observer);
        CreateDatabase(context.ActiveFile, "original");
        string source = Path.Combine(_root, "replacement.db");
        CreateDatabase(source, "replacement");

        Assert.ThrowsExactly<DatabaseImportException>(
            () => context.Service.Import(source));

        Assert.AreEqual("original", ReadOnlyMarker(context.ActiveFile));
        Assert.AreEqual(
            "ok",
            ReadOnlyText(context.ActiveFile, "PRAGMA quick_check"));
        Assert.IsFalse(context.Restarter.WasCalled);
        Assert.IsFalse(context.Coordinator.AreOperationsDisabled);
        AssertNoImportTemporaryFiles();
    }

    [TestMethod]
    public async Task Maintenance_WaitsForConnectionsAndBlocksQueuedHousekeepingWrite()
    {
        using var staged = new ManualResetEventSlim();
        using var maintenance = new ManualResetEventSlim();
        using var releaseRestart = new ManualResetEventSlim();
        var observer = new CallbackObserver(stage =>
        {
            if (stage == DatabaseImportStage.AfterStaging)
            {
                staged.Set();
            }

            if (stage == DatabaseImportStage.AfterSafetyBackup)
            {
                maintenance.Set();
            }
        });
        var restarter = new FakeRestarter(() =>
        {
            releaseRestart.Wait(TimeSpan.FromSeconds(10));
            return true;
        });
        ImportContext context = CreateContext(restarter, observer);
        CreateDatabase(context.ActiveFile, "original");
        string source = Path.Combine(_root, "replacement.db");
        CreateDatabase(source, "replacement");

        using SqliteConnection openConnection = context.Database.Open();
        Task<DatabaseImportResult> importTask = context.Service.ImportAsync(source);
        try
        {
            Assert.IsTrue(staged.Wait(TimeSpan.FromSeconds(5)));
            await Task.Delay(100);
            Assert.IsFalse(importTask.IsCompleted);

            openConnection.Dispose();
            Assert.IsTrue(maintenance.Wait(TimeSpan.FromSeconds(5)));

            var repository = new SnippetRepository(context.Database);
            Task<int> queuedHousekeeping = Task.Run(repository.MoveExpiredToTrash);
            await Task.Delay(100);
            Assert.IsFalse(queuedHousekeeping.IsCompleted);

            releaseRestart.Set();
            await importTask;
            await Assert.ThrowsExactlyAsync<DatabaseOperationsDisabledException>(async () =>
            {
                await queuedHousekeeping;
            });
            Assert.AreEqual("replacement", ReadOnlyMarker(context.ActiveFile));
            AssertNoImportTemporaryFiles();
        }
        finally
        {
            releaseRestart.Set();
            openConnection.Dispose();
        }
    }

    [TestMethod]
    public void RestartFailure_RestoresOriginalAndPermanentlyDisablesOperations()
    {
        var restarter = new FakeRestarter(() => false);
        ImportContext context = CreateContext(restarter);
        CreateDatabase(context.ActiveFile, "original");
        string source = Path.Combine(_root, "replacement.db");
        CreateDatabase(source, "replacement");

        Assert.ThrowsExactly<DatabaseRestartRequiredException>(
            () => context.Service.Import(source));

        Assert.AreEqual("original", ReadOnlyMarker(context.ActiveFile));
        Assert.AreEqual(
            "ok",
            ReadOnlyText(context.ActiveFile, "PRAGMA quick_check"));
        Assert.IsTrue(restarter.WasCalled);
        Assert.IsTrue(context.Coordinator.AreOperationsDisabled);
        Assert.ThrowsExactly<DatabaseOperationsDisabledException>(
            () => context.Database.Open());
        AssertNoImportTemporaryFiles();
    }

    private ImportContext CreateContext(
        FakeRestarter? restarter = null,
        IDatabaseImportObserver? observer = null,
        IDatabaseFileOperations? files = null)
    {
        string active = Path.Combine(_root, "Data", "keestashdock.db");
        Directory.CreateDirectory(Path.GetDirectoryName(active)!);
        var coordinator = new DatabaseMaintenanceCoordinator();
        var database = new Db(active, coordinator);
        var backup = new BackupService(database, _logger);
        var actualRestarter = restarter ?? new FakeRestarter(() => true);
        var service = new DatabaseImportService(
            database,
            coordinator,
            backup,
            new DatabaseSchemaValidator(),
            files ?? new PhysicalDatabaseFileOperations(),
            actualRestarter,
            _logger,
            observer);
        return new ImportContext(
            active,
            coordinator,
            database,
            actualRestarter,
            service);
    }

    private void CreateDatabase(string path, string marker)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var database = new Db(path);
        new MigrationRunner(database, _logger).Run();
        using (SqliteConnection connection = database.Open())
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO snippets (content, content_hash, created_at, updated_at)
                VALUES ($content, $hash, $now, $now)
                """;
            command.Parameters.AddWithValue("$content", marker);
            command.Parameters.AddWithValue("$hash", "hash-" + marker);
            command.Parameters.AddWithValue(
                "$now",
                DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
    }

    private static void CreateInvalidFile(string path, InvalidFileKind kind)
    {
        switch (kind)
        {
            case InvalidFileKind.Empty:
                using (File.Create(path))
                {
                }
                break;
            case InvalidFileKind.RandomText:
                File.WriteAllText(path, "not a sqlite database", Encoding.UTF8);
                break;
            case InvalidFileKind.TruncatedSqlite:
                CreateStandaloneDatabase(path, rows: 1);
                byte[] complete = File.ReadAllBytes(path);
                File.WriteAllBytes(path, complete[..Math.Min(100, complete.Length)]);
                break;
            case InvalidFileKind.CorruptedSqlite:
                CreateStandaloneDatabase(path, rows: 400);
                byte[] corrupted = File.ReadAllBytes(path);
                int offset = Math.Min(corrupted.Length - 64, 4096 + 128);
                Array.Fill(corrupted, (byte)0xFF, offset, 32);
                File.WriteAllBytes(path, corrupted);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static void CreateStandaloneDatabase(string path, int rows)
    {
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Pooling = false,
            }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE payload(value TEXT NOT NULL)";
        command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO payload(value) VALUES ($value)";
        SqliteParameter parameter = command.Parameters.Add("$value", SqliteType.Text);
        for (int i = 0; i < rows; i++)
        {
            parameter.Value = new string((char)('A' + (i % 20)), 200);
            command.ExecuteNonQuery();
        }
    }

    private static void DamageSchema(string path, InvalidSchemaKind kind)
    {
        string sql = kind switch
        {
            InvalidSchemaKind.MissingTable => "DROP TABLE tags",
            InvalidSchemaKind.MissingColumn => "ALTER TABLE snippets DROP COLUMN use_count",
            InvalidSchemaKind.WrongFtsDefinition => """
                DROP TRIGGER trg_snip_ai;
                DROP TRIGGER trg_snip_ad;
                DROP TRIGGER trg_snip_au;
                DROP TABLE snippets_fts;
                CREATE TABLE snippets_fts(title TEXT, content TEXT);
                """,
            InvalidSchemaKind.FutureVersion => "PRAGMA user_version = 999",
            InvalidSchemaKind.MissingGroupNameIndex => "DROP INDEX idx_categories_name_key",
            InvalidSchemaKind.WrongGroupNameIndex => """
                DROP INDEX idx_categories_name_key;
                CREATE UNIQUE INDEX idx_categories_name_key ON categories(color);
                """,
            InvalidSchemaKind.InvalidGroupNameKey => """
                INSERT INTO categories(name, name_key) VALUES ('Team', 'incorrect key');
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        Execute(path, sql);
    }

    private static void Execute(string path, string sql)
    {
        SqliteConnection.ClearAllPools();
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Pooling = false,
            }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string ReadOnlyMarker(string path)
        => ReadOnlyText(path, "SELECT content FROM snippets ORDER BY id LIMIT 1");

    private static int ReadOnlyInt(string path, string sql)
        => Convert.ToInt32(ReadOnlyScalar(path, sql), CultureInfo.InvariantCulture);

    private static string ReadOnlyText(string path, string sql)
        => Convert.ToString(ReadOnlyScalar(path, sql), CultureInfo.InvariantCulture)
            ?? string.Empty;

    private static object? ReadOnlyScalar(string path, string sql)
    {
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private void AssertNoImportTemporaryFiles()
    {
        string dataDirectory = Path.Combine(_root, "Data");
        if (!Directory.Exists(dataDirectory))
        {
            return;
        }

        string[] temporary = Directory.GetFiles(dataDirectory, ".*.db");
        Assert.AreEqual(
            0,
            temporary.Length,
            "Import temporary files remained: " + string.Join(", ", temporary));
    }

    public enum InvalidFileKind
    {
        Empty,
        RandomText,
        TruncatedSqlite,
        CorruptedSqlite,
    }

    public enum InvalidSchemaKind
    {
        MissingTable,
        MissingColumn,
        WrongFtsDefinition,
        FutureVersion,
        MissingGroupNameIndex,
        WrongGroupNameIndex,
        InvalidGroupNameKey,
    }

    private sealed record ImportContext(
        string ActiveFile,
        DatabaseMaintenanceCoordinator Coordinator,
        Db Database,
        FakeRestarter Restarter,
        DatabaseImportService Service);

    private sealed class FakeRestarter : IApplicationRestarter
    {
        private readonly Func<bool> _restart;

        public FakeRestarter(Func<bool> restart) => _restart = restart;

        public bool WasCalled { get; private set; }

        public bool TryRestart()
        {
            WasCalled = true;
            return _restart();
        }
    }

    private sealed class CallbackObserver : IDatabaseImportObserver
    {
        private readonly Action<DatabaseImportStage> _callback;

        public CallbackObserver(Action<DatabaseImportStage> callback)
            => _callback = callback;

        public void OnStage(DatabaseImportStage stage) => _callback(stage);
    }
}
