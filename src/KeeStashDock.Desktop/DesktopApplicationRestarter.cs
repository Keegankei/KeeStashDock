using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using KeeStashDock.Infrastructure.Storage;

namespace KeeStashDock.Desktop;

internal sealed class DesktopApplicationRestarter : IApplicationRestarter
{
    internal const string RestartArgument = "--restart-after";

    public bool TryRestart()
    {
        try
        {
            string? executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable)) return false;
            using Process current = Process.GetCurrentProcess();
            using Process? child = Process.Start(CreateStartInfo(executable, current.Id,
                current.StartTime.ToUniversalTime().Ticks));
            return child is not null;
        }
        catch
        {
            return false;
        }
    }

    internal static ProcessStartInfo CreateStartInfo(string executable, int parentId, long parentStartedTicks)
    {
        var start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        start.ArgumentList.Add(RestartArgument);
        start.ArgumentList.Add(parentId.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add(parentStartedTicks.ToString(CultureInfo.InvariantCulture));
        return start;
    }

    /// <summary>The replacement waits before claiming the single-instance mutex or
    /// touching the database. The start time prevents waiting on a reused process ID.</summary>
    internal static bool WaitForPreviousInstance(string[] arguments, TimeSpan? timeout = null)
    {
        if (arguments.Length == 0 || arguments[0] != RestartArgument) return true;
        if (arguments.Length != 3 ||
            !int.TryParse(arguments[1], NumberStyles.None, CultureInfo.InvariantCulture, out int parentId) ||
            !long.TryParse(arguments[2], NumberStyles.None, CultureInfo.InvariantCulture, out long startedTicks) ||
            parentId <= 0 || parentId == Environment.ProcessId || startedTicks <= 0)
            return false;
        try
        {
            using Process parent = Process.GetProcessById(parentId);
            if (parent.StartTime.ToUniversalTime().Ticks != startedTicks) return true;
            return parent.WaitForExit((int)(timeout ?? TimeSpan.FromSeconds(60)).TotalMilliseconds);
        }
        catch (ArgumentException) { return true; } // Already exited.
        catch (InvalidOperationException) { return true; }
        catch (Win32Exception) { return false; }
    }
}
