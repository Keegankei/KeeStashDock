using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KeeStashDock.Domain.Entities;

namespace KeeStashDock.Desktop;

/// <summary>
/// Shared palette for selection actions, translation cards and confirmations.
/// Default uses fixed light surfaces; FollowTheme uses the active theme's
/// bubble colors. Text stays crisp on every surface.
/// </summary>
public static class GlassBubble
{
    /// <summary>Styles the shared shell; the fixed default stays light even on a dark desktop.</summary>
    public static Color Apply(AppSettings cfg, Border surface)
        => Apply(Resolve(cfg), surface);

    internal static Color Apply(BubblePalette palette, Border surface)
    {
        surface.BorderThickness = new Thickness(1);
        surface.BorderBrush = new SolidColorBrush(palette.Border);
        if (palette.HighContrast)
        {
            surface.Background = SystemColors.WindowBrush;
            surface.Effect = null;
            return palette.Text;
        }
        (byte top, byte bottom) = SurfaceAlphas(palette.Opacity);
        Color upper = Mix(palette.Surface, palette.Highlight, .12);
        Color lower = Mix(palette.Surface, palette.Accent, .025);
        var background = new LinearGradientBrush(
            Color.FromArgb(top, upper.R, upper.G, upper.B),
            Color.FromArgb(bottom, lower.R, lower.G, lower.B), 105);
        background.Freeze();
        surface.Background = background;
        surface.Effect = null;
        return palette.Text;
    }

    internal static BubblePalette Resolve(AppSettings saved)
    {
        if (SystemParameters.HighContrast)
            return new(SystemColors.WindowTextColor, SystemColors.WindowTextColor, SystemColors.HighlightColor,
                SystemColors.HighlightTextColor, SystemColors.WindowColor, SystemColors.WindowColor,
                SystemColors.WindowTextColor, SystemColors.HighlightColor, SystemColors.HighlightColor,
                SystemColors.WindowColor, true, 1);

        AppSettings cfg = saved.SelectionBubbleStyle == "FollowTheme" ? ThemeManager.CreatePanelAppearanceSettings(saved) : saved;
        double opacity = double.IsFinite(cfg.BubbleOpacity) ? Math.Clamp(cfg.BubbleOpacity, .5, 1) : 1;
        Color Read(string key, Color fallback) => Application.Current?.TryFindResource(key) is SolidColorBrush brush ? brush.Color : fallback;
        if (cfg.SelectionBubbleStyle == "Default")
            return new(Color.FromRgb(36, 41, 54), Color.FromRgb(96, 105, 120), Color.FromRgb(66, 86, 126), Colors.White,
                Color.FromRgb(250, 250, 248), Colors.White, Color.FromRgb(216, 220, 228),
                Color.FromRgb(232, 237, 246), Color.FromRgb(214, 223, 239), Colors.White,
                false, opacity);

        if (cfg.SelectionBubbleStyle == "FollowTheme" && ThemeManager.ActiveVariant is not null)
        {
            Color text = Read("B.BubbleText", Read("B.TextPrimary", Colors.Black));
            Color background = Read("B.BubbleBg", Read("B.CardBg", Colors.White));
            Color accent = Read("B.BubbleAccent", Read("B.Accent", text));
            Color hover = Read("B.BubbleAccentSoft", Mix(background, accent, .16));
            return new(text, Read("B.BubbleMuted", Mix(text, background, .3)), accent,
                Read("B.OnAccent", Luminance(accent) > .6 ? Colors.Black : Colors.White),
                background, Mix(background, Luminance(text) > .5 ? Colors.Black : Colors.White, .15),
                Read("B.BubbleBorder", Read("B.Border", Mix(text, background, .65))), hover, Mix(hover, accent, .13),
                Read("B.BubbleHighlight", Colors.White), false, opacity);
        }

        Color ink = TextPalettes.Presets.TryGetValue(cfg.SelectionBubbleStyle, out var pinned) ? pinned.P : ResolveTextColor(cfg);
        RgbToHsv(ink, out double h, out double s, out _);
        bool dark = Luminance(ink) >= .5;
        Color surface = HsvToRgb(h, Math.Min(s, .55) * .45, dark ? .19 : .975);
        Color accentInk = dark ? Mix(ink, Colors.White, .2) : Mix(ink, Colors.Black, .1);
        Color wash = Mix(surface, accentInk, .12);
        return new(ink, Mix(ink, surface, .26), accentInk, surface, surface,
            Mix(surface, dark ? Colors.Black : Colors.White, .18), Mix(surface, ink, .22), wash,
            Mix(surface, accentInk, .22), dark ? Mix(surface, ink, .25) : Colors.White,
            false, opacity);
    }

