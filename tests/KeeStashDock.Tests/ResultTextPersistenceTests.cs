using KeeStashDock.Core.Services;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Database;
using KeeStashDock.Infrastructure.Logging;
using KeeStashDock.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;

namespace KeeStashDock.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ResultTextPersistenceTests
{
    [TestMethod]
    public void ExplicitSaveReusesDuplicatesAndMovesTheVerifiedEntryToTheChosenGroup()
    {
        WithStore((snippets, categories, saver) =>
        {
            long group = categories.Insert(new Category { Name = "Result samples" });
            long existing = snippets.Add("retained result", SnippetSource.Manual);
            Assert.AreEqual(existing, saver.Save("retained result", group));
            Assert.AreEqual(group, snippets.GetById(existing)!.CategoryId);
            Assert.AreEqual(existing, saver.Save("retained result", group));
            Assert.HasCount(1, snippets.AllLive());
            Assert.AreEqual(existing, saver.Save("retained result", null));
            Assert.IsNull(snippets.GetById(existing)!.CategoryId);
        });
    }

    [TestMethod]
    public void DeletedGroupCannotPersistNewContentOrMoveADuplicate()
    {
        WithStore((snippets, categories, saver) =>
        {
            long group = categories.Insert(new Category { Name = "Removed group" });
            long existing = snippets.Add("existing source", SnippetSource.Manual);
            categories.Delete(group);
            Assert.ThrowsExactly<InvalidOperationException>(() => saver.Save("new source", group));
            Assert.ThrowsExactly<InvalidOperationException>(() => saver.Save("existing source", group));
            Assert.HasCount(1, snippets.AllLive());
            Assert.IsNull(snippets.GetById(existing)!.CategoryId);
            // The transaction itself must also reject a disappearing target group,
            // even if the application's preflight happened before the deletion.
            DateTime before = snippets.GetById(existing)!.UpdatedAt;
            Assert.ThrowsExactly<SqliteException>(() => snippets.Add("existing source", SnippetSource.Manual,
                categoryId: group, placeInCategoryOnDuplicate: true));
            Assert.AreEqual(before, snippets.GetById(existing)!.UpdatedAt);
        });
    }

    private static void WithStore(Action<SnippetService, CategoryRepository, ResultTextSaveService> verify)
    {
        string parent = Path.Combine(Path.GetTempPath(), "KeeStashDock.Tests", "ResultPersistence");
        string root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var log = new FileLogger(Path.Combine(root, "Logs"));
            var db = new Db(Path.Combine(root, "results.db"));
            new MigrationRunner(db, log).Run();
            var snippets = new SnippetService(new SnippetRepository(db), log);
            var categories = new CategoryRepository(db);
            verify(snippets, categories, new ResultTextSaveService(snippets, categories));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            string resolved = Path.GetFullPath(root);
            Assert.AreEqual(Path.GetFullPath(parent), Path.GetDirectoryName(resolved));
            Assert.IsTrue(Guid.TryParseExact(Path.GetFileName(resolved), "N", out _));
            Directory.Delete(resolved, recursive: true);
        }
    }
}
