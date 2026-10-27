using KeeStashDock.Core.Services;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Database;
using KeeStashDock.Infrastructure.Logging;
using KeeStashDock.Infrastructure.Repositories;
using KeeStashDock.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KeeStashDock.Tests;

[TestClass]
[DoNotParallelize]
public sealed class LibraryOrganizationTests
{
    private static readonly string Parent = Path.Combine(Path.GetTempPath(), "KeeStashDock.Tests", "LibraryOrganization");
    private string _root = null!;
    private FileLogger _log = null!;
    private Db _db = null!;
    private SnippetRepository _repository = null!;
    private SnippetService _service = null!;
    private CategoryRepository _categories = null!;
    private TagRepository _tags = null!;
    private SearchService _search = null!;
    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _log = new FileLogger(Path.Combine(_root, "Logs"));
        _db = new Db(Path.Combine(_root, "library.db"));
        new MigrationRunner(_db, _log).Run();
        _repository = new SnippetRepository(_db);
        _service = new SnippetService(_repository, _log);
        _categories = new CategoryRepository(_db);
        _tags = new TagRepository(_db);
        _search = new SearchService(_repository);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _log.Dispose();
        SqliteConnection.ClearAllPools();
        Assert.AreEqual(Path.GetFullPath(Parent), Path.GetDirectoryName(Path.GetFullPath(_root)));
        Assert.IsTrue(Guid.TryParseExact(Path.GetFileName(_root), "N", out _));
        Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public void UsageSnapshot_RecordsOnceAndHonorsBurnAfterRead()
    {
        long id = _service.Add("One use {{name}}", SnippetSource.Manual, expire: ExpirePolicy.BurnAfterRead, isTemplate: true);
        Snippet item = _service.GetById(id)!;
        BatchSelection token = _service.CaptureUse(item)!;
        Assert.IsNotNull(token);
        Assert.IsTrue(_service.TryRegisterUse(item, token));
        Assert.AreEqual(1, item.UseCount);
        Assert.IsTrue(item.IsDeleted);
        Assert.IsFalse(_service.TryRegisterUse(item, token), "Reusing the completed-output token must not count or burn twice.");
        Assert.AreEqual(1, _service.GetById(id)!.UseCount);
    }

    [TestMethod]
    public void UsageSnapshot_DatabaseReplacementCannotCountOrBurnSameId()
    {
        long id = _service.Add("A template {{name}}", SnippetSource.Manual, expire: ExpirePolicy.BurnAfterRead, isTemplate: true);
        Snippet item = _service.GetById(id)!;
        BatchSelection token = _service.CaptureUse(item)!;
        Execute("UPDATE database_metadata SET identity='replacement-library'");
        Assert.IsFalse(_service.TryRegisterUse(item, token));
        Snippet unchanged = _service.GetById(id)!;
        Assert.AreEqual(0, unchanged.UseCount);
        Assert.IsFalse(unchanged.IsDeleted);
    }

    [TestMethod]
    public void UsageSnapshot_ChangedOrDeletedItemsDoNotFailCompletedOutput()
    {
        long id = _service.Add("An output {{name}}", SnippetSource.Manual, isTemplate: true);
        Snippet item = _service.GetById(id)!;
        BatchSelection token = _service.CaptureUse(item)!;
        _tags.SetForSnippet(id, ["changed-while-dialog-open"]);
        Assert.IsFalse(_service.TryRegisterUse(item, token));
        Assert.AreEqual(0, _service.GetById(id)!.UseCount);
        token = _service.CaptureUse(item)!;
        _service.DeleteForever(id);
        Assert.IsFalse(_service.TryRegisterUse(item, token));
        Assert.IsNull(_service.CaptureUse(item));
        Assert.AreEqual(0, item.UseCount);
    }

    [TestMethod]
    public void UsageSnapshot_LateDatabaseFailureIsBestEffort()
    {
        long id = _service.Add("Already copied {{name}}", SnippetSource.Manual, isTemplate: true);
        Snippet item = _service.GetById(id)!;
        BatchSelection token = _service.CaptureUse(item)!;
        Execute("DELETE FROM database_metadata");
        Assert.IsFalse(_service.TryRegisterUse(item, token), "Accounting failure must not throw after a successful clipboard write.");
        Assert.AreEqual(0, _service.GetById(id)!.UseCount);
    }

