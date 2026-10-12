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
    private static readonly NativeMethods.WinEventProc ForegroundChanged = OnForegroundChanged;
    private static nint _foregroundHook;
    private static nint _pastePanelWindow;
    private static readonly DirectPasteCoordinator Coordinator = new(
        new Win32ForegroundWindowService(),
        new Win32InputSender(),
        new SystemDirectPasteClock(),
        (uint)Environment.ProcessId);

    /// <summary>Injected by App at startup (MiniToast pattern).</summary>
    public static SettingsStore? Settings { get; set; }

    /// <summary>
    /// Remembers the external foreground window, retaining an existing valid
    /// target only when the registered quick panel itself owns the foreground.
    /// </summary>
    public static void RememberForeground()
        => Coordinator.RememberForeground(_pastePanelWindow);

    public static void RegisterPanel(nint windowHandle) => _pastePanelWindow = windowHandle;

    /// <summary>
    /// Observe actual foreground changes, including keyboard activation and
    /// switching applications while the always-visible panel stays on screen.
    /// The callback records window identity only; it never reads window text.
    /// </summary>
    public static void StartTracking()
    {
        if (_foregroundHook != nint.Zero) return;
        _foregroundHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            nint.Zero, ForegroundChanged, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
        if (_foregroundHook == nint.Zero)
            App.Log?.Warn("Direct-paste foreground tracking could not be initialized.");
        RememberForeground();
    }

    public static void StopTracking()
    {
        if (_foregroundHook != nint.Zero)
            NativeMethods.UnhookWinEvent(_foregroundHook);
        _foregroundHook = nint.Zero;
        _pastePanelWindow = nint.Zero;
    }

    private static void OnForegroundChanged(
        nint hook, uint eventType, nint windowHandle, int objectId,
        int childId, uint eventThread, uint eventTime)
        => RememberForeground();

    /// <summary>
    /// Attempts one verified Ctrl+V operation. The returned result is always
    /// observed by the UI command, so the core workflow has no async-void edge.
    /// </summary>
    public static async Task<DirectPasteResult> TryPasteToTargetAsync(
        CancellationToken cancellationToken = default)
    {
        if (Settings?.Current.DirectPasteEnabled != true)
            return DirectPasteResult.Disabled;

        DirectPasteResult result = await Coordinator.PasteAsync(cancellationToken);
        if (result != DirectPasteResult.Success)
            App.Log?.Debug($"Direct paste was not sent: {result}.");
        return result;
    }

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
            => NativeMethods.GetForegroundWindow() == windowHandle ||
               NativeMethods.SetForegroundWindow(windowHandle);
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
