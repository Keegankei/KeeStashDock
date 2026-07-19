using System.Windows.Media;
using KeeStashDock.Domain.Entities;

namespace KeeStashDock.Desktop;

/// <summary>
/// The named text-color palettes (VSCode-theme style) shared by the quick
/// panel's foreground engine and the selection bubble. Each palette is a
/// (primary, muted) pair; picking one is an explicit "this exact color
/// everywhere" decision, unlike Auto which keeps per-surface contrast logic.
/// </summary>
public static class TextPalettes
{
    public static readonly System.Collections.Generic.Dictionary<string, (Color P, Color M)>
        Presets = new()
    {
        ["Ivory"]    = (Color.FromRgb(0xF8, 0xF4, 0xE9), Color.FromRgb(0xD8, 0xD1, 0xBE)),
        ["Graphite"] = (Color.FromRgb(0x23, 0x27, 0x2E), Color.FromRgb(0x4B, 0x55, 0x63)),
        ["Amber"]    = (Color.FromRgb(0xFF, 0xC6, 0x6D), Color.FromRgb(0xC9, 0xA2, 0x5B)),
        ["Mint"]     = (Color.FromRgb(0xA8, 0xE6, 0xCF), Color.FromRgb(0x80, 0xBF, 0xA5)),
        ["Sky"]      = (Color.FromRgb(0x8F, 0xD3, 0xFF), Color.FromRgb(0x6C, 0xA7, 0xCC)),
        ["Rose"]     = (Color.FromRgb(0xFF, 0xB3, 0xC1), Color.FromRgb(0xCC, 0x8B, 0x98)),
        ["Lavender"] = (Color.FromRgb(0xC9, 0xB8, 0xF5), Color.FromRgb(0xA3, 0x96, 0xC7)),
        ["Coral"]    = (Color.FromRgb(0xFF, 0x9E, 0x80), Color.FromRgb(0xCC, 0x81, 0x68)),
        ["Teal"]     = (Color.FromRgb(0x7F, 0xD8, 0xC8), Color.FromRgb(0x66, 0xAB, 0x9F)),
        ["Slate"]    = (Color.FromRgb(0x9F, 0xB2, 0xC8), Color.FromRgb(0x7E, 0x8C, 0xA0)),
    };

    /// <summary>Resolves a preset/custom palette from the settings. False
    /// for "Auto" (per-surface auto-contrast stays in charge). The retired
    /// "Light"/"Dark" values from older settings.json files alias to the
    /// closest presets (Ivory / Graphite) instead of silently degrading.</summary>
    public static bool TryResolve(AppSettings cfg, out Color primary, out Color muted)
    {
        string mode = cfg.PanelTextColorMode switch
        {
            "Light" => "Ivory",
            "Dark" => "Graphite",
            _ => cfg.PanelTextColorMode,
        };

        if (Presets.TryGetValue(mode, out var pair))
        {
            (primary, muted) = pair;
            return true;
        }
        if (mode == "Custom")
        {
            try
            {
                primary = (Color)ColorConverter.ConvertFromString(
                    cfg.PanelTextCustomColor?.Trim() ?? "#FFFFFF");
            }
            catch { primary = Colors.White; }   // bad hex -> safe fallback
            // Muted = primary pulled 35% toward mid-gray (keeps the hue).
            static byte Mix(byte c) => (byte)(c + (0x88 - c) * 0.35);
            muted = Color.FromRgb(Mix(primary.R), Mix(primary.G), Mix(primary.B));
            return true;
        }
        primary = muted = default;
        return false;
    }
}
