using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace KeeStashDock.Desktop;

/// <summary>
/// Optional compositor blur for the existing layered, non-activating bubbles.
/// No desktop capture, render loop or changes to the window's activation styles.
/// Unsupported/disabled composition uses an opaque palette instead.
/// </summary>
internal static class BubbleBackdrop
{
    private static readonly ConditionalWeakTable<Window, Backdrop> Backdrops = new();

    internal static BubblePalette Apply(Window? window, Border surface, BubblePalette palette, bool enabled)
    {
        if (window is null) return ReadablePalette(palette, enabled, false);
        if (!Backdrops.TryGetValue(window, out Backdrop? backdrop))
        {
            // The default path creates no native state or additional event hooks.
            if (!enabled) return palette;
            backdrop = new Backdrop(window, surface);
            Backdrops.Add(window, backdrop);
        }
        return backdrop.Update(palette, enabled);
    }

    internal static BubblePalette ReadablePalette(BubblePalette palette, bool requested, bool active)
        => requested && !active ? palette with { Opacity = 1 } : palette;

    private sealed class Backdrop
    {
        private readonly Window _window;
        private readonly Border _surface;
        private HwndSource? _source;
        private BubblePalette? _palette;
        private bool _requested, _active, _clipped, _updating, _closed;
        private Rect _lastBounds;
        private Color _lastTint;

        internal Backdrop(Window window, Border surface)
        {
            _window = window; _surface = surface;
            window.SourceInitialized += OnSourceInitialized;
            window.IsVisibleChanged += OnVisibilityChanged;
            window.Closed += OnClosed;
            surface.SizeChanged += OnSizeChanged;
            AttachSource();
        }

        internal BubblePalette Update(BubblePalette palette, bool requested)
        {
            _palette = palette; _requested = requested;
            Refresh();
            return ReadablePalette(palette, requested, _active);
        }

        private void OnSourceInitialized(object? sender, EventArgs e) { AttachSource(); Refresh(); }
        private void AttachSource()
        {
            if (_source is not null) return;
            nint handle = new WindowInteropHelper(_window).Handle;
            if (handle != 0 && HwndSource.FromHwnd(handle) is { } source)
            { _source = source; source.AddHook(OnMessage); }
        }
        private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => Refresh();
        private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Refresh();

        private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
        {
            // Composition, system transparency and per-monitor DPI can change
            // while the result is open. Let WPF finish layout before reapplying.
            if (message is 0x031E or 0x001A or 0x02E0)
            {
                _lastBounds = Rect.Empty;
                if (!_window.Dispatcher.HasShutdownStarted)
                    _window.Dispatcher.BeginInvoke(new Action(Refresh));
            }
            return 0;
        }

        private void Refresh()
        {
            if (_updating || _closed || _palette is null) return;
            _updating = true;
            try
            {
                nint handle = _source is { IsDisposed: false } ? _source.Handle : 0;
                bool wanted = _requested && !_palette.HighContrast && _palette.Opacity < .995 &&
                    _window.IsVisible && NativeBackdrop.IsAvailable;
                if (handle == 0 || !wanted || !TryBounds(out Rect bounds))
                { Disable(handle); }
                else if (!_active || bounds != _lastBounds || _palette.Surface != _lastTint)
                {
                    if (NativeBackdrop.Enable(handle, _palette.Surface) && NativeBackdrop.Clip(handle, bounds,
                        _surface.CornerRadius.TopLeft * VisualTreeHelper.GetDpi(_surface).DpiScaleX))
                    {
                        _active = _clipped = true; _lastBounds = bounds; _lastTint = _palette.Surface;
                    }
                    else { NativeBackdrop.Disable(handle); Disable(handle); }
                }
                GlassBubble.Apply(ReadablePalette(_palette, _requested, _active), _surface);
            }
            finally { _updating = false; }
        }

        private bool TryBounds(out Rect bounds)
        {
            bounds = Rect.Empty;
            if (_surface.ActualWidth <= 0 || _surface.ActualHeight <= 0 || _source?.CompositionTarget is null) return false;
            try
            {
                Rect logical = _surface.TransformToAncestor(_window).TransformBounds(new Rect(_surface.RenderSize));
                bounds = Rect.Transform(logical, _source.CompositionTarget.TransformToDevice);
                return !bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0;
            }
            catch (InvalidOperationException) { return false; }
        }

