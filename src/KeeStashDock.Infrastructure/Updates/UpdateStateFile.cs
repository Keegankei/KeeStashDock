using System.Diagnostics;
using System.Text.Json;

namespace KeeStashDock.Infrastructure.Updates;

/// <summary>Durable update metadata shared by the application and its helper processes.</summary>
public static class UpdateStateFile
{
    public static T ReadJson<T>(string path, int maximumBytes, JsonSerializerOptions? options = null)
    {
        string fullPath = Path.GetFullPath(path);
        byte[] bytes;
        // Windows can reject an overwrite rename even when an open reader shares
        // deletion. Close every cooperating reader before replacing this pathname.
        using (AcquireIoLease(fullPath))
        {
            UpdatePathPolicy.EnsureNoReparsePoints(fullPath);
            using var input = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length <= 0 || input.Length > maximumBytes)
                throw new InvalidDataException("The update state file exceeds its size limit or is empty.");
            bytes = new byte[checked((int)input.Length)];
            input.ReadExactly(bytes);
        }
        return JsonSerializer.Deserialize<T>(bytes, options)
            ?? throw new InvalidDataException("The update state file is empty.");
    }

    public static void WriteJson<T>(string path, T value, int maximumBytes, JsonSerializerOptions? options = null)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, options);
        if (bytes.Length > maximumBytes) throw new InvalidDataException("The update state file exceeds its size limit.");
        string fullPath = Path.GetFullPath(path);
        UpdatePathPolicy.EnsureNoReparsePoints(fullPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        using var lease = AcquireIoLease(fullPath);
        string temporary = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        bool created = false;
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None, 4096, FileOptions.WriteThrough))
            {
                created = true;
                output.Write(bytes);
                output.Flush(flushToDisk: true);
            }
            UpdatePathPolicy.EnsureNoReparsePoints(fullPath);
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (created && File.Exists(temporary))
            {
                UpdatePathPolicy.EnsureNoReparsePoints(temporary);
                File.Delete(temporary);
            }
        }
    }

    // A persistent sidecar inherits the metadata directory's permissions, so
    // elevated file operations and the normal application use the same lease.
    // Never delete it on close: another process may already be waiting on it.
    private static FileStream AcquireIoLease(string fullPath)
    {
        string lockPath = fullPath + ".io.lock";
        var waiting = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                UpdatePathPolicy.EnsureNoReparsePoints(lockPath);
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33)
            {
                if (waiting.Elapsed >= TimeSpan.FromSeconds(5))
                    throw new IOException("Another process is busy reading or saving update metadata.", ex);
                Thread.Sleep(10);
            }
        }
    }
}
