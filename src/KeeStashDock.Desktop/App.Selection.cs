using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KeeStashDock.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using KeeStashDock.Core.Services;
using KeeStashDock.Desktop.Interop;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Desktop.Views;
using KeeStashDock.Infrastructure.Storage;

namespace KeeStashDock.Desktop;

public partial class App
{
    private readonly SelectionSession _selectionSession = new();
    private static readonly SemaphoreSlim SelectionReadGate = new(1, 1);
    private long? _selectionActionRevision;

    private void InitializeSelectionServices(SettingsStore settings)
    {
        // Selection popup: a mouse hook spots select-like gestures anywhere
        // on the desktop and offers a "copy to KeeStashDock" bubble beside
        // the selection.
        _selectionWatcher = new SelectionWatcher();
        _selectionPopup = new SelectionPopupWindow(settings);
        _translationCard = new TranslationResultWindow(settings);
        _selectionWatcherController = new SelectionWatcherController(
            _selectionWatcher.Start, _selectionWatcher.Stop, _selectionPopup.HideBubble,
            () => settings.Current.SelectionPopupEnabled, () => _translationCard.IsVisible);
        _translationCard.IsVisibleChanged += (_, _) =>
        {
            if (!_translationCard.IsVisible) _translationCancellation?.Cancel();
            ApplySelectionPopupSetting();
        };
        MiniToast.Settings = settings;
        DirectPaste.Settings = settings;
        DirectPaste.StartTracking();
        _clipboardWatcher = new ClipboardWatcher(
            settings, _services.GetRequiredService<SnippetService>());
        _selectionWatcher.SelectionLikely += (x, y) =>
        {
            if (_capturingScreenshot) return;
            _selectionPopup.HideBubble();
            _internalSelectionSource = null;
            _translationCancellation?.Cancel();
            _translationCard.HideCard();   // a new selection retires old results
            if (!settings.Current.SelectionPopupEnabled) return;
            (_lastSelectionX, _lastSelectionY) = (x, y);
            nint source = NativeMethods.GetForegroundWindow();
            _selectionSession.Begin(token => GrabSelectionTextAsync(source, token));
            _selectionPopup.ShowAt(x, y);
        };
        _selectionWatcher.AnyMouseDown += (_, _) => DismissSelection();
        _selectionPopup.Dismissed += () =>
        {
            _internalSelectionSource = null;
            _selectionSession.Clear();
        };
        // The global hook intentionally ignores all application windows. Own UI
        // clicks outside either bubble must still retire its source and result.
        EventManager.RegisterClassHandler(typeof(Window), Mouse.PreviewMouseDownEvent,
            new MouseButtonEventHandler((sender, _) =>
            {
                DismissSelectionForMouseDown(sender as Window, _capturingScreenshot,
                    _selectionPopup, _translationCard, DismissSelection);
            }), handledEventsToo: true);
        Exit += (_, _) => _selectionSession.Dispose();
        _selectionPopup.CaptureRequested += CaptureSelectionAsync;
        _selectionPopup.TranslateRequested += TranslateSelectionAsync;
        ApplySelectionPopupSetting();
        settings.Changed += ApplySelectionPopupSetting;

    }

    private void ApplySelectionPopupSetting()
        => _selectionWatcherController.Apply();

    /// <summary>Bubble clicked: synthesize Ctrl+C into the still-focused
    /// source app, VERIFY via the clipboard sequence number that a copy
    /// actually happened (the gesture heuristic can misfire on plain drags),
    /// wash the formatting off, stash into the panel's current group.
    ///
    /// Rich-text hardening (bug: browser PAGE selections toasted "no
    /// selection" while input-box text worked). Two distinct failure modes
    /// were being conflated:
    ///   1. Browsers write HTML + RTF + text and can take well over the old
    ///      600 ms - the wait window is now 2 s with early exit;
    ///   2. worse, the sequence number often bumps while the source app
    ///      STILL HOLDS the clipboard open for the remaining formats, so the
    ///      first GetText() threw CLIPBRD_E_CANT_OPEN, was caught into "",
    ///      and misreported as "no selection". Reading now retries, and if a
    ///      copy verifiably happened (seq changed) but text stays
    ///      unreachable, falls back to the CF_HTML fragment stripped to
    ///      plain text; only then does it report "capture failed" - a
    ///      DIFFERENT message from "no selection".</summary>
    private async void CaptureSelectionAsync()
    {
        long revision = _selectionSession.Revision;
        if (!_selectionSession.IsOpen || _selectionSession.HasCaptured || _selectionActionRevision == revision) return;
        _selectionActionRevision = revision;
        _selectionPopup.SetBusy(true);
        try
        {
            string? text = await _selectionSession.ReadAsync(_shutdownCancellation.Token);
            if (text is null || revision != _selectionSession.Revision) return;
            // Read-for-translation and the deliberate plain-text write are both
            // marked as application-owned, so history cannot duplicate the entry.
            ClipboardWriteGuard.Shared.Write(() => Clipboard.SetDataObject(text, copy: true));
            if (!_selectionSession.TryMarkCaptured(revision)) return;
            long? category = _panel?.CurrentCategoryId;
            long id = _services.GetRequiredService<SnippetService>()
                .Add(text, SnippetSource.Clipboard, categoryId: category);
            if (category is long cat)
                _services.GetRequiredService<SnippetService>().AssignCategory(id, cat);
            _selectionPopup.MarkCaptured();
            MiniToast.Show(LocalizationService.Instance["Toast.Captured"]);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log?.Warn($"Selection capture failed: {ex.GetType().Name}");
            MiniToast.Show(LocalizationService.Instance["Toast.CaptureFailed"]);
            _selectionPopup.HideBubble();
        }
        finally
        {
            if (_selectionActionRevision == revision) _selectionActionRevision = null;
            if (_selectionSession.Revision == revision) _selectionPopup.SetBusy(false);
        }
    }

