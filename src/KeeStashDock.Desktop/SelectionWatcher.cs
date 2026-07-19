using KeeStashDock.Desktop.Interop;

namespace KeeStashDock.Desktop;

/// <summary>
/// Global "the user probably just selected text" detector. A WH_MOUSE_LL
/// hook watches for the two selection gestures:
///   - DRAG: left-down, move beyond a threshold, left-up;
///   - DOUBLE-CLICK: two quick left clicks in nearly the same spot
///     (which selects a word in practically every text control).
/// On either, <see cref="SelectionLikely"/> fires with the screen point
/// (device px). It cannot know FOR SURE that text was selected - that is
/// verified later by the capture flow via the clipboard sequence number -
/// this class only decides when showing the bubble is worth it.
///
/// Gestures over this process's own windows are ignored (selecting text in
/// the workbench editor should not offer to capture itself).
///
/// Lifetime notes: the hook delegate is kept in a field - if it were GC'd
/// the callback would crash the hook chain; and every event is raised on
/// the installing (UI) thread because LL hooks deliver on the thread that
/// owns the message loop they were installed from.
/// </summary>
public sealed class SelectionWatcher : IDisposable
{
    private const int DragThresholdPx = 24;
    private const int DragMinMs = 120;
    private const int DoubleClickMs = 420;
    private const int DoubleClickSlopPx = 6;

    private readonly NativeMethods.LowLevelMouseProc _proc;   // GC anchor
    private IntPtr _hook;
    private readonly uint _ownPid = (uint)Environment.ProcessId;

    private NativeMethods.POINT _downPt;
    private uint _downTime;
    private uint _lastClickTime;
    private NativeMethods.POINT _lastClickPt;

    /// <summary>Fired when a selection gesture completed (screen device px).</summary>
    public event Action<int, int>? SelectionLikely;

    /// <summary>Fired on left-button-down OUTSIDE this process's windows
    /// (used to dismiss the bubble). Downs on our own windows - notably on
    /// the bubble itself - must NOT fire, or the dismissal would swallow the
    /// bubble's own click before its MouseLeftButtonUp could run.</summary>
    public event Action<int, int>? AnyMouseDown;

    public SelectionWatcher() => _proc = HookProc;

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL, _proc,
            NativeMethods.GetModuleHandle(null), 0);
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero) return;
        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = System.Runtime.InteropServices.Marshal
                .PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            switch ((int)wParam)
            {
                case NativeMethods.WM_LBUTTONDOWN:
                    _downPt = info.pt;
                    _downTime = info.time;
                    if (!IsOwnWindowAt(info.pt))
                        AnyMouseDown?.Invoke(info.pt.X, info.pt.Y);
                    break;

                case NativeMethods.WM_LBUTTONUP:
                    OnUp(info);
                    break;
            }
        }
        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void OnUp(NativeMethods.MSLLHOOKSTRUCT info)
    {
        int dx = Math.Abs(info.pt.X - _downPt.X);
        int dy = Math.Abs(info.pt.Y - _downPt.Y);
        uint held = info.time - _downTime;

        bool drag = (dx > DragThresholdPx || dy > DragThresholdPx) && held >= DragMinMs;

        // Double-click: this up completes a second click near the first.
        bool dbl = info.time - _lastClickTime <= DoubleClickMs &&
                   Math.Abs(info.pt.X - _lastClickPt.X) <= DoubleClickSlopPx &&
                   Math.Abs(info.pt.Y - _lastClickPt.Y) <= DoubleClickSlopPx &&
                   dx <= DoubleClickSlopPx && dy <= DoubleClickSlopPx;

        _lastClickTime = info.time;
        _lastClickPt = info.pt;

        if (!drag && !dbl) return;
        if (IsOwnWindowAt(info.pt)) return;
        SelectionLikely?.Invoke(info.pt.X, info.pt.Y);
    }

    private bool IsOwnWindowAt(NativeMethods.POINT pt)
    {
        IntPtr hwnd = NativeMethods.WindowFromPoint(pt);
        if (hwnd == IntPtr.Zero) return false;
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == _ownPid;
    }

    public void Dispose() => Stop();
}
