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
        QuickPanelWindowScenarios.Verify(isolatedRoot);
        using var log = new FileLogger(Path.Combine(isolatedRoot, "image-logs"));
        var db = new Db(Path.Combine(isolatedRoot, "image-editor.db"), new DatabaseMaintenanceCoordinator());
        new MigrationRunner(db, log).Run();
        var snippets = new SnippetService(new SnippetRepository(db), log);
        var sourcePixels = Enumerable.Range(0, 800 * 500 * 3).Select(index => (byte)(index * 13 % 251)).ToArray();
        var bitmap = BitmapSource.Create(800, 500, 168, 168, PixelFormats.Bgr24, null, sourcePixels, 800 * 3);
        var cropBounds = new Int32Rect(100, 80, 600, 300);
        byte[] png = ScreenshotCaptureService.EncodeCrop(bitmap, cropBounds).PngBytes;
        byte[] thumbnail = ImagePresentation.Thumbnail(png);
        var dimensions = PngImageValidator.Validate(thumbnail, thumbnail: true);
        Assert.IsTrue(Math.Max(dimensions.Width, dimensions.Height) <= 320);
        long id = snippets.AddImage(png, thumbnail);
        var savedImage = new FormatConvertedBitmap(ImagePresentation.Decode(snippets.GetImage(id)!), PixelFormats.Bgr24, null, 0);
        var expectedPixels = new byte[600 * 300 * 3];
        var savedPixels = new byte[expectedPixels.Length];
        bitmap.CopyPixels(cropBounds, expectedPixels, 600 * 3, 0);
        savedImage.CopyPixels(savedPixels, 600 * 3, 0);
        Assert.AreEqual(600, savedImage.PixelWidth);
        Assert.AreEqual(300, savedImage.PixelHeight);
        CollectionAssert.AreEqual(expectedPixels, savedPixels,
            "Saving a cropped screenshot must retain every original pixel, independent of DPI and thumbnail scaling.");
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
        translation.SetRecognizedSource("Recognized source sample");
        translation.ShowError("Translate.Failed");
        Assert.IsTrue(translation.TryCopySource(value => copiedResult = value));
        Assert.AreEqual("Recognized source sample", copiedResult, "Translation failure must preserve the recognized source.");
        Assert.IsFalse(translation.TryCopyResult(_ => Assert.Fail("An error is not a translation to copy.")));
        translation.HideCard();
        Assert.IsFalse(translation.TryCopySource(_ => Assert.Fail("Dismissal must release the source text.")));
        translation.Close();

        var screenshotActions = new System.Windows.Controls.StackPanel();
        var extractAction = new Button();
        var translateAction = new Button();
        screenshotActions.Children.Add(extractAction);
        screenshotActions.Children.Add(translateAction);
        int extracts = 0, translations = 0;
        extractAction.Click += (_, _) => extracts++;
        translateAction.Click += (_, _) => translations++;
        Assert.IsTrue(ScreenshotSelection.InvokeFocusedAction(screenshotActions, extractAction));
        Assert.IsTrue(ScreenshotSelection.InvokeFocusedAction(screenshotActions, translateAction));
        Assert.AreEqual(1, extracts, "Enter on the OCR action must invoke OCR, not default Save.");
        Assert.AreEqual(1, translations, "Enter on the translation action must invoke translation, not default Save.");
        Assert.IsFalse(ScreenshotSelection.InvokeFocusedAction(screenshotActions, new Button()));
        Assert.IsFalse(ScreenshotSelection.InvokeFocusedAction(screenshotActions, null));
        CaptureActionDocumentationPreviews(isolatedRoot);

        WeakReference closedPreview = CreateClosedImageWindow(snippets, id, png);
        WeakReference closedWindow = CreateClosedTrackedWindow();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.IsFalse(closedWindow.IsAlive, "Theme updates must not retain closed windows and their image buffers.");
        Assert.IsFalse(closedPreview.IsAlive, "Snippet change subscriptions must not retain closed image previews.");
    }

    private static void CaptureActionDocumentationPreviews(string isolatedRoot)
    {
        string? destination = Environment.GetEnvironmentVariable("KEESTASHDOCK_DOC_IMAGES");
        if (string.IsNullOrWhiteSpace(destination)) return;
        string directory = Path.GetFullPath(destination);
        Directory.CreateDirectory(directory);
        var settings = new SettingsStore(Path.Combine(isolatedRoot, "action-previews.json"));
        settings.Current.SelectionAction = "Both";
        settings.Current.BubbleTextGlow = false;
        var selection = new SelectionPopupWindow(settings);
        var recognition = new TranslationResultWindow(settings);
        try
        {
            // Render actual view content without showing native windows, triggering
            // selection reads/hotkeys, capturing desktop pixels, or touching clipboard.
            RenderWindow(selection, "selection-actions.png");
            recognition.ShowRecognizedText("Keep ideas close.\nCapture a region, extract its text, then copy only when you need it.");
            RenderWindow(recognition, "screenshot-text.png");
        }
        finally { selection.Close(); recognition.Close(); }

        void RenderWindow(Window view, string fileName)
        {
            var content = (FrameworkElement)view.Content;
            view.Content = null;
            var frame = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(230, 235, 243)),
                Padding = new Thickness(24),
                FlowDirection = view.FlowDirection,
                Child = content,
            };
            try
            {
                // Render the arranged visual directly. VisualBrush would scale the
                // margin/effect bounds into ActualWidth/Height and compress the text.
                frame.Measure(new Size(468, double.PositiveInfinity));
                frame.Arrange(new Rect(frame.DesiredSize));
                frame.UpdateLayout();
                int width = (int)Math.Ceiling(frame.ActualWidth);
                int height = (int)Math.Ceiling(frame.ActualHeight);
                var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                image.Render(frame);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using var output = File.Create(Path.Combine(directory, fileName));
                encoder.Save(output);
            }
            finally
            {
                frame.Child = null;
                view.Content = content;
            }
        }
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
