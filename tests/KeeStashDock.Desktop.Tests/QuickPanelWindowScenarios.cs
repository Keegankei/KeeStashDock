using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using KeeStashDock.Core.Services;
using KeeStashDock.Desktop.Views;
using KeeStashDock.Infrastructure.Database;
using KeeStashDock.Infrastructure.Logging;
using KeeStashDock.Infrastructure.Repositories;
using KeeStashDock.Infrastructure.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

internal static class QuickPanelWindowScenarios
{
    internal static void Verify(string isolatedRoot)
    {
        // Draft recovery uses the executable's portable directory. Claim an empty,
        // test-only directory before constructing the panel; never inspect old data.
        string draftRoot = Path.Combine(AppContext.BaseDirectory, "Data");
        Assert.IsFalse(Directory.Exists(draftRoot), "The UI fixture requires an unused test output directory.");
        Directory.CreateDirectory(draftRoot);
        QuickPanelWindow? panel = null;
        try
        {
            Assert.AreEqual(draftRoot, PortablePaths.DataRoot);
            using var log = new FileLogger(Path.Combine(isolatedRoot, "panel-logs"));
            var db = new Db(Path.Combine(isolatedRoot, "panel.db"), new DatabaseMaintenanceCoordinator());
            new MigrationRunner(db, log).Run();
            var repository = new SnippetRepository(db);
            var snippets = new SnippetService(repository, log);
            var settings = new SettingsStore(Path.Combine(isolatedRoot, "panel-settings.json"));
            panel = new QuickPanelWindow(snippets, new SearchService(repository), settings,
                new CategoryRepository(db), () => { }, () => { })
            { Title = "KeeStashDock isolated UI smoke" };

            panel.ShowPanel();
            PumpUntil(() => panel.IsVisible && panel.Opacity >= 0.99);
            nint handle = new WindowInteropHelper(panel).Handle;
            Assert.AreNotEqual(nint.Zero, handle, "Summoning must create a native panel window.");
            Assert.IsTrue(IsWindowVisible(handle), "The panel must be visible to Windows even without a taskbar button.");
            Assert.IsFalse(panel.ShowInTaskbar);
            Assert.IsFalse(panel.ShowActivated);
            panel.ShowPanel();
            PumpUntil(() => panel.IsVisible && panel.Opacity >= 0.99);
            Assert.AreEqual(handle, new WindowInteropHelper(panel).Handle);
            Assert.IsTrue(IsWindowVisible(handle), "Repeated launch requests must keep an already visible panel visible.");
            panel.HidePanel();
            panel.ShowPanel();
            PumpUntil(() => panel.IsVisible && panel.Opacity >= 0.99);
            Assert.IsTrue(IsWindowVisible(handle), "A summon during fade-out must cancel the pending hide.");

            panel.HidePanel();
            PumpUntil(() => !panel.IsVisible);
            Assert.IsFalse(IsWindowVisible(handle));
            panel.ShowPanel();
            PumpUntil(() => panel.IsVisible && panel.Opacity >= 0.99);
            Assert.AreEqual(handle, new WindowInteropHelper(panel).Handle);
            Assert.IsTrue(IsWindowVisible(handle), "A second summon must reuse and reveal the panel.");
            panel.HidePanel();
            panel.Close();
            panel = null;
            DateTime afterFade = DateTime.UtcNow.AddMilliseconds(250);
            PumpUntil(() => DateTime.UtcNow >= afterFade);
            Assert.IsFalse(IsWindowVisible(handle), "Closing during fade-out must leave no late callback acting on the destroyed window.");
        }
        finally
        {
            panel?.Close();
            // This fixture never edits quick input. Non-recursive deletion fails
            // safely if anything unexpected appeared in its empty draft directory.
            Directory.Delete(draftRoot, recursive: false);
        }
    }

    private static void PumpUntil(Func<bool> complete)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        timer.Tick += (_, _) => { if (complete() || DateTime.UtcNow >= deadline) frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Assert.IsTrue(complete(), "The panel did not finish its visibility transition.");
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
}
