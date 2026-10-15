using System.Windows;
using System.Windows.Input;
using KeeStashDock.Domain.Entities;

namespace KeeStashDock.Desktop.Views;

internal static class InspectionGesture
{
    internal static bool Matches(AppSettings settings, MouseButton button, ModifierKeys modifiers)
    {
        if (settings.FloatingInspectionGesture == "Middle")
            return button == MouseButton.Middle && modifiers == ModifierKeys.None;
        if (settings.FloatingInspectionGesture != "Custom")
            return button == MouseButton.Right && modifiers == ModifierKeys.None;
        if (!Enum.TryParse(settings.FloatingInspectionButton, out MouseButton configured) ||
            string.IsNullOrWhiteSpace(settings.FloatingInspectionModifiers)) return false;
        ModifierKeys required = ModifierKeys.None;
        foreach (string part in settings.FloatingInspectionModifiers.Split('+'))
        {
            if (!Enum.TryParse(part, out ModifierKeys flag) ||
                (flag & ~(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift)) != 0) return false;
            required |= flag;
        }
        return button == configured && modifiers == required;
    }

    internal static PerspectivePose DragPose(PerspectivePose start, Vector delta, Size size)
    {
        if (!double.IsFinite(delta.X) || !double.IsFinite(delta.Y)) return start;
        return new(Math.Clamp(start.Yaw + delta.X * 32 / Math.Max(200, size.Width),
                -PerspectiveSurface.MaximumYaw, PerspectiveSurface.MaximumYaw),
            Math.Clamp(start.Pitch + delta.Y * 24 / Math.Max(200, size.Height),
                -PerspectiveSurface.MaximumPitch, PerspectiveSurface.MaximumPitch), 0, 0, false);
    }
}
