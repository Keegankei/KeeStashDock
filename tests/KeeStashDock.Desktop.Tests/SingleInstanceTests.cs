using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

[TestClass]
public sealed class SingleInstanceTests
{
    [TestMethod]
    public void SecondaryProcessesFindOwnerAndRepeatedlyDeliverActivation()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string mutexName = @"Local\KeeStashDock_Test_" + suffix;
        string pipeName = "KeeStashDock_Test_" + suffix;
        using var activation = new AutoResetEvent(false);
        using (var primary = new SingleInstance(mutexName, pipeName))
        {
            primary.ActivationRequested += () => activation.Set();
            Assert.IsTrue(primary.TryClaim());
            for (int attempt = 0; attempt < 2; attempt++)
            {
                RunSecondaryProcess(mutexName, pipeName);
                Assert.IsTrue(activation.WaitOne(TimeSpan.FromSeconds(5)),
                    "A separate process must deliver each SHOW request to the existing instance.");
            }
        }
        // Ownership must be available again after an orderly shutdown. The names
        // are unique to this test and never address the user's running instance.
        using var replacement = new SingleInstance(mutexName, pipeName);
        Assert.IsTrue(replacement.TryClaim());
    }

    [TestMethod]
    public void ProductSecondaryPathNotifiesWithoutTakingMutexOwnership()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string mutexName = @"Local\KeeStashDock_Test_" + suffix;
        string pipeName = "KeeStashDock_Test_" + suffix;
        using var activation = new AutoResetEvent(false);
        using var primary = new SingleInstance(mutexName, pipeName);
        primary.ActivationRequested += () => activation.Set();
        Assert.IsTrue(primary.TryClaim());
        using (var secondary = new SingleInstance(mutexName, pipeName))
            Assert.IsFalse(secondary.TryClaim());
        Assert.IsTrue(activation.WaitOne(TimeSpan.FromSeconds(5)));
        using var third = new SingleInstance(mutexName, pipeName);
        Assert.IsFalse(third.TryClaim(), "Disposing a secondary instance must not release primary ownership.");
        Assert.IsTrue(activation.WaitOne(TimeSpan.FromSeconds(5)));
    }

    private static void RunSecondaryProcess(string mutexName, string pipeName)
    {
        // Use a real child process and the documented mutex/pipe protocol. A
        // same-thread Mutex is recursive and alone cannot prove cross-process IPC.
        string script = $$"""
            $ErrorActionPreference = 'Stop'
            $created = $false
            $mutex = [System.Threading.Mutex]::new($true, '{{mutexName}}', [ref]$created)
            if ($created) { $mutex.ReleaseMutex(); $mutex.Dispose(); throw 'Primary mutex was not found.' }
            $client = [System.IO.Pipes.NamedPipeClientStream]::new('.', '{{pipeName}}', [System.IO.Pipes.PipeDirection]::Out)
            try {
                $client.Connect(3000)
                $writer = [System.IO.StreamWriter]::new($client)
                $writer.Write('SHOW')
                $writer.Dispose()
            } finally { $client.Dispose(); $mutex.Dispose() }
            """;
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using Process process = Process.Start(start)!;
        Task<string> error = process.StandardError.ReadToEndAsync();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        bool exited = process.WaitForExit(10_000);
        if (!exited) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
        Assert.IsTrue(exited, "The isolated secondary process timed out.");
        Assert.AreEqual(0, process.ExitCode, error.GetAwaiter().GetResult() + output.GetAwaiter().GetResult());
    }
}
