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

    public LocalOcrService() : this(AppContext.BaseDirectory, TimeSpan.FromSeconds(45)) { }

    internal LocalOcrService(string baseDirectory, TimeSpan timeout)
    {
        _baseDirectory = baseDirectory;
        _timeout = timeout;
    }

    public async Task<OcrRecognitionResult> RecognizeAsync(byte[] pngBytes,
        CancellationToken cancellationToken = default)
    {
        OcrFailure validation = ValidateImage(pngBytes);
        if (validation != OcrFailure.None) return new("", validation);
        if (cancellationToken.IsCancellationRequested) return new("", OcrFailure.Cancelled);
        string models = Path.Combine(_baseDirectory, "OcrModels");
        if (!File.Exists(Path.Combine(models, "eng.traineddata")) ||
            !File.Exists(Path.Combine(models, "chi_sim.traineddata")))
            return new("", OcrFailure.MissingModels);
        string executable = Path.Combine(_baseDirectory, "x64", "tesseract.exe");
        if (!Environment.Is64BitProcess || !File.Exists(executable)) return new("", OcrFailure.EngineUnavailable);

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

            Task<string> output = ReadBoundedAsync(process.StandardOutput, 1_000_000, deadline.Token);
            Task<string> errors = ReadBoundedAsync(process.StandardError, 32_768, deadline.Token);
            Task input = WriteImageAsync(process, pngBytes, deadline.Token);
            await Task.WhenAll(input, output, errors, process.WaitForExitAsync(deadline.Token)).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            if (process.ExitCode is unchecked((int)0xC0000135) or unchecked((int)0xC000007B))
                return new("", OcrFailure.EngineUnavailable);
            if (process.ExitCode != 0) return new("", OcrFailure.RecognitionFailed);
            string text = (await output.ConfigureAwait(false)).Trim();
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
