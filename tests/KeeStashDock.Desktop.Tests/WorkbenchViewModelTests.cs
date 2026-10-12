using System.IO;
using KeeStashDock.Core.Services;
using KeeStashDock.Desktop.ViewModels;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Database;
using KeeStashDock.Infrastructure.Logging;
using KeeStashDock.Infrastructure.Repositories;
using KeeStashDock.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

[TestClass]
public sealed class WorkbenchViewModelTests
{
    private string _root = null!;
    private FileLogger _log = null!;
    private SnippetService _snippets = null!;
    private SearchService _search = null!;
    private CategoryRepository _categories = null!;
    private TagRepository _tags = null!;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "KeeStashDock.Tests", "Workbench", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _log = new FileLogger(Path.Combine(_root, "Logs"));
        var db = new Db(Path.Combine(_root, "workbench.db"), new DatabaseMaintenanceCoordinator());
        new MigrationRunner(db, _log).Run();
        var repository = new SnippetRepository(db);
        _snippets = new SnippetService(repository, _log);
        _search = new SearchService(repository);
        _categories = new CategoryRepository(db);
        _tags = new TagRepository(db);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _log.Dispose();
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    [DataRow("matching", "matching live", "matching deleted")]
    [DataRow("\u6587\u5B57", "\u6587\u5B57 live", "\u6587\u5B57 deleted")]
    public void TrashSearchOnlyReturnsDeletedEntries(string query, string liveText, string deletedText)
    {
        long live = _snippets.Add(liveText, SnippetSource.Manual);
        long deleted = _snippets.Add(deletedText, SnippetSource.Manual);
        _snippets.MoveToTrash(deleted);
        var vm = CreateViewModel();
        vm.SelectedNav = vm.Nav.Single(item => item.Id == -3);
        vm.SearchText = query;

        CollectionAssert.AreEqual(new[] { deleted }, vm.Items.Select(item => item.Id).ToArray());
        Assert.AreEqual(deleted, vm.SelectedItem?.Id);
        vm.DeleteForeverCommand.Execute(null);
        Assert.IsNull(_snippets.GetById(deleted));
        Assert.IsNotNull(_snippets.GetById(live), "Searching the recycle bin must never expose a live entry to permanent deletion.");
    }

    [TestMethod]
    public void PermanentDeleteRejectsAStaleSelectionRestoredElsewhere()
    {
        long id = _snippets.Add("restore race", SnippetSource.Manual);
        _snippets.MoveToTrash(id);
        var notifications = new Queue<Action>();
        var vm = CreateViewModel(notifications.Enqueue);
        vm.SelectedNav = vm.Nav.Single(item => item.Id == -3);
        Assert.AreEqual(id, vm.SelectedItem?.Id);
        _snippets.RestoreFromTrash(id);
        vm.DeleteForeverCommand.Execute(null);
        Assert.IsNotNull(_snippets.GetById(id));
        Assert.IsFalse(_snippets.GetById(id)!.IsDeleted);
    }

    [TestMethod]
    public async Task SearchHidingDirtyEditorPreservesDraftWithoutFalseDeletionConflict()
    {
        long hidden = _snippets.Add("original alpha", SnippetSource.Manual);
        long match = _snippets.Add("visible beta", SnippetSource.Manual);
        var vm = CreateViewModel();
        Assert.IsTrue(await vm.RequestSelectionChangeAsync(vm.Items.Single(item => item.Id == hidden)));
        vm.EditContent = "draft alpha";
        vm.SearchText = "beta";

        CollectionAssert.AreEqual(new[] { match }, vm.Items.Select(item => item.Id).ToArray());
        Assert.IsTrue(vm.IsDirty);
        Assert.IsFalse(vm.HasExternalConflict, "A search filter is not an external deletion.");
        Assert.AreEqual("draft alpha", vm.EditContent);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual("draft alpha", _snippets.GetById(hidden)!.Content);
        Assert.IsFalse(vm.HasSaveError);
    }

    [TestMethod]
    public async Task SearchHidingCleanEditorSelectsVisibleResult()
    {
        long hidden = _snippets.Add("original alpha", SnippetSource.Manual);
        long match = _snippets.Add("visible beta", SnippetSource.Manual);
        var vm = CreateViewModel();
        Assert.IsTrue(await vm.RequestSelectionChangeAsync(vm.Items.Single(item => item.Id == hidden)));
        vm.SearchText = "beta";
        Assert.AreEqual(match, vm.SelectedItem?.Id);
        Assert.AreEqual("visible beta", vm.EditContent);
        Assert.IsFalse(vm.HasExternalConflict);
    }

    private WorkbenchViewModel CreateViewModel(Action<Action>? dispatch = null)
        => new(_snippets, _search, _categories, _tags, dispatch: dispatch ?? (action => action()));
}