        private void Disable(nint handle)
        {
            if (handle != 0)
            {
                if (_active) NativeBackdrop.Disable(handle);
                if (_clipped) NativeBackdrop.ClearClip(handle);
            }
            _active = _clipped = false; _lastBounds = Rect.Empty;
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _closed = true;
            if (_source is { IsDisposed: false })
            { Disable(_source.Handle); _source.RemoveHook(OnMessage); }
            _source = null;
            _window.SourceInitialized -= OnSourceInitialized;
            _window.IsVisibleChanged -= OnVisibilityChanged;
            _window.Closed -= OnClosed;
            _surface.SizeChanged -= OnSizeChanged;
            Backdrops.Remove(_window);
        }
    }

    private static class NativeBackdrop
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Accent { public int State, Flags; public uint Tint; public int Animation; }
        [StructLayout(LayoutKind.Sequential)]
        private struct Attribute { public int Kind; public nint Data; public nuint Size; }
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private delegate bool SetComposition(nint window, ref Attribute attribute);
        private static readonly SetComposition? SetAttribute = ResolveEntryPoint();

        internal static bool IsAvailable
        {
            get
            {
                if (SetAttribute is null || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763) ||
                    SystemParameters.HighContrast || SystemParameters.IsRemoteSession) return false;
                try
                {
                    if (DwmIsCompositionEnabled(out bool enabled) != 0 || !enabled) return false;
                    using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                    return key?.GetValue("EnableTransparency") is not int value || value != 0;
                }
                catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or
                    System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException) { return false; }
            }
        }

        private static SetComposition? ResolveEntryPoint()
        {
            // This compatibility entry point is required by layered WPF windows;
            // DWM's system backdrop attribute targets a different window model.
            // Resolve the optional user32 export rather than assuming it exists.
            nint module = GetModuleHandle("user32.dll");
            nint address = module == 0 ? 0 : GetProcAddress(module, "SetWindowCompositionAttribute");
            return address == 0 ? null : Marshal.GetDelegateForFunctionPointer<SetComposition>(address);
        }

        internal static bool Enable(nint window, Color tint) => Set(window, new Accent
        {
            State = 4, Flags = 2,
            // Minimal native tint; the existing WPF surface controls opacity.
            Tint = 0x01000000u | (uint)(tint.B << 16 | tint.G << 8 | tint.R)
        });
        internal static void Disable(nint window) => Set(window, new Accent());
        private static bool Set(nint window, Accent accent)
        {
            if (window == 0 || SetAttribute is null) return false;
            nint memory = Marshal.AllocHGlobal(Marshal.SizeOf<Accent>());
            try
            {
                Marshal.StructureToPtr(accent, memory, false);
                var attribute = new Attribute { Kind = 19, Data = memory, Size = (nuint)Marshal.SizeOf<Accent>() };
                return SetAttribute(window, ref attribute);
            }
            finally { Marshal.FreeHGlobal(memory); }
        }

        internal static bool Clip(nint window, Rect rect, double radius)
        {
            nint region = CreateRoundRectRgn((int)Math.Floor(rect.Left), (int)Math.Floor(rect.Top),
                (int)Math.Ceiling(rect.Right) + 1, (int)Math.Ceiling(rect.Bottom) + 1,
                (int)Math.Ceiling(radius * 2), (int)Math.Ceiling(radius * 2));
            if (region == 0) return false;
            if (SetWindowRgn(window, region, true) != 0) return true; // Windows owns the region now.
            DeleteObject(region); return false;
        }
        internal static void ClearClip(nint window) => SetWindowRgn(window, 0, true);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)] private static extern nint GetProcAddress(nint module, string name);
        [DllImport("dwmapi.dll")] private static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);
        [DllImport("gdi32.dll")] private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
        [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint handle);
        [DllImport("user32.dll")] private static extern int SetWindowRgn(nint window, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    }
}