    internal static Color Mix(Color first, Color second, double amount) => Color.FromRgb(
        (byte)Math.Round(first.R + (second.R - first.R) * amount),
        (byte)Math.Round(first.G + (second.G - first.G) * amount),
        (byte)Math.Round(first.B + (second.B - first.B) * amount));
    /// <summary>Maps the user opacity (0.5..1.0) to the glass gradient's two
    /// alpha stops. At 1.0 both stops are FULLY opaque - the guaranteed fix
    /// when the desktop bleeds through the bubble text.</summary>
    private static (byte Top, byte Bottom) SurfaceAlphas(double opacity)
    {
        double op = Math.Clamp(opacity, 0.5, 1.0);
        byte top = (byte)Math.Round(255 * op);
        byte bot = (byte)Math.Round(255 * Math.Max(0.0, op - 0.055));
        if (op >= 0.995) bot = 255;   // "opaque" must mean opaque
        return (top, bot);
    }

    /// <summary>Text color per the current settings; theme foreground when
    /// the scheme is Auto (a bubble has no wallpaper to sample).</summary>
    public static Color ResolveTextColor(AppSettings cfg)
    {
        if (TextPalettes.TryResolve(cfg, out Color text, out _)) return text;
        return ((SolidColorBrush)Application.Current
            .FindResource("B.TextPrimary")).Color;
    }

    /// <summary>Dresses a Border as bubble glass matched to a text color:
    /// tinted translucent vertical gradient and hairline border.
    /// The caller keeps ownership of radius/padding/margin.</summary>
    public static void StyleSurface(Border surface, Color text,
                                    double opacity = 0.88)
    {
        RgbToHsv(text, out double h, out double s, out _);
        bool lightText = Luminance(text) >= 0.5;
        double bs = Math.Min(s, 0.55) * 0.45;
        Color bgTop = HsvToRgb(h, bs, lightText ? 0.24 : 0.985);
        Color bgBot = HsvToRgb(h, bs, lightText ? 0.15 : 0.925);
        (byte aTop, byte aBot) = SurfaceAlphas(opacity);
        surface.Background = new LinearGradientBrush(
            Color.FromArgb(aTop, bgTop.R, bgTop.G, bgTop.B),
            Color.FromArgb(aBot, bgBot.R, bgBot.G, bgBot.B), 90.0);
        surface.BorderBrush = new SolidColorBrush(lightText
            ? Color.FromArgb(0x5E, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x38, 0x00, 0x00, 0x00));
        surface.BorderThickness = new Thickness(1);
        surface.Effect = null;
    }

    public static double Luminance(Color c) =>
        (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    public static void RgbToHsv(Color c, out double h, out double s, out double v)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double d = max - min;
        v = max;
        s = max <= 0 ? 0 : d / max;
        if (d <= 0) { h = 0; return; }
        if (max == r) h = 60 * (((g - b) / d) % 6);
        else if (max == g) h = 60 * ((b - r) / d + 2);
        else h = 60 * ((r - g) / d + 4);
        if (h < 0) h += 360;
    }

    public static Color HsvToRgb(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = v - c;
        (double r, double g, double b) = ((int)(h / 60) % 6) switch
        {
            0 => (c, x, 0.0), 1 => (x, c, 0.0), 2 => (0.0, c, x),
            3 => (0.0, x, c), 4 => (x, 0.0, c), _ => (c, 0.0, x),
        };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255),
                             (byte)((b + m) * 255));
    }
}
