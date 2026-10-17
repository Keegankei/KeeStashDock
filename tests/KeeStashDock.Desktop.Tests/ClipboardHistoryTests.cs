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

    [TestMethod]
    public async Task DelayedSelectionReadDoesNotBecomeHistory_AfterCancelOrAfterCompletion()
    {
        uint sequence = 40;
        var guard = new ClipboardWriteGuard(() => sequence);
        using (guard.BeginWrite())
        {
            await Task.Yield(); // The source app responds on a later dispatcher turn.
            sequence++;
            Assert.IsTrue(guard.IsOwnWrite(sequence));
            await Task.Yield(); // Even if the requester dismissed its UI meanwhile.
            sequence++; // The source adds its remaining clipboard formats.
            Assert.IsTrue(guard.IsOwnWrite(sequence));
        }
        Assert.IsTrue(guard.IsOwnWrite(sequence), "Queued notifications after the source read are still ours.");
        sequence++;
        Assert.IsFalse(guard.IsOwnWrite(sequence), "A subsequent user copy must return to normal history.");
    }

    [TestMethod]
    public void NestedReadAndWriteScopesRemainSuppressedUntilBothFinish()
    {
        uint sequence = 50;
        var guard = new ClipboardWriteGuard(() => sequence);
        using (guard.BeginWrite())
        {
            guard.Write(() => sequence++);
            Assert.IsTrue(guard.IsOwnWrite(sequence + 1));
        }
        Assert.IsTrue(guard.IsOwnWrite(sequence));
        Assert.IsFalse(guard.IsOwnWrite(sequence + 1));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SelectionTranslationStaysTransient_CopyThenTranslateCreatesOneEntry(bool historyEnabled)
    {
        string root = Path.Combine(Path.GetTempPath(), "KeeStashDock.Tests", "SelectionHistory", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var log = new FileLogger(Path.Combine(root, "Logs"));
            var database = new Db(Path.Combine(root, "selection.db"), new DatabaseMaintenanceCoordinator());
            new MigrationRunner(database, log).Run();
            var snippets = new SnippetService(new SnippetRepository(database), log);
            var settings = new SettingsStore(Path.Combine(root, "settings.json"));
            settings.Current.ClipboardWatchEnabled = historyEnabled;
            uint sequence = 20;
            string clipboard = "Synthetic test selection";
            var guard = new ClipboardWriteGuard(() => sequence);
            using var watcher = new ClipboardWatcher(settings, snippets, () => sequence, () => clipboard, guard);
            using var session = new SelectionSession();
            session.Begin(async _ =>
            {
                using var read = guard.BeginWrite();
                await Task.Yield();
                sequence++;
                watcher.TryCollect();
                return clipboard;
            });
            Assert.AreEqual(clipboard, await session.ReadAsync());
            watcher.TryCollect();
            Assert.AreEqual(0, snippets.AllLive().Count, "Reading text for translation must not populate history.");
            guard.Write(() => { sequence++; watcher.TryCollect(); });
            if (session.TryMarkCaptured(session.Revision)) snippets.Add(clipboard, SnippetSource.Clipboard);
            watcher.TryCollect();
            Assert.AreEqual(clipboard, await session.ReadAsync());
            if (session.TryMarkCaptured(session.Revision)) snippets.Add(clipboard, SnippetSource.Clipboard);
            Assert.AreEqual(1, snippets.AllLive().Count, "Copy, then Translate, then a repeated Copy produces exactly one deliberate entry.");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }
}
