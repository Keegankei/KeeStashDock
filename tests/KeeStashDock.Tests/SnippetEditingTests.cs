using KeeStashDock.Core.Services;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Database;
using KeeStashDock.Infrastructure.Logging;
using KeeStashDock.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;

namespace KeeStashDock.Tests;

[TestClass]
[DoNotParallelize]
public sealed class SnippetEditingTests
{
    private static readonly string TempParent = Path.Combine(
        Path.GetTempPath(), "KeeStashDock.Tests", "SnippetEditing");

    private string _root = null!;
    private FileLogger _logger = null!;
    private Db _database = null!;
    private SnippetRepository _repository = null!;
    private TagRepository _tags = null!;
    private SnippetService _service = null!;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(TempParent, Guid.NewGuid().ToString("N"));
        string logs = Path.Combine(_root, "Logs");
        Directory.CreateDirectory(logs);
        _logger = new FileLogger(logs);
        _database = new Db(Path.Combine(_root, "snippets.db"));
        new MigrationRunner(_database, _logger).Run();
        _repository = new SnippetRepository(_database);
        _tags = new TagRepository(_database);
        _service = new SnippetService(_repository, _logger);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _logger.Dispose();
        SqliteConnection.ClearAllPools();
        string resolvedRoot = Path.GetFullPath(_root);
        Assert.AreEqual(
            Path.GetFullPath(TempParent),
            Path.GetDirectoryName(resolvedRoot),
            "Test cleanup must remain inside its temporary parent directory.");
        Assert.IsTrue(Guid.TryParseExact(Path.GetFileName(resolvedRoot), "N", out _));
        if (Directory.Exists(resolvedRoot))
            Directory.Delete(resolvedRoot, recursive: true);
    }

    [TestMethod]
    public void Update_NormalizesTrailingLineBreaksLikeCapturePreservingOtherWhitespace()
    {
        long id = _service.Add("initial", SnippetSource.Manual);
        Snippet draft = Read(id);
        draft.Content = "  edited content \t\r\n\n";
        int changes = 0;
        _service.Changed += () => changes++;

        _service.Update(draft);

        Snippet stored = Read(id);
        Assert.AreEqual("  edited content \t", stored.Content);
        Assert.AreEqual(stored.Content, draft.Content);
        Assert.AreEqual(stored.ContentHash, draft.ContentHash);
        Assert.AreEqual(stored.UpdatedAt, draft.UpdatedAt);
        Assert.AreEqual(1, changes);
        Assert.AreEqual(id, _service.Add("  edited content \t\n", SnippetSource.Manual));
        Assert.HasCount(1, _repository.GetAllLive());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" \t")]
    [DataRow("\r\n")]
    [DataRow("\u3000")]
    public void Update_RejectsBlankContentWithoutChangingStoredFieldsOrModelMetadata(string content)
    {
        long id = _service.Add("originalcontent", SnippetSource.Manual, title: "Original title");
        _tags.SetForSnippet(id, ["original-tag"]);
        Snippet original = Read(id);
        Snippet draft = Read(id);
        draft.Content = content;
        draft.Title = "Unsaved title";
        int changes = 0;
        _service.Changed += () => changes++;

        Assert.ThrowsExactly<ArgumentException>(() => _service.Update(draft, ["unsaved-tag"]));

        AssertEditorFieldsEqual(original, Read(id));
        CollectionAssert.AreEqual(new[] { "original-tag" }, _tags.GetForSnippet(id));
        Assert.AreEqual(original.ContentHash, draft.ContentHash);
        Assert.AreEqual(original.UpdatedAt, draft.UpdatedAt);
        Assert.AreEqual(content, draft.Content);
        Assert.AreEqual("Unsaved title", draft.Title);
        Assert.AreEqual(0, changes);
    }

    [TestMethod]
    public void Update_DuplicateLiveContentReportsExistingIdAndPreservesBothRecords()
    {
        long existingId = _service.Add("occupiedcontent", SnippetSource.Manual);
        long editingId = _service.Add("originalcontent", SnippetSource.Manual, title: "Original title");
        _tags.SetForSnippet(editingId, ["original-tag"]);
        Snippet existing = Read(existingId);
        Snippet original = Read(editingId);
        Snippet draft = Read(editingId);
        draft.Content = "occupiedcontent\r\n";
        draft.Title = "Unsaved title";
        draft.ExpirePolicy = ExpirePolicy.SevenDays;
        draft.ExpireAt = DateTime.UtcNow.AddDays(7);
        int changes = 0;
        _service.Changed += () => changes++;

        DuplicateSnippetContentException failure = Assert.ThrowsExactly<DuplicateSnippetContentException>(
            () => _service.Update(draft, ["unsaved-tag"]));

        Assert.AreEqual(existingId, failure.ExistingSnippetId);
        AssertEditorFieldsEqual(existing, Read(existingId));
        AssertEditorFieldsEqual(original, Read(editingId));
        CollectionAssert.AreEqual(new[] { "original-tag" }, _tags.GetForSnippet(editingId));
        Assert.AreEqual(original.ContentHash, draft.ContentHash);
        Assert.AreEqual(original.UpdatedAt, draft.UpdatedAt);
        Assert.AreEqual("occupiedcontent\r\n", draft.Content);
        Assert.AreEqual(editingId, _repository.SearchFts("originalcontent", 10).Single().Id);
        Assert.AreEqual(0, changes);
    }

    [TestMethod]
    public void Update_UnchangedContentAllowsOtherEditorFieldsAndTagsToCommit()
    {
        long id = _service.Add("unchangedcontent", SnippetSource.Manual);
        _tags.SetForSnippet(id, ["old-tag"]);
        Snippet draft = Read(id);
        draft.Title = "Revised title";
        draft.ExpirePolicy = ExpirePolicy.OneDay;
        draft.ExpireAt = DateTime.UtcNow.AddDays(1);

        _service.Update(draft, [" new-tag ", "new-tag"]);

        AssertEditorFieldsEqual(draft, Read(id));
        CollectionAssert.AreEqual(new[] { "new-tag" }, _tags.GetForSnippet(id));
        Assert.HasCount(1, _repository.GetAllLive());
    }

    [TestMethod]
    public void Update_ContentInTrashDoesNotBlockLiveEdit()
    {
        long deletedId = _service.Add("reusablecontent", SnippetSource.Manual);
        _service.MoveToTrash(deletedId);
        long editingId = _service.Add("originalcontent", SnippetSource.Manual);
        Snippet draft = Read(editingId);
        draft.Content = "reusablecontent";

        _service.Update(draft);

        Assert.AreEqual("reusablecontent", Read(editingId).Content);
        Assert.AreEqual(deletedId, _repository.GetTrash().Single().Id);
        Assert.AreEqual(editingId, _repository.SearchFts("reusablecontent", 10).Single().Id);
    }

    [TestMethod]
    public void Update_TagInsertionFailureRollsBackEditorFieldsTagsAndSearchIndex()
    {
        long id = _service.Add("originalcontent", SnippetSource.Manual, title: "Original title");
        _tags.SetForSnippet(id, ["original-tag"]);
        Snippet original = Read(id);
        Snippet draft = Read(id);
        draft.Content = "replacementcontent\r\n";
        draft.Title = "Replacement title";
        draft.ExpirePolicy = ExpirePolicy.SevenDays;
        draft.ExpireAt = DateTime.UtcNow.AddDays(7);
        Execute("""
            CREATE TRIGGER reject_test_tag BEFORE INSERT ON tags
            WHEN NEW.name = 'reject-tag'
            BEGIN
                SELECT RAISE(ABORT, 'Injected tag insertion failure');
            END;
            """);
        int changes = 0;
        _service.Changed += () => changes++;

        Assert.ThrowsExactly<SqliteException>(
            () => _service.Update(draft, ["intermediate-tag", "reject-tag"]));

        AssertEditorFieldsEqual(original, Read(id));
        CollectionAssert.AreEqual(new[] { "original-tag" }, _tags.GetForSnippet(id));
        Assert.AreEqual(0L, ReadCount("SELECT COUNT(*) FROM tags WHERE name IN ('intermediate-tag', 'reject-tag')"));
        Assert.AreEqual(id, _repository.SearchFts("originalcontent", 10).Single().Id);
        Assert.IsEmpty(_repository.SearchFts("replacementcontent", 10));
        Assert.AreEqual(original.ContentHash, draft.ContentHash);
        Assert.AreEqual(original.UpdatedAt, draft.UpdatedAt);
        Assert.AreEqual("replacementcontent\r\n", draft.Content);
        Assert.AreEqual(0, changes);
    }

    [TestMethod]
    [DataRow(" \r\n")]
    [DataRow("occupiedcontent")]
    public async Task EditorSave_ValidationFailurePreservesDirtyDraftAndOriginalSelection(string content)
    {
        _service.Add("occupiedcontent", SnippetSource.Manual);
        long id = _service.Add("originalcontent", SnippetSource.Manual, title: "Original title");
        Snippet original = Read(id);
        var session = new WorkbenchEditorSession();
        session.Load(new WorkbenchEditSnapshot(
            id, original.Title, original.Content, original.ExpirePolicy, []));
        session.EditTitle = "Unsaved title";
        session.EditContent = content;
        session.EditExpirePolicy = ExpirePolicy.SevenDays;
        session.EditTags = "unsaved-tag";

        WorkbenchLeaveResult result = await session.SaveAsync((snapshot, _) =>
        {
            Snippet draft = Read(snapshot.ItemId);
            draft.Title = snapshot.Title;
            draft.Content = snapshot.Content;
            draft.ExpirePolicy = snapshot.ExpirePolicy;
            draft.ExpireAt = SnippetService.ComputeExpireAt(snapshot.ExpirePolicy, DateTime.UtcNow);
            _service.Update(draft, snapshot.Tags);
            return Task.CompletedTask;
        });

        Assert.AreEqual(WorkbenchLeaveResult.SaveFailed, result);
        Assert.IsTrue(session.IsDirty);
        Assert.AreEqual(id, session.ItemId);
        Assert.AreEqual("Unsaved title", session.EditTitle);
        Assert.AreEqual(content, session.EditContent);
        Assert.AreEqual(ExpirePolicy.SevenDays, session.EditExpirePolicy);
        Assert.AreEqual("unsaved-tag", session.EditTags);
        AssertEditorFieldsEqual(original, Read(id));
        Assert.IsEmpty(_tags.GetForSnippet(id));
    }

    [TestMethod]
    public async Task ConcurrentCapture_IdenticalContentCreatesOnlyOneLiveRecord()
    {
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<long>[] captures = Enumerable.Range(0, 12).Select(index => Task.Run(async () =>
        {
            await start.Task;
            return _service.Add(
                index % 2 == 0 ? "concurrentcontent" : "concurrentcontent\r\n",
                SnippetSource.Manual);
        })).ToArray();

        start.SetResult(true);
        long[] ids = await Task.WhenAll(captures);

        Assert.HasCount(1, ids.Distinct().ToArray());
        Assert.AreEqual(ids[0], _repository.GetAllLive().Single().Id);
        Assert.AreEqual(1L, ReadCount("SELECT COUNT(*) FROM snippets"));
        Assert.AreEqual(ids[0], _repository.SearchFts("concurrentcontent", 10).Single().Id);
    }

    [TestMethod]
    public async Task ConcurrentEdit_IdenticalContentAllowsOneWinnerAndPreservesLosingRecord()
    {
        long firstId = _service.Add("firstoriginal", SnippetSource.Manual);
        long secondId = _service.Add("secondoriginal", SnippetSource.Manual);
        Snippet[] originals = [Read(firstId), Read(secondId)];
        Snippet[] drafts = [Read(firstId), Read(secondId)];
        foreach (Snippet draft in drafts)
            draft.Content = "sharededitedcontent";
        int changes = 0;
        _service.Changed += () => Interlocked.Increment(ref changes);
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<(long Id, DuplicateSnippetContentException? Failure)>[] edits = drafts.Select(draft => Task.Run(async () =>
        {
            await start.Task;
            try
            {
                _service.Update(draft, ["winner-tag"]);
                return (draft.Id, (DuplicateSnippetContentException?)null);
            }
            catch (DuplicateSnippetContentException exception)
            {
                return (draft.Id, exception);
            }
        })).ToArray();

        start.SetResult(true);
        (long Id, DuplicateSnippetContentException? Failure)[] results = await Task.WhenAll(edits);

        long winnerId = results.Single(result => result.Failure is null).Id;
        (long losingId, DuplicateSnippetContentException? failure) = results.Single(result => result.Failure is not null);
        Assert.IsNotNull(failure);
        Assert.AreEqual(winnerId, failure.ExistingSnippetId);
        Assert.AreEqual("sharededitedcontent", Read(winnerId).Content);
        AssertEditorFieldsEqual(originals.Single(snippet => snippet.Id == losingId), Read(losingId));
        CollectionAssert.AreEqual(new[] { "winner-tag" }, _tags.GetForSnippet(winnerId));
        Assert.IsEmpty(_tags.GetForSnippet(losingId));
        Assert.AreEqual(winnerId, _repository.SearchFts("sharededitedcontent", 10).Single().Id);
        Assert.AreEqual(1, changes);
    }

    private Snippet Read(long id) => _repository.GetAllLive().Single(snippet => snippet.Id == id);

    private static void AssertEditorFieldsEqual(Snippet expected, Snippet actual)
    {
        Assert.AreEqual(expected.Id, actual.Id);
        Assert.AreEqual(expected.Title, actual.Title);
        Assert.AreEqual(expected.Content, actual.Content);
        Assert.AreEqual(expected.ContentHash, actual.ContentHash);
        Assert.AreEqual(expected.ExpirePolicy, actual.ExpirePolicy);
        Assert.AreEqual(expected.ExpireAt, actual.ExpireAt);
        Assert.AreEqual(expected.CreatedAt, actual.CreatedAt);
        Assert.AreEqual(expected.UpdatedAt, actual.UpdatedAt);
    }

    private void Execute(string sql)
    {
        using SqliteConnection connection = _database.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private long ReadCount(string sql)
    {
        using SqliteConnection connection = _database.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }
}
