using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace KeeStashDock.Desktop;

public enum ComponentKind { Ocr, OfflineTranslation }
public enum ComponentStatus { Missing, Ready }
public enum ComponentInstallPhase { Checking, Downloading, Installing, Starting }
public enum ComponentInstallFailure { None, Cancelled, Timeout, MissingFiles, AccessDenied, Failed, RestartRequired }
public sealed record ComponentInstallProgress(ComponentInstallPhase Phase);
public sealed record ComponentInstallResult(ComponentInstallFailure Failure, string Detail = "")
{
    public bool IsSuccess => Failure == ComponentInstallFailure.None;
}

/// <summary>
/// Runs only bundled setup scripts with no console window, verifying model checksums
/// and the Microsoft signature on the system runtime installer.
/// Downloads require an explicit install action; starting an installed engine never downloads.
/// </summary>
public sealed class ComponentInstallService
{
    private static readonly SemaphoreSlim OperationGate = new(1, 1);
    private readonly string _baseDirectory;
    private readonly string _localDataDirectory;
    private readonly TimeSpan _timeout;
    private readonly Func<string, bool> _hasOcrModels;
    private readonly Func<string, bool> _hasOcrEngine;

    internal static string DefaultLocalDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeeStashDock");

    public ComponentInstallService() : this(AppContext.BaseDirectory, DefaultLocalDataDirectory,
        TimeSpan.FromMinutes(30)) { }

    internal ComponentInstallService(string baseDirectory, string localDataDirectory, TimeSpan timeout,
        Func<string, bool>? hasOcrModels = null, Func<string, bool>? hasOcrEngine = null)
    {
        _baseDirectory = Path.GetFullPath(baseDirectory);
        _localDataDirectory = Path.GetFullPath(localDataDirectory);
        _timeout = timeout;
        _hasOcrModels = hasOcrModels ?? HasOcrModels;
        _hasOcrEngine = hasOcrEngine ?? HasOcrEngine;
    }

    public ComponentStatus GetStatus(ComponentKind component)
    {
        bool ready = component switch
        {
            ComponentKind.Ocr => _hasOcrModels(ResolveOcrModelsDirectory(_baseDirectory, _localDataDirectory, _hasOcrModels))
                && _hasOcrEngine(ResolveOcrEngineDirectory(_baseDirectory, _localDataDirectory, _hasOcrEngine)),
            ComponentKind.OfflineTranslation => File.Exists(Path.Combine(OfflineRuntimeDirectory, "installed.json"))
                && File.Exists(Path.Combine(OfflineRuntimeDirectory, "python", "pythonw.exe")),
            _ => false
        };
        return ready ? ComponentStatus.Ready : ComponentStatus.Missing;
    }

