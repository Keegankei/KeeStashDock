using KeeStashDock.Desktop.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Drawing = System.Drawing;

namespace KeeStashDock.Desktop.Tests;

[TestClass]
public sealed class PopupPlacementTests
{
    [TestMethod]
    public void NegativeMonitorOriginIsNotDividedByDpiScale()
    {
        Assert.AreEqual(new Drawing.Point(-2385, -184), PopupPlacement.Calculate(
            new(-2400, -100), new(300, 72), new(-2560, -300, 2560, 1400), 1.5, preferAbove: true));
    }

    [TestMethod]
    public void RightSecondaryMonitorUsesItsOwnTaskbarWorkAreaAndActualPixelSize()
    {
        Assert.AreEqual(new Drawing.Point(3240, 640), PopupPlacement.Calculate(
            new(3810, 1000), new(600, 400), new(1920, 0, 1920, 1040), 2, preferAbove: false));
    }

    [TestMethod]
    public void BubbleFlipsBelowTopEdgeOnAnAbovePrimaryMonitor()
    {
        Assert.AreEqual(new Drawing.Point(-985, -1043), PopupPlacement.Calculate(
            new(-1000, -1070), new(200, 60), new(-1920, -1080, 1920, 1040), 1.5, preferAbove: true));
    }

    [TestMethod]
    public void ExpandedTranslationIsReclampedAfterItsMeasuredSizeChanges()
    {
        var anchor = new Drawing.Point(3800, 1000);
        var workArea = new Drawing.Rectangle(1920, 0, 1920, 1040);
        Assert.AreEqual(new Drawing.Point(3640, 970), PopupPlacement.Calculate(anchor, new(200, 70), workArea, 2, false));
        Assert.AreEqual(new Drawing.Point(3240, 640), PopupPlacement.Calculate(anchor, new(600, 400), workArea, 2, false));
    }

    [TestMethod]
    public void OversizedPopupRemainsAnchoredAtWorkAreaOriginWithoutInvalidClampBounds()
    {
        Assert.AreEqual(new Drawing.Point(100, 100), PopupPlacement.Calculate(
            new(200, 200), new(420, 300), new(100, 100, 160, 120), 1, false));
    }

    [TestMethod]
    public async Task PersistentClipboardLockStopsAfterTwoAttempts()
    {
        int attempts = 0, delays = 0;
        bool copied = await QuickPanelViewModel.TryWriteClipboardAsync(
            () => { attempts++; throw new System.Runtime.InteropServices.COMException("Clipboard busy"); },
            () => { delays++; return Task.CompletedTask; });
        Assert.IsFalse(copied);
        Assert.AreEqual(2, attempts);
        Assert.AreEqual(1, delays);
    }

    [TestMethod]
    public async Task TransientClipboardLockRetriesOnceAndSuccessDoesNotRepeat()
    {
        int attempts = 0, delays = 0;
        bool copied = await QuickPanelViewModel.TryWriteClipboardAsync(
            () => { if (++attempts == 1) throw new System.Runtime.InteropServices.COMException("Clipboard busy"); },
            () => { delays++; return Task.CompletedTask; });
        Assert.IsTrue(copied);
        Assert.AreEqual(2, attempts);
        Assert.AreEqual(1, delays);
        copied = await QuickPanelViewModel.TryWriteClipboardAsync(() => attempts++,
            () => { Assert.Fail("A successful write must not wait or retry."); return Task.CompletedTask; });
        Assert.IsTrue(copied);
        Assert.AreEqual(3, attempts);
    }
}
