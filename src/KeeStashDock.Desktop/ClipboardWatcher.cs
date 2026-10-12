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
/// Copies of existing entries are excluded by clipboard sequence, including
/// events delivered while OLE is writing. Otherwise burn-after-read content
/// could be captured again after its original entry has moved to trash.
/// </summary>
public sealed class ClipboardWatcher : IDisposable
{
    private readonly SettingsStore _settings;
    private readonly SnippetService _snippets;
    private HwndSource? _source;
    private bool _listening;
    private uint? _lastCapturedSequence;
    private readonly Func<uint> _getSequence;
    private readonly Func<string?> _readText;
    private readonly ClipboardWriteGuard _ownWrites;

    public ClipboardWatcher(SettingsStore settings, SnippetService snippets)
        : this(settings, snippets, NativeMethods.GetClipboardSequenceNumber,
            () => Clipboard.ContainsText() ? Clipboard.GetText() : null, ClipboardWriteGuard.Shared)
    {
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

    // Tests supply clipboard events without reading or changing the user's clipboard.
    internal ClipboardWatcher(SettingsStore settings, SnippetService snippets,
        Func<uint> getSequence, Func<string?> readText, ClipboardWriteGuard ownWrites)
    {
        _settings = settings;
        _snippets = snippets;
        _getSequence = getSequence;
        _readText = readText;
        _ownWrites = ownWrites;
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

    internal void TryCollect()
    {
        if (!_settings.Current.ClipboardWatchEnabled) return;
        uint sequence = _getSequence();
        if (_ownWrites.IsOwnWrite(sequence) || _lastCapturedSequence == sequence) return;

        string? text;
        try
        {
            text = _readText();
        }
        catch
        {
            // Source app still holds the clipboard open for other formats;
            // one delayed retry covers virtually all real cases.
            try
            {
                System.Threading.Thread.Sleep(80);
                text = _readText();
            }
            catch { return; }
        }

        if (string.IsNullOrWhiteSpace(text)) return;
        if (text.Length > 100_000) return;         // guard: no accidental books
        // Re-check after reading: OLE may pump another clipboard event while data is retrieved.
        if (_getSequence() != sequence || _ownWrites.IsOwnWrite(sequence)) return;
        try
        {
            _snippets.Add(text, SnippetSource.Clipboard);
            _lastCapturedSequence = sequence;
        }
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
