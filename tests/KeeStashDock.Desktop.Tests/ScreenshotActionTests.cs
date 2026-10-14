using System.Windows;
using KeeStashDock.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

[TestClass]
public sealed class ScreenshotActionTests
{
    private static readonly CapturedScreenshot Image = new([1, 2, 3], 2, 2);

    [TestMethod]
    public async Task SaveIsTheOnlyRouteThatPersistsAndDoesNotStartOcrOrTranslation()
    {
        int saves = 0;
        var processor = new ScreenshotActionProcessor(image => { Assert.AreSame(Image, image); saves++; },
            (_, _) => throw new AssertFailedException("Save must not invoke OCR."),
            (_, _) => throw new AssertFailedException("Save must not invoke translation."));
        ScreenshotProcessingResult result = await processor.ExecuteAsync(Image, ScreenshotAction.Save, CancellationToken.None);
        Assert.AreEqual(1, saves);
        Assert.IsTrue(result.Saved);
        Assert.IsNull(result.Recognition);
    }

    [TestMethod]
    public async Task ExtractOnlyReturnsTextWithoutSavingOrCallingTranslation()
    {
        var processor = new ScreenshotActionProcessor(_ => throw new AssertFailedException("Transient action persisted."),
            (bytes, _) => { Assert.AreSame(Image.PngBytes, bytes); return Task.FromResult(new OcrRecognitionResult("sample", OcrFailure.None)); },
            (_, _) => throw new AssertFailedException("Extract must not call a provider."));
        var result = await processor.ExecuteAsync(Image, ScreenshotAction.ExtractText, CancellationToken.None);
        Assert.AreEqual("sample", result.Recognition!.Text);
        Assert.IsFalse(result.Saved);
        Assert.IsNull(result.Translation);
    }

    [TestMethod]
    public async Task TranslationReceivesOnlyRecognizedTextAndFailureKeepsSource()
    {
        var sequence = new List<string>();
        var processor = new ScreenshotActionProcessor(_ => throw new AssertFailedException("Transient action persisted."),
            (_, _) => Task.FromResult(new OcrRecognitionResult("recognized sample", OcrFailure.None)),
            (text, _) =>
            {
                sequence.Add("translate");
                Assert.AreEqual("recognized sample", text);
                return Task.FromResult(new TranslationResult("configured-provider", null, TranslationFailure.Network));
            });
        var result = await processor.ExecuteAsync(Image, ScreenshotAction.ExtractAndTranslate, CancellationToken.None,
            source => { Assert.AreEqual("recognized sample", source); sequence.Add("show source"); });
        CollectionAssert.AreEqual(new[] { "show source", "translate" }, sequence);
        Assert.AreEqual("recognized sample", result.Recognition!.Text);
        Assert.AreEqual(TranslationFailure.Network, result.Translation!.Failure);
        Assert.IsFalse(result.Saved);
    }

    [TestMethod]
    [DataRow(OcrFailure.NoText)]
    [DataRow(OcrFailure.MissingModels)]
    [DataRow(OcrFailure.EngineUnavailable)]
    [DataRow(OcrFailure.Timeout)]
    public async Task OcrFailureNeverCallsTranslationOrPersists(OcrFailure failure)
    {
        var processor = new ScreenshotActionProcessor(_ => throw new AssertFailedException("Unexpected save."),
            (_, _) => Task.FromResult(new OcrRecognitionResult("", failure)),
            (_, _) => throw new AssertFailedException("Failed OCR must not translate."));
        var result = await processor.ExecuteAsync(Image, ScreenshotAction.ExtractAndTranslate, CancellationToken.None,
            _ => throw new AssertFailedException("Failed OCR must not display recognized text."));
        Assert.AreEqual(failure, result.Recognition!.Failure);
        Assert.IsNull(result.Translation);
    }

    [TestMethod]
    public async Task DismissalRejectsLateRecognitionEvenIfEngineIgnoresCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var completion = new TaskCompletionSource<OcrRecognitionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var processor = new ScreenshotActionProcessor(_ => throw new AssertFailedException("Unexpected save."),
            (_, _) => completion.Task, (_, _) => throw new AssertFailedException("Stale OCR must not translate."));
        Task<ScreenshotProcessingResult> pending = processor.ExecuteAsync(Image, ScreenshotAction.ExtractAndTranslate,
            cancellation.Token, _ => throw new AssertFailedException("Dismissed card must not receive text."));
        cancellation.Cancel();
        completion.SetResult(new("late sample", OcrFailure.None));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () => await pending);
    }

    [TestMethod]
    public async Task DismissalRejectsLateTranslationWithoutSaving()
    {
        using var cancellation = new CancellationTokenSource();
        var completion = new TaskCompletionSource<TranslationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var processor = new ScreenshotActionProcessor(_ => throw new AssertFailedException("Unexpected save."),
            (_, _) => Task.FromResult(new OcrRecognitionResult("sample", OcrFailure.None)), (_, _) => completion.Task);
        var pending = processor.ExecuteAsync(Image, ScreenshotAction.ExtractAndTranslate, cancellation.Token);
        cancellation.Cancel();
        completion.SetResult(new("configured-provider", "late text"));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () => await pending);
    }

    [TestMethod]
    public async Task CancelledSaveCannotCreateAnEntry()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var processor = new ScreenshotActionProcessor(_ => throw new AssertFailedException("Cancelled save persisted."),
            (_, _) => throw new AssertFailedException("Unexpected OCR."), (_, _) => throw new AssertFailedException("Unexpected translation."));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => processor.ExecuteAsync(Image, ScreenshotAction.Save, cancellation.Token));
    }

    [TestMethod]
    [DataRow(1d)]
    [DataRow(1.5d)]
    [DataRow(2d)]
    public void ToolbarAtMonitorEdgesFitsScaledBounds(double scale)
    {
        Size monitor = new(1920 / scale, 1080 / scale);
        Size toolbar = new(650, 90);
        foreach (Rect selection in new[] { new Rect(0, 0, 2, 2),
                     new Rect(monitor.Width - 3, monitor.Height - 3, 2, 2),
                     new Rect(-800, -300, monitor.Width + 1600, monitor.Height + 600) })
        {
            Point point = ScreenshotSelection.PlaceToolbar(selection, toolbar, monitor);
            Assert.IsTrue(point.X >= 0 && point.Y >= 0);
            Assert.IsTrue(point.X + toolbar.Width <= monitor.Width);
            Assert.IsTrue(point.Y + toolbar.Height <= monitor.Height);
        }
    }
}
