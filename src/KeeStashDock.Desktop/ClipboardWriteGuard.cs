using KeeStashDock.Desktop.Interop;

namespace KeeStashDock.Desktop;

/// <summary>Distinguishes application-owned clipboard writes and selection reads from new history.</summary>
internal sealed class ClipboardWriteGuard(Func<uint> getSequence)
{
    internal static ClipboardWriteGuard Shared { get; } = new(NativeMethods.GetClipboardSequenceNumber);
    private int _writeDepth;
    private uint? _ownSequence;

    internal bool IsOwnWrite(uint sequence) => _writeDepth > 0 || _ownSequence == sequence;

    internal void Write(Action write)
    {
        using var scope = BeginWrite();
        write();
    }

    /// <summary>
    /// Also covers delayed clipboard notifications while an external application
    /// responds to our simulated Ctrl+C. The caller must keep this scope until
    /// that copy has completed, including when its UI request has been canceled.
    /// </summary>
    internal IDisposable BeginWrite()
    {
        uint before = getSequence();
        _writeDepth++;
        return new WriteScope(this, before);
    }

    private void CompleteWrite(uint before)
    {
        uint after = getSequence();
        if (after != before) _ownSequence = after;
        _writeDepth--;
    }

    private sealed class WriteScope(ClipboardWriteGuard owner, uint before) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            owner.CompleteWrite(before);
        }
    }
}
