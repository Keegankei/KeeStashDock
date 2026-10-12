using System.Diagnostics;
using System.Globalization;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

[TestClass]
public sealed class DesktopApplicationRestarterTests
{
    [TestMethod]
    public void RestartLaunchPreservesExecutablePathWithoutShellExpansion()
    {
        const string executable = @"C:\Apps\User's %folder% & tools\KeeStashDock.exe";
        ProcessStartInfo start = DesktopApplicationRestarter.CreateStartInfo(executable, 123, 456);
        Assert.AreEqual(executable, start.FileName);
        Assert.IsFalse(start.UseShellExecute);
        CollectionAssert.AreEqual(new[] { "--restart-after", "123", "456" }, start.ArgumentList.ToArray());
        Assert.AreEqual(string.Empty, start.Arguments);
    }

    [TestMethod]
    public void ReplacementWaitsForSlowParentToActuallyExit()
    {
        using Process parent = StartIsolatedParent(2200);
        try
        {
            string[] arguments = ParentArguments(parent);
            var elapsed = Stopwatch.StartNew();
            Assert.IsTrue(DesktopApplicationRestarter.WaitForPreviousInstance(arguments, TimeSpan.FromSeconds(10)));
            Assert.IsTrue(parent.HasExited, "The replacement must not contend for the live parent's single-instance mutex.");
            Assert.IsTrue(elapsed.Elapsed >= TimeSpan.FromSeconds(1), "A slow shutdown must not be replaced by a fixed one-second delay.");
        }
        finally { if (!parent.HasExited) parent.Kill(entireProcessTree: true); }
    }

    [TestMethod]
    public void TimedOutParentDoesNotAllowReplacementToStart()
    {
        using Process parent = StartIsolatedParent(10000);
        try
        {
            Assert.IsFalse(DesktopApplicationRestarter.WaitForPreviousInstance(ParentArguments(parent), TimeSpan.FromMilliseconds(50)));
            Assert.IsFalse(parent.HasExited);
        }
        finally { if (!parent.HasExited) parent.Kill(entireProcessTree: true); }
    }

    [TestMethod]
    public void ReusedProcessIdDoesNotWaitForUnrelatedProcess()
    {
        using Process parent = StartIsolatedParent(10000);
        try
        {
            string[] arguments = ParentArguments(parent);
            arguments[2] = (parent.StartTime.ToUniversalTime().Ticks - 1).ToString(CultureInfo.InvariantCulture);
            Assert.IsTrue(DesktopApplicationRestarter.WaitForPreviousInstance(arguments, TimeSpan.FromMilliseconds(50)));
            Assert.IsFalse(parent.HasExited);
        }
        finally { if (!parent.HasExited) parent.Kill(entireProcessTree: true); }
    }

    [TestMethod]
    public void InvalidRestartArgumentsAreRejectedWhileOrdinaryLaunchIsUnaffected()
    {
        Assert.IsTrue(DesktopApplicationRestarter.WaitForPreviousInstance([]));
        Assert.IsFalse(DesktopApplicationRestarter.WaitForPreviousInstance(["--restart-after", "0", "123"]));
        Assert.IsFalse(DesktopApplicationRestarter.WaitForPreviousInstance(["--restart-after", "123"]));
        Assert.IsFalse(DesktopApplicationRestarter.WaitForPreviousInstance([
            "--restart-after", Environment.ProcessId.ToString(CultureInfo.InvariantCulture), "123"]));
    }

    private static string[] ParentArguments(Process parent) => [DesktopApplicationRestarter.RestartArgument,
        parent.Id.ToString(CultureInfo.InvariantCulture), parent.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture)];

    private static Process StartIsolatedParent(int milliseconds)
    {
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add($"[System.Threading.Thread]::Sleep({milliseconds})");
        return Process.Start(start) ?? throw new AssertFailedException("Could not start the isolated parent fixture.");
    }
}
