using System.Drawing;
using KeeStashDock.Desktop.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

[TestClass]
public sealed class EdgeDockGeometryTests
{
    [TestMethod]
    public void EdgeHandlesStayInsideTheirOwnWorkAreaAtMixedDpiAndNegativeOrigins()
    {
        foreach (double scale in new[] { 1d, 1.25, 1.5, 2d })
        foreach (Rectangle work in new[] { new Rectangle(0, 0, 1920, 1040), new Rectangle(-2560, -240, 2560, 1400) })
        foreach (PanelDockSide side in new[] { PanelDockSide.Left, PanelDockSide.Top, PanelDockSide.Right, PanelDockSide.Bottom })
        {
            var original = new Rectangle(work.Left + 120, work.Top + 100, 600, 780);
            Rectangle restored = EdgeDockGeometry.RestoreBounds(original, work, side);
            Assert.AreEqual(side, EdgeDockGeometry.Detect(restored, work, scale));
            Rectangle handle = EdgeDockGeometry.HandleBounds(restored, work, side, scale);
            Assert.IsTrue(work.Contains(handle), "A hidden panel must not spill onto an adjacent screen or taskbar.");
            Assert.IsTrue(handle.Width <= 6 * scale + 1 || handle.Height <= 6 * scale + 1);
            Assert.IsTrue(handle.Width <= 72 * scale + 1 && handle.Height <= 72 * scale + 1);
            Rectangle roundTrip = EdgeDockGeometry.RestoreBounds(restored, work, side);
            Assert.AreEqual(restored, roundTrip);
        }
    }

    [TestMethod]
    public void MovingAwayCancelsDockingAndRemovedMonitorBoundsRemainRecoverable()
    {
        var work = new Rectangle(-1920, 0, 1920, 1040);
        Assert.AreEqual(PanelDockSide.None, EdgeDockGeometry.Detect(new(-1800, 100, 600, 780), work, 1));
        Assert.AreEqual(PanelDockSide.Left, EdgeDockGeometry.Detect(new(-1900, 100, 600, 780), work, 1.5));
        Assert.AreEqual(PanelDockSide.None, EdgeDockGeometry.Detect(new(-1900, 100, 600, 780), work, 1));
        var remainingMonitor = new Rectangle(0, 0, 1280, 720);
        Rectangle recovered = EdgeDockGeometry.RestoreBounds(new(-1900, -400, 2200, 1400), remainingMonitor, PanelDockSide.Right);
        Assert.AreEqual(remainingMonitor, recovered);
    }
}
