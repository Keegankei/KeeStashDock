using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

[TestClass]
public sealed class HotkeyFeedbackTests
{
    [TestMethod]
    public void StartupFailures_IncludeBothShortcutsAndNotifyOnceUntilRecovery()
    {
        bool available = false;
        using var panel = new HotkeyManager((_, _, _) => available, _ => { });
        using var screenshot = new HotkeyManager((_, _, _) => available, _ => { });
        var feedback = new HotkeyConflictNotifications();
        panel.RegistrationChanged += () => feedback.Observe(HotkeyAction.Panel, panel.RegistrationStatus);
        screenshot.RegistrationChanged += () => feedback.Observe(HotkeyAction.Screenshot, screenshot.RegistrationStatus);

        Assert.IsFalse(panel.Initialize(1, 0x51));
        Assert.IsFalse(screenshot.Initialize(1, 0x57));
        var initial = feedback.TakePending();
        Assert.HasCount(2, initial);
        Assert.AreEqual(HotkeyAction.Panel, initial[0].Action);
        Assert.AreEqual("Alt + Q", initial[0].Status.FailedAttempt?.ToString());
        Assert.AreEqual("Alt + W", initial[1].Status.FailedAttempt?.ToString());
        Assert.IsNull(initial[0].Status.Active);

        Assert.IsFalse(panel.Rebind(1, 0x51));
        Assert.IsFalse(panel.Rebind(3, 0x51));
        Assert.IsEmpty(feedback.TakePending(), "An unresolved failure must not repeatedly notify, even if another candidate also fails.");
        available = true;
        Assert.IsTrue(panel.Rebind(3, 0x51));
        Assert.IsFalse(panel.RegistrationStatus.HasFailure);
        Assert.IsEmpty(feedback.TakePending());
        available = false;
        Assert.IsFalse(panel.Rebind(3, 0x50));
        var next = feedback.TakePending();
        Assert.HasCount(1, next);
        Assert.AreEqual(new HotkeyCombination(3, 0x51), next[0].Status.Active);
        Assert.IsTrue(panel.IsRegistered, "A new failed attempt must not release the recovered binding.");
    }

    [TestMethod]
    public void DisabledAndRecoveredBeforePresentation_DoNotProduceStaleNotifications()
    {
        var feedback = new HotkeyConflictNotifications();
        using var manager = new HotkeyManager((_, _, _) => false, _ => { });
        manager.RegistrationChanged += () => feedback.Observe(HotkeyAction.Screenshot, manager.RegistrationStatus);
        Assert.IsTrue(manager.Initialize(0, 0));
        Assert.IsFalse(manager.RegistrationStatus.HasFailure);
        Assert.IsEmpty(feedback.TakePending());

        Assert.IsFalse(manager.Rebind(1, 0x57));
        manager.Disable();
        Assert.IsEmpty(feedback.TakePending(), "Disabling before the dispatcher callback must remove the pending warning.");
        Assert.IsFalse(manager.RegistrationStatus.HasFailure);
        Assert.IsFalse(manager.Rebind(1, 0x57));
        Assert.HasCount(1, feedback.TakePending(), "Re-enabling after disable starts a new failure episode.");
        feedback.Observe(HotkeyAction.Screenshot, new(new(1, 0x57)));
        Assert.IsEmpty(feedback.TakePending());
    }

    [TestMethod]
    public void SelectingTheStillActiveShortcut_ClearsFailedRebindStatus()
    {
        int registrations = 0;
        using var manager = new HotkeyManager((_, _, _) => ++registrations == 1, _ => { });
        Assert.IsTrue(manager.Initialize(1, 0x51));
        Assert.IsFalse(manager.Rebind(1, 0x57));
        Assert.IsTrue(manager.RegistrationStatus.HasFailure);
        Assert.AreEqual(new HotkeyCombination(1, 0x51), manager.RegistrationStatus.Active);
        Assert.IsTrue(manager.Rebind(1, 0x51));
        Assert.IsFalse(manager.RegistrationStatus.HasFailure);
        Assert.AreEqual(2, registrations, "The existing reservation must be reused, not released and acquired again.");
    }

    [STATestMethod]
    public void NativeRegistration_ReportsRealConflictThenRecoversWithoutStealing()
    {
        // Reserve only an available Ctrl+Alt+Shift+F13..F24 test combination.
        // Never touch the user's normal Alt+Q/Alt+W bindings or send key input.
        using var owner = new HotkeyManager();
        Assert.IsTrue(owner.Initialize(0, 0));
        uint key = 0;
        for (uint candidate = 0x87; candidate >= 0x7c; candidate--)
            if (owner.Rebind(7, candidate)) { key = candidate; break; }
        if (key == 0) Assert.Inconclusive("No isolated function-key shortcut is available in this Windows session.");
        using var panel = new HotkeyManager();
        using var screenshot = new HotkeyManager();
        Assert.IsFalse(panel.Initialize(7, key));
        Assert.IsFalse(screenshot.Initialize(7, key));
        Assert.IsTrue(owner.IsRegistered);
        Assert.AreEqual(new HotkeyCombination(7, key), panel.RegistrationStatus.FailedAttempt);
        Assert.AreEqual(new HotkeyCombination(7, key), screenshot.RegistrationStatus.FailedAttempt);
        Assert.IsFalse(panel.IsRegistered);
        screenshot.Disable();
        Assert.IsFalse(screenshot.RegistrationStatus.HasFailure);
        owner.Disable();
        Assert.IsTrue(panel.Rebind(7, key), "The same shortcut should register after its owner voluntarily releases it.");
        Assert.IsFalse(panel.RegistrationStatus.HasFailure);
    }
}
