using KeeStashDock.Core.Services;

namespace KeeStashDock.Desktop;

internal sealed record ScreenshotProcessingResult(bool Saved,
    OcrRecognitionResult? Recognition = null, TranslationResult? Translation = null);

/// <summary>
/// Routes the user's explicit action. Only Save can reach the persistence delegate;
/// OCR stays in memory and only ExtractAndTranslate can send recognized text to a provider.
/// </summary>
internal sealed class ScreenshotActionProcessor(
    Action<CapturedScreenshot> save,
    Func<byte[], CancellationToken, Task<OcrRecognitionResult>> recognize,
    Func<string, CancellationToken, Task<TranslationResult>> translate)
{
    internal async Task<ScreenshotProcessingResult> ExecuteAsync(CapturedScreenshot image,
        ScreenshotAction action, CancellationToken token, Action<string>? onRecognized = null)
    {
        token.ThrowIfCancellationRequested();
        if (action == ScreenshotAction.Save)
        {
            save(image);
            return new(true);
        }
        if (action is not (ScreenshotAction.ExtractText or ScreenshotAction.ExtractAndTranslate))
            throw new ArgumentOutOfRangeException(nameof(action));

        OcrRecognitionResult recognition = await recognize(image.PngBytes, token);
        token.ThrowIfCancellationRequested();
        if (!recognition.IsSuccess) return new(false, recognition);
        onRecognized?.Invoke(recognition.Text);
        token.ThrowIfCancellationRequested();
        if (action == ScreenshotAction.ExtractText) return new(false, recognition);

        TranslationResult translation = await translate(recognition.Text, token);
        token.ThrowIfCancellationRequested();
        return new(false, recognition, translation);
    }

    internal static string RecognitionMessageKey(OcrFailure failure) => failure switch
    {
        OcrFailure.NoText => "Screenshot.OcrNoText",
        OcrFailure.MissingModels => "Screenshot.OcrMissingModels",
        OcrFailure.EngineUnavailable => "Screenshot.OcrEngineUnavailable",
        OcrFailure.Timeout => "Screenshot.OcrTimeout",
        OcrFailure.ImageTooLarge => "Screenshot.OcrImageTooLarge",
        OcrFailure.InvalidImage => "Screenshot.OcrInvalidImage",
        OcrFailure.Cancelled => "Screenshot.OcrCancelled",
        _ => "Screenshot.OcrTransientFailed"
    };
}
