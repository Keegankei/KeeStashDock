namespace KeeStashDock.Domain.Entities;

/// <summary>Portable, appearance-only theme format. Paths are always relative to a validated theme root.</summary>
public sealed class ThemeManifest
{
    public int FormatVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string MinimumAppVersion { get; set; } = "1.3.0";
    public string Light { get; set; } = "variants/light.json";
    public string Dark { get; set; } = "variants/dark.json";
    public string Source { get; set; } = "SOURCE.md";
}

public sealed class ThemeVariant
{
    public Dictionary<string, string> Colors { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, ThemeBackground> Backgrounds { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Explicitly hides Settings artwork instead of falling back to workspace.</summary>
    public bool HideSettingsBackground { get; set; }
    public double CardOpacity { get; set; } = 0.90;
    public double SettingsSurfaceOpacity { get; set; } = 1;
    public bool SettingsFrostedGlassEnabled { get; set; }
    public double SettingsFrostedGlassRadius { get; set; } = 8;
    public double BubbleOpacity { get; set; } = 1;
    public string BubbleStyle { get; set; } = "FollowTheme";
    public string PanelTextColorMode { get; set; } = "Auto";
    public string PanelTextCustomColor { get; set; } = "#FFFFFF";
}

public sealed class ThemeBackground
{
    public string Path { get; set; } = "";
    public double Opacity { get; set; } = 1;
    public double OverlayOpacity { get; set; } = 0.75;
    public double BlurRadius { get; set; }
    public double FocalX { get; set; } = 0.5;
    public double FocalY { get; set; } = 0.5;
}

public sealed record ThemeScheme(string Key, string Directory, ThemeManifest Manifest, ThemeVariant Light, ThemeVariant Dark);