    /// <summary>Selection translation grabs the selection with the
    /// same Ctrl+C flow as capture, then shows the glass result card in an
    /// immediate "translating..." state so the click always visibly lands,
    /// swapping in the translation (or a calm error) when the service
    /// answers. Nothing is stored and the clipboard is NOT washed - the
    /// user asked to READ this text, not to keep it.</summary>
    private async void TranslateSelectionAsync()
    {
        long revision = _selectionSession.Revision;
        if (!_selectionSession.IsOpen || _selectionActionRevision == revision) return;
        _selectionActionRevision = revision;
        _selectionPopup.SetBusy(true);
        _translationCancellation?.Cancel();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCancellation.Token);
        _translationCancellation = request;
        try
        {
            string? text = await _selectionSession.ReadAsync(request.Token);
            if (text is null || request.IsCancellationRequested || revision != _selectionSession.Revision) return;
            _selectionPopup.HideBubble();
            _translationCard.ShowTranslating(_lastSelectionX, _lastSelectionY);
            _translationCard.SetRecognizedSource(text);
            var cfg = _services.GetRequiredService<SettingsStore>().Current;
            TranslationResult result = await TranslationService.TranslateDetailedAsync(
                text, cfg, request.Token);

            if (request.IsCancellationRequested ||
                !ReferenceEquals(_translationCancellation, request) ||
                !_translationCard.IsVisible) return;
            if (result.IsSuccess)
            {
                _translationCard.ShowResult(result.Text!);
            }
            else if (result.Failure != TranslationFailure.Cancelled)
            {
                // Only diagnostic categories are logged, never text or credentials.
                Log?.Warn($"Translation failed: provider={result.Provider}, " +
                    $"failure={result.Failure}, http={result.HttpStatusCode}");
                _translationCard.ShowError(TranslationService.FailureMessageKey(result));
            }
        }
        catch (OperationCanceledException)
        {
            // A changed selection or dismissed request has no result to show.
        }
        catch (Exception ex)
        {
            Log?.Warn($"Selection translation failed: {ex.GetType().Name}");
            if (!request.IsCancellationRequested && ReferenceEquals(_translationCancellation, request))
            {
                if (_translationCard.IsVisible) _translationCard.ShowError("Translate.Failed");
                else MiniToast.Show(LocalizationService.Instance["Toast.CaptureFailed"]);
            }
        }
        finally
        {
            if (_selectionActionRevision == revision) _selectionActionRevision = null;
            if (_selectionSession.Revision == revision) _selectionPopup.SetBusy(false);
            if (ReferenceEquals(_translationCancellation, request))
                _translationCancellation = null;
        }
    }

    /// <summary>Synthesizes Ctrl+C and reads the selection as plain text.
    /// Null (with the appropriate toast already shown) when nothing was
    /// selected or nothing text-like could be read.</summary>
    private static async Task<string?> GrabSelectionTextAsync(
        nint sourceWindow,
        CancellationToken cancellationToken = default)
    {
        // Do not let the response to a canceled source read be mistaken for the
        // next selection's Ctrl+C. The previous bounded read drains first.
        await SelectionReadGate.WaitAsync(cancellationToken);
        try { return await ReadSelectionClipboardAsync(sourceWindow, cancellationToken); }
        finally { SelectionReadGate.Release(); }
    }

    private static async Task<string?> ReadSelectionClipboardAsync(
        nint sourceWindow, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Keyboard invocation keeps focus in the source window; let its hotkey
        // modifiers go before injecting plain Ctrl+C, avoiding recursive shortcuts.
        for (int i = 0; i < 50 && SelectionModifiersHeld(); i++)
            await Task.Delay(20, cancellationToken);
        if (SelectionModifiersHeld() || sourceWindow == nint.Zero || NativeMethods.GetForegroundWindow() != sourceWindow)
            return null;
        using var clipboardRead = ClipboardWriteGuard.Shared.BeginWrite();
        uint seqBefore = NativeMethods.GetClipboardSequenceNumber();

        NativeMethods.keybd_event(NativeMethods.VK_CONTROL, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_C, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_C, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_CONTROL, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);

        bool copied = false;
        for (int i = 0; i < 50; i++)                  // up to ~2 s, exits early
        {
            // Even a dismissed bubble must suppress the delayed response to our
            // own Ctrl+C. Finish this bounded read, then honor cancellation.
            await Task.Delay(40);
            if (NativeMethods.GetClipboardSequenceNumber() != seqBefore)
            { copied = true; break; }
        }
        if (!copied)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Sequence never moved: nothing was selected (or the page blocks
            // copying) - genuinely "no selection".
            MiniToast.Show(LocalizationService.Instance["Toast.NoSelection"]);
            return null;
        }

        // A copy DID happen. Read with retries: the source may still hold
        // the clipboard open while writing its remaining formats.
        string text = string.Empty;
        for (int i = 0; i < 12 && text.Length == 0; i++)
        {
            try
            {
                var data = Clipboard.GetDataObject();
                if (data?.GetData(DataFormats.UnicodeText) is string t &&
                    !string.IsNullOrWhiteSpace(t))
                {
                    text = t;
                    break;
                }
                if (data is not null)
                {
                    // Formats are readable but none is plain text: some web
                    // apps put ONLY HTML on the clipboard - strip it down.
                    if (data.GetDataPresent(DataFormats.Html) &&
                        data.GetData(DataFormats.Html) is string html)
                    {
                        string stripped = HtmlClipboardToPlainText(html);
                        if (!string.IsNullOrWhiteSpace(stripped))
                        {
                            text = stripped;
                            break;
                        }
                    }
                    if (!data.GetDataPresent(DataFormats.UnicodeText))
                        break;   // image/file-only clipboard: retrying won't help
                }
            }
            catch (System.Runtime.InteropServices.COMException)
            { /* clipboard transiently locked by the source app - retry */ }
            await Task.Delay(60);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // The copy happened but yielded nothing text-like - tell the
            // truth instead of blaming the (real) selection.
            MiniToast.Show(LocalizationService.Instance["Toast.CaptureFailed"]);
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return text;
    }

    private static bool SelectionModifiersHeld() =>
        (NativeMethods.GetAsyncKeyState(0x11) & 0x8000) != 0 ||
        (NativeMethods.GetAsyncKeyState(0x12) & 0x8000) != 0 ||
        (NativeMethods.GetAsyncKeyState(0x10) & 0x8000) != 0;

    /// <summary>Last-resort plain text from a CF_HTML clipboard payload:
    /// takes the StartFragment..EndFragment slice, turns the common block
    /// terminators into newlines, drops every remaining tag, and decodes
    /// entities. Not a general HTML renderer - good enough for the "web app
    /// copied HTML only" fallback path.</summary>
    private static string HtmlClipboardToPlainText(string cfHtml)
    {
        string html = cfHtml;
        int start = cfHtml.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase);
        int end = cfHtml.IndexOf("<!--EndFragment-->", StringComparison.OrdinalIgnoreCase);
        if (start >= 0 && end > start)
            html = cfHtml[(start + "<!--StartFragment-->".Length)..end];

        html = System.Text.RegularExpressions.Regex.Replace(
            html, @"<(br|/p|/div|/li|/tr|/h[1-6])\s*/?>", "\n",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        html = System.Text.RegularExpressions.Regex.Replace(
            html, @"<script[\s\S]*?</script>|<style[\s\S]*?</style>", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        html = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", "");
        html = System.Net.WebUtility.HtmlDecode(html);
        html = System.Text.RegularExpressions.Regex.Replace(html, "[ \\t]+\\n", "\n");
        html = System.Text.RegularExpressions.Regex.Replace(html, "\\n{3,}", "\n\n");
        return html.Trim();
    }

    private void ShowInternalSelection(TextBox source)
    {
        var settings = _services.GetRequiredService<SettingsStore>().Current;
        if (!settings.SelectionPopupEnabled || _capturingScreenshot || string.IsNullOrWhiteSpace(source.SelectedText)) return;
        _translationCancellation?.Cancel();
        _translationCard.HideCard();
        _internalSelectionSource = source;
        Rect caret = source.GetRectFromCharacterIndex(source.SelectionStart + source.SelectionLength);
        Point local = caret.IsEmpty ? Mouse.GetPosition(source) : caret.BottomRight;
        Point point = source.PointToScreen(local);
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) return;
        string selected = source.SelectedText;
        _selectionSession.Begin(token =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult<string?>(selected);
        });
        (_lastSelectionX, _lastSelectionY) = ((int)point.X, (int)point.Y);
        _selectionPopup.ShowAt(_lastSelectionX, _lastSelectionY);
    }

    private void DismissInternalSelection()
    {
        if (_internalSelectionSource is null) return;
        DismissSelection();
    }

    private void DismissSelection()
    {
        // Screenshot overlays are application windows too. Their first preview
        // mouse event must not cancel the request before the drag handler runs.
        if (_capturingScreenshot) return;
        _internalSelectionSource = null;
        _translationCancellation?.Cancel();
        _selectionPopup.HideBubble();
        _translationCard.HideCard();
    }

    internal static void DismissSelectionForMouseDown(Window? window, bool capturingScreenshot,
        Window? selectionPopup, Window? translationCard, Action dismiss)
    {
        if (!capturingScreenshot && window is not null && window != selectionPopup && window != translationCard)
            dismiss();
    }

}
