using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using KeeStashDock.Desktop.Interop;

namespace KeeStashDock.Desktop;

/// <summary>
/// Runtime Light, Dark, and System theme switching. "System" reads the
/// Windows AppsUseLightTheme registry value; panel effects are applied by the
/// quick-panel appearance layer.
/// </summary>
public static class ThemeManager
{
    /// <summary>Effective darkness of the ACTIVE palette (resolves "System").</summary>
    public static bool IsDark { get; private set; }

    /// <summary>Raised after every palette swap; chromed windows re-tint.</summary>
    public static event Action? Changed;

    public static void Apply(string theme)
    {
        bool dark = theme switch
        {
            "Dark" => true,
            "Light" => false,
            _ => IsSystemDark(),
        };
        IsDark = dark;
        var uri = new Uri(dark ? "Themes/Theme.Dark.xaml" : "Themes/Theme.Light.xaml",
                          UriKind.Relative);
        var dicts = Application.Current.Resources.MergedDictionaries;
        // Slot 0 = palette, slot 1 = Styles.xaml (order fixed by App.xaml).
        dicts[0] = new ResourceDictionary { Source = uri };
        Changed?.Invoke();
    }

    /// <summary>Tints the NATIVE window frame (title bar) to match the active
    /// palette and requests Win11 rounded corners. Before this, chromed
    /// windows (workbench / settings / dialogs) kept a white title bar in
    /// dark mode - the biggest remaining "dark theme still off" offender.
    /// No-op silently on systems without these DWM attributes.</summary>
    public static void ApplyWindowChrome(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        try
        {
            int dark = IsDark ? 1 : 0;
            NativeMethods.DwmSetWindowAttribute(
                hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            int corner = NativeMethods.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(
                hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        }
        catch (EntryPointNotFoundException) { /* pre-20H1 Windows: skip */ }
    }

    /// <summary>Hooks a window so its native frame follows theme switches.</summary>
    public static void TrackWindowChrome(Window window)
    {
        window.SourceInitialized += (_, _) => ApplyWindowChrome(window);
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            ApplyWindowChrome(window);
        Changed += () => ApplyWindowChrome(window);
    }

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }
}