    [TestMethod]
    public void TemplateAndOrdinaryIdenticalText_HaveIndependentIdentityAndRoundTrip()
    {
        long ordinary = _service.Add("Hello {{name}}", SnippetSource.Manual);
        long template = _service.Add("Hello {{name}}", SnippetSource.Manual, isTemplate: true);
        Assert.AreNotEqual(ordinary, template);
        Assert.AreEqual(template, _service.Add("Hello {{name}}", SnippetSource.Manual, isTemplate: true));
        Assert.IsTrue(_service.GetById(template)!.IsTemplate);
        Assert.IsFalse(_service.GetById(ordinary)!.IsTemplate);
        var edited = _service.GetById(ordinary)!;
        edited.IsTemplate = true;
        Assert.ThrowsExactly<DuplicateSnippetContentException>(() => _service.Update(edited));
        Assert.IsFalse(_service.GetById(ordinary)!.IsTemplate);
    }

    [TestMethod]
    public void GroupV3_PreservesTemplatesAndLegacyDoesNotInferThem()
    {
        long group = _categories.Insert(new Category { Name = "Templates" });
        _service.Add("Hello {{name}}", SnippetSource.Manual, categoryId: group, isTemplate: true);
        var io = new GroupIoService(_service, _categories);
        string file = Path.Combine(_root, "group.ksdgrp");
        io.ExportGroup(group, "Templates", file);
        var package = io.ReadPackage(file);
        Assert.AreEqual(3, package.Version);
        Assert.IsTrue(package.Items.Single().IsTemplate);
        io.ImportGroup(package, "Restored templates", null);
        long restoredGroup = _categories.FindByName("Restored templates")!.Id;
        Assert.IsTrue(_service.ByCategory(restoredGroup).Single().IsTemplate);
        io.ImportGroup(new GroupIoService.GroupPackage(2, "Legacy", [new(null, "Hello {{name}}", IsTemplate: true)]), "Legacy", null);
        Assert.IsFalse(_service.ByCategory(_categories.FindByName("Legacy")!.Id).Single().IsTemplate);
    }

    [TestMethod]
    public void TemplateBackup_PreservesFlagHashAndRawText()
    {
        long id = _service.Add("Hello {{name}} {{date}}", SnippetSource.Manual, isTemplate: true);
        string file = Path.Combine(_root, "template-backup.db");
        new BackupService(_db, _log).BackupTo(file);
        var restored = new SnippetRepository(new Db(file)).FindById(id)!;
        Assert.IsTrue(restored.IsTemplate);
        Assert.AreEqual(_service.GetById(id)!.ContentHash, restored.ContentHash);
        Assert.AreEqual("Hello {{name}} {{date}}", restored.Content);
        Assert.AreEqual(MigrationRunner.LatestSupportedVersion, new DatabaseSchemaValidator().Validate(file, true).SchemaVersion);
    }

    [TestMethod]
    public void GroupV3_InvalidTemplateRejectsBeforeCreatingOrReplacingGroup()
    {
        long group = _categories.Insert(new Category { Name = "Original" });
        _service.Add("Original entry", SnippetSource.Manual, categoryId: group);
        var io = new GroupIoService(_service, _categories);
        var package = new GroupIoService.GroupPackage(3, "Imported",
            [new(null, "valid {{name}}", IsTemplate: true), new(null, "https://example.org", SnippetType.Link, IsTemplate: true)]);
        Assert.ThrowsExactly<InvalidDataException>(() => io.ImportGroup(package, "Original", group));
        Assert.AreEqual("Original entry", _service.ByCategory(group).Single().Content);
        Assert.AreEqual(1, _categories.GetAll().Count);
    }

