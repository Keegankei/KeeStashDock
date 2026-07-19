using System.Windows.Interop;
using KeeStashDock.Desktop.Interop;

namespace KeeStashDock.Desktop;

/// <summary>
/// Global hotkey registration on a hidden message-only window, so the hotkey
/// works while every product window is closed/hidden (the "stowed" state).
///
/// Re-registration: <see cref="Rebind"/> is called by the Settings page after
/// the user records a new combination; failure (combination already taken by
/// another app) is reported back so the UI can show a conflict warning
/// instead of failing silently - the promised "conflict detection".
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private const int HotkeyId = 0xB00B;        // panel toggle
    private HwndSource? _source;

    public event Action? HotkeyPressed;

    /// <summary>Creates the hidden message window and registers the initial hotkey.</summary>
    public bool Initialize(uint modifiers, uint vk)
    {
        // HWND_MESSAGE-style hidden window: 0 size, never shown, just a WndProc.
        var p = new HwndSourceParameters("KeeStashDockHotkeyWindow")
        {
            Width = 0, Height = 0,
            WindowStyle = 0,
            HwndSourceHook = WndProc,
        };
        _source = new HwndSource(p);
        return Register(modifiers, vk);
    }

    public bool Rebind(uint modifiers, uint vk)
    {
        if (_source is null) return false;
        NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        return Register(modifiers, vk);
    }

    private bool Register(uint modifiers, uint vk)
        => _source is not null &&
           NativeMethods.RegisterHotKey(_source.Handle, HotkeyId,
                                        modifiers | NativeMethods.MOD_NOREPEAT, vk);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_source is not null)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
            _source.Dispose();
        }
    }
}
