using System.Globalization;
using System.Windows.Input;
using KeeStashDock.Desktop.Interop;
using KeeStashDock.Desktop.Localization;

namespace KeeStashDock.Desktop;

internal readonly record struct HotkeyCombination(uint Modifiers, uint VirtualKey)
{
    public override string ToString()
    {
        var parts = new List<string>(5);
        if ((Modifiers & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((Modifiers & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
        if ((Modifiers & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((Modifiers & NativeMethods.MOD_WIN) != 0) parts.Add("Win");
        parts.Add(KeyInterop.KeyFromVirtualKey((int)VirtualKey).ToString());
        return string.Join(" + ", parts);
    }
}

internal sealed record HotkeyRegistrationStatus(
    HotkeyCombination? Active = null, HotkeyCombination? FailedAttempt = null)
{
    public bool HasFailure => FailedAttempt.HasValue;

    public string Describe()
    {
        var loc = LocalizationService.Instance;
        if (FailedAttempt is { } failed)
        {
            string description = string.Format(CultureInfo.CurrentCulture, loc["Hotkeys.Status.Unavailable"], failed);
            if (Active is { } previous)
                description += " " + string.Format(CultureInfo.CurrentCulture, loc["Hotkeys.Status.Preserved"], previous);
            return description;
        }
        return Active is { } active
            ? string.Format(CultureInfo.CurrentCulture, loc["Hotkeys.Status.Active"], active)
            : loc["Hotkeys.Status.Disabled"];
    }
}

internal enum HotkeyAction { Panel, Screenshot }

/// <summary>
/// Coalesces startup failures without hiding one action behind the other's toast.
/// A successful registration or explicit disable ends an episode. Merely retrying
/// an unavailable shortcut does not send another notification.
/// </summary>
internal sealed class HotkeyConflictNotifications
{
    private readonly Dictionary<HotkeyAction, HotkeyRegistrationStatus> _latest = new();
    private readonly HashSet<HotkeyAction> _notified = new();
    private readonly HashSet<HotkeyAction> _pending = new();

    public void Observe(HotkeyAction action, HotkeyRegistrationStatus status)
    {
        _latest[action] = status;
        if (!status.HasFailure)
        {
            _notified.Remove(action);
            _pending.Remove(action);
        }
        else if (!_notified.Contains(action)) _pending.Add(action);
    }

    public IReadOnlyList<(HotkeyAction Action, HotkeyRegistrationStatus Status)> TakePending()
    {
        var result = _pending.OrderBy(action => action)
            .Select(action => (Action: action, Status: _latest[action])).ToArray();
        _notified.UnionWith(_pending);
        _pending.Clear();
        return result;
    }
}
