namespace KeeStashDock.Desktop;

/// <summary>
/// Holds text only for one visible selection bubble. A revision and cancellation
/// token prevent a late source read from replacing a newer selection. This type
/// never touches the clipboard, database, or translation provider itself.
/// </summary>
internal sealed class SelectionSession : IDisposable
{
    private CancellationTokenSource? _cancellation;
    private Func<CancellationToken, Task<string?>>? _reader;
    private Task<string?>? _pendingRead;
    private string? _text;

    internal long Revision { get; private set; }
    internal bool HasCaptured { get; private set; }
    internal bool IsOpen => _reader is not null;

    internal void Begin(Func<CancellationToken, Task<string?>> reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Clear();
        _reader = reader;
        _cancellation = new CancellationTokenSource();
    }

    internal Task<string?> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_reader is null || _cancellation is null) return Task.FromResult<string?>(null);
        if (_text is not null) return Task.FromResult<string?>(_text);
        _pendingRead ??= ReadCoreAsync(_reader, Revision, _cancellation.Token);
        return _pendingRead.WaitAsync(cancellationToken);
    }

    private async Task<string?> ReadCoreAsync(Func<CancellationToken, Task<string?>> reader,
        long revision, CancellationToken cancellationToken)
    {
        string? text = await reader(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (revision != Revision) return null;
        _text = string.IsNullOrWhiteSpace(text) ? null : text;
        return _text;
    }

    internal bool TryMarkCaptured(long revision)
    {
        if (!IsOpen || revision != Revision || HasCaptured) return false;
        HasCaptured = true;
        return true;
    }

    internal void Clear()
    {
        Revision++;
        _reader = null;
        _pendingRead = null;
        _text = null;
        HasCaptured = false;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
    }

    public void Dispose() => Clear();
}
