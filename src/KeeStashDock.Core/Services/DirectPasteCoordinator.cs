namespace KeeStashDock.Core.Services;

/// <summary>Identity snapshot of the external window selected for direct paste.</summary>
public readonly record struct DirectPasteTarget(
    nint WindowHandle,
    uint ProcessId,
    DateTimeOffset CapturedAtUtc,
    DateTimeOffset? ProcessStartedAtUtc);

public enum DirectPasteResult
{
    Success,
    Disabled,
    NoTarget,
    InvalidTarget,
    TargetExpired,
    TargetProcessChanged,
    FocusDenied,
    FocusLost,
    TargetChanged,
    Busy,
    Cancelled,
    InputFailed,
}

public enum DirectPasteKey : byte
{
    Control = 0x11,
    Shift = 0x10,
    Alt = 0x12,
    V = 0x56,
    LeftWindows = 0x5B,
    RightWindows = 0x5C,
}

public interface IForegroundWindowService
{
    nint GetForegroundWindow();
    bool IsWindow(nint windowHandle);
    bool TryGetWindowProcessId(nint windowHandle, out uint processId);
    bool TryGetProcessStartTimeUtc(uint processId, out DateTimeOffset startedAtUtc);
    bool SetForegroundWindow(nint windowHandle);
}

public interface IInputSender
{
    bool IsKeyDown(DirectPasteKey key);
    void KeyDown(DirectPasteKey key);
    void KeyUp(DirectPasteKey key);
}

public interface IDirectPasteClock
{
    DateTimeOffset UtcNow { get; }
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

/// <summary>
/// Fail-closed direct-paste workflow. It sends input only after the remembered
/// HWND, process identity and foreground focus have all been revalidated.
/// </summary>
public sealed class DirectPasteCoordinator
{
    public static readonly TimeSpan DefaultTargetLifetime = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan DefaultFocusDelay = TimeSpan.FromMilliseconds(140);

    private static readonly DirectPasteKey[] Modifiers =
    [
        DirectPasteKey.Control,
        DirectPasteKey.Shift,
        DirectPasteKey.Alt,
        DirectPasteKey.LeftWindows,
        DirectPasteKey.RightWindows,
    ];

    private readonly IForegroundWindowService _windows;
    private readonly IInputSender _input;
    private readonly IDirectPasteClock _clock;
    private readonly uint _currentProcessId;
    private readonly TimeSpan _targetLifetime;
    private readonly TimeSpan _focusDelay;
    private readonly object _targetGate = new();
    private DirectPasteTarget? _target;
    private int _pasteInProgress;

    public DirectPasteCoordinator(
        IForegroundWindowService windows,
        IInputSender input,
        IDirectPasteClock clock,
        uint currentProcessId,
        TimeSpan? targetLifetime = null,
        TimeSpan? focusDelay = null)
    {
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentProcessId = currentProcessId;
        _targetLifetime = targetLifetime ?? DefaultTargetLifetime;
        _focusDelay = focusDelay ?? DefaultFocusDelay;

        if (_targetLifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(targetLifetime));
        if (_focusDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(focusDelay));
    }

    /// <summary>
    /// Captures a valid external foreground target. The registered paste panel
    /// may keep an existing, still-valid target while the user interacts with
    /// it. Other own-process windows and invalid observations clear authority.
    /// </summary>
    public void RememberForeground(nint pastePanelWindow = default)
    {
        DirectPasteTarget? captured = null;
        try
        {
            nint windowHandle = _windows.GetForegroundWindow();
            if (windowHandle != nint.Zero &&
                _windows.IsWindow(windowHandle) &&
                _windows.TryGetWindowProcessId(windowHandle, out uint processId) &&
                processId != 0)
            {
                DirectPasteTarget? previous = GetTarget();
                if (processId == _currentProcessId)
                {
                    // WM_MOUSEACTIVATE can also be delivered while the panel
                    // already owns foreground focus. It is not a new target.
                    if (windowHandle == pastePanelWindow &&
                        previous is DirectPasteTarget target &&
                        ValidateTarget(target) == DirectPasteResult.Success)
                    {
                        captured = target;
                    }
                }
                else if (Volatile.Read(ref _pasteInProgress) != 0 &&
                         previous is DirectPasteTarget pending &&
                         pending.WindowHandle == windowHandle &&
                         ValidateTarget(pending) == DirectPasteResult.Success)
                {
                    // SetForegroundWindow raises a foreground event itself.
                    // Do not replace the snapshot that the in-flight paste is
                    // validating merely because its own focus request worked.
                    captured = pending;
                }
                else
                {
                    DateTimeOffset? processStartedAt =
                        _windows.TryGetProcessStartTimeUtc(processId, out DateTimeOffset startedAt)
                            ? startedAt
                            : null;
                    captured = new DirectPasteTarget(
                        windowHandle,
                        processId,
                        _clock.UtcNow,
                        processStartedAt);
                }
            }
        }
        catch
        {
            // Window/process inspection is inherently racy. Treat every
            // failure as loss of authority and retain no stale target.
        }

        lock (_targetGate)
        {
            _target = captured;
        }
    }

