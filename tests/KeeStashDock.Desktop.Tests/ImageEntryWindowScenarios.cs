using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KeeStashDock.Core.Services;
using KeeStashDock.Desktop.Views;
using KeeStashDock.Infrastructure.Database;
using KeeStashDock.Infrastructure.Logging;
using KeeStashDock.Infrastructure.Repositories;
using KeeStashDock.Infrastructure.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

// Runs in the existing isolated STA Application; WPF permits only one Application per process.
internal static class ImageEntryWindowScenarios
{
    internal static void Verify(string isolatedRoot)
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try { VerifyCore(isolatedRoot); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static void VerifyCore(string isolatedRoot)
    {
        using var log = new FileLogger(Path.Combine(isolatedRoot, "image-logs"));
        var db = new Db(Path.Combine(isolatedRoot, "image-editor.db"), new DatabaseMaintenanceCoordinator());
        new MigrationRunner(db, log).Run();
        var snippets = new SnippetService(new SnippetRepository(db), log);
        var bitmap = BitmapSource.Create(600, 300, 96, 96, PixelFormats.Bgr24, null, new byte[600 * 300 * 3], 600 * 3);
        byte[] png = ScreenshotCaptureService.EncodeCrop(bitmap, new Int32Rect(0, 0, 600, 300)).PngBytes;
        byte[] thumbnail = ImagePresentation.Thumbnail(png);
        var dimensions = PngImageValidator.Validate(thumbnail, thumbnail: true);
        Assert.IsTrue(Math.Max(dimensions.Width, dimensions.Height) <= 320);
        long id = snippets.AddImage(png, thumbnail);
        string hash = snippets.GetById(id)!.ContentHash;
        TaskCompletionSource<OcrRecognitionResult>? pending = null;
        CancellationToken token = default;
        var window = new ImageEntryWindow(snippets, (_, ct) =>
        {
            token = ct;
            return pending?.Task ?? Task.FromResult(new OcrRecognitionResult("Recognized screenshot text", OcrFailure.None));
        }, snippets.GetById(id)!, png);
        TextBox text = (TextBox)window.FindName("OcrText");
        Button recognize = (Button)window.FindName("RecognizeButton");
        recognize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.AreEqual("Recognized screenshot text", snippets.GetById(id)!.Content);
        Assert.AreEqual(1, snippets.AllLive().Count, "OCR must update the image entry instead of making a text card.");
        Assert.AreEqual(hash, snippets.GetById(id)!.ContentHash);
        CollectionAssert.AreEqual(png, snippets.GetImage(id));

        string? offered = null;
        window.SelectionRequested += source => offered = source.SelectedText;
        text.Select(0, 10);
        text.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
        Assert.AreEqual("Recognized", offered, "Internal selection must offer the existing bubble without copying the clipboard.");

        text.Text = string.Empty;
        Assert.IsTrue(window.SaveText(), "An image can retain an empty OCR field.");
        pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        recognize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ((Button)window.FindName("CancelRecognitionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.IsTrue(token.IsCancellationRequested);
        pending.SetResult(new OcrRecognitionResult("Late cancelled output", OcrFailure.None));
        PumpUntil(() => recognize.IsEnabled);
        Assert.AreEqual(string.Empty, text.Text);
        Assert.AreEqual(string.Empty, snippets.GetById(id)!.Content);

        pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        recognize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        snippets.MoveToTrash(id);
        Assert.IsTrue(token.IsCancellationRequested, "Deleting an image elsewhere must cancel its in-flight OCR.");
        pending.SetResult(new OcrRecognitionResult("Late deleted output", OcrFailure.None));
        PumpUntil(() => window.RecognitionCompletion.IsCompleted);
        Assert.AreEqual(string.Empty, snippets.GetById(id)!.Content);
        Assert.IsTrue(text.IsReadOnly, "Deleting the entry in another window must immediately lock OCR edits.");
        Assert.IsFalse(recognize.IsEnabled);
        snippets.RestoreFromTrash(id);
        Assert.IsFalse(text.IsReadOnly, "Restoring the entry must unlock the existing preview without reopening it.");
        Assert.IsTrue(recognize.IsEnabled);
        var changed = snippets.GetById(id)!;
        changed.Content = "Clean editor refresh";
        snippets.Update(changed);
        Assert.AreEqual(changed.Content, text.Text, "A clean preview must follow edits made in the workbench.");
        text.Text = "Unsaved old editor";
        changed.Content = "Changed in another editor";
        snippets.Update(changed);
        Assert.AreEqual("Unsaved old editor", text.Text, "External edits must preserve a dirty preview draft.");
        Assert.IsFalse(window.SaveText(), "A stale image editor must not overwrite a newer edit.");
        Assert.AreEqual("Changed in another editor", snippets.GetById(id)!.Content);
        text.Text = "Clean editor refresh";
        window.Close();

        changed.Content = string.Empty;
        snippets.Update(changed);
        pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var closing = new ImageEntryWindow(snippets, (_, ct) => { token = ct; return pending.Task; }, changed, png);
        ((Button)closing.FindName("RecognizeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        closing.Close();
        Assert.IsTrue(token.IsCancellationRequested, "Closing the preview must cancel its OCR process.");
        pending.SetResult(new OcrRecognitionResult("Late closed output", OcrFailure.None));
        PumpUntil(() => pending.Task.IsCompleted);
        Assert.AreEqual(string.Empty, snippets.GetById(id)!.Content);

        var translation = new TranslationResultWindow(new SettingsStore(Path.Combine(isolatedRoot, "translation-copy.json")));
        translation.ShowResult("Translated sample");
        Assert.IsFalse(translation.TryCopyResult(_ => throw new System.Runtime.InteropServices.COMException("Clipboard busy")));
        string? copiedResult = null;
        Assert.IsTrue(translation.TryCopyResult(value => copiedResult = value));
        Assert.AreEqual("Translated sample", copiedResult, "A failed copy must leave the translation available for retry.");
        translation.HideCard();
        translation.Close();

        WeakReference closedPreview = CreateClosedImageWindow(snippets, id, png);
        WeakReference closedWindow = CreateClosedTrackedWindow();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.IsFalse(closedWindow.IsAlive, "Theme updates must not retain closed windows and their image buffers.");
        Assert.IsFalse(closedPreview.IsAlive, "Snippet change subscriptions must not retain closed image previews.");
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference CreateClosedImageWindow(SnippetService snippets, long id, byte[] png)
    {
        var window = new ImageEntryWindow(snippets, (_, _) => Task.FromResult(new OcrRecognitionResult(string.Empty, OcrFailure.NoText)),
            snippets.GetById(id)!, png);
        var reference = new WeakReference(window);
        window.Close();
        return reference;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference CreateClosedTrackedWindow()
    {
        var closed = new Window();
        ThemeManager.TrackWindowChrome(closed);
        var reference = new WeakReference(closed);
        closed.Close();
        return reference;
    }

    private static void PumpUntil(Func<bool> complete)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        timer.Tick += (_, _) => { if (complete() || DateTime.UtcNow >= deadline) frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Assert.IsTrue(complete(), "The image editor operation did not complete.");
    }
}
