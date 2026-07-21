using System.Diagnostics;
using KeeStashDock.Core.Services;
using KeeStashDock.Desktop.Interop;
using KeeStashDock.Infrastructure.Storage;

namespace KeeStashDock.Desktop;

/// <summary>
/// Desktop adapter for the fail-closed direct-paste workflow. Window identity
/// checks and input sequencing live in <see cref="DirectPasteCoordinator"/>
/// so they can be verified without sending real desktop input in tests.
/// </summary>
public static class DirectPaste
{
    private static readonly DirectPasteCoordinator Coordinator = new(
        new Win32ForegroundWindowService(),
        new Win32InputSender(),
        new SystemDirectPasteClock(),
        (uint)Environment.ProcessId);

    /// <summary>Injected by App at startup (MiniToast pattern).</summary>
    public static SettingsStore? Settings { get; set; }

    /// <summary>
    /// Remembers a valid external foreground window. Invalid, inaccessible,
    /// zero or same-process windows clear any previously remembered target.
    /// </summary>
    public static void RememberForeground() => Coordinator.RememberForeground();

    /// <summary>
    /// Attempts one verified Ctrl+V operation. The returned result is always
    /// observed by the UI command, so the core workflow has no async-void edge.
    /// </summary>
    public static Task<DirectPasteResult> TryPasteToTargetAsync(
        CancellationToken cancellationToken = default)
        => Settings?.Current.DirectPasteEnabled == true
            ? Coordinator.PasteAsync(cancellationToken)
            : Task.FromResult(DirectPasteResult.Disabled);

    private sealed class Win32ForegroundWindowService : IForegroundWindowService
    {
        public nint GetForegroundWindow() => NativeMethods.GetForegroundWindow();

        public bool IsWindow(nint windowHandle) => NativeMethods.IsWindow(windowHandle);

        public bool TryGetWindowProcessId(nint windowHandle, out uint processId)
            => NativeMethods.GetWindowThreadProcessId(windowHandle, out processId) != 0 &&
               processId != 0;

        public bool TryGetProcessStartTimeUtc(
            uint processId,
            out DateTimeOffset startedAtUtc)
        {
            try
            {
                using Process process = Process.GetProcessById(checked((int)processId));
                startedAtUtc = process.StartTime.ToUniversalTime();
                return !process.HasExited;
            }
            catch (Exception ex) when (
                ex is ArgumentException or InvalidOperationException or
                System.ComponentModel.Win32Exception or OverflowException)
            {
                startedAtUtc = default;
                return false;
            }
        }

        public bool SetForegroundWindow(nint windowHandle)
            => NativeMethods.SetForegroundWindow(windowHandle);
    }

    private sealed class Win32InputSender : IInputSender
    {
        public bool IsKeyDown(DirectPasteKey key)
            => (NativeMethods.GetAsyncKeyState((int)key) & 0x8000) != 0;

        public void KeyDown(DirectPasteKey key)
            => NativeMethods.keybd_event((byte)key, 0, 0, UIntPtr.Zero);

        public void KeyUp(DirectPasteKey key)
            => NativeMethods.keybd_event(
                (byte)key,
                0,
                NativeMethods.KEYEVENTF_KEYUP,
                UIntPtr.Zero);
    }

    private sealed class SystemDirectPasteClock : IDirectPasteClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
            => Task.Delay(delay, cancellationToken);
    }
}
