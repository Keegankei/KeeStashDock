using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace KeeStashDock.Desktop;

public enum OcrFailure
{
    None, InvalidImage, ImageTooLarge, MissingModels, EngineUnavailable,
    Timeout, Cancelled, NoText, RecognitionFailed
}

public sealed record OcrRecognitionResult(string Text, OcrFailure Failure)
{
    public bool IsSuccess => Failure == OcrFailure.None;
}

/// <summary>
/// Offline English/Simplified Chinese OCR. The isolated engine receives image
/// bytes on stdin and returns UTF-8 text on stdout. No temporary image files,
/// network access, clipboard mutation, or logging of image/text are performed.
/// </summary>
public sealed class LocalOcrService
{
    private static readonly SemaphoreSlim RecognitionGate = new(1, 1);
    private readonly string _baseDirectory;
    private readonly TimeSpan _timeout;
    private readonly string? _localDataDirectory;

    public LocalOcrService() : this(AppContext.BaseDirectory, TimeSpan.FromSeconds(45),
        ComponentInstallService.DefaultLocalDataDirectory) { }

    internal LocalOcrService(string baseDirectory, TimeSpan timeout, string? localDataDirectory = null)
    {
        _baseDirectory = baseDirectory;
        _timeout = timeout;
        _localDataDirectory = localDataDirectory;
    }

    public async Task<OcrRecognitionResult> RecognizeAsync(byte[] pngBytes,
        CancellationToken cancellationToken = default)
    {
        OcrFailure validation = ValidateImage(pngBytes);
        if (validation != OcrFailure.None) return new("", validation);
        if (cancellationToken.IsCancellationRequested) return new("", OcrFailure.Cancelled);
        string models = _localDataDirectory is null ? Path.Combine(_baseDirectory, "OcrModels")
            : ComponentInstallService.ResolveOcrModelsDirectory(_baseDirectory, _localDataDirectory);
        if (!ComponentInstallService.HasOcrModels(models))
            return new("", OcrFailure.MissingModels);
        string engine = _localDataDirectory is null ? Path.Combine(_baseDirectory, "x64")
            : ComponentInstallService.ResolveOcrEngineDirectory(_baseDirectory, _localDataDirectory);
        if (!Environment.Is64BitProcess || !ComponentInstallService.HasOcrEngine(engine))
            return new("", OcrFailure.EngineUnavailable);
        string executable = Path.Combine(engine, "tesseract.exe");

        bool entered = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);
        using var process = new Process();
        try
        {
            await RecognitionGate.WaitAsync(deadline.Token).ConfigureAwait(false);
            entered = true;
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
                WorkingDirectory = Path.GetDirectoryName(executable)!
            };
            foreach (string argument in new[] { "stdin", "stdout", "--tessdata-dir", models,
                "-l", "chi_sim+eng", "--oem", "1", "--psm", "3", "-c", "debug_file=NUL" })
                start.ArgumentList.Add(argument);
            // Ignore ambient model paths and limit native parallelism; one image at a time.
            start.Environment.Remove("TESSDATA_PREFIX");
            start.Environment["OMP_THREAD_LIMIT"] = "2";
            process.StartInfo = start;
            if (!process.Start()) return new("", OcrFailure.EngineUnavailable);

            string recognizedText = await ExchangeImageAsync(process, pngBytes, deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            if (process.ExitCode is unchecked((int)0xC0000135) or unchecked((int)0xC000007B))
                return new("", OcrFailure.EngineUnavailable);
            if (process.ExitCode != 0) return new("", OcrFailure.RecognitionFailed);
            string text = recognizedText.Trim();
            return text.Length == 0 ? new("", OcrFailure.NoText) : new(text, OcrFailure.None);
        }
        catch (OperationCanceledException)
        {
            return new("", cancellationToken.IsCancellationRequested ? OcrFailure.Cancelled : OcrFailure.Timeout);
        }
        catch (Win32Exception) { return new("", OcrFailure.EngineUnavailable); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            if (deadline.IsCancellationRequested)
                return new("", cancellationToken.IsCancellationRequested ? OcrFailure.Cancelled : OcrFailure.Timeout);
            return new("", OcrFailure.RecognitionFailed);
        }
        finally
        {
            try
            {
                if (process.Id > 0 && !process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await process.WaitForExitAsync(shutdown.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
            if (entered) RecognitionGate.Release();
        }
    }

    internal static async Task<string> ExchangeImageAsync(Process process, byte[] bytes, CancellationToken token)
    {
        using var exchange = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task<string> output = ReadBoundedAsync(process.StandardOutput, 1_000_000, exchange.Token);
        Task<string> errors = ReadBoundedAsync(process.StandardError, 32_768, exchange.Token);
        Task[] operations = [WriteImageAsync(process, bytes, exchange.Token), output, errors,
            process.WaitForExitAsync(exchange.Token)];
        var pending = operations.ToList();
        try
        {
            // A failed/bounded pipe reader must stop the other waits immediately. Waiting
            // for all tasks first leaves an engine blocked on that pipe until the deadline.
            while (pending.Count > 0)
            {
                Task completed = await Task.WhenAny(pending).ConfigureAwait(false);
                await completed.ConfigureAwait(false);
                pending.Remove(completed);
            }
            return await output.ConfigureAwait(false);
        }
        finally
        {
            exchange.Cancel();
            // Observe every pending I/O task before the caller disposes the process.
            try { await Task.WhenAll(operations).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or InvalidOperationException) { }
        }
    }

    private static async Task WriteImageAsync(Process process, byte[] bytes, CancellationToken token)
    {
        await process.StandardInput.BaseStream.WriteAsync(bytes, token).ConfigureAwait(false);
        await process.StandardInput.BaseStream.FlushAsync(token).ConfigureAwait(false);
        process.StandardInput.Close();
    }

    private static async Task<string> ReadBoundedAsync(StreamReader stream, int limit, CancellationToken token)
    {
        var result = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await stream.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) != 0)
        {
            if (result.Length + count > limit) throw new IOException("OCR output exceeded its limit.");
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }

    internal static OcrFailure ValidateImage(byte[]? bytes)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytes is null || bytes.Length < 33 || !bytes.AsSpan(0, 8).SequenceEqual(signature) ||
            !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8)) return OcrFailure.InvalidImage;
        uint width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4));
        uint height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
        if (width == 0 || height == 0) return OcrFailure.InvalidImage;
        if (bytes.Length > 48 * 1024 * 1024 || width > 16384 || height > 16384 ||
            (ulong)width * height > 40_000_000) return OcrFailure.ImageTooLarge;
        return OcrFailure.None;
    }
}
