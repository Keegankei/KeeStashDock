using System.Windows;
using KeeStashDock.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using KeeStashDock.Core.Services;
using KeeStashDock.Desktop.Interop;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Desktop.Views;
using KeeStashDock.Infrastructure.Database;
using KeeStashDock.Infrastructure.Storage;

namespace KeeStashDock.Desktop;

public partial class App
{
    private void CaptureScreenshot()
        => CaptureScreenshotFrom(_workbench?.IsActive == true ? _workbench.CurrentCategoryId : _panel?.CurrentCategoryId,
            _workbench?.IsActive == true ? ExpirePolicy.Never : _panel?.CurrentExpirePolicy ?? ExpirePolicy.Never);

    private async void CaptureScreenshotFrom(long? categoryId, ExpirePolicy expire)
    {
        if (_exitPending || _capturingScreenshot || _preparingShutdown || _shutdownCancellation.IsCancellationRequested ||
            !_services.GetRequiredService<DatabaseMaintenanceCoordinator>().CanStartOperation) return;
        var settings = _services.GetRequiredService<SettingsStore>();
        var visible = Windows.OfType<Window>().Where(window => window.IsVisible &&
            window is not SelectionPopupWindow && window is not TranslationResultWindow &&
            window is not PanelShadowWindow).ToArray();
        nint previousForeground = NativeMethods.GetForegroundWindow();
        _translationCancellation?.Cancel();
        _internalSelectionSource = null;
        _selectionPopup.HideBubble();
        _translationCard.HideCard();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCancellation.Token);
        _translationCancellation = request;
        void RestoreWindows()
        {
            foreach (Window window in visible)
                if (Windows.OfType<Window>().Contains(window))
                {
                    if (window is QuickPanelWindow quickPanel) quickPanel.ShowPanel();
                    else window.Show();
                }
            if (previousForeground != nint.Zero) NativeMethods.SetForegroundWindow(previousForeground);
        }

        try
        {
            ScreenshotCaptureResult? captured = await _selectionWatcherController.CaptureAsync(async () =>
            {
                foreach (Window window in visible) window.Hide();
                // Let the compositor remove our windows before freezing the screen.
                await Task.Delay(180, request.Token);
                return await _screenshotCapture.CaptureAsync(request.Token);
            }, RestoreWindows, _shutdownCancellation.Token);
            // The workspace and selection hook are restored before asynchronous OCR.
            if (captured is null || request.IsCancellationRequested) return;

            var processor = new ScreenshotActionProcessor(image =>
            {
                var snippets = _services.GetRequiredService<SnippetService>();
                long id = snippets.AddImage(image.PngBytes, ImagePresentation.Thumbnail(image.PngBytes), categoryId, expire);
                if (categoryId is long group) snippets.AssignCategory(id, group);
            }, _localOcr.RecognizeAsync,
                (text, token) => TranslationService.TranslateDetailedAsync(text, settings.Current, token));

            if (captured.Action != ScreenshotAction.Save)
                _translationCard.ShowRecognizing(captured.AnchorX, captured.AnchorY);

            ScreenshotProcessingResult result = await processor.ExecuteAsync(captured.Image, captured.Action, request.Token,
                text =>
                {
                    if (captured.Action == ScreenshotAction.ExtractAndTranslate)
                    {
                        _translationCard.ShowTranslating(captured.AnchorX, captured.AnchorY);
                        _translationCard.SetRecognizedSource(text);
                    }
                });
            if (!ReferenceEquals(_translationCancellation, request) || request.IsCancellationRequested) return;
            if (result.Saved)
            {
                Panel.ShowPanel();
                MiniToast.Show(LocalizationService.Instance["Screenshot.Saved"]);
                return;
            }
            if (!_translationCard.IsVisible) return;
            if (result.Recognition is { IsSuccess: false } failed)
            {
                if (failed.Failure == OcrFailure.Cancelled) { _translationCard.HideCard(); return; }
                _translationCard.ShowError(ScreenshotActionProcessor.RecognitionMessageKey(failed.Failure));
                _translationCard.SetActions(null,
                    failed.Failure is OcrFailure.MissingModels or OcrFailure.EngineUnavailable ? ShowComponentSettings : null);
            }
            else if (result.Translation is { } translation)
                ShowScreenshotTranslation(translation, result.Recognition!.Text, captured.AnchorX, captured.AnchorY);
            else
                _translationCard.ShowRecognizedText(result.Recognition!.Text);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!request.IsCancellationRequested)
            {
                _translationCard.HideCard();
                MiniToast.Show(LocalizationService.Instance["Screenshot.CaptureFailed"]);
            }
        }
        finally
        {
            if (ReferenceEquals(_translationCancellation, request)) _translationCancellation = null;
        }
    }

    private void ShowScreenshotTranslation(TranslationResult result, string source, int x, int y)
    {
        if (result.IsSuccess) _translationCard.ShowResult(result.Text!);
        else if (result.Failure == TranslationFailure.Cancelled) { _translationCard.HideCard(); return; }
        else _translationCard.ShowError(TranslationService.FailureMessageKey(result));
        _translationCard.SetActions(() => RetryScreenshotTranslation(source, x, y),
            result.Failure is TranslationFailure.LocalServiceUnavailable or TranslationFailure.LanguageModelUnavailable
                ? ShowComponentSettings : null);
    }

    private async void RetryScreenshotTranslation(string source, int x, int y)
    {
        if (_preparingShutdown || _shutdownCancellation.IsCancellationRequested) return;
        _translationCancellation?.Cancel();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCancellation.Token);
        _translationCancellation = request;
        try
        {
            _translationCard.ShowTranslating(x, y);
            _translationCard.SetRecognizedSource(source);
            TranslationResult result = await TranslationService.TranslateDetailedAsync(source,
                _services.GetRequiredService<SettingsStore>().Current, request.Token);
            if (!request.IsCancellationRequested && ReferenceEquals(_translationCancellation, request) && _translationCard.IsVisible)
                ShowScreenshotTranslation(result, source, x, y);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!request.IsCancellationRequested && ReferenceEquals(_translationCancellation, request) && _translationCard.IsVisible)
                ShowScreenshotTranslation(new TranslationResult("", null, TranslationFailure.ProviderUnavailable), source, x, y);
        }
        finally
        {
            if (ReferenceEquals(_translationCancellation, request)) _translationCancellation = null;
        }
    }
}
