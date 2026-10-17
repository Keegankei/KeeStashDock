using System.Windows;
using System.Windows.Input;
using KeeStashDock.Desktop.Views;
using KeeStashDock.Domain.Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

[TestClass]
public sealed class InspectionGestureTests
{
    [TestMethod]
    public void PresetsRequireTheChosenButtonWithoutModifiers()
    {
        var settings = new AppSettings();
        foreach (var (preset, expected) in new[] { ("Right", MouseButton.Right), ("Middle", MouseButton.Middle) })
        {
            settings.FloatingInspectionGesture = preset;
            foreach (MouseButton button in Enum.GetValues<MouseButton>())
            foreach (ModifierKeys modifiers in new[] { ModifierKeys.None, ModifierKeys.Alt, ModifierKeys.Control, ModifierKeys.Shift, ModifierKeys.Windows })
                Assert.AreEqual(button == expected && modifiers == ModifierKeys.None,
                    InspectionGesture.Matches(settings, button, modifiers));
        }
    }

    [TestMethod]
    public void CustomGestureRequiresTheExactModifierCombination()
    {
        var settings = new AppSettings { FloatingInspectionGesture = "Custom" };
        foreach (MouseButton button in Enum.GetValues<MouseButton>())
        foreach (var (text, required) in new[]
        {
            ("None", ModifierKeys.None), ("Alt", ModifierKeys.Alt), ("Control", ModifierKeys.Control),
            ("Shift", ModifierKeys.Shift), ("Control+Alt", ModifierKeys.Control | ModifierKeys.Alt),
            ("Control+Shift", ModifierKeys.Control | ModifierKeys.Shift),
            ("Alt+Shift", ModifierKeys.Alt | ModifierKeys.Shift),
            ("Control+Alt+Shift", ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift)
        })
        {
            settings.FloatingInspectionButton = button.ToString();
            settings.FloatingInspectionModifiers = text;
            for (int flags = 0; flags <= 15; flags++)
                Assert.AreEqual((ModifierKeys)flags == required,
                    InspectionGesture.Matches(settings, button, (ModifierKeys)flags),
                    $"{button} with {text} must not consume a different shortcut.");
            MouseButton other = button == MouseButton.Left ? MouseButton.Right : MouseButton.Left;
            Assert.IsFalse(InspectionGesture.Matches(settings, other, required));
        }
        settings.FloatingInspectionButton = "Unknown";
        Assert.IsFalse(InspectionGesture.Matches(settings, MouseButton.Left, ModifierKeys.None));
        settings.FloatingInspectionButton = "Left";
        foreach (string modifiers in new[] { "", "Unknown", "Windows", "Alt+Windows" })
        {
            settings.FloatingInspectionModifiers = modifiers;
            Assert.IsFalse(InspectionGesture.Matches(settings, MouseButton.Left, ModifierKeys.None));
        }
    }

    [TestMethod]
    public void InspectionDragKeepsAnglesBoundedAndDoesNotMoveTheWindowPlane()
    {
        foreach (Size size in new[] { new Size(400, 600), new Size(600, 900), new Size(10, 10), new Size(0, 0) })
        foreach (double x in new[] { -100000.0, -150.0, 0.0, 150.0, 100000.0 })
        foreach (double y in new[] { -100000.0, -150.0, 0.0, 150.0, 100000.0 })
        {
            PerspectivePose pose = InspectionGesture.DragPose(PerspectivePose.Fixed, new Vector(x, y), size);
            Assert.IsTrue(double.IsFinite(pose.Yaw) && Math.Abs(pose.Yaw) <= PerspectiveSurface.MaximumYaw);
            Assert.IsTrue(double.IsFinite(pose.Pitch) && Math.Abs(pose.Pitch) <= PerspectiveSurface.MaximumPitch);
            Assert.AreEqual(0, pose.OffsetX);
            Assert.AreEqual(0, pose.OffsetY);
            Assert.IsFalse(pose.Nearby);
        }
        PerspectivePose start = new(0, 0, 0, 0, false);
        PerspectivePose positive = InspectionGesture.DragPose(start, new Vector(100, 100), new Size(400, 600));
        PerspectivePose negative = InspectionGesture.DragPose(start, new Vector(-100, -100), new Size(400, 600));
        Assert.IsTrue(positive.Yaw > 0 && positive.Pitch > 0);
        Assert.AreEqual(-positive.Yaw, negative.Yaw);
        Assert.AreEqual(-positive.Pitch, negative.Pitch);
        Assert.AreEqual(start, InspectionGesture.DragPose(start, new Vector(double.NaN, 0), new Size(400, 600)));
        Assert.AreEqual(start, InspectionGesture.DragPose(start, new Vector(0, double.PositiveInfinity), new Size(400, 600)));
    }
}
