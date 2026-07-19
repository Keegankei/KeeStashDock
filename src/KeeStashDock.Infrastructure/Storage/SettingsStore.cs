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
            if (File.Exists(PortablePaths.SettingsFile))
            {
                string json = File.ReadAllText(PortablePaths.SettingsFile);
                Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
            }
        }
        catch
        {
            // Corrupt settings must never brick the app: fall back to defaults.
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            string tmp = PortablePaths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, JsonOpts));
            if (File.Exists(PortablePaths.SettingsFile))
                File.Replace(tmp, PortablePaths.SettingsFile, destinationBackupFileName: null);
            else
                File.Move(tmp, PortablePaths.SettingsFile);
        }
        Changed?.Invoke();
    }
}
