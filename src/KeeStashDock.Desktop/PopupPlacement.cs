using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace KeeStashDock.Desktop;

/// <summary>Places non-activating bubbles using one monitor's physical coordinates and current window DPI.</summary>
internal static class PopupPlacement
{
    private const uint NoSize = 0x0001, NoZOrder = 0x0004, NoActivate = 0x0010;

    internal static void ShowNear(Window window, int deviceX, int deviceY, bool preferAbove)
    {
        nint handle = new WindowInteropHelper(window).EnsureHandle();
        Drawing.Rectangle workArea = Forms.Screen.FromPoint(new(deviceX, deviceY)).WorkingArea;
        Drawing.Size size = GetSize(handle);
        // First move the HWND onto the destination monitor so WM_DPICHANGED and
        // GetDpiForWindow use that monitor, instead of the previous bubble's scale.
        Drawing.Point initial = Calculate(new(deviceX, deviceY), size, workArea,
            GetScale(handle), preferAbove);
        SetWindowPos(handle, nint.Zero, initial.X, initial.Y, 0, 0, NoSize | NoZOrder | NoActivate);
        window.Show();
        window.UpdateLayout();
        PlaceNear(window, deviceX, deviceY, preferAbove);
        // SizeToContent can finish a DPI-dependent layout after the first native move.
        window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (window.IsVisible) PlaceNear(window, deviceX, deviceY, preferAbove);
        });
    }

    internal static void PlaceNear(Window window, int deviceX, int deviceY, bool preferAbove)
    {
        nint handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero) return;
        Drawing.Rectangle workArea = Forms.Screen.FromPoint(new(deviceX, deviceY)).WorkingArea;
        Drawing.Point location = Calculate(new(deviceX, deviceY), GetSize(handle), workArea,
            GetScale(handle), preferAbove);
        SetWindowPos(handle, nint.Zero, location.X, location.Y, 0, 0, NoSize | NoZOrder | NoActivate);
    }

    /// <summary>Only offsets are DIP-scaled; anchors, HWND sizes, and monitor bounds are physical pixels.</summary>
    internal static Drawing.Point Calculate(Drawing.Point anchor, Drawing.Size windowSize,
        Drawing.Rectangle workArea, double dpiScale, bool preferAbove)
    {
        if (!double.IsFinite(dpiScale) || dpiScale <= 0) dpiScale = 1;
        int offsetX = (int)Math.Round(10 * dpiScale);
        int gap = (int)Math.Round((preferAbove ? 8 : 16) * dpiScale);
        int width = Math.Max(0, windowSize.Width), height = Math.Max(0, windowSize.Height);
        long x = (long)anchor.X + offsetX;
        long y = preferAbove ? (long)anchor.Y - height - gap : (long)anchor.Y + gap;
        if (preferAbove && y < workArea.Top) y = (long)anchor.Y + (int)Math.Round(18 * dpiScale);
        return new Drawing.Point(
            (int)Math.Clamp(x, workArea.Left, Math.Max((long)workArea.Left, (long)workArea.Right - width)),
            (int)Math.Clamp(y, workArea.Top, Math.Max((long)workArea.Top, (long)workArea.Bottom - height)));
    }

    private static double GetScale(nint handle)
    {
        uint dpi = GetDpiForWindow(handle);
        return dpi == 0 ? 1 : dpi / 96d;
    }

    private static Drawing.Size GetSize(nint handle)
        => GetWindowRect(handle, out var rect)
            ? new Drawing.Size(Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top))
            : new Drawing.Size(1, 1);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rectangle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
