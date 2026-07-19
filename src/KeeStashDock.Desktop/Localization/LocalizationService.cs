using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace KeeStashDock.Desktop.Localization;

/// <summary>
/// Hot-switchable localization engine (frozen design: 10 launch languages,
/// live re-render, RTL mirroring, per-language font chain).
///
/// Mechanism
/// ---------
/// * Language packs are flat-key JSON files in Locales\&lt;tag&gt;.json.
///   A reserved "$meta" object carries direction ("ltr"/"rtl"), the native
///   display name and an optional preferred font family.
/// * The service exposes an indexer this[key]; the {loc:Tr Key} markup
///   extension binds to that indexer. Switching languages raises
///   PropertyChanged("Item[]") which makes WPF re-evaluate EVERY such
///   binding at once - the entire UI refreshes with no window restart.
/// * Missing keys fall back to the embedded en-US pack and are logged, so an
///   incomplete community translation degrades gracefully (never blank UI).
/// * FlowDirection of all open windows is flipped when an RTL pack (Arabic)
///   is activated; CultureInfo.CurrentUICulture follows the language so date
///   and number formatting matches user expectation.
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    public static LocalizationService Instance { get; } = new();

    private Dictionary<string, string> _current = new();
    private Dictionary<string, string> _fallback = new();   // en-US baseline
    private readonly HashSet<string> _reportedMissing = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>Raised after a successful switch (windows apply FlowDirection here).</summary>
    public event Action? LanguageChanged;

    public string CurrentLanguage { get; private set; } = "en-US";
    public bool IsRightToLeft { get; private set; }
    public string PreferredFont { get; private set; } = "";

    private static string LocalesDir => Path.Combine(AppContext.BaseDirectory, "Locales");

    /// <summary>Indexer consumed by the Tr markup extension bindings.</summary>
    public string this[string key]
    {
        get
        {
            if (_current.TryGetValue(key, out var v)) return v;
            if (_fallback.TryGetValue(key, out var f))
            {
                // Report each missing key once - feeds the translation workflow.
                if (_reportedMissing.Add(key))
                    App.Log?.Warn($"Missing localization key '{key}' in {CurrentLanguage}");
                return f;
            }
            return $"!{key}!";   // developer-visible marker: key absent everywhere
        }
    }

    /// <summary>Discovers available packs by scanning the Locales folder.
    /// Language packs are therefore drop-in extensible (a pack IS a minimal DLC).</summary>
    public IReadOnlyList<LanguageInfo> DiscoverLanguages()
    {
        var list = new List<LanguageInfo>();
        if (!Directory.Exists(LocalesDir)) return list;
        foreach (string file in Directory.GetFiles(LocalesDir, "*.json"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                string tag = Path.GetFileNameWithoutExtension(file);
                string native = tag;
                if (doc.RootElement.TryGetProperty("$meta", out var meta) &&
                    meta.TryGetProperty("nativeName", out var n))
                    native = n.GetString() ?? tag;
                list.Add(new LanguageInfo(tag, native));
            }
            catch { /* skip malformed pack */ }
        }
        return list.OrderBy(l => l.Tag).ToList();
    }

    /// <summary>Loads and activates a language pack; also (re)loads the en-US fallback.</summary>
    public void SetLanguage(string tag)
    {
        _fallback = LoadPack("en-US", out _, out _);
        _current  = LoadPack(tag, out bool rtl, out string font);
        CurrentLanguage = tag;
        IsRightToLeft = rtl;
        PreferredFont = font;
        _reportedMissing.Clear();

        try
        {
            var culture = CultureInfo.GetCultureInfo(tag);
            CultureInfo.CurrentCulture = culture;      // dates & numbers
            CultureInfo.CurrentUICulture = culture;
        }
        catch (CultureNotFoundException) { /* keep previous culture */ }

        // "Item[]" invalidates ALL indexer bindings -> whole UI re-renders live.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        LanguageChanged?.Invoke();
    }

    /// <summary>Reload the current pack from disk (Settings > "Reload language file"
    /// button - lets translators proof-read edits without restarting).</summary>
    public void Reload() => SetLanguage(CurrentLanguage);

    /// <summary>Apply the correct FlowDirection to a window (call on load + on switch).</summary>
    public void ApplyFlowDirection(Window w)
        => w.FlowDirection = IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    private static Dictionary<string, string> LoadPack(string tag, out bool rtl, out string font)
    {
        rtl = false; font = "";
        var dict = new Dictionary<string, string>();
        string path = Path.Combine(LocalesDir, tag + ".json");
        if (!File.Exists(path)) return dict;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name == "$meta")
                {
                    if (prop.Value.TryGetProperty("direction", out var d))
                        rtl = d.GetString() == "rtl";
                    if (prop.Value.TryGetProperty("preferredFont", out var f))
                        font = f.GetString() ?? "";
                    continue;
                }
                if (prop.Value.ValueKind == JsonValueKind.String)
                    dict[prop.Name] = prop.Value.GetString() ?? "";
            }
        }
        catch (Exception ex)
        {
            App.Log?.Error($"Failed to load language pack '{tag}'", ex);
        }
        return dict;
    }

    public sealed record LanguageInfo(string Tag, string NativeName);
}