    [TestMethod]
    public void Query_CombinesScopeTagAndKindBeforeStablePagination()
    {
        long group = _categories.Insert(new Category { Name = "Wanted" });
        long other = _categories.Insert(new Category { Name = "Other" });
        for (int i = 0; i < 120; i++) _service.Add("needle other " + i, SnippetSource.Manual, categoryId: other);
        var ids = new List<long>();
        for (int i = 0; i < 57; i++)
        {
            long id = _service.Add("needle wanted " + i, SnippetSource.Manual, categoryId: group, isTemplate: true);
            _tags.SetForSnippet(id, ["work", "keep"]);
            ids.Add(id);
        }
        Execute("UPDATE snippets SET updated_at='2026-01-01T00:00:00.0000000Z'");
        var query = new SnippetQuery { Text = "needle", Scope = QueryScope.Category, CategoryId = group, Tag = "work", Kind = SnippetKindFilter.Template, Limit = 50 };
        var first = _search.Query(query);
        var second = _search.Query(query with { Offset = 50 });
        Assert.AreEqual(57, first.TotalCount);
        Assert.IsTrue(first.HasMore);
        Assert.IsFalse(second.HasMore);
        CollectionAssert.AreEqual(ids.OrderDescending().ToArray(), first.Items.Concat(second.Items).Select(item => item.Id).ToArray());
        Assert.AreEqual(first.DatabaseIdentity, second.DatabaseIdentity);
    }

    [TestMethod]
    [DataRow("\u4e2d\u6587\u7247\u6bb5", "\u4e2d\u6587")]
    [DataRow("\u65e5\u672c\u8a9e\u30e1\u30e2", "\u65e5\u672c")]
    [DataRow("\ud55c\uad6d\uc5b4 \uba54\ubaa8", "\ud55c\uad6d")]
    [DataRow("\u0645\u0631\u062d\u0628\u0627 \u0628\u0627\u0644\u0639\u0627\u0644\u0645", "\u0645\u0631\u062d\u0628\u0627")]
    public void Query_SupportsUnicodeAndUncategorized(string content, string query)
    {
        long id = _service.Add(content, SnippetSource.Manual);
        Assert.AreEqual(id, _search.Query(new SnippetQuery { Text = query, Scope = QueryScope.Uncategorized }).Items.Single().Id);
    }

    [TestMethod]
    public void Query_CancellationAndExpiredEntriesAreHandled()
    {
        long id = _service.Add("expired", SnippetSource.Manual);
        var item = _service.GetById(id)!;
        item.ExpireAt = DateTime.UtcNow.AddSeconds(-1);
        _service.Update(item);
        Assert.AreEqual(0, _search.Query(new SnippetQuery()).TotalCount);
        Assert.ThrowsExactly<OperationCanceledException>(() => _search.Query(new SnippetQuery(), new CancellationToken(true)));
    }

    [TestMethod]
    public void Query_ImageFilterSearchesOnlyStoredOcrAndDoesNotChangeAssets()
    {
        byte[] png = ImageSnippetTests.Png();
        long image = _service.AddImage(png, png, ocrText: "\u5df2\u8bc6\u522b screenshot words");
        _service.Add("\u5df2\u8bc6\u522b screenshot words", SnippetSource.Manual);
        var result = _search.Query(new SnippetQuery { Text = "\u5df2\u8bc6\u522b", Kind = SnippetKindFilter.Image });
        Assert.AreEqual(image, result.Items.Single().Id);
        CollectionAssert.AreEqual(png, _service.GetImage(image));
        Assert.IsFalse(result.Items.Single().IsTemplate);
        Assert.AreEqual(1, _search.Query(new SnippetQuery { Text = "screenshot", Kind = SnippetKindFilter.Text }).TotalCount);
    }

    [TestMethod]
    public async Task Query_CancelsWhileWaitingForAnotherDatabaseOperation()
    {
        using var heldConnection = _db.Open();
        using var cancellation = new CancellationTokenSource();
        Task<SnippetPage> pending = _search.QueryAsync(new SnippetQuery(), cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending);
    }

