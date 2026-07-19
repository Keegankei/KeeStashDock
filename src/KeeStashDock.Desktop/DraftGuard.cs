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
    private string _pending = string.Empty;

    public DraftGuard()
    {
        _debounce.Elapsed += (_, _) =>
        {
            try
            {
                if (_pending.Length == 0) File.Delete(PortablePaths.DraftFile);
                else File.WriteAllText(PortablePaths.DraftFile, _pending);
            }
            catch { /* draft persistence is best-effort */ }
        };
    }

    /// <summary>Call from the input box TextChanged handler.</summary>
    public void OnTextChanged(string text)
    {
        _pending = text;
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>Call after the content was successfully stashed or intentionally cleared.</summary>
    public void Clear()
    {
        _pending = string.Empty;
        _debounce.Stop();
        try { File.Delete(PortablePaths.DraftFile); } catch { }
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

    public void Dispose() => _debounce.Dispose();
}
