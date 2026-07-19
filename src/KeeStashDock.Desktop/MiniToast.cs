using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KeeStashDock.Infrastructure.Storage;

namespace KeeStashDock.Desktop;

/// <summary>
/// Tiny transient confirmation bubble near the cursor for actions such as
/// capture and copy. It is styled by the shared <see cref="GlassBubble"/>
/// builder so it matches the selection bubble it usually follows:
/// settings-driven text color,
/// surface derived from it, crisp text over a blurred glow twin.
/// Pure code - no XAML - lives outside any product window, never activates.
/// </summary>
public static class MiniToast
{
    /// <summary>Wired once at startup so the toast can read the text-color
    /// scheme; falls back to plain theme colors when absent (early boot).</summary>
    public static SettingsStore? Settings { get; set; }

    public static void Show(string message)
    {
        TextBlock Make() => new()
        {
            Text = message,
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var crisp = Make();
        var glow = Make();

        var surface = new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 7, 14, 7),
            Child = GlassBubble.LayerGlow(crisp, glow),
            // Transparent room for the outer drop shadow to render into.
            Margin = new Thickness(10),
        };

        Color text;
        if (Settings is null)   // early boot: plain theme colors
        {
            text = ((SolidColorBrush)Application.Current
                .FindResource("B.TextPrimary")).Color;
            GlassBubble.StyleSurface(surface, text);
        }
        else
        {
            text = GlassBubble.Apply(Settings.Current, surface);
        }
        var fg = new SolidColorBrush(text);
        crisp.Foreground = fg;
        glow.Foreground = fg;
        GlassBubble.SetGlowEnabled(glow,
            Settings?.Current.BubbleTextGlow ?? true);

        var w = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            SizeToContent = SizeToContent.WidthAndHeight,
            Content = surface,
        };
        Localization.LocalizationService.Instance.ApplyFlowDirection(w);

        // Position just below-right of the cursor, clamped to the work area.
        // Cursor is in device px; convert with the main window's DPI scale
        // (a rough match is fine for a 1.4-second bubble).
        if (Interop.NativeMethods.GetCursorPos(out var pt))
        {
            var wa = SystemParameters.WorkArea;
            double dpi = GetDpiScale();
            w.Left = Math.Min(pt.X / dpi + 14, wa.Right - 200);
            w.Top = Math.Min(pt.Y / dpi + 18, wa.Bottom - 70);
        }

        w.Show();
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300))
        { BeginTime = TimeSpan.FromMilliseconds(1100) };
        fade.Completed += (_, _) => w.Close();
        w.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private static double GetDpiScale()
    {
        var main = Application.Current.MainWindow;
        var t = main is null ? null
            : PresentationSource.FromVisual(main)?.CompositionTarget;
        return t?.TransformToDevice.M11 ?? 1.0;
    }
}