    public async Task<DirectPasteResult> PasteAsync(
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            ClearTarget();
            return DirectPasteResult.Cancelled;
        }

        if (Interlocked.CompareExchange(ref _pasteInProgress, 1, 0) != 0)
            return DirectPasteResult.Busy;

        try
        {
            DirectPasteTarget? target = GetTarget();
            if (target is null)
                return DirectPasteResult.NoTarget;

            DirectPasteResult validation = ValidateTarget(target.Value);
            if (validation != DirectPasteResult.Success)
                return Fail(validation);
            if (!IsCurrentTarget(target.Value))
                return Fail(DirectPasteResult.TargetChanged);

            bool focused;
            try
            {
                focused = _windows.SetForegroundWindow(target.Value.WindowHandle);
            }
            catch
            {
                focused = false;
            }

            if (!focused)
                return Fail(DirectPasteResult.FocusDenied);

            try
            {
                await _clock.DelayAsync(_focusDelay, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return Fail(DirectPasteResult.Cancelled);
            }

            if (!IsCurrentTarget(target.Value))
                return Fail(DirectPasteResult.TargetChanged);

            validation = ValidateTarget(target.Value);
            if (validation != DirectPasteResult.Success)
                return Fail(validation);

            nint foreground;
            try
            {
                foreground = _windows.GetForegroundWindow();
            }
            catch
            {
                return Fail(DirectPasteResult.FocusLost);
            }

            if (foreground != target.Value.WindowHandle)
                return Fail(DirectPasteResult.FocusLost);

            if (!SendPasteChord())
                return Fail(DirectPasteResult.InputFailed);

            return DirectPasteResult.Success;
        }
        finally
        {
            Volatile.Write(ref _pasteInProgress, 0);
        }
    }

    private DirectPasteResult ValidateTarget(DirectPasteTarget target)
    {
        DateTimeOffset now = _clock.UtcNow;
        TimeSpan age = now - target.CapturedAtUtc;
        if (age < TimeSpan.Zero || age > _targetLifetime)
            return DirectPasteResult.TargetExpired;

        try
        {
            if (target.WindowHandle == nint.Zero || !_windows.IsWindow(target.WindowHandle))
                return DirectPasteResult.InvalidTarget;

            if (!_windows.TryGetWindowProcessId(target.WindowHandle, out uint processId) ||
                processId == 0 ||
                processId == _currentProcessId ||
                processId != target.ProcessId)
            {
                return DirectPasteResult.TargetProcessChanged;
            }

            if (target.ProcessStartedAtUtc is DateTimeOffset expectedStart &&
                (!_windows.TryGetProcessStartTimeUtc(processId, out DateTimeOffset actualStart) ||
                 actualStart != expectedStart))
            {
                return DirectPasteResult.TargetProcessChanged;
            }
        }
        catch
        {
            return DirectPasteResult.InvalidTarget;
        }

        return DirectPasteResult.Success;
    }

    private bool SendPasteChord()
    {
        var mayBeDown = new HashSet<DirectPasteKey>();
        bool completed = false;
        try
        {
            foreach (DirectPasteKey modifier in Modifiers)
            {
                if (_input.IsKeyDown(modifier))
                {
                    mayBeDown.Add(modifier);
                    _input.KeyUp(modifier);
                    mayBeDown.Remove(modifier);
                }
            }

            mayBeDown.Add(DirectPasteKey.Control);
            _input.KeyDown(DirectPasteKey.Control);
            mayBeDown.Add(DirectPasteKey.V);
            _input.KeyDown(DirectPasteKey.V);
            _input.KeyUp(DirectPasteKey.V);
            mayBeDown.Remove(DirectPasteKey.V);
            _input.KeyUp(DirectPasteKey.Control);
            mayBeDown.Remove(DirectPasteKey.Control);
            completed = true;
        }
        catch
        {
            // The finally block releases every key whose down transition may
            // have happened before an input adapter failure was reported.
        }
        finally
        {
            foreach (DirectPasteKey key in mayBeDown)
                TryRelease(key);
        }

        return completed;
    }

    private void TryRelease(DirectPasteKey key)
    {
        try
        {
            _input.KeyUp(key);
        }
        catch
        {
            // Best effort: continue releasing the remaining synthetic keys.
        }
    }

    private DirectPasteTarget? GetTarget()
    {
        lock (_targetGate)
        {
            return _target;
        }
    }

    private bool IsCurrentTarget(DirectPasteTarget target)
    {
        lock (_targetGate)
        {
            return _target == target;
        }
    }

    private void ClearTarget()
    {
        lock (_targetGate)
        {
            _target = null;
        }
    }

    private DirectPasteResult Fail(DirectPasteResult result)
    {
        ClearTarget();
        return result;
    }
}