    internal static bool HasOcrModels(string directory)
    {
        // Match the pinned setup manifest. Reject partial or corrupted user downloads
        // before allowing them to shadow a healthy model set shipped with the app.
        return VerifyModel("eng.traineddata", 4113088, "7D4322BD2A7749724879683FC3912CB542F19906C83BCC1A52132556427170B2")
            && VerifyModel("chi_sim.traineddata", 2469156, "A5FCB6F0DB1E1D6D8522F39DB4E848F05984669172E584E8D76B6B3141E1F730");

        bool VerifyModel(string name, long size, string expectedHash)
        {
            try
            {
                using var stream = File.OpenRead(Path.Combine(directory, name));
                return stream.Length == size && Convert.ToHexString(SHA256.HashData(stream)) == expectedHash;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        }
    }

    internal static string ResolveOcrModelsDirectory(string baseDirectory, string localDataDirectory,
        Func<string, bool>? hasOcrModels = null)
    {
        string userModels = Path.Combine(localDataDirectory, "OcrModels");
        return (hasOcrModels ?? HasOcrModels)(userModels) ? userModels : Path.Combine(baseDirectory, "OcrModels");
    }

    internal static bool HasOcrEngine(string directory)
    {
        // The standalone native files come from the pinned TesseractOCR distribution.
        return Verify("tesseract.exe", "856D2CDDF1B7CD5F014AA92445FC26789116B1B733606D31201F5B32035E36B8")
            && Verify("tesseract55.dll", "E9903623E901F2D754EB49EEA4DBFF229F599A8817055A3E3F1C292216670CA9")
            && Verify("leptonica-1.85.0.dll", "64BB1D584BCE83DB5E5C3625F1857E4BC12993625699CABD0A9B8B76E393BD01");

        bool Verify(string name, string expectedHash)
        {
            try
            {
                using var stream = File.OpenRead(Path.Combine(directory, name));
                return Convert.ToHexString(SHA256.HashData(stream)) == expectedHash;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        }
    }

    internal static string ResolveOcrEngineDirectory(string baseDirectory, string localDataDirectory,
        Func<string, bool>? hasOcrEngine = null)
    {
        string userEngine = Path.Combine(localDataDirectory, "OcrEngine");
        return (hasOcrEngine ?? HasOcrEngine)(userEngine) ? userEngine : Path.Combine(baseDirectory, "x64");
    }

    private string OfflineRuntimeDirectory
    {
        get
        {
            string portable = Path.Combine(_baseDirectory, "OfflineTranslation", "runtime");
            return File.Exists(Path.Combine(portable, "installed.json"))
                ? portable : Path.Combine(_localDataDirectory, "OfflineTranslation");
        }
    }

    public Task<ComponentInstallResult> InstallAsync(ComponentKind component,
        IProgress<ComponentInstallProgress>? progress = null, CancellationToken cancellationToken = default)
        => RunOperationAsync(async token =>
        {
            progress?.Report(new(ComponentInstallPhase.Checking));
            if (component is not (ComponentKind.Ocr or ComponentKind.OfflineTranslation))
                return new(ComponentInstallFailure.Failed);
            string componentFolder = component == ComponentKind.Ocr ? "OcrModels" : "OfflineTranslation";
            string[] requiredScripts = component == ComponentKind.Ocr ? ["Setup.ps1"] : ["Setup.ps1", "Start.ps1", "Stop.ps1"];
            if (requiredScripts.Any(name => !File.Exists(Path.Combine(_baseDirectory, componentFolder, name))))
                return new(ComponentInstallFailure.MissingFiles);
            // Tesseract's successful probe only proves OCR dependencies are usable.
            // Translation uses a separate native engine and must check the shared runtime.
            string engineDirectory = ResolveOcrEngineDirectory(_baseDirectory, _localDataDirectory, _hasOcrEngine);
            string nativeProbe = component == ComponentKind.Ocr && _hasOcrEngine(engineDirectory)
                ? Path.Combine(engineDirectory, "tesseract.exe") : "";
            var runtime = await RunScriptAsync("NativeRuntime", "Setup.ps1",
                ["-ProbeExecutable", nativeProbe], progress, token).ConfigureAwait(false);
            if (!runtime.IsSuccess) return runtime;
            if (component == ComponentKind.Ocr)
            {
                var result = await RunScriptAsync("OcrModels", "Setup.ps1",
                    ["-Destination", Path.Combine(_localDataDirectory, "OcrModels"),
                     "-EngineDirectory", Path.Combine(_localDataDirectory, "OcrEngine")], progress, token).ConfigureAwait(false);
                if (!result.IsSuccess) return result;
                string installedEngine = Path.Combine(_localDataDirectory, "OcrEngine");
                if (!_hasOcrEngine(installedEngine)) return new(ComponentInstallFailure.Failed);
                var probe = await RunScriptAsync("NativeRuntime", "Setup.ps1",
                    ["-ProbeExecutable", Path.Combine(installedEngine, "tesseract.exe")], progress, token).ConfigureAwait(false);
                if (!probe.IsSuccess) return probe;
            }
            else if (component == ComponentKind.OfflineTranslation)
            {
                string[] arguments = ["-RuntimeDirectory", OfflineRuntimeDirectory];
                var stopped = await RunScriptAsync("OfflineTranslation", "Stop.ps1", arguments, null, token).ConfigureAwait(false);
                if (!stopped.IsSuccess) return stopped;
                var installed = await RunScriptAsync("OfflineTranslation", "Setup.ps1", arguments, progress, token).ConfigureAwait(false);
                if (!installed.IsSuccess) return installed;
                progress?.Report(new(ComponentInstallPhase.Starting));
                var started = await RunScriptAsync("OfflineTranslation", "Start.ps1", arguments, null, token).ConfigureAwait(false);
                if (!started.IsSuccess) return started;
            }
            else return new(ComponentInstallFailure.Failed);
            return GetStatus(component) == ComponentStatus.Ready
                ? new(ComponentInstallFailure.None) : new(ComponentInstallFailure.Failed);
        }, cancellationToken, _timeout);

    public Task<ComponentInstallResult> StartOfflineAsync(CancellationToken cancellationToken = default)
        => RunOperationAsync(token => GetStatus(ComponentKind.OfflineTranslation) == ComponentStatus.Ready
            ? RunScriptAsync("OfflineTranslation", "Start.ps1", ["-RuntimeDirectory", OfflineRuntimeDirectory], null, token)
            : Task.FromResult(new ComponentInstallResult(ComponentInstallFailure.MissingFiles)),
            cancellationToken, TimeSpan.FromMinutes(2));

    public Task<ComponentInstallResult> StopOfflineAsync(CancellationToken cancellationToken = default)
        => RunOperationAsync(token => RunScriptAsync("OfflineTranslation", "Stop.ps1",
            ["-RuntimeDirectory", OfflineRuntimeDirectory], null, token), cancellationToken, TimeSpan.FromSeconds(30));

    private static async Task<ComponentInstallResult> RunOperationAsync(
        Func<CancellationToken, Task<ComponentInstallResult>> operation, CancellationToken cancellationToken,
        TimeSpan timeout)
    {
        bool entered = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            await OperationGate.WaitAsync(deadline.Token).ConfigureAwait(false);
            entered = true;
            return await operation(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new(cancellationToken.IsCancellationRequested
                ? ComponentInstallFailure.Cancelled : ComponentInstallFailure.Timeout);
        }
        catch (UnauthorizedAccessException) { return new(ComponentInstallFailure.AccessDenied); }
        catch (Exception ex) when (ex is IOException or Win32Exception or InvalidOperationException)
        {
            return new(ComponentInstallFailure.Failed, ex.Message);
        }
        finally { if (entered) OperationGate.Release(); }
    }

    private async Task<ComponentInstallResult> RunScriptAsync(string folder, string scriptName,
        string[] arguments, IProgress<ComponentInstallProgress>? progress, CancellationToken token)
    {
        string script = Path.Combine(_baseDirectory, folder, scriptName);
        string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(script) || !File.Exists(powershell)) return new(ComponentInstallFailure.MissingFiles);
        token.ThrowIfCancellationRequested();
        // Quote every value as a PowerShell literal, then pass one encoded command through
        // ArgumentList. A portable folder name cannot inject a command or expand variables.
        static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        string command = "$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; " +
            "[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); " +
            "$env:PSModulePath=(Join-Path $PSHOME 'Modules')+[IO.Path]::PathSeparator+$env:PSModulePath; " +
            "$global:LASTEXITCODE=0; try { & " + Literal(script) + " " +
            string.Join(" ", arguments.Chunk(2).Select(pair => pair[0] + " " + Literal(pair[1]))) + "; $taskScriptSucceeded=$?; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; if (-not $taskScriptSucceeded) { exit 1 } } " +
            "catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }";
        var start = new ProcessStartInfo(powershell)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false),
            WorkingDirectory = Path.GetDirectoryName(script)!
        };
        foreach (string argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
            "-OutputFormat", "Text", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(command)) })
            start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) return new(ComponentInstallFailure.Failed);
            using var outputDrain = CancellationTokenSource.CreateLinkedTokenSource(token);
            Task<string> output = ReadOutputAsync(process.StandardOutput, progress, outputDrain.Token);
            Task<string> errors = ReadOutputAsync(process.StandardError, null, outputDrain.Token);
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            // A successfully detached offline server can inherit redirected pipe handles.
            // Its lifetime must not prevent the already-finished setup worker from returning.
            outputDrain.CancelAfter(TimeSpan.FromSeconds(1));
            await Task.WhenAll(output, errors).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (process.ExitCode == 0) return new(ComponentInstallFailure.None);
            if (folder == "NativeRuntime" && process.ExitCode == 3010)
                return new(ComponentInstallFailure.RestartRequired);
            string details = (await errors.ConfigureAwait(false)).Trim();
            if (details.Length == 0) details = (await output.ConfigureAwait(false)).Trim();
            return new(ComponentInstallFailure.Failed, details);
        }
        finally
        {
            try
            {
                if (process.Id > 0 && !process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch (Exception ex) when (ex is Win32Exception or AggregateException)
                    {
                        // Windows owns an elevated runtime installer once UAC was accepted.
                        // Stop our worker; cancellation does not roll back that system install.
                        if (!process.HasExited) process.Kill();
                    }
                    using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await process.WaitForExitAsync(shutdown.Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or OperationCanceledException) { }
        }
    }

    private static async Task<string> ReadOutputAsync(StreamReader reader,
        IProgress<ComponentInstallProgress>? progress, CancellationToken token)
    {
        var tail = new StringBuilder();
        string? line;
        try
        {
            while ((line = await reader.ReadLineAsync(token).ConfigureAwait(false)) is not null)
            {
                if (line.Contains("Downloading", StringComparison.OrdinalIgnoreCase))
                    progress?.Report(new(ComponentInstallPhase.Downloading));
                else if (line.Contains("install", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("extract", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("verif", StringComparison.OrdinalIgnoreCase))
                    progress?.Report(new(ComponentInstallPhase.Installing));
                tail.AppendLine(line.Length > 2000 ? line[..2000] : line);
                if (tail.Length > 8000) tail.Remove(0, tail.Length - 8000);
            }
        }
        catch (OperationCanceledException) { }
        return tail.ToString();
    }
}
