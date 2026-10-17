using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

[TestClass]
public sealed class DraftGuardTests
{
    private string _root = null!, _draft = null!;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "KeeStashDock.Tests", "DraftGuard", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _draft = Path.Combine(_root, "draft.txt");
    }

    [TestCleanup]
    public void Cleanup()
    {
        string parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "KeeStashDock.Tests", "DraftGuard")) + Path.DirectorySeparatorChar;
        Assert.IsTrue(Path.GetFullPath(_root).StartsWith(parent, StringComparison.OrdinalIgnoreCase));
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public void ExplicitFlushPersistsPendingTextBeforeDebounceForUpdateSnapshot()
    {
        using var guard = new DraftGuard(_draft);
        guard.OnTextChanged("unsaved text typed just before upgrade");
        guard.Flush();
        Assert.AreEqual("unsaved text typed just before upgrade", File.ReadAllText(_draft));
    }

    [TestMethod]
    public async Task ClearRetiresQueuedTextSoLateTimerCannotRestoreIt()
    {
        using var guard = new DraftGuard(_draft);
        guard.OnTextChanged("pending text");
        guard.Clear();
        await Task.Delay(750);
        Assert.IsFalse(File.Exists(_draft));
        guard.Flush();
        Assert.IsFalse(File.Exists(_draft));
    }

    [TestMethod]
    public void UntouchedGuardDoesNotDeleteExistingRecoveredDraft()
    {
        File.WriteAllText(_draft, "existing recovered draft");
        using (var guard = new DraftGuard(_draft)) guard.Flush();
        Assert.AreEqual("existing recovered draft", File.ReadAllText(_draft));
    }
}
