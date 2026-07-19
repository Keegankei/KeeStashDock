using System.Windows;
using System.Windows.Interop;
using KeeStashDock.Core.Services;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Storage;
using KeeStashDock.Desktop.Interop;

namespace KeeStashDock.Desktop;

/// <summary>
/// Opt-in clipboard history collector. A message-only window subscribes
/// to WM_CLIPBOARDUPDATE (the modern listener API - no fragile viewer
/// chains). Every text copy anywhere on the desktop lands as a snippet.
///
/// Privacy stance:
///   - master switch is OFF by default; nothing is watched until the user
///     opts in;
///   - only plain text is read; images/files are ignored.
///
/// Self-noise: the app itself writes the clipboard constantly (card copy,
/// capture wash, translation copy). No suppression plumbing is needed -
/// SnippetService.Add dedupes identical content, so copying an existing
/// card merely bumps it (which is exactly what a history should do).
/// </summary>
public sealed class ClipboardWatcher : IDisposable
{
    private readonly SettingsStore _settings;
    private readonly SnippetService _snippets;
    private HwndSource? _source;
    private bool _listening;
    private string? _lastCaptured;

    public ClipboardWatcher(SettingsStore settings, SnippetService snippets)
    {
        _settings = settings;
        _snippets = snippets;

        // HWND_MESSAGE parent: a windowless message sink, never visible,
        // never in alt-tab, alive for the whole app session.
        _source = new HwndSource(new HwndSourceParameters("KeeStashDockClipboardSink")
        {
            Width = 0, Height = 0,
            ParentWindow = new IntPtr(-3),   // HWND_MESSAGE
            WindowStyle = 0,
        });
        _source.AddHook(WndProc);

        Refresh();
        _settings.Changed += Refresh;
    }

    /// <summary>(Un)subscribes the listener to match the master switch -
    /// flipping it off in settings stops collection instantly.</summary>
    private void Refresh()
    {
        bool want = _settings.Current.ClipboardWatchEnabled;
        if (want == _listening || _source is null) return;
        if (want) NativeMethods.AddClipboardFormatListener(_source.Handle);
        else NativeMethods.RemoveClipboardFormatListener(_source.Handle);
        _listening = want;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam,
                           ref bool handled)
    {
        if (msg == NativeMethods.WM_CLIPBOARDUPDATE)
        {
            handled = true;
            TryCollect();
        }
        return IntPtr.Zero;
    }

    private void TryCollect()
    {
        if (!_settings.Current.ClipboardWatchEnabled) return;

        string text;
        try
        {
            if (!Clipboard.ContainsText()) return;
            text = Clipboard.GetText();
        }
        catch
        {
            // Source app still holds the clipboard open for other formats;
            // one delayed retry covers virtually all real cases.
            try
            {
                System.Threading.Thread.Sleep(80);
                if (!Clipboard.ContainsText()) return;
                text = Clipboard.GetText();
            }
            catch { return; }
        }

        if (string.IsNullOrWhiteSpace(text)) return;
        if (text.Length > 100_000) return;         // guard: no accidental books
        if (text == _lastCaptured) return;         // WM_CLIPBOARDUPDATE fires in pairs
        _lastCaptured = text;

        try { _snippets.Add(text, SnippetSource.Clipboard); }
        catch { /* collection must never disturb the desktop */ }
    }

    public void Dispose()
    {
        _settings.Changed -= Refresh;
        if (_source is not null)
        {
            if (_listening)
                NativeMethods.RemoveClipboardFormatListener(_source.Handle);
            _source.Dispose();
            _source = null;
        }
    }
}
