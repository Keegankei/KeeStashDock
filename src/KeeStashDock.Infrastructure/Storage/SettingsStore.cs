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
    /// settings-derived visuals (background image, frost, glow) subscribe to
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
                Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
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

        Current.CollapseDelaySeconds = NormalizePositiveDouble(
            Current.CollapseDelaySeconds, 2.0, 0.5, 60.0);
        Current.ExpandDelaySeconds = NormalizePositiveDouble(
            Current.ExpandDelaySeconds, 0.1, 0.1, 10.0);
        Current.PanelWidth = NormalizePositiveDouble(Current.PanelWidth, 320, 200, 2000);
        Current.BackgroundOverlayOpacity = NormalizePositiveDouble(
            Current.BackgroundOverlayOpacity, 0.30, 0.20, 1.0);
        Current.FrostedGlassRadius = NormalizePositiveDouble(
            Current.FrostedGlassRadius, 4, 1, 18);
        Current.EdgeGlowStrength = NormalizePositiveDouble(
            Current.EdgeGlowStrength, 18, 4, 30);
        Current.EdgeGlowBrightness = NormalizePositiveDouble(
            Current.EdgeGlowBrightness, 0.30, 0.10, 1.0);
        Current.CardOpacity = NormalizePositiveDouble(Current.CardOpacity, 0.9, 0.1, 1.0);
        Current.BubbleOpacity = NormalizePositiveDouble(Current.BubbleOpacity, 1.0, 0.5, 1.0);

        Current.PanelPosX = NormalizeOptionalFinite(Current.PanelPosX);
        Current.PanelPosY = NormalizeOptionalFinite(Current.PanelPosY);
        Current.PanelUserWidth = NormalizeOptionalPositive(
            Current.PanelUserWidth, 160, 4000);
        Current.PanelUserHeight = NormalizeOptionalPositive(
            Current.PanelUserHeight, 120, 4000);

        if (string.IsNullOrWhiteSpace(Current.Language))
            Current.Language = "en-US";
        Current.TranslationApiKey ??= string.Empty;
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