    [TestMethod]
    [TestCategory("Scale")]
    [DataRow(10000)]
    [DataRow(50000)]
    public void Query_MixedLibraryScaleRecordsLatencyAndPreservesPagination(int count)
    {
        SeedMixedLibrary(count);
        SqliteConnection.ClearAllPools();
        var queries = new[]
        {
            new SnippetQuery { Text = "benchmark", Limit = 50 },
            new SnippetQuery { Text = "\u6587\u672c", Kind = SnippetKindFilter.Image, Limit = 50 },
            new SnippetQuery { Scope = QueryScope.Category, CategoryId = 2, Kind = SnippetKindFilter.Template, Tag = "bench", Limit = 50 },
            new SnippetQuery { Scope = QueryScope.Pinned, Limit = 50 },
        };
        long allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
        long managedBefore = GC.GetTotalMemory(forceFullCollection: false);
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        long workingSetBefore = process.WorkingSet64;
        var watch = Stopwatch.StartNew();
        var first = _search.Query(queries[0]);
        double firstConnectionMilliseconds = watch.Elapsed.TotalMilliseconds;
        Assert.AreEqual(count, first.TotalCount);
        var timings = new List<double>();
        for (int i = 0; i < 40; i++)
        {
            var query = queries[i % queries.Length];
            watch.Restart();
            var page = _search.Query(query);
            timings.Add(watch.Elapsed.TotalMilliseconds);
            Assert.AreEqual(50, page.Items.Count);
            Assert.AreEqual(50, page.Items.Select(item => item.Id).Distinct().Count());
            Assert.IsTrue(page.HasMore);
        }
        var second = _search.Query(queries[0] with { Offset = 50 });
        Assert.IsFalse(first.Items.Select(item => item.Id).Intersect(second.Items.Select(item => item.Id)).Any());
        Assert.AreEqual(first.DatabaseIdentity, second.DatabaseIdentity);
        Assert.AreEqual(first.TotalCount, second.TotalCount);
        timings.Sort();
        process.Refresh();
        // This reports a fresh SQLite connection, not a cold operating-system file cache.
        // Timing is informational: CI hardware must not turn correctness into a flaky threshold.
        TestContext.WriteLine($"Rows={count}; OS={RuntimeInformation.OSDescription}; arch={RuntimeInformation.ProcessArchitecture}; logical CPUs={Environment.ProcessorCount}; runtime={RuntimeInformation.FrameworkDescription}; first-connection-ms={firstConnectionMilliseconds:F2}; warm-p50-ms={timings[19]:F2}; warm-p95-ms={timings[37]:F2}; warm-max-ms={timings[^1]:F2}; managed-allocation-bytes={GC.GetTotalAllocatedBytes(true) - allocationBefore}; managed-memory-delta-bytes={GC.GetTotalMemory(false) - managedBefore}; working-set-delta-bytes={process.WorkingSet64 - workingSetBefore}");
    }

