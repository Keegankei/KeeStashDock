using System.Text.Json;
using KeeStashDock.Domain.Entities;

namespace KeeStashDock.Infrastructure.Storage;

/// <summary>
/// JSON-backed settings persistence with atomic writes.
///
/// Atomicity: settings are written to a .tmp file first and then swapped in
/// with File.Replace / File.Move, so a crash mid-write can never leave a
/// truncated settings.json behind (the portable folder is the user's only
/// copy - it must be treated as precious).
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly object _gate = new();
    private readonly string _settingsFile;

    public SettingsStore() : this(PortablePaths.SettingsFile)
    {
    }

    public SettingsStore(string settingsFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFile);
        _settingsFile = Path.GetFullPath(settingsFile);
    }

    public AppSettings Current { get; private set; } = new();

    /// <summary>Raised after every successful Save(). Views that render
    /// settings-derived visuals (background image, frost, opacity) subscribe to
    /// hot-apply changes; they must be cheap/no-op when nothing they care
    /// about actually changed, because Save() also fires for unrelated
    /// writes such as panel geometry persistence.</summary>
    public event Action? Changed;

    public void Load()
    {
        try
        {
            if (File.Exists(_settingsFile))
            {
                string json = File.ReadAllText(_settingsFile);
                using JsonDocument document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("autoBackupIntervalMinutes", out JsonElement minutes) &&
                    (minutes.ValueKind != JsonValueKind.Number || !minutes.TryGetInt32(out _)))
                {
                    var normalized = System.Text.Json.Nodes.JsonNode.Parse(json)!;
                    normalized["autoBackupIntervalMinutes"] = AppSettings.DefaultAutoBackupIntervalMinutes;
                    json = normalized.ToJsonString();
                }
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("fontSizePercent", out JsonElement fontSize) &&
                    (fontSize.ValueKind != JsonValueKind.Number || !fontSize.TryGetInt32(out _)))
                {
                    // A malformed new preference must not discard unrelated saved settings.
                    var normalized = System.Text.Json.Nodes.JsonNode.Parse(json)!;
                    normalized["fontSizePercent"] = AppSettings.DefaultFontSizePercent;
                    json = normalized.ToJsonString();
                }
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var normalized = System.Text.Json.Nodes.JsonNode.Parse(json)!;
                    if (document.RootElement.TryGetProperty("themePresetId", out JsonElement preset) && preset.ValueKind != JsonValueKind.String)
                        normalized["themePresetId"] = "";
                    foreach (string name in new[] { "themeUseCustomBackground", "themeUseCustomSettingsBackground", "themeAppearanceCustomized", "settingsBackgroundOverlayCustomized", "settingsAppearanceCustomized" })
                        if (document.RootElement.TryGetProperty(name, out JsonElement flag) && flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                            normalized[name] = name == "themeUseCustomBackground";
                    if (document.RootElement.TryGetProperty("settingsBackgroundImagePath", out JsonElement settingsImage) &&
                        settingsImage.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                        normalized["settingsBackgroundImagePath"] = null;
                    if (document.RootElement.TryGetProperty("settingsBackgroundOverlayOpacity", out JsonElement settingsOverlay) &&
                        (settingsOverlay.ValueKind != JsonValueKind.Number || !settingsOverlay.TryGetDouble(out _)))
                        normalized["settingsBackgroundOverlayOpacity"] = AppSettings.DefaultBackgroundOverlayOpacity;
                    json = normalized.ToJsonString();
                }
                Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    !document.RootElement.TryGetProperty("autoBackupIntervalMinutes", out _))
                {
                    // Read the raw legacy value before its compatibility field
                    // is clamped. Invalid intervals use the documented default.
                    int hours = Current.AutoBackupIntervalHours;
                    Current.AutoBackupIntervalMinutes = hours is > 0 and <= AppSettings.MaxAutoBackupIntervalHours
                        ? hours * 60 : AppSettings.DefaultAutoBackupIntervalMinutes;
                }
            }
        }
        catch
        {
            // Corrupt settings must never brick the app: fall back to defaults.
            Current = new AppSettings();
        }

        ValidateAndNormalize();
    }

    public void Save()
    {
        lock (_gate)
        {
            ValidateAndNormalize();
            string? directory = Path.GetDirectoryName(_settingsFile);
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            string tmp = _settingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, JsonOpts));
            if (File.Exists(_settingsFile))
                File.Replace(tmp, _settingsFile, destinationBackupFileName: null);
            else
                File.Move(tmp, _settingsFile);
        }
        Changed?.Invoke();
    }

    public void ValidateAndNormalize()
    {
        if (Current.ThemePresetId is null || Current.ThemePresetId.Length > 100 ||
            (Current.ThemePresetId.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(Current.ThemePresetId,
                @"^(builtin|custom):[a-z0-9][a-z0-9-]{0,63}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)))
            Current.ThemePresetId = "";
        Current.FontSizePercent = Math.Clamp(Current.FontSizePercent,
            AppSettings.MinFontSizePercent, AppSettings.MaxFontSizePercent);

        if (Current.ScreenshotHotkeyModifiers == 0 ||
            (Current.ScreenshotHotkeyModifiers & ~0x0fu) != 0 ||
            Current.ScreenshotHotkeyVirtualKey is 0 or > 0xfe or 0x10 or 0x11 or 0x12 or 0x5b or 0x5c or >= 0xa0 and <= 0xa5)
        {
            Current.ScreenshotHotkeyModifiers = AppSettings.DefaultScreenshotHotkeyModifiers;
            Current.ScreenshotHotkeyVirtualKey = AppSettings.DefaultScreenshotHotkeyVirtualKey;
        }
        // An existing user's panel shortcut takes priority over the new action.
        if (Current.ScreenshotHotkeyModifiers == Current.HotkeyModifiers &&
            Current.ScreenshotHotkeyVirtualKey == Current.HotkeyVirtualKey)
            Current.ScreenshotHotkeyEnabled = false;

        Current.TrashRetentionDays = NormalizePositiveInt(
            Current.TrashRetentionDays,
            AppSettings.DefaultTrashRetentionDays,
            AppSettings.MaxTrashRetentionDays);
        Current.BackupKeepCount = NormalizePositiveInt(
            Current.BackupKeepCount,
            AppSettings.DefaultBackupKeepCount,
            AppSettings.MaxBackupKeepCount);
        Current.AutoBackupIntervalHours = NormalizePositiveInt(
            Current.AutoBackupIntervalHours,
            AppSettings.DefaultAutoBackupIntervalHours,
            AppSettings.MaxAutoBackupIntervalHours);
        if (Current.AutoBackupIntervalMinutes is < 15 or > AppSettings.MaxAutoBackupIntervalMinutes)
            Current.AutoBackupIntervalMinutes = AppSettings.DefaultAutoBackupIntervalMinutes;

        Current.CollapseDelaySeconds = NormalizePositiveDouble(
            Current.CollapseDelaySeconds, 2.0, 0.5, 60.0);
        Current.ExpandDelaySeconds = NormalizePositiveDouble(
            Current.ExpandDelaySeconds, 0.1, 0.1, 10.0);
        Current.PanelWidth = NormalizePositiveDouble(Current.PanelWidth, 320, 200, 2000);
        Current.BackgroundOverlayOpacity = NormalizePositiveDouble(
            Current.BackgroundOverlayOpacity, AppSettings.DefaultBackgroundOverlayOpacity, 0.20, 1.0);
        Current.SettingsBackgroundOverlayOpacity = double.IsFinite(Current.SettingsBackgroundOverlayOpacity)
            ? Math.Clamp(Current.SettingsBackgroundOverlayOpacity, 0, 1) : AppSettings.DefaultBackgroundOverlayOpacity;
        Current.FrostedGlassRadius = NormalizePositiveDouble(
            Current.FrostedGlassRadius, 4, 1, 18);
        Current.CardOpacity = NormalizePositiveDouble(Current.CardOpacity, 0.9, 0.1, 1.0);
        Current.SettingsSurfaceOpacity = NormalizePositiveDouble(Current.SettingsSurfaceOpacity, 1.0, 0.1, 1.0);
        Current.SettingsFrostedGlassRadius = NormalizePositiveDouble(Current.SettingsFrostedGlassRadius, 8, 1, 18);
        Current.BubbleOpacity = NormalizePositiveDouble(Current.BubbleOpacity, 1.0, 0.5, 1.0);

        Current.PanelPosX = NormalizeOptionalFinite(Current.PanelPosX);
        Current.PanelPosY = NormalizeOptionalFinite(Current.PanelPosY);
        // A physical location is meaningful only as a complete pair. Invalid
        // monitor coordinates fall back to the existing safe DIP placement.
        if (Current.PanelDevicePosX is not (>= -100_000 and <= 100_000) ||
            Current.PanelDevicePosY is not (>= -100_000 and <= 100_000))
        {
            Current.PanelDevicePosX = null;
            Current.PanelDevicePosY = null;
        }
        Current.PanelUserWidth = NormalizeOptionalPositive(
            Current.PanelUserWidth, 160, 4000);
        Current.PanelUserHeight = NormalizeOptionalPositive(
            Current.PanelUserHeight, 120, 4000);

        if (Current.FloatingInspectionGesture is not ("Right" or "Middle" or "Custom"))
            Current.FloatingInspectionGesture = "Right";
        if (Current.FloatingInspectionButton is not ("Left" or "Right" or "Middle" or "XButton1" or "XButton2"))
            Current.FloatingInspectionButton = "Left";
        if (Current.FloatingInspectionModifiers is not ("None" or "Control" or "Alt" or "Shift" or
            "Control+Alt" or "Control+Shift" or "Alt+Shift" or "Control+Alt+Shift"))
            Current.FloatingInspectionModifiers = "Alt";

        if (string.IsNullOrWhiteSpace(Current.Language))
            Current.Language = "en-US";
        Current.TranslationApiKey ??= string.Empty;
        Current.TranslationTargetLanguage = TranslationTargets.Normalize(Current.TranslationTargetLanguage);
        Current.TranslationAppId ??= string.Empty;
        Current.BaiduTranslationApiKey ??= string.Empty;
        Current.AliyunAccessKeyId ??= string.Empty;
        Current.AliyunAccessKeySecret ??= string.Empty;
        if (string.IsNullOrWhiteSpace(Current.OfflineTranslationEndpoint))
            Current.OfflineTranslationEndpoint = AppSettings.DefaultOfflineTranslationEndpoint;
        if (string.IsNullOrWhiteSpace(Current.TranslationEngine))
            Current.TranslationEngine = AppSettings.DefaultTranslationEngine;
        if (string.IsNullOrWhiteSpace(Current.SelectionBubbleStyle))
            Current.SelectionBubbleStyle = AppSettings.DefaultSelectionBubbleStyle;
    }

    private static int NormalizePositiveInt(int value, int defaultValue, int maximum)
        => value <= 0 ? defaultValue : Math.Min(value, maximum);

    private static double NormalizePositiveDouble(
        double value,
        double defaultValue,
        double minimum,
        double maximum)
        => !double.IsFinite(value) || value <= 0
            ? defaultValue
            : Math.Clamp(value, minimum, maximum);

    private static double? NormalizeOptionalFinite(double? value)
        => value is double number && double.IsFinite(number) && Math.Abs(number) <= 100_000
            ? number
            : null;

    private static double? NormalizeOptionalPositive(
        double? value,
        double minimum,
        double maximum)
        => value is double number && double.IsFinite(number) && number > 0
            ? Math.Clamp(number, minimum, maximum)
            : null;
}
