using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Text;
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

    [TestMethod]
    public async Task ExcessiveEngineOutputFailsPromptlyWithoutWaitingForTheRecognitionDeadline()
    {
        string shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(shell)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        string command = "[Console]::Error.Write(('x' * 100000)); Start-Sleep -Seconds 30";
        foreach (string argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand",
            Convert.ToBase64String(Encoding.Unicode.GetBytes(command)) }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            var timer = Stopwatch.StartNew();
            await Assert.ThrowsExactlyAsync<IOException>(() =>
                LocalOcrService.ExchangeImageAsync(process, Header(20, 20), deadline.Token)
                    .WaitAsync(TimeSpan.FromSeconds(8)));
            Assert.IsTrue(timer.Elapsed < TimeSpan.FromSeconds(8),
                "Output overflow must be returned promptly, rather than mislabeled as an OCR timeout.");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
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
