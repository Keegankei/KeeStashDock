using System.Drawing;

namespace KeeStashDock.Desktop.Views;

internal enum PanelDockSide { None, Left, Top, Right, Bottom }

/// <summary>Physical-pixel geometry keeps negative monitor origins and mixed DPI safe.</summary>
internal static class EdgeDockGeometry
{
    internal const double SnapDistanceDip = 14;

    internal static PanelDockSide Detect(Rectangle bounds, Rectangle workArea, double dpiScale)
    {
        int threshold = (int)Math.Ceiling(SnapDistanceDip * ValidScale(dpiScale));
        var distances = new[]
        {
            (Side: PanelDockSide.Left, Distance: Math.Abs((long)bounds.Left - workArea.Left)),
            (Side: PanelDockSide.Top, Distance: Math.Abs((long)bounds.Top - workArea.Top)),
            (Side: PanelDockSide.Right, Distance: Math.Abs((long)bounds.Right - workArea.Right)),
            (Side: PanelDockSide.Bottom, Distance: Math.Abs((long)bounds.Bottom - workArea.Bottom)),
        };
        var nearest = distances.MinBy(value => value.Distance);
        return nearest.Distance <= threshold ? nearest.Side : PanelDockSide.None;
    }

    internal static Rectangle HandleBounds(Rectangle normal, Rectangle workArea, PanelDockSide side, double dpiScale)
    {
        double scale = ValidScale(dpiScale);
        int thickness = Math.Max(4, (int)Math.Round(6 * scale));
        int length = Math.Max(thickness, (int)Math.Round(72 * scale));
        bool vertical = side is PanelDockSide.Left or PanelDockSide.Right;
        int width = Math.Min(workArea.Width, vertical ? thickness : length);
        int height = Math.Min(workArea.Height, vertical ? length : thickness);
        int x = normal.Left + (normal.Width - width) / 2;
        int y = normal.Top + (normal.Height - height) / 2;
        if (side == PanelDockSide.Left) x = workArea.Left;
        if (side == PanelDockSide.Right) x = workArea.Right - width;
        if (side == PanelDockSide.Top) y = workArea.Top;
        if (side == PanelDockSide.Bottom) y = workArea.Bottom - height;
        return Clamp(new Rectangle(x, y, width, height), workArea);
    }

    internal static Rectangle RestoreBounds(Rectangle normal, Rectangle workArea, PanelDockSide side)
    {
        Rectangle restored = Clamp(normal, workArea);
        return side switch
        {
            PanelDockSide.Left => restored with { X = workArea.Left },
            PanelDockSide.Top => restored with { Y = workArea.Top },
            PanelDockSide.Right => restored with { X = workArea.Right - restored.Width },
            PanelDockSide.Bottom => restored with { Y = workArea.Bottom - restored.Height },
            _ => restored,
        };
    }

    internal static Rectangle Clamp(Rectangle bounds, Rectangle workArea)
    {
        int width = Math.Clamp(bounds.Width, 1, Math.Max(1, workArea.Width));
        int height = Math.Clamp(bounds.Height, 1, Math.Max(1, workArea.Height));
        return new Rectangle(Math.Clamp(bounds.X, workArea.Left, workArea.Right - width),
            Math.Clamp(bounds.Y, workArea.Top, workArea.Bottom - height), width, height);
    }

    private static double ValidScale(double value) => double.IsFinite(value) && value > 0 ? value : 1;
}
