using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ComponentInstallServiceTests
{
    private static readonly string TempParent = Path.Combine(Path.GetTempPath(), "KeeStashDock.Tests", "ComponentInstall");
    private string _root = null!;
    private string _baseDirectory = null!;
    private string _localDataDirectory = null!;
    private string OcrDirectory => Path.Combine(_localDataDirectory, "OcrModels");
    private string OfflineDirectory => Path.Combine(_localDataDirectory, "OfflineTranslation");

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(TempParent, Guid.NewGuid().ToString("N"));
        // These characters must arrive literally through the encoded PowerShell command.
        string special = "space ' \u6d4b\u8bd5 $value `literal";
        _baseDirectory = Path.Combine(_root, "portable " + special);
        _localDataDirectory = Path.Combine(_root, "local " + special);
        Directory.CreateDirectory(_baseDirectory);
        Directory.CreateDirectory(_localDataDirectory);
        WriteFile(Path.Combine(_baseDirectory, "x64", "tesseract.exe"), "fixture, never executed");
        WriteScript("NativeRuntime", "Setup.ps1", """
            param([AllowEmptyString()][Parameter(Mandatory=$true)][string]$ProbeExecutable)
            [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'bound-probe.txt'), $ProbeExecutable)
            """);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        string resolved = Path.GetFullPath(_root);
        Assert.AreEqual(Path.GetFullPath(TempParent), Path.GetDirectoryName(resolved));
        Assert.IsTrue(Guid.TryParseExact(Path.GetFileName(resolved), "N", out _));
        // Windows may briefly retain a terminated console process's working-directory handle.
        for (int attempt = 0; Directory.Exists(resolved); attempt++)
        {
            try { Directory.Delete(resolved, recursive: true); }
            catch (IOException) when (attempt < 20) { await Task.Delay(100); }
        }
    }

    [TestMethod]
    public async Task OcrInstallBindsSpecialPathsAndReportsProgressWithoutDownloading()
    {
        WriteScript("OcrModels", "Setup.ps1", """
            param([Parameter(Mandatory=$true)][string]$Destination)
            [IO.Directory]::CreateDirectory($Destination) | Out-Null
            [IO.File]::WriteAllText((Join-Path $Destination 'argument.txt'), $Destination)
            Write-Output 'Downloading fixture model'
            Write-Output 'Verifying fixture model'
            [IO.File]::WriteAllText((Join-Path $Destination 'eng.traineddata'), 'fixture')
            [IO.File]::WriteAllText((Join-Path $Destination 'chi_sim.traineddata'), 'fixture')
            """);
        var service = CreateService();
        var progress = new ImmediateProgress();

        ComponentInstallResult result = await service.InstallAsync(ComponentKind.Ocr, progress);

        Assert.IsTrue(result.IsSuccess, result.Detail);
        Assert.AreEqual(OcrDirectory, File.ReadAllText(Path.Combine(OcrDirectory, "argument.txt")));
        Assert.AreEqual(Path.Combine(_baseDirectory, "x64", "tesseract.exe"),
            File.ReadAllText(Path.Combine(_baseDirectory, "NativeRuntime", "bound-probe.txt")));
        Assert.AreEqual(ComponentStatus.Ready, service.GetStatus(ComponentKind.Ocr));
        CollectionAssert.AreEqual(new[] { ComponentInstallPhase.Checking, ComponentInstallPhase.Downloading,
            ComponentInstallPhase.Installing }, progress.Phases.ToArray());
    }

    [TestMethod]
    [DataRow(ComponentKind.Ocr)]
    [DataRow(ComponentKind.OfflineTranslation)]
    public async Task FreshInstallFromReadOnlyProgramDirectoryUsesWritableUserDirectory(ComponentKind component)
    {
        WriteOfflineScripts();
        WriteScript("NativeRuntime", "Setup.ps1", "param([AllowEmptyString()][string]$ProbeExecutable)");
        WriteSuccessfulOcrSetup();
        var directory = new DirectoryInfo(_baseDirectory);
        DirectorySecurity originalAccess = directory.GetAccessControl();
        DirectorySecurity readOnlyAccess = directory.GetAccessControl();
        using var identity = WindowsIdentity.GetCurrent();
        readOnlyAccess.AddAccessRule(new FileSystemAccessRule(identity.User!,
            FileSystemRights.Write, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Deny));
        directory.SetAccessControl(readOnlyAccess);
        try
        {
            Assert.ThrowsExactly<UnauthorizedAccessException>(() =>
                File.WriteAllText(Path.Combine(_baseDirectory, "must-not-write.txt"), "fixture"));

            ComponentInstallResult result = await CreateService().InstallAsync(component);

            Assert.IsTrue(result.IsSuccess, result.Detail);
            Assert.AreEqual(ComponentStatus.Ready, CreateService().GetStatus(component));
            Assert.IsTrue(Directory.Exists(component == ComponentKind.Ocr ? OcrDirectory : OfflineDirectory));
            Assert.IsFalse(Directory.Exists(Path.Combine(_baseDirectory, "OfflineTranslation", "runtime")));
            Assert.IsFalse(File.Exists(Path.Combine(_baseDirectory, "OcrModels", "eng.traineddata")));
        }
        finally { directory.SetAccessControl(originalAccess); }
    }

    [TestMethod]
    [DataRow(ComponentKind.Ocr)]
    [DataRow(ComponentKind.OfflineTranslation)]
    public async Task NativeRuntimeFailureStopsBeforeModelOrTranslationSetup(ComponentKind component)
    {
        WriteOfflineScripts();
        WriteScript("OcrModels", "Setup.ps1", """
            param([Parameter(Mandatory=$true)][string]$Destination)
            [IO.Directory]::CreateDirectory($Destination) | Out-Null
            throw 'The OCR setup must not run'
            """);
        WriteScript("NativeRuntime", "Setup.ps1", """
            param([AllowEmptyString()][Parameter(Mandatory=$true)][string]$ProbeExecutable)
            [Console]::Error.WriteLine('Synthetic native runtime failure')
            exit 12
            """);

        ComponentInstallResult result = await CreateService().InstallAsync(component);

        Assert.AreEqual(ComponentInstallFailure.Failed, result.Failure);
        StringAssert.Contains(result.Detail, "Synthetic native runtime failure");
        Assert.IsFalse(Directory.Exists(OcrDirectory));
        Assert.IsFalse(Directory.Exists(OfflineDirectory));
    }

    [TestMethod]
    [DataRow(ComponentKind.Ocr)]
    [DataRow(ComponentKind.OfflineTranslation)]
    public async Task NativeRuntimeRestartExitStopsSetupAndReportsRestartRequired(ComponentKind component)
    {
        WriteOfflineScripts();
        WriteScript("OcrModels", "Setup.ps1", """
            param([Parameter(Mandatory=$true)][string]$Destination)
            [IO.Directory]::CreateDirectory($Destination) | Out-Null
            throw 'The OCR setup must not run before Windows restarts'
            """);
        WriteScript("NativeRuntime", "Setup.ps1", """
            param([AllowEmptyString()][Parameter(Mandatory=$true)][string]$ProbeExecutable)
            exit 3010
            """);

        ComponentInstallResult result = await CreateService().InstallAsync(component);

        Assert.AreEqual(ComponentInstallFailure.RestartRequired, result.Failure);
        Assert.IsFalse(Directory.Exists(OcrDirectory));
        Assert.IsFalse(Directory.Exists(OfflineDirectory));
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("empty")]
    [DataRow("fake")]
    [DataRow("corrupted")]
    public void ProductionModelValidationRejectsMissingEmptyFakeAndCorrectSizeCorruptedModels(string damage)
    {
        if (damage != "missing")
        {
            Directory.CreateDirectory(OcrDirectory);
            byte[] english = damage == "empty" ? [] : damage == "fake" ? [1, 2, 3] : new byte[4113088];
            byte[] chinese = damage == "empty" ? [] : damage == "fake" ? [4, 5, 6] : new byte[2469156];
            File.WriteAllBytes(Path.Combine(OcrDirectory, "eng.traineddata"), english);
            File.WriteAllBytes(Path.Combine(OcrDirectory, "chi_sim.traineddata"), chinese);
        }

        Assert.IsFalse(ComponentInstallService.HasOcrModels(OcrDirectory));
        Assert.AreEqual(Path.Combine(_baseDirectory, "OcrModels"),
            ComponentInstallService.ResolveOcrModelsDirectory(_baseDirectory, _localDataDirectory));
        Assert.AreEqual(ComponentStatus.Missing,
            new ComponentInstallService(_baseDirectory, _localDataDirectory, TimeSpan.FromSeconds(30))
                .GetStatus(ComponentKind.Ocr));
    }

    [TestMethod]
    public async Task NonzeroScriptExitAndTerminatingErrorReturnFailure()
    {
        WriteScript("OcrModels", "Setup.ps1", """
            param([string]$Destination)
            [Console]::Error.WriteLine('Synthetic setup failure')
            exit 7
            """);
        ComponentInstallResult exitFailure = await CreateService().InstallAsync(ComponentKind.Ocr);
        Assert.AreEqual(ComponentInstallFailure.Failed, exitFailure.Failure);
        StringAssert.Contains(exitFailure.Detail, "Synthetic setup failure");

        WriteScript("OcrModels", "Setup.ps1", "param([string]$Destination)\nthrow 'Synthetic terminating error'");
        ComponentInstallResult exceptionFailure = await CreateService().InstallAsync(ComponentKind.Ocr);
        Assert.AreEqual(ComponentInstallFailure.Failed, exceptionFailure.Failure);
        StringAssert.Contains(exceptionFailure.Detail, "Synthetic terminating error");
    }

    [TestMethod]
    public async Task MissingScriptsOrNativeOcrFilesReturnMissingFiles()
    {
        var service = CreateService();
        Assert.AreEqual(ComponentInstallFailure.MissingFiles,
            (await service.InstallAsync(ComponentKind.Ocr)).Failure);
        WriteScript("OcrModels", "Setup.ps1", "throw 'Must not run'");
        File.Delete(Path.Combine(_baseDirectory, "x64", "tesseract.exe"));
        Assert.AreEqual(ComponentInstallFailure.MissingFiles,
            (await service.InstallAsync(ComponentKind.Ocr)).Failure);
        Assert.AreEqual(ComponentStatus.Missing, service.GetStatus(ComponentKind.Ocr));
    }

    [TestMethod]
    public void OcrModelsPreferCompleteUserInstallationAndFallBackToPortableModels()
    {
        string portable = Path.Combine(_baseDirectory, "OcrModels");
        var service = CreateService();
        Assert.AreEqual(ComponentStatus.Missing, service.GetStatus(ComponentKind.Ocr));
        WriteFile(Path.Combine(portable, "eng.traineddata"), "portable");
        WriteFile(Path.Combine(portable, "chi_sim.traineddata"), "portable");
        Assert.AreEqual(portable, ComponentInstallService.ResolveOcrModelsDirectory(_baseDirectory, _localDataDirectory, HasFixtureOcrModels));
        WriteFile(Path.Combine(OcrDirectory, "eng.traineddata"), "partial user");
        Assert.AreEqual(portable, ComponentInstallService.ResolveOcrModelsDirectory(_baseDirectory, _localDataDirectory, HasFixtureOcrModels));
        WriteFile(Path.Combine(OcrDirectory, "chi_sim.traineddata"), "complete user");
        Assert.AreEqual(OcrDirectory, ComponentInstallService.ResolveOcrModelsDirectory(_baseDirectory, _localDataDirectory, HasFixtureOcrModels));
        Assert.AreEqual(ComponentStatus.Ready, service.GetStatus(ComponentKind.Ocr));
    }

    [TestMethod]
    public async Task OfflineInstallRunsStopSetupStartInOrderAndReusesInstalledRuntime()
    {
        WriteOfflineScripts();
        var service = CreateService();
        var progress = new ImmediateProgress();

        ComponentInstallResult result = await service.InstallAsync(ComponentKind.OfflineTranslation, progress);

        Assert.IsTrue(result.IsSuccess, result.Detail);
        CollectionAssert.AreEqual(new[] { "stop", "setup", "start" }, File.ReadAllLines(Path.Combine(OfflineDirectory, "order.txt")));
        Assert.AreEqual(OfflineDirectory, File.ReadAllText(Path.Combine(OfflineDirectory, "argument.txt")));
        Assert.AreEqual("", File.ReadAllText(Path.Combine(_baseDirectory, "NativeRuntime", "bound-probe.txt")),
            "An OCR probe cannot establish that the different translation engine's native prerequisites work.");
        Assert.AreEqual(ComponentStatus.Ready, service.GetStatus(ComponentKind.OfflineTranslation));
        Assert.AreEqual(ComponentInstallPhase.Starting, progress.Phases.Last());
        Assert.IsTrue((await service.StartOfflineAsync()).IsSuccess);
        Assert.IsTrue((await service.StopOfflineAsync()).IsSuccess);
        CollectionAssert.AreEqual(new[] { "stop", "setup", "start", "start", "stop" },
            File.ReadAllLines(Path.Combine(OfflineDirectory, "order.txt")));
    }

    [TestMethod]
    public async Task OfflineSetupFailurePreventsStartingTheEngine()
    {
        WriteOfflineScripts();
        WriteScript("OfflineTranslation", "Setup.ps1", """
            param([Parameter(Mandatory=$true)][string]$RuntimeDirectory)
            [IO.File]::AppendAllText((Join-Path $RuntimeDirectory 'order.txt'), "setup`n")
            exit 9
            """);

        var result = await CreateService().InstallAsync(ComponentKind.OfflineTranslation);

        Assert.AreEqual(ComponentInstallFailure.Failed, result.Failure);
        CollectionAssert.AreEqual(new[] { "stop", "setup" }, File.ReadAllLines(Path.Combine(OfflineDirectory, "order.txt")));
    }

    [TestMethod]
    public async Task OfflineStartRequiresInstalledFilesAndUsesPortableRuntimeWhenPresent()
    {
        WriteOfflineScripts();
        var service = CreateService();
        Assert.AreEqual(ComponentInstallFailure.MissingFiles, (await service.StartOfflineAsync()).Failure);
        Assert.IsFalse(Directory.Exists(OfflineDirectory));
        string portable = Path.Combine(_baseDirectory, "OfflineTranslation", "runtime");
        WriteFile(Path.Combine(portable, "installed.json"), "{}");
        WriteFile(Path.Combine(portable, "python", "pythonw.exe"), "fixture, never executed");

        Assert.IsTrue((await service.StartOfflineAsync()).IsSuccess);

        CollectionAssert.AreEqual(new[] { "start" }, File.ReadAllLines(Path.Combine(portable, "order.txt")));
        Assert.IsFalse(Directory.Exists(OfflineDirectory));
    }

    [TestMethod]
    public async Task DetachedEngineInheritingOutputPipesDoesNotBlockCompletedInstallation()
    {
        WriteOfflineScripts();
        string childReady = Path.Combine(OfflineDirectory, "detached.ready");
        string childCode = "Write-Output 'Downloading detached fixture marker'; " +
            "[Console]::Error.WriteLine('Detached fixture error stream'); " +
            "[IO.File]::WriteAllText(" + Literal(childReady) + ", 'ready'); Start-Sleep -Seconds 30";
        string encodedChild = Convert.ToBase64String(Encoding.Unicode.GetBytes(childCode));
        WriteScript("OfflineTranslation", "Setup.ps1", $$"""
            param([Parameter(Mandatory=$true)][string]$RuntimeDirectory)
            [IO.Directory]::CreateDirectory((Join-Path $RuntimeDirectory 'python')) | Out-Null
            [IO.File]::WriteAllText((Join-Path $RuntimeDirectory 'python\pythonw.exe'), 'fixture, never executed')
            [IO.File]::WriteAllText((Join-Path $RuntimeDirectory 'installed.json'), '{}')
            Add-Type -TypeDefinition @'
            using System;
            using System.Runtime.InteropServices;
            using System.Text;
            public static class DetachedFixture {
                [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
                public struct StartupInfo {
                    public int Size; public string Reserved, Desktop, Title;
                    public int X, Y, Width, Height, CountX, CountY, Fill, Flags;
                    public short Show, ReservedSize; public IntPtr ReservedData, Input, Output, Error;
                }
                [StructLayout(LayoutKind.Sequential)]
                public struct ProcessInfo { public IntPtr Process, Thread; public int Id, ThreadId; }
                [DllImport("kernel32.dll")] public static extern IntPtr GetStdHandle(int handle);
                [DllImport("kernel32.dll", SetLastError = true)]
                public static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
                [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
                public static extern bool CreateProcess(string application, StringBuilder command,
                    IntPtr processSecurity, IntPtr threadSecurity, bool inherit, uint flags,
                    IntPtr environment, string directory, ref StartupInfo startup, out ProcessInfo process);
                [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
            }
            '@
            $startup = New-Object DetachedFixture+StartupInfo
            $startup.Size = [Runtime.InteropServices.Marshal]::SizeOf($startup)
            $startup.Flags = 256
            $startup.Output = [DetachedFixture]::GetStdHandle(-11)
            $startup.Error = [DetachedFixture]::GetStdHandle(-12)
            [DetachedFixture]::SetHandleInformation($startup.Output, 1, 1) | Out-Null
            [DetachedFixture]::SetHandleInformation($startup.Error, 1, 1) | Out-Null
            $processInfo = New-Object DetachedFixture+ProcessInfo
            $executable = Join-Path $PSHOME 'powershell.exe'
            $command = New-Object Text.StringBuilder
            [void]$command.Append('"' + $executable + '" -NoLogo -NoProfile -NonInteractive -EncodedCommand {{encodedChild}}')
            if (-not [DetachedFixture]::CreateProcess($executable, $command, [IntPtr]::Zero, [IntPtr]::Zero,
                $true, 134217728, [IntPtr]::Zero, $RuntimeDirectory, [ref]$startup, [ref]$processInfo)) {
                throw 'Could not start the inherited-pipe fixture.'
            }
            [IO.File]::WriteAllText((Join-Path $RuntimeDirectory 'detached.pid'), [string]$processInfo.Id)
            [DetachedFixture]::CloseHandle($processInfo.Process) | Out-Null
            [DetachedFixture]::CloseHandle($processInfo.Thread) | Out-Null
            $deadline = [DateTime]::UtcNow.AddSeconds(5)
            while (-not [IO.File]::Exists((Join-Path $RuntimeDirectory 'detached.ready'))) {
                if ([DateTime]::UtcNow -gt $deadline) { throw 'The detached fixture did not become ready.' }
                Start-Sleep -Milliseconds 25
            }
            """);
        using var cancellation = new CancellationTokenSource();
        var progress = new ImmediateProgress();
        var timer = Stopwatch.StartNew();
        Task<ComponentInstallResult> operation = CreateService().InstallAsync(
            ComponentKind.OfflineTranslation, progress, cancellation.Token);
        Process? child = null;
        try
        {
            int childId = await ReadProcessIdAsync(Path.Combine(OfflineDirectory, "detached.pid"));
            child = Process.GetProcessById(childId);
            ComponentInstallResult result = await operation.WaitAsync(TimeSpan.FromSeconds(8));

            Assert.IsTrue(result.IsSuccess, result.Detail);
            Assert.IsTrue(timer.Elapsed < TimeSpan.FromSeconds(12), "Finished setup must not wait for the background engine's lifetime.");
            Assert.IsFalse(child.HasExited, "The successfully started background engine must remain running.");
            CollectionAssert.Contains(progress.Phases, ComponentInstallPhase.Downloading,
                "The detached fixture must really inherit the setup worker's output pipe.");
        }
        finally
        {
            // This handle came only from the PID written by our isolated fixture.
            if (child is not null)
            {
                try
                {
                    if (!child.HasExited) child.Kill(entireProcessTree: true);
                    await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
                finally { child.Dispose(); }
            }
            cancellation.Cancel();
            await operation.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancellationAndTimeoutKillSetupProcessTreeBeforeReturning(bool timeout)
    {
        string childStarted = Path.Combine(OcrDirectory, "child.pid");
        string lateWrite = Path.Combine(OcrDirectory, "late.txt");
        string childCode = "[IO.File]::WriteAllText(" + Literal(childStarted) + ", [string]$PID); " +
            "Start-Sleep -Seconds 20; [IO.File]::WriteAllText(" + Literal(lateWrite) + ", 'must not happen')";
        string encodedChild = Convert.ToBase64String(Encoding.Unicode.GetBytes(childCode));
        WriteScript("OcrModels", "Setup.ps1", $$"""
            param([Parameter(Mandatory=$true)][string]$Destination)
            [IO.Directory]::CreateDirectory($Destination) | Out-Null
            [IO.File]::WriteAllText((Join-Path $Destination 'parent.pid'), [string]$PID)
            Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -WindowStyle Hidden -ArgumentList '-NoLogo -NoProfile -NonInteractive -EncodedCommand {{encodedChild}}'
            Start-Sleep -Seconds 30
            [IO.File]::WriteAllText((Join-Path $Destination 'parent-late.txt'), 'must not happen')
            """);
        using var cancellation = new CancellationTokenSource();
        var service = CreateService(timeout ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(30));
        Task<ComponentInstallResult> operation = service.InstallAsync(ComponentKind.Ocr, cancellationToken: cancellation.Token);
        try
        {
            int childId = await ReadProcessIdAsync(childStarted);
            int parentId = await ReadProcessIdAsync(Path.Combine(OcrDirectory, "parent.pid"));
            if (!timeout) cancellation.Cancel();
            ComponentInstallResult result = await operation.WaitAsync(TimeSpan.FromSeconds(20));

            Assert.AreEqual(timeout ? ComponentInstallFailure.Timeout : ComponentInstallFailure.Cancelled, result.Failure);
            AssertProcessExited(parentId);
            AssertProcessExited(childId);
            Assert.IsFalse(File.Exists(lateWrite));
            Assert.IsFalse(File.Exists(Path.Combine(OcrDirectory, "parent-late.txt")));
            WriteSuccessfulOcrSetup();
            ComponentInstallResult retry = await CreateService().InstallAsync(ComponentKind.Ocr);
            Assert.IsTrue(retry.IsSuccess, "A completed cancellation or timeout must permit a fresh install: " + retry.Detail);
        }
        finally
        {
            cancellation.Cancel();
            await operation.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [TestMethod]
    public async Task AlreadyCancelledInstallDoesNotStartScripts()
    {
        WriteScript("OcrModels", "Setup.ps1", "throw 'Must not run'");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.AreEqual(ComponentInstallFailure.Cancelled,
            (await CreateService().InstallAsync(ComponentKind.Ocr, cancellationToken: cancellation.Token)).Failure);
        Assert.IsFalse(Directory.Exists(OcrDirectory));
    }

    [TestMethod]
    [DataRow("http://127.0.0.1:5000", true)]
    [DataRow("http://127.0.0.1:5000/", true)]
    [DataRow("https://127.0.0.1:5000", false)]
    [DataRow("http://127.0.0.1:5001", false)]
    [DataRow("http://localhost:5000", false)]
    [DataRow("http://127.0.0.1:5000/custom", false)]
    [DataRow("http://127.0.0.1:5000/?option=1", false)]
    [DataRow("http://127.0.0.1:5000/#fragment", false)]
    [DataRow("http://user:password@127.0.0.1:5000", false)]
    [DataRow("http://example.com:5000", false)]
    public void OnlyBundledEndpointIsAutomaticallyManaged(string endpoint, bool expected)
        => Assert.AreEqual(expected, TranslationService.IsManagedOfflineEndpoint(endpoint));

    private ComponentInstallService CreateService(TimeSpan? timeout = null)
        => new(_baseDirectory, _localDataDirectory, timeout ?? TimeSpan.FromSeconds(30), HasFixtureOcrModels);

    private static bool HasFixtureOcrModels(string directory)
        => new[] { "eng.traineddata", "chi_sim.traineddata" }.All(name =>
        {
            string file = Path.Combine(directory, name);
            return File.Exists(file) && new FileInfo(file).Length > 0;
        });

    private void WriteOfflineScripts()
    {
        WriteScript("OfflineTranslation", "Stop.ps1", """
            param([Parameter(Mandatory=$true)][string]$RuntimeDirectory)
            [IO.Directory]::CreateDirectory($RuntimeDirectory) | Out-Null
            [IO.File]::AppendAllText((Join-Path $RuntimeDirectory 'order.txt'), "stop`n")
            """);
        WriteScript("OfflineTranslation", "Setup.ps1", """
            param([Parameter(Mandatory=$true)][string]$RuntimeDirectory)
            [IO.File]::AppendAllText((Join-Path $RuntimeDirectory 'order.txt'), "setup`n")
            [IO.File]::WriteAllText((Join-Path $RuntimeDirectory 'argument.txt'), $RuntimeDirectory)
            [IO.Directory]::CreateDirectory((Join-Path $RuntimeDirectory 'python')) | Out-Null
            [IO.File]::WriteAllText((Join-Path $RuntimeDirectory 'python\pythonw.exe'), 'fixture, never executed')
            [IO.File]::WriteAllText((Join-Path $RuntimeDirectory 'installed.json'), '{}')
            """);
        WriteScript("OfflineTranslation", "Start.ps1", """
            param([Parameter(Mandatory=$true)][string]$RuntimeDirectory)
            [IO.File]::AppendAllText((Join-Path $RuntimeDirectory 'order.txt'), "start`n")
            """);
    }

    private void WriteSuccessfulOcrSetup()
        => WriteScript("OcrModels", "Setup.ps1", """
            param([Parameter(Mandatory=$true)][string]$Destination)
            [IO.Directory]::CreateDirectory($Destination) | Out-Null
            [IO.File]::WriteAllText((Join-Path $Destination 'eng.traineddata'), 'fixture')
            [IO.File]::WriteAllText((Join-Path $Destination 'chi_sim.traineddata'), 'fixture')
            """);

    private void WriteScript(string folder, string name, string content)
        => WriteFile(Path.Combine(_baseDirectory, folder, name), content);

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static async Task<int> ReadProcessIdAsync(string path)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(8))
        {
            try
            {
                if (File.Exists(path) && int.TryParse(File.ReadAllText(path), out int id)) return id;
            }
            catch (IOException) { }
            await Task.Delay(50);
        }
        Assert.Fail("The isolated setup fixture did not start its process within the deadline.");
        return 0;
    }

    private static void AssertProcessExited(int id)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            Assert.IsTrue(process.HasExited, "The setup process tree must stop before cancellation completes.");
        }
        catch (ArgumentException) { }
    }

    private sealed class ImmediateProgress : IProgress<ComponentInstallProgress>
    {
        public List<ComponentInstallPhase> Phases { get; } = [];
        public void Report(ComponentInstallProgress value) => Phases.Add(value.Phase);
    }
}
