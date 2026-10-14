using System.Windows;

namespace KeeStashDock.Desktop.Views;

/// <summary>Cursor-relative pose in untransformed panel DIPs, including its nearby desktop area.</summary>
internal readonly record struct PerspectivePose(double Yaw, double Pitch, double OffsetX, double OffsetY, bool Nearby)
{
    internal const double Reach = 180;

    internal static PerspectivePose FromPointer(Point point, Size size)
    {
        if (size.Width <= 0 || size.Height <= 0 || !double.IsFinite(size.Width) || !double.IsFinite(size.Height) ||
            !double.IsFinite(point.X) || !double.IsFinite(point.Y)) return default;
        double outsideX = Math.Max(0, Math.Max(-point.X, point.X - size.Width));
        double outsideY = Math.Max(0, Math.Max(-point.Y, point.Y - size.Height));
        double distance = Math.Sqrt(outsideX * outsideX + outsideY * outsideY);
        if (distance >= Reach) return default;
        double weight = 1 - distance / Reach;
        weight = weight * weight * (3 - 2 * weight);
        double x = Math.Clamp((point.X - size.Width / 2) / (size.Width / 2), -1, 1);
        double y = Math.Clamp((point.Y - size.Height / 2) / (size.Height / 2), -1, 1);
        // Quantization ignores sub-pixel pointer noise; unchanged poses retain
        // no animation clocks. The camera fit covers these bounded values.
        return new(Math.Round(-x * 10 * weight, 1), Math.Round(-y * 7 * weight, 1),
            Math.Round(x * 2 * weight, 1), Math.Round(y * 2 * weight, 1), true);
    }
}
