using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using KeeStashDock.Domain.Entities;

namespace KeeStashDock.Desktop;

/// <summary>
/// The ONE place that knows what a KeeStashDock "glass bubble" looks like.
/// Shared by the selection bubble, the translation result card, and the
/// MiniToast confirmations, so they always read as the same family:
///   - text color follows the panel text-color setting (preset/custom
///     palette applies directly; Auto falls back to the theme foreground);
///   - the surface is DERIVED from the text color: same hue, value pushed
///     to the opposite end ("comfort zone"), so any palette lands at
///     readable contrast - light text gets deep tinted glass, dark text a
///     pale one;
///   - the glow is a blurred TWIN stacked UNDER crisp pixel-snapped text,
///     never an effect ON the text (that rasterizes and blurs it).
/// </summary>
public static class GlassBubble
{
    /// <summary>One-call dress: styles the surface per the configured
    /// bubble style and returns the matching text color.
    ///   "Default"     - a plain, soft monochrome: warm off-WHITE glass
    ///                   with soft near-black text (white surface, black
    ///                   ink). Deliberately understated and FIXED - it
    ///                   never chases the theme or palette, so the bubble
    ///                   stays quietly legible on any background;
    ///   "FollowTheme" - glass derived from the text-color scheme.</summary>
    public static Color Apply(AppSettings cfg, Border surface)
    {
        // Named palette pinned DIRECTLY on the bubble family, independent of
        // the panel's text-color setting - "the bubble is always ivory glass
        // no matter what the panel wears".
        if (TextPalettes.Presets.TryGetValue(cfg.SelectionBubbleStyle,
                                             out var pinned))
        {
            StyleSurface(surface, pinned.P, cfg.BubbleOpacity);
            if (!cfg.BubbleShadow) surface.Effect = null;
            return pinned.P;
        }
        if (cfg.SelectionBubbleStyle == "Default")
        {
            // Soft black-and-white = WHITE surface, BLACK ink - with both
            // pulled back from the pure ends (warm off-white, not #FFF;
            // soft charcoal, not #000) so the combo reads calm, not stark.
            (byte aTop, byte aBot) = SurfaceAlphas(cfg.BubbleOpacity);
            surface.Background = new LinearGradientBrush(
                Color.FromArgb(aTop, 0xFB, 0xFA, 0xF8),
                Color.FromArgb(aBot, 0xEF, 0xEE, 0xEB), 90.0);
            surface.BorderBrush = new SolidColorBrush(
                Color.FromArgb(0x30, 0x00, 0x00, 0x00));
            surface.BorderThickness = new Thickness(1);
            surface.Effect = cfg.BubbleShadow ? MakeShadow(0.24) : null;
            return Color.FromRgb(0x2C, 0x2C, 0x2F);
        }
        Color text = ResolveTextColor(cfg);
        StyleSurface(surface, text, cfg.BubbleOpacity);
        if (!cfg.BubbleShadow) surface.Effect = null;
        return text;
    }

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

    /// <summary>Applies the configured glow toggle to a glow twin created
    /// by <see cref="LayerGlow"/>. Hidden (not Collapsed) keeps the layout
    /// byte-identical either way, so toggling never shifts the bubble.</summary>
    public static void SetGlowEnabled(UIElement glowTwin, bool on) =>
        glowTwin.Visibility = on ? Visibility.Visible : Visibility.Hidden;

    private static DropShadowEffect MakeShadow(double opacity) => new()
    {
        ShadowDepth = 2,
        BlurRadius = 12,
        Opacity = opacity,
        Color = Colors.Black,
    };

    /// <summary>Text color per the current settings; theme foreground when
    /// the scheme is Auto (a bubble has no wallpaper to sample).</summary>
    public static Color ResolveTextColor(AppSettings cfg)
    {
        if (TextPalettes.TryResolve(cfg, out Color text, out _)) return text;
        return ((SolidColorBrush)Application.Current
            .FindResource("B.TextPrimary")).Color;
    }

    /// <summary>Dresses a Border as bubble glass matched to a text color:
    /// tinted translucent vertical gradient, hairline border, soft lift
    /// shadow. The caller keeps ownership of radius/padding/margin.</summary>
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
        surface.Effect = new DropShadowEffect
        {
            ShadowDepth = 2,
            BlurRadius = 12,
            Opacity = 0.35,
            Color = Colors.Black,
        };
    }

    /// <summary>Stacks a blurred glow twin UNDER a crisp copy. The crisp
    /// element gets Display (pixel-snapped) text formatting; the twin gets
    /// all the blur and never hit-tests.</summary>
    public static Grid LayerGlow(FrameworkElement crisp, FrameworkElement glowTwin,
                                 double radius = 6, double opacity = 0.65)
    {
        TextOptions.SetTextFormattingMode(crisp, TextFormattingMode.Display);
        glowTwin.Effect = new BlurEffect { Radius = radius };
        glowTwin.Opacity = opacity;
        glowTwin.IsHitTestVisible = false;
        var g = new Grid();
        g.Children.Add(glowTwin);   // glow underneath
        g.Children.Add(crisp);      // crisp copy on top
        return g;
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
