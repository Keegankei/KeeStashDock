using System.Diagnostics;
using KeeStashDock.Infrastructure.Storage;

namespace KeeStashDock.Desktop;

internal sealed class DesktopApplicationRestarter : IApplicationRestarter
{
    public bool TryRestart()
    {
        try
        {
            string? executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable))
            {
                return false;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c timeout /t 1 /nobreak >nul & start \"\" \"{executable}\"",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
