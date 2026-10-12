using KeeStashDock.Desktop.Interop;

namespace KeeStashDock.Desktop;

/// <summary>Distinguishes copying an existing entry from new clipboard history.</summary>
internal sealed class ClipboardWriteGuard(Func<uint> getSequence)
{
    internal static ClipboardWriteGuard Shared { get; } = new(NativeMethods.GetClipboardSequenceNumber);
    private int _writeDepth;
    private uint? _ownSequence;

    internal bool IsOwnWrite(uint sequence) => _writeDepth > 0 || _ownSequence == sequence;

    internal void Write(Action write)
    {
        uint before = getSequence();
        _writeDepth++;
        try { write(); }
        finally
        {
            uint after = getSequence();
            if (after != before) _ownSequence = after;
            _writeDepth--;
        }
    }
}
