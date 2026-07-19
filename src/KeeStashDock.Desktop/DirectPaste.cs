using System.Diagnostics;
using KeeStashDock.Infrastructure.Storage;
using KeeStashDock.Desktop.Interop;

namespace KeeStashDock.Desktop;

/// <summary>
/// Opt-in "click a card = paste it where I was typing" behavior. When the
/// user reaches for the panel, the CURRENT foreground window is remembered;
/// after a card copy, focus is handed back there and a synthetic Ctrl+V
/// lands the text - the middleman Ctrl+V the user used to type is gone.
///
/// The remembered handle deliberately skips windows of THIS process, so
/// clicking around inside the panel can never redirect the paste at the
/// panel itself.
/// </summary>
public static class DirectPaste
{
    /// <summary>Injected by App at startup (MiniToast pattern).</summary>
    public static SettingsStore? Settings { get; set; }

    private static IntPtr _target;

    /// <summary>Records the foreground window IF it belongs to another
    /// process. Called when the panel is summoned (hotkey / edge / tray)
    /// and when the panel is about to take activation from a click.</summary>
    public static void RememberForeground()
    {
        IntPtr h = NativeMethods.GetForegroundWindow();
        if (h == IntPtr.Zero) return;
        NativeMethods.GetWindowThreadProcessId(h, out uint pid);
        if (pid == (uint)Environment.ProcessId) return;   // our own window
        _target = h;
    }

    /// <summary>After a card copy: focuses the remembered window and sends
    /// Ctrl+V. No-op unless the feature is enabled and a target exists.</summary>
    public static async void TryPasteToTarget()
    {
        if (Settings?.Current.DirectPasteEnabled != true) return;
        if (_target == IntPtr.Zero) return;

        NativeMethods.SetForegroundWindow(_target);
        await Task.Delay(140);   // let focus + clipboard settle

        // Release any physically held modifiers first so the synthetic
        // Ctrl+V is not polluted into Ctrl+Shift+V etc.
        if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU) & 0x8000) != 0)
            NativeMethods.keybd_event(NativeMethods.VK_MENU, 0,
                NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);

        NativeMethods.keybd_event(NativeMethods.VK_CONTROL, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_V, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_V, 0,
            NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_CONTROL, 0,
            NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}
