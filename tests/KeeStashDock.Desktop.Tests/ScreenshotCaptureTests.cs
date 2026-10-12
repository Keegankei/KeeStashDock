using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Drawing = System.Drawing;

namespace KeeStashDock.Desktop.Tests;

[TestClass]
public sealed class ScreenshotCaptureTests
{
    [TestMethod]
    public void SelectionCrossesNegativeOriginAndMainMonitorInPhysicalPixels()
    {
        var desktop = new Drawing.Rectangle(-2560, -300, 6400, 2460);
        Assert.AreEqual(new Drawing.Rectangle(-120, -50, 700, 450),
            ScreenshotSelection.Clamp(new(-120, -50), new(580, 400), desktop));
        Assert.AreEqual(new Drawing.Rectangle(-120, -50, 700, 450),
            ScreenshotSelection.Clamp(new(580, 400), new(-120, -50), desktop));
    }

    [TestMethod]
    public void SelectionClampsOutsideDesktopWithoutNegativeDimensions()
    {
        var desktop = new Drawing.Rectangle(-1920, 0, 3840, 1080);
        Assert.AreEqual(desktop, ScreenshotSelection.Clamp(new(-3000, -900), new(5000, 2000), desktop));
        Assert.AreEqual(new Drawing.Rectangle(1920, 1080, 0, 0),
            ScreenshotSelection.Clamp(new(2000, 1200), new(2200, 1300), desktop));
    }

    [STATestMethod]
    public void CropKeepsExactPixelsAndWritesCompatibleRgbPng()
    {
        byte[] pixels = [0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255,
                         255, 255, 0, 255, 0, 255, 0, 255, 255, 0, 0, 0];
        var source = BitmapSource.Create(4, 2, 144, 144, PixelFormats.Bgr24, null, pixels, 12);
        var result = ScreenshotCaptureService.EncodeCrop(source, new Int32Rect(1, 0, 2, 2));
        Assert.AreEqual(2, result.PixelWidth);
        Assert.AreEqual(2, result.PixelHeight);
        Assert.AreEqual((byte)8, result.PngBytes[24], "PNG must use 8-bit channels.");
        Assert.AreEqual((byte)2, result.PngBytes[25], "PNG must use RGB color type.");
        Assert.AreEqual((byte)0, result.PngBytes[28], "PNG must not be interlaced.");
        using var stream = new MemoryStream(result.PngBytes);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        var rgb = new FormatConvertedBitmap(frame, PixelFormats.Bgr24, null, 0);
        var actual = new byte[12];
        rgb.CopyPixels(actual, 6, 0);
        CollectionAssert.AreEqual(new byte[] { 0, 255, 0, 255, 0, 0, 255, 0, 255, 0, 255, 255 }, actual);
    }

    [STATestMethod]
    public void IndexedImageIsNormalizedToRgbWhenCropped()
    {
        var source = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Gray8, null, new byte[4], 2);
        var result = ScreenshotCaptureService.EncodeCrop(source, new Int32Rect(0, 0, 2, 2));
        Assert.AreEqual((byte)2, result.PngBytes[25]);
        Assert.AreEqual(OcrFailure.None, LocalOcrService.ValidateImage(result.PngBytes));
    }

    [STATestMethod]
    [DataRow(1.0)]
    [DataRow(1.5)]
    [DataRow(2.0)]
    public void OverlayHighlightsTheSamePhysicalPixelsAtEachMonitorScale(double scale)
    {
        var white = Enumerable.Repeat((byte)255, 900 * 600 * 3).ToArray();
        var source = BitmapSource.Create(900, 600, 96, 96, PixelFormats.Bgr24, null, white, 2700);
        var surface = new ScreenshotCaptureService.SelectionSurface(new Drawing.Rectangle(-900, 0, 900, 600),
            source, "Drag to capture");
        surface.ShowSelection(new Drawing.Rectangle(-600, 200, 200, 200));
        surface.Measure(new Size(900 / scale, 600 / scale));
        surface.Arrange(new Rect(0, 0, 900 / scale, 600 / scale));
        var rendered = new RenderTargetBitmap(900, 600, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rendered.Render(surface);
        var inside = new byte[4];
        var outside = new byte[4];
        rendered.CopyPixels(new Int32Rect(350, 250, 1, 1), inside, 4, 0);
        rendered.CopyPixels(new Int32Rect(700, 500, 1, 1), outside, 4, 0);
        Assert.AreEqual((byte)255, inside[0], "The selected physical region remains undimmed.");
        Assert.IsTrue(outside[0] < 160, "The rest of each monitor must be dimmed.");
    }
}