    private void SeedMixedLibrary(int count)
    {
        byte[] png = ImageSnippetTests.Png();
        using var connection = _db.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO categories(id,name,name_key) VALUES(1,'First','FIRST'),(2,'Second','SECOND');
            INSERT INTO tags(id,name) VALUES(1,'bench');
            WITH RECURSIVE entries(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM entries WHERE n<$count)
            INSERT INTO snippets(id,title,content,content_hash,type,is_template,category_id,is_pinned,created_at,updated_at)
            SELECT n, 'Sample ' || n,
                'benchmark sample ' || n || CASE WHEN n%4=0 THEN $cjkText ELSE $rtlText END,
                CASE WHEN n%10=0 THEN $imageHash WHEN n%10=1 THEN 'template:' || printf('%064x',n) ELSE printf('%064x',n) END,
                CASE WHEN n%10=0 THEN 5 WHEN n%10=1 THEN 0 ELSE n%5 END,
                CASE WHEN n%10=1 THEN 1 ELSE 0 END, 1+n%2,
                CASE WHEN n%17=0 THEN 1 ELSE 0 END,$now,$now
            FROM entries;
            INSERT INTO snippet_images(snippet_id,png,thumbnail_png,width,height)
                SELECT id,$png,$png,1,1 FROM snippets WHERE type=5;
            INSERT INTO snippet_tags(snippet_id,tag_id) SELECT id,1 FROM snippets WHERE id%3=0;
            """;
        command.Parameters.AddWithValue("$count", count);
        command.Parameters.AddWithValue("$cjkText", " \u6587\u672c \u65e5\u672c\u8a9e \ud55c\uad6d\uc5b4");
        command.Parameters.AddWithValue("$rtlText", " English \u0627\u0644\u0639\u0631\u0628\u064a\u0629");
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$png", png);
        command.Parameters.AddWithValue("$imageHash", PngImageValidator.ImageHash(png));
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    [TestMethod]
    public void BatchTagsAndPin_PreserveOtherTagsAndNotifyOnce()
    {
        long a = _service.Add("a", SnippetSource.Manual), b = _service.Add("b", SnippetSource.Manual);
        _tags.SetForSnippet(a, ["one"]); _tags.SetForSnippet(b, ["two"]);
        int notifications = 0;
        _service.Changed += () => notifications++;
        _service.ApplyBatch(_service.CaptureBatchSelection([a, b]), BatchAction.AddTags, tags: ["shared"]);
        Assert.AreEqual(1, notifications);
        CollectionAssert.AreEqual(new[] { "one", "shared" }, _tags.GetForSnippet(a));
        CollectionAssert.AreEqual(new[] { "shared", "two" }, _tags.GetForSnippet(b));
        _service.ApplyBatch(_service.CaptureBatchSelection([a, b]), BatchAction.RemoveTags, tags: ["shared"]);
        CollectionAssert.AreEqual(new[] { "one" }, _tags.GetForSnippet(a));
        _service.ApplyBatch(_service.CaptureBatchSelection([a, b]), BatchAction.Pin);
        Assert.IsTrue(_service.GetById(a)!.IsPinned && _service.GetById(b)!.IsPinned);
    }

    [TestMethod]
    public void StaleSelectionsAndMissingGroup_AbortTheEntireBatch()
    {
        long a = _service.Add("a", SnippetSource.Manual), b = _service.Add("b", SnippetSource.Manual);
        var selected = _service.CaptureBatchSelection([a, b]);
        _repository.SetPinned(a, true);
        Assert.AreEqual(BatchConflictReason.ChangedItem, Assert.ThrowsExactly<BatchConflictException>(() => _service.ApplyBatch(selected, BatchAction.Trash)).Reason);
        Assert.AreEqual(2, _service.AllLive().Count);
        Assert.AreEqual(BatchConflictReason.DatabaseChanged, Assert.ThrowsExactly<BatchConflictException>(() => _service.CaptureBatchSelection([a], "old-database")).Reason);
        Assert.AreEqual(BatchConflictReason.MissingCategory, Assert.ThrowsExactly<BatchConflictException>(() => _service.ApplyBatch(_service.CaptureBatchSelection([a,b]), BatchAction.MoveToCategory, 123456)).Reason);
        Assert.IsNull(_service.GetById(a)!.CategoryId);
    }

    [TestMethod]
    public void TransactionFailure_DoesNotLeavePartialChangesOrNotifications()
    {
        long a = _service.Add("a", SnippetSource.Manual), b = _service.Add("b", SnippetSource.Manual);
        int notifications = 0;
        _service.Changed += () => notifications++;
        var selected = _service.CaptureBatchSelection([a,b]);
        Execute($"CREATE TRIGGER fail_batch BEFORE UPDATE ON snippets WHEN new.id={b} BEGIN SELECT RAISE(ABORT,'injected'); END;");
        Assert.ThrowsExactly<SqliteException>(() => _service.ApplyBatch(selected, BatchAction.Pin));
        Assert.IsFalse(_service.GetById(a)!.IsPinned || _service.GetById(b)!.IsPinned);
        Assert.AreEqual(0, notifications);
    }

    [TestMethod]
    public void TrashUndo_IsScopedAndRejectsSubsequentChanges()
    {
        long a = _service.Add("a", SnippetSource.Manual), b = _service.Add("b", SnippetSource.Manual);
        var result = _service.ApplyBatch(_service.CaptureBatchSelection([a,b]), BatchAction.Trash);
        Assert.IsNotNull(result.Undo);
        _service.UndoTrash(result.Undo);
        Assert.AreEqual(2, _service.AllLive().Count);
        result = _service.ApplyBatch(_service.CaptureBatchSelection([a,b]), BatchAction.Trash);
        _repository.SetPinned(a, true);
        Assert.ThrowsExactly<BatchConflictException>(() => _service.UndoTrash(result.Undo!));
        Assert.AreEqual(2, _service.Trash().Count);
    }

    [TestMethod]
    public void Restore_RequiresExplicitExpiryResetAndRejectsDuplicates()
    {
        long id = _service.Add("expired", SnippetSource.Manual);
        var item = _service.GetById(id)!;
        item.ExpireAt = DateTime.UtcNow.AddHours(-1); item.ExpirePolicy = ExpirePolicy.OneDay;
        _service.Update(item); _service.MoveToTrash(id);
        var selected = _service.CaptureBatchSelection([id]);
        Assert.AreEqual(BatchConflictReason.ExpiredItem, Assert.ThrowsExactly<BatchConflictException>(() => _service.ApplyBatch(selected, BatchAction.Restore)).Reason);
        _service.ApplyBatch(selected, BatchAction.Restore, resetExpiredOnRestore: true);
        Assert.IsNull(_service.GetById(id)!.ExpireAt);
        Assert.AreEqual(ExpirePolicy.Never, _service.GetById(id)!.ExpirePolicy);
        _service.MoveToTrash(id);
        _service.Add("expired", SnippetSource.Manual);
        Assert.AreEqual(BatchConflictReason.DuplicateContent, Assert.ThrowsExactly<BatchConflictException>(() => _service.ApplyBatch(_service.CaptureBatchSelection([id]), BatchAction.Restore)).Reason);
        Assert.IsTrue(_service.GetById(id)!.IsDeleted);
    }

    private void Execute(string sql)
    {
        using var connection = _db.Open(); using var command = connection.CreateCommand();
        command.CommandText = sql; command.ExecuteNonQuery();
    }

    [TestMethod]
    public void V4Migration_CreatesVerifiedSnapshotIncludingCommittedWalBeforeSchemaChange()
    {
        DowngradeToV4();
        using var writer = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = _db.DatabaseFile, Pooling = false }.ToString());
        writer.Open();
        using (var insert = writer.CreateCommand())
        {
            insert.CommandText = "PRAGMA wal_autocheckpoint=0; INSERT INTO snippets(content,content_hash,created_at,updated_at) VALUES('WAL marker','wal-marker',$now,$now)";
            insert.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
            insert.ExecuteNonQuery();
        }
        var runner = new MigrationRunner(_db, _log);
        runner.Run();
        Assert.IsNotNull(runner.SafetyBackupPath);
        Assert.AreEqual(4, new DatabaseSchemaValidator().Validate(runner.SafetyBackupPath, false).SchemaVersion);
        using var snapshot = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = runner.SafetyBackupPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        snapshot.Open();
        using var read = snapshot.CreateCommand();
        read.CommandText = "SELECT content FROM snippets";
        Assert.AreEqual("WAL marker", read.ExecuteScalar());
        Assert.IsFalse(_service.AllLive().Single().IsTemplate);
        Assert.AreEqual(5, new DatabaseSchemaValidator().Validate(_db.DatabaseFile, true).SchemaVersion);
    }

    [TestMethod]
    public void V4Migration_BackupFailureLeavesOldSchemaUntouched()
    {
        DowngradeToV4();
        File.WriteAllText(Path.Combine(_root, "Backups"), "Blocks backup directory creation.");
        Assert.ThrowsExactly<IOException>(() => new MigrationRunner(_db, _log).Run());
        using var connection = _db.Open();
        using var read = connection.CreateCommand();
        read.CommandText = "PRAGMA user_version";
        Assert.AreEqual(4L, read.ExecuteScalar());
        read.CommandText = "SELECT COUNT(*) FROM pragma_table_info('snippets') WHERE name='is_template'";
        Assert.AreEqual(0L, read.ExecuteScalar());
    }

    private void DowngradeToV4() => Execute("""
        DROP TRIGGER trg_template_insert;
        DROP TRIGGER trg_template_update;
        DROP INDEX idx_snippets_query;
        DROP TABLE database_metadata;
        ALTER TABLE snippets DROP COLUMN is_template;
        UPDATE schema_version SET version=4;
        PRAGMA user_version=4;
        """);
}
