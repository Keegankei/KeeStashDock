using Microsoft.Win32;

namespace KeeStashDock.Desktop;

/// <summary>
/// Launch-at-startup via the per-user HKCU Run key - the correct mechanism
/// for a portable app (no admin rights, no installer, no scheduled task).
/// The stored path always points at the CURRENT executable location, so if
/// the user moves the portable folder, toggling the setting once re-heals it.
/// </summary>
public static class AutoStartHelper
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "KeeStashDock";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --minimized");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
