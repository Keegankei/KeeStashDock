using KeeStashDock.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

[TestClass]
public sealed class SelectionWatcherControllerTests
{
    private static readonly CapturedScreenshot Image = new([1, 2, 3], 2, 2);

    [TestMethod]
    [DataRow(true, true)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(false, false)]
    public async Task SaveOrCancelRestoresSelectionWithInitiallyHiddenTranslation(bool popupEnabled, bool save)
    {
        var fixture = new WatcherFixture(popupEnabled);
        var capture = new TaskCompletionSource<ScreenshotCaptureResult?>();
        Task<ScreenshotCaptureResult?> pending = fixture.Controller.CaptureAsync(
            () => capture.Task, fixture.RestoreWindows, CancellationToken.None);
        AssertSuspended(fixture);

        capture.SetResult(save ? new(Image, ScreenshotAction.Save, 0, 0) : null);
        ScreenshotCaptureResult? result = await pending;
        AssertRestored(fixture, popupEnabled);
        int saved = 0;
        var processor = new ScreenshotActionProcessor(_ =>
        {
            AssertRestored(fixture, popupEnabled);
            saved++;
        }, (_, _) => throw new AssertFailedException("Save must not start OCR."),
            (_, _) => throw new AssertFailedException("Save must not translate."));
        if (result is not null)
            await processor.ExecuteAsync(result.Image, result.Action, CancellationToken.None);
        Assert.AreEqual(save ? 1 : 0, saved);
        fixture.Controller.Apply();
        Assert.AreEqual(popupEnabled ? 2 : 0, fixture.Starts,
            "Enabled selection resumes once after capture; disabled selection must not start a hook.");
    }

