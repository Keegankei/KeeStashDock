using System.Collections.Concurrent;
using System.Text;

namespace KeeStashDock.Infrastructure.Logging;

/// <summary>
/// Minimal thread-safe rolling file logger.
///
/// Why hand-rolled instead of Serilog/NLog?
/// A stash tool must stay lightweight; this compact logger covers the required
/// behavior: async writes off the UI thread, 10 MB x 5 file rotation,
/// and a synchronous Flush for the crash handler. Levels: DBG/INF/WRN/ERR.
/// </summary>
public sealed class FileLogger : IDisposable
{
    private const long MaxFileBytes = 10 * 1024 * 1024;   // 10 MB per file
    private const int  MaxFiles     = 5;                  // keep 5 rotated files

    private readonly string _dir;
    private readonly BlockingCollection<string> _queue = new(boundedCapacity: 10_000);
    private readonly Thread _writer;

    public FileLogger(string logDirectory)
    {
        _dir = logDirectory;
        _writer = new Thread(WriterLoop) { IsBackground = true, Name = "KeeStashDock.Logger" };
        _writer.Start();
    }

    public void Debug(string msg) => Enqueue("DBG", msg);
    public void Info(string msg)  => Enqueue("INF", msg);
    public void Warn(string msg)  => Enqueue("WRN", msg);
    public void Error(string msg, Exception? ex = null)
        => Enqueue("ERR", ex is null ? msg : $"{msg}{Environment.NewLine}{ex}");

    private void Enqueue(string level, string msg)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {msg}";
        // TryAdd: if the queue is full (pathological), drop rather than block the UI.
        try { _queue.TryAdd(line); }
        catch (InvalidOperationException) when (_queue.IsAddingCompleted)
        {
            // Background operations may finish after shutdown has closed the queue.
            // Late logging must not turn an otherwise safe cancellation into a crash.
        }
    }

    private void WriterLoop()
    {
        foreach (string line in _queue.GetConsumingEnumerable())
        {
            try
            {
                string file = CurrentFile();
                File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
            }
            catch { /* Logging must never take the app down. */ }
        }
    }

    private string CurrentFile()
    {
        string file = Path.Combine(_dir, "keestashdock.log");
        var fi = new FileInfo(file);
        if (fi.Exists && fi.Length >= MaxFileBytes) Rotate(file);
        return file;
    }

    /// <summary>keestashdock.log -&gt; .1 -&gt; .2 ... oldest (.5) is deleted.</summary>
    private static void Rotate(string baseFile)
    {
        string oldest = $"{baseFile}.{MaxFiles - 1}";
        if (File.Exists(oldest)) File.Delete(oldest);
        for (int i = MaxFiles - 2; i >= 1; i--)
        {
            string src = $"{baseFile}.{i}";
            if (File.Exists(src)) File.Move(src, $"{baseFile}.{i + 1}");
        }
        File.Move(baseFile, $"{baseFile}.1");
    }

    /// <summary>Synchronously drain pending lines - called from the crash handler.</summary>
    public void Flush()
    {
        // Give the background writer a brief window to drain the queue.
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (_queue.Count > 0 && DateTime.UtcNow < deadline) Thread.Sleep(20);
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _writer.Join(TimeSpan.FromSeconds(2));
    }
}
