using System.Buffers.Binary;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

[TestClass]
public sealed class LocalOcrServiceTests
{
    [TestMethod]
    public void InvalidImagesAreRejectedBeforeStartingNativeEngine()
    {
        Assert.AreEqual(OcrFailure.InvalidImage, LocalOcrService.ValidateImage(null));
        Assert.AreEqual(OcrFailure.InvalidImage, LocalOcrService.ValidateImage([1, 2, 3]));
        Assert.AreEqual(OcrFailure.InvalidImage, LocalOcrService.ValidateImage(Header(0, 20)));
        Assert.AreEqual(OcrFailure.InvalidImage, LocalOcrService.ValidateImage(Header(20, 0)));
    }

    [TestMethod]
    public void ImageBoundsRejectHugeOrOverflowingDimensions()
    {
        Assert.AreEqual(OcrFailure.ImageTooLarge, LocalOcrService.ValidateImage(Header(uint.MaxValue, uint.MaxValue)));
        Assert.AreEqual(OcrFailure.ImageTooLarge, LocalOcrService.ValidateImage(Header(16385, 2)));
        Assert.AreEqual(OcrFailure.ImageTooLarge, LocalOcrService.ValidateImage(Header(8000, 8000)));
        Assert.AreEqual(OcrFailure.None, LocalOcrService.ValidateImage(Header(8000, 5000)));
    }

    [TestMethod]
    public async Task MissingModelsReturnActionableFailureWithoutLaunchingProcess()
    {
        var service = new LocalOcrService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            TimeSpan.FromSeconds(1));
        var result = await service.RecognizeAsync(Header(120, 20));
        Assert.AreEqual(OcrFailure.MissingModels, result.Failure);
        Assert.AreEqual("", result.Text);
        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public async Task AlreadyCancelledRequestReturnsWithoutAccessingModels()
    {
        using var token = new CancellationTokenSource();
        token.Cancel();
        var result = await new LocalOcrService().RecognizeAsync(Header(120, 20), token.Token);
        Assert.AreEqual(OcrFailure.Cancelled, result.Failure);
    }

    private static byte[] Header(uint width, uint height)
    {
        var bytes = new byte[33];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), height);
        return bytes;
    }
}
