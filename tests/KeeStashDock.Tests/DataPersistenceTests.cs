using System.Globalization;
using System.Security.Cryptography;
using KeeStashDock.Core.Services;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Database;
using KeeStashDock.Infrastructure.Logging;
using KeeStashDock.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;

namespace KeeStashDock.Tests;

[TestClass]
[DoNotParallelize]
public sealed class DataPersistenceTests
{
    private static readonly string TempParent = Path.Combine(
        Path.GetTempPath(),
        "KeeStashDock.Tests",
        "DataPersistence");

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
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [TestMethod]
    public void CopyPinAndEdit_PreserveMonotonicUseCount()
    {
        (SnippetRepository repository, SnippetService service, _) = CreateServices();
        long id = service.Add("initial", SnippetSource.Manual);
        Snippet model = repository.GetAllLive().Single();

        Assert.IsFalse(service.RegisterUse(model));
        service.TogglePin(model);
        model.Content = "edited";
        service.Update(model);
        service.RegisterUse(model);

        Snippet stored = repository.GetAllLive().Single(snippet => snippet.Id == id);
        Assert.AreEqual(2, stored.UseCount);
        Assert.AreEqual(2, model.UseCount);
        Assert.IsTrue(stored.IsPinned);
        Assert.AreEqual("edited", stored.Content);
        Assert.IsNotNull(stored.LastUsedAt);
        Assert.AreEqual(DateTimeKind.Utc, stored.LastUsedAt.Value.Kind);
    }

    [TestMethod]
    public void StaleEntities_UpdateDifferentFieldsWithoutOverwritingEachOther()
    {
        (SnippetRepository repository, SnippetService service, _) = CreateServices();
        service.Add("initial", SnippetSource.Manual);
        Snippet stalePin = repository.GetAllLive().Single();
        Snippet staleContent = repository.GetAllLive().Single();

        service.TogglePin(stalePin);
        staleContent.Content = "changed by editor";
        service.Update(staleContent);

        Snippet stored = repository.GetAllLive().Single();
        Assert.IsTrue(stored.IsPinned);
        Assert.AreEqual("changed by editor", stored.Content);
        Assert.AreEqual(0, stored.UseCount);
    }

