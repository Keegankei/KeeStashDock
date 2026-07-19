namespace KeeStashDock.Infrastructure.Storage;

/// <summary>
/// Resolves the portable "Data" directory layout.
///
/// Portable contract
/// -----------------
/// KeeStashDock is shipped as a green/portable app: everything the user owns
/// (database, settings, logs, backups, and reserved extension data) lives in
/// one Data\ folder next to the executable, so copying the app folder migrates
/// the whole installation.
///
/// Fallback rule
/// -------------
/// If the executable sits in a non-writable location (e.g. someone drops it
/// into C:\Program Files), we fall back to %AppData%\KeeStashDock and log a
/// notice, instead of crashing on the first write.
/// </summary>
public static class PortablePaths
{
    private static readonly Lazy<string> _dataRoot = new(ResolveDataRoot);

    /// <summary>Root of all user data. Guaranteed writable and existing.</summary>
    public static string DataRoot => _dataRoot.Value;

    public static string DatabaseFile => Path.Combine(DataRoot, "keestashdock.db");
    public static string SettingsFile => Path.Combine(DataRoot, "settings.json");
    public static string DraftFile    => Path.Combine(DataRoot, "draft.txt");
    public static string LogsDir      => EnsureDir(Path.Combine(DataRoot, "Logs"));
    public static string BackupsDir   => EnsureDir(Path.Combine(DataRoot, "Backups"));
    public static string PluginsDir   => EnsureDir(Path.Combine(DataRoot, "Plugins")); // reserved

    /// <summary>True when we had to fall back to %AppData% (surfaced in Settings &gt; About).</summary>
    public static bool IsAppDataFallback { get; private set; }

    private static string ResolveDataRoot()
    {
        string exeDir = AppContext.BaseDirectory;
        string portable = Path.Combine(exeDir, "Data");
        try
        {
            Directory.CreateDirectory(portable);
            // Probe actual writability - Directory.CreateDirectory may succeed
            // on a folder where file creation is still denied by ACLs.
            string probe = Path.Combine(portable, ".write_probe");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return portable;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            IsAppDataFallback = true;
            string appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "KeeStashDock");
            Directory.CreateDirectory(appData);
            return appData;
        }
    }

    private static string EnsureDir(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
