using System.IO;
using System.IO.Pipes;

namespace KeeStashDock.Desktop;

/// <summary>
/// Single-instance guard (design decision: launching KeeStashDock a second
/// time must summon the existing quick panel, not spawn a duplicate).
///
/// Mechanism: a named Mutex claims instance ownership; the owner runs a named
/// pipe server. A second launch fails to claim the mutex, sends "SHOW" through
/// the pipe and exits immediately.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\KeeStashDock_SingleInstance";
    private const string PipeName  = "KeeStashDock_ActivationPipe";

    private Mutex? _mutex;
    private bool _ownsMutex;     // true ONLY in the primary instance
    private CancellationTokenSource? _cts;

    /// <summary>Raised on a worker thread when another launch asks us to show the panel.</summary>
    public event Action? ActivationRequested;

    /// <summary>Returns true if this process is the primary instance.</summary>
    public bool TryClaim()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        _ownsMutex = createdNew;
        if (!createdNew)
        {
            // We do NOT own the mutex here - Dispose must never release it,
            // or ReleaseMutex throws "called from an unsynchronized block".
            NotifyExistingInstance();
            return false;
        }
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => PipeServerLoop(_cts.Token));
        return true;
    }

    private static void NotifyExistingInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeout: 1500);
            using var w = new StreamWriter(client);
            w.Write("SHOW");
            w.Flush();
        }
        catch { /* Primary instance may be mid-shutdown; nothing to do. */ }
    }

    private async Task PipeServerLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, maxNumberOfServerInstances: 1);
                await server.WaitForConnectionAsync(ct);
                using var r = new StreamReader(server);
                string? msg = await r.ReadToEndAsync(ct);
                if (msg?.Contains("SHOW") == true) ActivationRequested?.Invoke();
            }
            catch (OperationCanceledException) { break; }
            catch { /* transient pipe error - keep serving */ }
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        if (_ownsMutex)
        {
            // Defensive try/catch: if shutdown ever runs on a different thread
            // than the one that acquired the mutex, ReleaseMutex would throw -
            // and the OS releases an abandoned mutex on process exit anyway.
            try { _mutex?.ReleaseMutex(); } catch (ApplicationException) { }
        }
        _mutex?.Dispose();
    }
}