    [TestMethod]
    public async Task ConcurrentRegisterUse_ReturnsEveryMonotonicCount()
    {
        (SnippetRepository repository, SnippetService service, _) = CreateServices();
        long id = service.Add("count me", SnippetSource.Manual);

        Task<RegisterUseResult>[] operations = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => repository.RegisterUse(id, moveToTrash: false)))
            .ToArray();
        RegisterUseResult[] results = await Task.WhenAll(operations);

        CollectionAssert.AreEqual(
            Enumerable.Range(1, 20).ToArray(),
            results.Select(result => result.UseCount).Order().ToArray());
        Assert.AreEqual(20, repository.GetAllLive().Single().UseCount);
    }

    [TestMethod]
    public void BurnAfterRead_RegisterUseSynchronizesModelAndDatabase()
    {
        (SnippetRepository repository, SnippetService service, _) = CreateServices();
        service.Add(
            "burn once",
            SnippetSource.Manual,
            expire: ExpirePolicy.BurnAfterRead);
        Snippet model = repository.GetAllLive().Single();

        bool movedToTrash = service.RegisterUse(model);

        Assert.IsTrue(movedToTrash);
        Assert.AreEqual(1, model.UseCount);
        Assert.IsNotNull(model.LastUsedAt);
        Assert.AreEqual(model.LastUsedAt, model.DeletedAt);
        Snippet stored = repository.GetTrash().Single();
        Assert.AreEqual(model.UseCount, stored.UseCount);
        Assert.AreEqual(model.LastUsedAt, stored.LastUsedAt);
        Assert.AreEqual(model.DeletedAt, stored.DeletedAt);
    }

    [TestMethod]
    public void NonUtcDateTimes_AreRejectedBeforePersistence()
    {
        (SnippetRepository repository, SnippetService service, string databaseFile) =
            CreateServices();
        var invalidInsert = new Snippet
        {
            Content = "local time",
            ContentHash = "hash-local",
            CreatedAt = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Local),
            UpdatedAt = DateTime.UtcNow,
        };

        Assert.ThrowsExactly<ArgumentException>(() => repository.Insert(invalidInsert));
        Assert.AreEqual(0, ReadInt(databaseFile, "SELECT COUNT(*) FROM snippets"));

        service.Add("valid", SnippetSource.Manual);
        Snippet existing = repository.GetAllLive().Single();
        existing.ExpireAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        Assert.ThrowsExactly<ArgumentException>(
            () => repository.UpdateContent(existing, DateTime.UtcNow));
        Assert.IsNull(repository.GetAllLive().Single().ExpireAt);
    }

    [TestMethod]
    public void VersionOneMixedTimestampFormats_MigrateToSortableUtcRoundTrip()
    {
        (SnippetRepository repository, SnippetService service, string databaseFile) =
            CreateServices();
        long laterId = service.Add("later", SnippetSource.Manual);
        long earlierId = service.Add("earlier", SnippetSource.Manual);
        DowngradeToVersionOne(databaseFile);
        Execute(databaseFile, $"""
            UPDATE snippets SET
                created_at = '2026-01-01 10:00:00.0000000',
                updated_at = '2026-01-01 10:00:00.0000000'
            WHERE id = {laterId};
            UPDATE snippets SET
                created_at = '2026-01-01T09:30:00.0000000Z',
                updated_at = '2026-01-01T09:30:00.0000000Z'
            WHERE id = {earlierId};
            """);

        new MigrationRunner(new Db(databaseFile), _logger).Run();

        string[] ordered = ReadStrings(
            databaseFile,
            "SELECT content FROM snippets ORDER BY updated_at ASC");
        CollectionAssert.AreEqual(new[] { "earlier", "later" }, ordered);
        string[] timestamps = ReadStrings(
            databaseFile,
            "SELECT updated_at FROM snippets ORDER BY id");
        Assert.IsTrue(timestamps.All(value => value.EndsWith('Z') && value.Contains('T')));
        Assert.IsTrue(timestamps.All(value =>
            DateTime.ParseExact(
                value,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind).Kind == DateTimeKind.Utc));
        Assert.AreEqual(MigrationRunner.LatestSupportedVersion,
            ReadInt(databaseFile, "PRAGMA user_version"));
        Assert.AreEqual(MigrationRunner.LatestSupportedVersion,
            ReadInt(databaseFile, "SELECT version FROM schema_version"));
    }

    [TestMethod]
    public void InvalidLegacyTimestamps_UseDocumentedSafeFallbacks()
    {
        (_, SnippetService service, string databaseFile) = CreateServices();
        long id = service.Add("invalid time", SnippetSource.Manual);
        DowngradeToVersionOne(databaseFile);
        Execute(databaseFile, $"""
            UPDATE snippets SET
                created_at = 'invalid-created',
                updated_at = 'invalid-updated',
                expire_at = 'invalid-expire',
                deleted_at = 'invalid-deleted'
            WHERE id = {id};
            """);

        new MigrationRunner(new Db(databaseFile), _logger).Run();

        Assert.AreEqual(
            UtcTimestamp.InvalidRequiredFallback,
            ReadText(databaseFile, "SELECT created_at FROM snippets"));
        Assert.AreEqual(
            UtcTimestamp.InvalidRequiredFallback,
            ReadText(databaseFile, "SELECT updated_at FROM snippets"));
        Assert.AreEqual(1, ReadInt(databaseFile,
            "SELECT COUNT(*) FROM snippets WHERE expire_at IS NULL AND deleted_at IS NULL"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FutureSchema_IsRejectedWithoutChangingDatabaseBytes(
        bool futureUserVersion)
    {
        (_, SnippetService service, string databaseFile) = CreateServices();
        service.Add("future", SnippetSource.Manual);
        Execute(databaseFile, $"""
            UPDATE schema_version SET version = 99;
            PRAGMA user_version = {(futureUserVersion ? 99 : MigrationRunner.LatestSupportedVersion)};
            """);
        SqliteConnection.ClearAllPools();
        byte[] before = SHA256.HashData(File.ReadAllBytes(databaseFile));

        Assert.ThrowsExactly<InvalidOperationException>(
            () => new MigrationRunner(new Db(databaseFile), _logger).Run());

        byte[] after = SHA256.HashData(File.ReadAllBytes(databaseFile));
        CollectionAssert.AreEqual(before, after);
    }

    [TestMethod]
    public void InvalidRetentionCannotPurgeRecentTrash()
    {
        (SnippetRepository repository, SnippetService service, string databaseFile) =
            CreateServices();
        long recent = service.Add("recent trash", SnippetSource.Manual);
        long old = service.Add("old trash", SnippetSource.Manual);
        service.MoveToTrash(recent);
        service.MoveToTrash(old);
        Execute(databaseFile, $"""
            UPDATE snippets SET deleted_at = '{UtcTimestamp.Format(DateTime.UtcNow.AddDays(-1))}'
            WHERE id = {recent};
            UPDATE snippets SET deleted_at = '{UtcTimestamp.Format(DateTime.UtcNow.AddDays(-40))}'
            WHERE id = {old};
            """);

        int purged = repository.PurgeTrash(retentionDays: 0);

        Assert.AreEqual(1, purged);
        Assert.AreEqual(recent, repository.GetTrash().Single().Id);
    }

    private (SnippetRepository Repository, SnippetService Service, string DatabaseFile)
        CreateServices()
    {
        string databaseFile = Path.Combine(_root, "snippets.db");
        var database = new Db(databaseFile);
        new MigrationRunner(database, _logger).Run();
        var repository = new SnippetRepository(database);
        var service = new SnippetService(repository, _logger);
        return (repository, service, databaseFile);
    }

    private static void DowngradeToVersionOne(string databaseFile)
    {
        Execute(databaseFile, """
            ALTER TABLE snippets DROP COLUMN last_used_at;
            UPDATE schema_version SET version = 1;
            PRAGMA user_version = 1;
            """);
    }

    private static void Execute(string databaseFile, string sql)
    {
        SqliteConnection.ClearAllPools();
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = databaseFile,
                Pooling = false,
            }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static int ReadInt(string databaseFile, string sql)
        => Convert.ToInt32(ReadScalar(databaseFile, sql), CultureInfo.InvariantCulture);

    private static string ReadText(string databaseFile, string sql)
        => Convert.ToString(ReadScalar(databaseFile, sql), CultureInfo.InvariantCulture)
            ?? string.Empty;

    private static object? ReadScalar(string databaseFile, string sql)
    {
        using SqliteConnection connection = OpenReadOnly(databaseFile);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static string[] ReadStrings(string databaseFile, string sql)
    {
        using SqliteConnection connection = OpenReadOnly(databaseFile);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using SqliteDataReader reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }

        return values.ToArray();
    }

    private static SqliteConnection OpenReadOnly(string databaseFile)
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = databaseFile,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());
        connection.Open();
        return connection;
    }
}
