using System.IO;
using System.Runtime.InteropServices;
using KeeStashDock.Core.Services;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Database;
using KeeStashDock.Infrastructure.Logging;
using KeeStashDock.Infrastructure.Repositories;
using KeeStashDock.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

[TestClass]
public sealed class ClipboardHistoryTests
{
    [TestMethod]
    public void OwnCopyCannotRecreateBurnedEntry_ExternalRecopyStillCollects()
    {
        string root = Path.Combine(Path.GetTempPath(), "KeeStashDock.Tests", "ClipboardHistory", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var log = new FileLogger(Path.Combine(root, "Logs"));
            var database = new Db(Path.Combine(root, "history.db"), new DatabaseMaintenanceCoordinator());
            new MigrationRunner(database, log).Run();
            var snippets = new SnippetService(new SnippetRepository(database), log);
            var settings = new SettingsStore(Path.Combine(root, "settings.json"));
            settings.Current.ClipboardWatchEnabled = true;
            uint sequence = 10;
            string clipboard = "Private single-use text";
            var guard = new ClipboardWriteGuard(() => sequence);
            using var watcher = new ClipboardWatcher(settings, snippets, () => sequence, () => clipboard, guard);
            long id = snippets.Add(clipboard, SnippetSource.Manual, ExpirePolicy.BurnAfterRead);
            guard.Write(() =>
            {
                sequence++;
                watcher.TryCollect(); // OLE can dispatch an update before the write returns.
            });
            snippets.RegisterUse(snippets.GetById(id)!);
            watcher.TryCollect(); // The queued update arrives after burn-after-read.
            Assert.AreEqual(0, snippets.AllLive().Count, "Copying an entry must not recreate its burned content.");
            Assert.AreEqual(1, snippets.Trash().Count);

            sequence++;
            watcher.TryCollect();
            Assert.AreEqual(1, snippets.AllLive().Count, "A later deliberate copy from another app is new history.");
            snippets.MoveToTrash(snippets.AllLive().Single().Id);
            watcher.TryCollect();
            Assert.AreEqual(0, snippets.AllLive().Count, "Duplicate notifications for one clipboard revision must be ignored.");
            sequence++;
            watcher.TryCollect();
            Assert.AreEqual(1, snippets.AllLive().Count, "Identical text in a new revision must not be permanently suppressed.");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void FailedWriteDoesNotSuppressExistingExternalClipboard()
    {
        uint sequence = 5;
        var guard = new ClipboardWriteGuard(() => sequence);
        Assert.ThrowsExactly<COMException>(() => guard.Write(() => throw new COMException()));
        Assert.IsFalse(guard.IsOwnWrite(sequence));
        guard.Write(() => sequence++);
        Assert.IsTrue(guard.IsOwnWrite(sequence));
        Assert.IsFalse(guard.IsOwnWrite(sequence + 1));
    }
}
