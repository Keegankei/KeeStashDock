using System.IO;
using KeeStashDock.Infrastructure.Storage;

namespace KeeStashDock.Desktop;

/// <summary>
/// Crash recovery for the quick-input box.
/// Every text change is debounced ~600 ms and written to Data\draft.txt.
/// On a clean save/clear the draft file is deleted; if the app dies with a
/// non-empty draft on disk, the next launch restores it into the input box -
/// the user never loses half-typed content to a crash or power cut.
/// </summary>
public sealed class DraftGuard : IDisposable
{
    private readonly System.Timers.Timer _debounce = new(600) { AutoReset = false };
    private readonly object _gate = new();
    private readonly string _path;
    private string _pending = string.Empty;
    private bool _dirty;

    public DraftGuard() : this(PortablePaths.DraftFile) { }

    internal DraftGuard(string path)
    {
        _path = path;
        _debounce.Elapsed += (_, _) => { try { Flush(); } catch { /* best-effort crash recovery */ } };
    }

    /// <summary>Call from the input box TextChanged handler.</summary>
    public void OnTextChanged(string text)
    {
        lock (_gate)
        {
            _pending = text;
            _dirty = true;
            _debounce.Stop();
            _debounce.Start();
        }
    }

    /// <summary>Persist pending text before an intentional restart or data snapshot.</summary>
    public void Flush()
    {
        lock (_gate)
        {
            _debounce.Stop();
            if (!_dirty) return;
            if (_pending.Length == 0) File.Delete(_path);
            else File.WriteAllText(_path, _pending);
            _dirty = false;
        }
    }

    /// <summary>Call after the content was successfully stashed or intentionally cleared.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _pending = string.Empty;
            _dirty = true;
            try { Flush(); } catch { /* a later flush can retry */ }
        }
    }

    /// <summary>Returns a crash-orphaned draft (or null) - consumed once at startup.</summary>
    public static string? RecoverOrphan()
    {
        try
        {
            return File.Exists(PortablePaths.DraftFile)
                ? File.ReadAllText(PortablePaths.DraftFile) : null;
        }
        catch { return null; }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            try { Flush(); } catch { }
            _debounce.Dispose();
        }
    }
}