    [TestMethod]
    [DataRow(true, false)]
    [DataRow(true, true)]
    [DataRow(false, false)]
    [DataRow(false, true)]
    public async Task CaptureFailureOrRequestCancellationRestoresSelection(bool popupEnabled, bool cancelled)
    {
        var fixture = new WatcherFixture(popupEnabled);
        var capture = new TaskCompletionSource<ScreenshotCaptureResult?>();
        Task<ScreenshotCaptureResult?> pending = fixture.Controller.CaptureAsync(
            () => capture.Task, fixture.RestoreWindows, CancellationToken.None);
        AssertSuspended(fixture);
        if (cancelled)
        {
            capture.SetCanceled();
            await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await pending);
        }
        else
        {
            capture.SetException(new InvalidOperationException("Synthetic capture failure"));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => await pending);
        }
        AssertRestored(fixture, popupEnabled);
    }

    [TestMethod]
    [DataRow(true, ScreenshotAction.ExtractText)]
    [DataRow(false, ScreenshotAction.ExtractText)]
    [DataRow(true, ScreenshotAction.ExtractAndTranslate)]
    [DataRow(false, ScreenshotAction.ExtractAndTranslate)]
    public async Task OcrStartsAfterRestorationAndResultVisibilityKeepsDismissalListening(
        bool popupEnabled, ScreenshotAction action)
    {
        var fixture = new WatcherFixture(popupEnabled);
        var capture = new TaskCompletionSource<ScreenshotCaptureResult?>();
        Task<ScreenshotCaptureResult?> pendingCapture = fixture.Controller.CaptureAsync(
            () => capture.Task, fixture.RestoreWindows, CancellationToken.None);
        AssertSuspended(fixture);
        capture.SetResult(new(Image, action, 0, 0));
        ScreenshotCaptureResult? captured = await pendingCapture;
        AssertRestored(fixture, popupEnabled);

        // These are the same visibility notifications wired by App. The result
        // begins hidden, so it cannot accidentally repair the capture cache.
        fixture.TranslationVisible = true;
        fixture.Controller.Apply();
        Assert.IsTrue(fixture.Listening, "OCR results need outside-click dismissal even with selection bubbles disabled.");
        var recognition = new TaskCompletionSource<OcrRecognitionResult>();
        int translations = 0;
        var processor = new ScreenshotActionProcessor(_ => throw new AssertFailedException("Transient OCR must not save."),
            (_, _) =>
            {
                Assert.IsFalse(fixture.Controller.IsCapturingScreenshot);
                Assert.AreEqual(1, fixture.Restores, "The desktop must be restored before slow OCR starts.");
                Assert.IsTrue(fixture.Listening);
                return recognition.Task;
            }, (text, _) =>
            {
                translations++;
                return Task.FromResult(new TranslationResult("test-provider", text));
            });
        Task<ScreenshotProcessingResult> processing = processor.ExecuteAsync(captured!.Image, action, CancellationToken.None);
        Assert.IsFalse(processing.IsCompleted);
        recognition.SetResult(new("recognized text", OcrFailure.None));
        ScreenshotProcessingResult result = await processing;
        Assert.AreEqual("recognized text", result.Recognition!.Text);
        Assert.AreEqual(action == ScreenshotAction.ExtractAndTranslate ? 1 : 0, translations);

        fixture.TranslationVisible = false;
        fixture.Controller.Apply();
        Assert.AreEqual(popupEnabled, fixture.Listening,
            "Dismissing OCR must preserve the user's bubble setting instead of leaving a hook enabled or disabled.");
        Assert.AreEqual(1, fixture.Restores, "Finishing OCR must not restore the workspace a second time.");
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ExistingTranslationIsRetiredBeforeCaptureAndDoesNotOverrideBubbleSetting(bool popupEnabled)
    {
        var fixture = new WatcherFixture(popupEnabled);
        fixture.TranslationVisible = true;
        fixture.Controller.Apply();
        Assert.IsTrue(fixture.Listening);

        // App hides the previous translation and receives IsVisibleChanged
        // before starting the screenshot phase and its new cancellation request.
        fixture.TranslationVisible = false;
        fixture.Controller.Apply();
        var capture = new TaskCompletionSource<ScreenshotCaptureResult?>();
        Task<ScreenshotCaptureResult?> pending = fixture.Controller.CaptureAsync(
            () => capture.Task, fixture.RestoreWindows, CancellationToken.None);
        AssertSuspended(fixture);
        capture.SetResult(null);
        await pending;
        AssertRestored(fixture, popupEnabled);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task SettingsChangedDuringCaptureAreAppliedOnlyAfterCapture(bool initiallyEnabled)
    {
        var fixture = new WatcherFixture(initiallyEnabled);
        var capture = new TaskCompletionSource<ScreenshotCaptureResult?>();
        Task<ScreenshotCaptureResult?> pending = fixture.Controller.CaptureAsync(
            () => capture.Task, fixture.RestoreWindows, CancellationToken.None);
        fixture.PopupEnabled = !initiallyEnabled;
        fixture.TranslationVisible = true;
        fixture.Controller.Apply();
        AssertSuspended(fixture);
        fixture.TranslationVisible = false;
        fixture.Controller.Apply();
        capture.SetResult(null);
        await pending;
        AssertRestored(fixture, !initiallyEnabled);
    }

    [TestMethod]
    public async Task ShutdownDoesNotRestoreWindowsOrRestartListening()
    {
        var fixture = new WatcherFixture(popupEnabled: true);
        using var shutdown = new CancellationTokenSource();
        var capture = new TaskCompletionSource<ScreenshotCaptureResult?>();
        Task<ScreenshotCaptureResult?> pending = fixture.Controller.CaptureAsync(
            () => capture.Task, fixture.RestoreWindows, shutdown.Token);
        AssertSuspended(fixture);
        shutdown.Cancel();
        capture.SetResult(null);
        await pending;
        Assert.IsFalse(fixture.Controller.IsCapturingScreenshot);
        Assert.IsFalse(fixture.Listening);
        Assert.AreEqual(0, fixture.Restores);
        Assert.AreEqual(1, fixture.Starts);
    }

    [TestMethod]
    public async Task WindowRestorationFailureStillRestartsSelection()
    {
        var fixture = new WatcherFixture(popupEnabled: true);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Controller.CaptureAsync(
            () => Task.FromResult<ScreenshotCaptureResult?>(null),
            () => throw new InvalidOperationException("Synthetic window restoration failure"), CancellationToken.None));
        Assert.IsFalse(fixture.Controller.IsCapturingScreenshot);
        Assert.IsTrue(fixture.Listening);
        Assert.AreEqual(2, fixture.Starts);
    }

    [TestMethod]
    public async Task RepeatedSaveAndCancelRestartOncePerCapture()
    {
        var fixture = new WatcherFixture(popupEnabled: true);
        for (int iteration = 0; iteration < 4; iteration++)
        {
            ScreenshotCaptureResult? expected = iteration % 2 == 0 ? new(Image, ScreenshotAction.Save, 0, 0) : null;
            ScreenshotCaptureResult? actual = await fixture.Controller.CaptureAsync(() =>
            {
                Assert.IsTrue(fixture.Controller.IsCapturingScreenshot);
                Assert.IsFalse(fixture.Listening);
                fixture.Controller.Apply();
                return Task.FromResult(expected);
            }, fixture.RestoreWindows, CancellationToken.None);
            Assert.AreSame(expected, actual);
            Assert.IsFalse(fixture.Controller.IsCapturingScreenshot);
            Assert.IsTrue(fixture.Listening);
            Assert.AreEqual(iteration + 1, fixture.Restores);
            Assert.AreEqual(iteration + 2, fixture.Starts);
        }
    }

    [TestMethod]
    public void UpdateStopCanBeReconciledAfterAnAbortedUpdate()
    {
        var fixture = new WatcherFixture(popupEnabled: true);
        fixture.Controller.Stop();
        Assert.IsFalse(fixture.Listening);
        fixture.Controller.Apply();
        Assert.IsTrue(fixture.Listening);
        Assert.AreEqual(2, fixture.Starts);
    }

    private static void AssertSuspended(WatcherFixture fixture)
    {
        Assert.IsTrue(fixture.Controller.IsCapturingScreenshot);
        Assert.IsFalse(fixture.Listening, "Screenshot dragging must not trigger selection or result dismissal.");
        Assert.AreEqual(0, fixture.Restores);
    }

    private static void AssertRestored(WatcherFixture fixture, bool listening)
    {
        Assert.IsFalse(fixture.Controller.IsCapturingScreenshot);
        Assert.AreEqual(listening, fixture.Listening,
            "Selection listening must resume after capture even when the translation card was already hidden.");
        Assert.AreEqual(1, fixture.Restores);
    }

    // Only native hook and window effects are replaced. The applied-state cache,
    // suspension, finally restoration and visibility decisions are production code.
    private sealed class WatcherFixture
    {
        internal bool PopupEnabled, TranslationVisible, Listening;
        internal int Starts, Restores;
        internal SelectionWatcherController Controller { get; }

        internal WatcherFixture(bool popupEnabled)
        {
            PopupEnabled = popupEnabled;
            Controller = new SelectionWatcherController(
                () => { Listening = true; Starts++; }, () => Listening = false, () => { },
                () => PopupEnabled, () => TranslationVisible);
            Controller.Apply();
            Assert.AreEqual(popupEnabled, Listening);
        }

        internal void RestoreWindows()
        {
            Assert.IsFalse(Controller.IsCapturingScreenshot);
            Restores++;
        }
    }
}
