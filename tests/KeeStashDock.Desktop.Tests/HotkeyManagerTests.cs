using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

[TestClass]
public sealed class HotkeyManagerTests
{
    [TestMethod]
    public void FailedRebind_PreservesOldBindingAndOtherAction()
    {
        var bindings = new Dictionary<int, (uint Modifiers, uint Key)>();
        bool Register(int id, uint mods, uint key)
        {
            if (bindings.Values.Contains((mods, key))) return false;
            bindings.Add(id, (mods, key));
            return true;
        }
        using var panel = new HotkeyManager(Register, id => bindings.Remove(id));
        using var capture = new HotkeyManager(Register, id => bindings.Remove(id));
        Assert.IsTrue(panel.Initialize(1, 0x51));
        Assert.IsTrue(capture.Initialize(3, 0x53));
        Assert.HasCount(2, bindings);

        Assert.IsFalse(capture.Rebind(1, 0x51));
        Assert.IsTrue(capture.IsRegistered);
        Assert.HasCount(2, bindings);
        Assert.IsTrue(bindings.Values.Contains((0x4003u, 0x53u)), "The previous capture shortcut must remain reserved.");

        Assert.IsTrue(capture.Rebind(3, 0x54));
        Assert.HasCount(2, bindings);
        Assert.IsFalse(bindings.Values.Contains((0x4003u, 0x53u)));
        Assert.IsTrue(bindings.Values.Contains((0x4001u, 0x51u)));
        capture.Disable();
        Assert.IsFalse(capture.IsRegistered);
        Assert.HasCount(1, bindings);
        Assert.IsTrue(panel.IsRegistered);
        Assert.IsTrue(capture.Rebind(3, 0x53));
        Assert.HasCount(2, bindings);
    }

    [TestMethod]
    public void DisabledInitializationAndRepeatedBinding_DoNotDuplicateRegistrations()
    {
        int registered = 0;
        int removed = 0;
        var manager = new HotkeyManager((_, _, _) => { registered++; return true; }, _ => removed++);
        Assert.IsTrue(manager.Initialize(0, 0));
        Assert.AreEqual(0, registered);
        Assert.IsTrue(manager.Rebind(3, 0x53));
        Assert.IsTrue(manager.Rebind(3, 0x53));
        Assert.IsTrue(manager.Initialize(3, 0x53));
        Assert.AreEqual(1, registered);
        manager.Dispose();
        manager.Dispose();
        Assert.AreEqual(1, removed);
        Assert.IsFalse(manager.Rebind(3, 0x53));
    }
}
