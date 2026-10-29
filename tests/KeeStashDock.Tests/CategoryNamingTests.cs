using KeeStashDock.Core.Services;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Database;
using KeeStashDock.Infrastructure.Logging;
using KeeStashDock.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;

namespace KeeStashDock.Tests;

[TestClass]
[DoNotParallelize]
public sealed class CategoryNamingTests
{
    private string _root = null!;
    private FileLogger _logger = null!;
    private Db _database = null!;
    private CategoryRepository _categories = null!;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "KeeStashDock.Tests", "CategoryNaming",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        string logs = Path.Combine(_root, "Logs");
        Directory.CreateDirectory(logs);
        _logger = new FileLogger(logs);
        _database = new Db(Path.Combine(_root, "groups.db"));
        new MigrationRunner(_database, _logger).Run();
        _categories = new CategoryRepository(_database);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _logger.Dispose();
        SqliteConnection.ClearAllPools();
        string parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(),
            "KeeStashDock.Tests", "CategoryNaming")) + Path.DirectorySeparatorChar;
        string target = Path.GetFullPath(_root);
        Assert.IsTrue(target.StartsWith(parent, StringComparison.OrdinalIgnoreCase));
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
    }

    [TestMethod]
    public void InsertAndLookup_NormalizeWhitespaceAndUnicodeCase()
    {
        var category = new Category { Name = " \t\u00c9quipe\u2003" };
        long id = _categories.Insert(category);

        Assert.AreEqual("\u00c9quipe", category.Name);
        Assert.AreEqual(id, _categories.FindByName("  \u00e9QUIPE ")?.Id);
        Assert.IsNull(_categories.FindByName("\u00e9quipe", excludingId: id));
        DuplicateCategoryNameException error = Assert.ThrowsExactly<DuplicateCategoryNameException>(
            () => _categories.Insert(new Category { Name = "\u00e9QUIPE" }));
        Assert.AreEqual(id, error.ExistingCategoryId);
        Assert.HasCount(1, _categories.GetAll());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" \t\r\n")]
    [DataRow("\u2003")]
    public void EmptyNames_AreRejectedWithoutMutatingTheDatabase(string name)
    {
        long id = _categories.Insert(new Category { Name = "Original" });
        Assert.Throws<ArgumentException>(() => _categories.Insert(new Category { Name = name }));
        Assert.Throws<ArgumentException>(() => _categories.Rename(id, name));
        Assert.AreEqual("Original", _categories.GetAll().Single().Name);
    }

    [TestMethod]
    public void Rename_AllowsOwnCaseChangeAndRejectsAnotherGroupsName()
    {
        long first = _categories.Insert(new Category { Name = "Team" });
        long second = _categories.Insert(new Category { Name = "Archive" });
        int notifications = 0;
        _categories.Changed += () => notifications++;

        _categories.Rename(first, " TEAM ");
        DuplicateCategoryNameException error = Assert.ThrowsExactly<DuplicateCategoryNameException>(
            () => _categories.Rename(second, " team "));

        Assert.AreEqual("team", error.Name);
        Assert.AreEqual(first, error.ExistingCategoryId);
        Assert.AreEqual("TEAM", _categories.FindByName("team")?.Name);
        Assert.AreEqual(second, _categories.FindByName("Archive")?.Id);
        Assert.AreEqual(1, notifications);
    }

    [TestMethod]
    public void UniqueName_SkipsOccupiedSuffixesUsingTheSameNameKey()
    {
        _categories.Insert(new Category { Name = "\u00c9quipe" });
        _categories.Insert(new Category { Name = "\u00e9QUIPE (2)" });
        Assert.AreEqual("\u00e9quipe (3)", _categories.GetUniqueName(" \u00e9quipe "));
        Assert.AreEqual("Other", _categories.GetUniqueName(" Other "));
    }

    [TestMethod]
    public async Task ChangeNotifications_ReleaseTheMaintenanceLockBeforeSubscribersRead()
    {
        int notifications = 0;
        _categories.Changed += () =>
        {
            _categories.GetAll();
            notifications++;
        };

        await Task.Run(() =>
        {
            long id = _categories.Insert(new Category { Name = "Initial" });
            _categories.Rename(id, "Renamed");
            _categories.Delete(id);
        }).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(3, notifications);
    }

    [TestMethod]
    public async Task IndependentWriters_CannotCreateTheSameNormalizedGroupName()
    {
        var other = new CategoryRepository(new Db(_database.DatabaseFile));
        using var start = new ManualResetEventSlim();
        Task<bool> first = Task.Run(() => InsertAfterSignal(_categories, "Team", start));
        Task<bool> second = Task.Run(() => InsertAfterSignal(other, " TEAM ", start));
        start.Set();

        bool[] inserted = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.AreEqual(1, inserted.Count(value => value));
        Assert.HasCount(1, _categories.GetAll());
    }

    [TestMethod]
    public void DatabaseConstraint_RejectsDuplicateKeysAndMissingKeys()
    {
        _categories.Insert(new Category { Name = "Team" });
        Assert.ThrowsExactly<SqliteException>(() => Execute("""
            INSERT INTO categories(name, name_key) VALUES ('TEAM', 'TEAM')
            """));
        Assert.ThrowsExactly<SqliteException>(() => Execute("""
            INSERT INTO categories(name) VALUES ('Other')
            """));
        Assert.HasCount(1, _categories.GetAll());
    }

    [TestMethod]
    public void GroupImport_UsesRepositoryConflictRulesAndUniqueCopyNames()
    {
        var snippets = new SnippetService(new SnippetRepository(_database), _logger);
        var groups = new GroupIoService(snippets, _categories);
        long existing = _categories.Insert(new Category { Name = "\u00c9quipe" });
        var package = new GroupIoService.GroupPackage(1, " \u00e9quipe ",
            [new("Imported", "new content")]);

        Assert.AreEqual(existing, groups.FindCategoryByName(package.Name)?.Id);
        Assert.ThrowsExactly<DuplicateCategoryNameException>(
            () => groups.ImportGroup(package, package.Name, null));
        Assert.HasCount(0, snippets.ByCategory(existing));

        string copyName = groups.GetUniqueCategoryName(package.Name);
        Assert.AreEqual(1, groups.ImportGroup(package, copyName, null));
        long copyId = _categories.FindByName(copyName)!.Id;
        Assert.AreEqual("\u00e9quipe (2)", copyName);
        Assert.AreEqual("new content", snippets.ByCategory(copyId).Single().Content);
        Assert.HasCount(0, snippets.ByCategory(existing));
    }

    [TestMethod]
    public void InvalidGroupPackage_IsRejectedBeforeReplacementChangesExistingContent()
    {
        var snippets = new SnippetService(new SnippetRepository(_database), _logger);
        var groups = new GroupIoService(snippets, _categories);
        long existing = _categories.Insert(new Category { Name = "Team" });
        long snippetId = snippets.Add("keep me", SnippetSource.Manual, categoryId: existing);
        var unsupported = new GroupIoService.GroupPackage(4, "Team", []);
        var malformed = new GroupIoService.GroupPackage(1, "Team", [null!]);

        Assert.ThrowsExactly<InvalidDataException>(() => groups.ImportGroup(unsupported, "Team", existing));
        Assert.ThrowsExactly<InvalidDataException>(() => groups.ImportGroup(malformed, "Team", existing));
        Assert.AreEqual(snippetId, snippets.ByCategory(existing).Single().Id);
    }

    [TestMethod]
    public void ReadPackage_NormalizesNamesAndRejectsUnsupportedVersions()
    {
        var groups = new GroupIoService(new SnippetService(new SnippetRepository(_database), _logger),
            _categories);
        string path = Path.Combine(_root, "group.ksdgrp");
        File.WriteAllText(path, """{"version":1,"name":" Team ","items":[]}""");
        Assert.AreEqual("Team", groups.ReadPackage(path).Name);
        File.WriteAllText(path, """{"version":4,"name":"Team","items":[]}""");
        Assert.ThrowsExactly<InvalidDataException>(() => groups.ReadPackage(path));
    }

    private static bool InsertAfterSignal(CategoryRepository repository, string name,
        ManualResetEventSlim start)
    {
        start.Wait();
        try
        {
            repository.Insert(new Category { Name = name });
            return true;
        }
        catch (DuplicateCategoryNameException)
        {
            return false;
        }
    }

    private void Execute(string sql)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
