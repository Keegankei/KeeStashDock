using System.Windows.Interop;
using KeeStashDock.Desktop.Interop;

namespace KeeStashDock.Desktop;

/// <summary>
/// Owns one global action shortcut on a hidden message window. Independent
/// instances can register panel and screenshot actions without sharing IDs.
/// Rebinding reserves the new combination before releasing the working one.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private static int _nextId = 0x1000;
    private readonly int _firstId = Interlocked.Add(ref _nextId, 2) - 1;
    private readonly Func<int, uint, uint, bool> _register;
    private readonly Action<int> _unregister;
    private readonly bool _usesNativeWindow;
    private HwndSource? _source;
    private int? _registeredId;
    private uint _modifiers;
    private uint _virtualKey;
    private bool _initialized;
    private bool _disposed;

    public HotkeyManager()
    {
        _usesNativeWindow = true;
        _register = (id, modifiers, vk) => _source is not null &&
            NativeMethods.RegisterHotKey(_source.Handle, id, modifiers, vk);
        _unregister = id =>
        {
            if (_source is not null) NativeMethods.UnregisterHotKey(_source.Handle, id);
        };
    }

    // A registration seam keeps conflict tests independent of the user's shortcuts.
    internal HotkeyManager(Func<int, uint, uint, bool> register, Action<int> unregister)
    {
        _register = register;
        _unregister = unregister;
    }

    public bool IsRegistered => _registeredId.HasValue;
    public event Action? HotkeyPressed;
    internal HotkeyRegistrationStatus RegistrationStatus { get; private set; } = new();
    internal event Action? RegistrationChanged;

    /// <summary>Creates the hidden window. Pass zero/zero to start without a shortcut.</summary>
    public bool Initialize(uint modifiers, uint vk)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized) return modifiers == 0 && vk == 0 ? DisableAndReturn() : Rebind(modifiers, vk);
        if (_usesNativeWindow)
        {
            var parameters = new HwndSourceParameters("KeeStashDockHotkeyWindow")
            {
                Width = 0, Height = 0,
                WindowStyle = 0,
                HwndSourceHook = WndProc,
            };
            _source = new HwndSource(parameters);
        }
        _initialized = true;
        return modifiers == 0 && vk == 0 ? DisableAndReturn() : Rebind(modifiers, vk);
    }

    public bool Rebind(uint modifiers, uint vk)
    {
        if (!_initialized || _disposed) return false;
        var requested = new HotkeyCombination(modifiers, vk);
        if (modifiers == 0 || (modifiers & ~0x0fu) != 0 || vk == 0 || vk > 0xfe)
        {
            SetStatus(RegistrationStatus with { FailedAttempt = requested });
            return false;
        }
        if (IsRegistered && modifiers == _modifiers && vk == _virtualKey)
        {
            SetStatus(new(requested));
            return true;
        }

        // A failed registration leaves the existing binding intact, without a
        // release/reacquire race with another application.
        int nextId = _registeredId == _firstId ? _firstId + 1 : _firstId;
        if (!_register(nextId, modifiers | NativeMethods.MOD_NOREPEAT, vk))
        {
            SetStatus(RegistrationStatus with { FailedAttempt = requested });
            return false;
        }
        int? previous = _registeredId;
        _registeredId = nextId;
        _modifiers = modifiers;
        _virtualKey = vk;
        if (previous is int oldId) _unregister(oldId);
        SetStatus(new(requested));
        return true;
    }

    /// <summary>Stops this action's shortcut without affecting other actions.</summary>
    public void Disable()
    {
        if (_registeredId is int id) _unregister(id);
        _registeredId = null;
        SetStatus(new());
    }

    private void SetStatus(HotkeyRegistrationStatus status)
    {
        if (RegistrationStatus == status) return;
        RegistrationStatus = status;
        RegistrationChanged?.Invoke();
    }

    private bool DisableAndReturn() { Disable(); return true; }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && _registeredId == wParam.ToInt32())
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Disable();
        _source?.Dispose();
        _source = null;
        _disposed = true;
    }
}
