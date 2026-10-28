using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using KeeStashDock.Domain.Entities;

namespace KeeStashDock.Infrastructure.Storage;

/// <summary>Bounded, data-only theme packages. A single atomic pointer publishes immutable custom versions.</summary>
public sealed class ThemePackageStore
{
    public const long MaxArchiveBytes = 64L * 1024 * 1024;
    public const long MaxExpandedBytes = 128L * 1024 * 1024;
    public const int MaxFiles = 100;
    public static readonly string[] RequiredColors = ["B.WindowBg", "B.PanelBg", "B.CardBg", "B.CardHover", "B.Border",
        "B.TextPrimary", "B.TextMuted", "B.Accent", "B.AccentSoft", "B.OnAccent", "B.CardTextPrimary", "B.CardTextMuted",
        "B.InputText", "B.InputTextMuted", "B.InputBg", "B.Danger"];
    public static readonly string[] OptionalColors = ["B.Success", "B.Warning", "B.Focus", "B.Disabled", "B.Link",
        "B.ScrollThumb", "B.SearchHighlight", "B.BubbleBg", "B.BubbleText", "B.BubbleAccent", "B.BubbleAccentSoft",
        "B.BubbleBorder", "B.BubbleMuted", "B.BubbleHighlight"];
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, MaxDepth = 12,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private readonly Action<string>? _decodeImage;
    private readonly Version _appVersion;
    public string Root { get; }

    public ThemePackageStore(string root, Action<string>? decodeImage = null, Version? appVersion = null)
    {
        Root = Path.GetFullPath(root);
        _decodeImage = decodeImage;
        _appVersion = appVersion ?? new Version(1, 3, 0);
    }

    public IReadOnlyList<ThemeScheme> Discover(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new List<ThemeScheme>();
        foreach (string kind in new[] { "builtin", "custom" })
        {
            string parent = Path.Combine(Root, kind);
            if (!Directory.Exists(parent)) continue;
            string[] directories;
            try { EnsureNoLinks(parent); directories = Directory.EnumerateDirectories(parent).Order(StringComparer.Ordinal).Take(256).ToArray(); }
            catch (Exception ex) when (IsPackageFailure(ex)) { continue; }
            foreach (string directory in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    string id = Path.GetFileName(directory);
                    if (!ValidId(id)) continue;
                    string actual = kind == "builtin" ? directory : ResolveCustom(directory);
                    ThemeScheme scheme = Read(actual, kind + ":" + id, cancellationToken);
                    if (scheme.Manifest.Id == id) result.Add(scheme);
                }
                catch (Exception ex) when (IsPackageFailure(ex)) { /* A broken user scheme never prevents startup. */ }
            }
        }
        return result;
    }

    public ThemeScheme? Find(string key)
    {
        string[] parts = key.Split(':');
        if (parts.Length != 2 || parts[0] is not ("builtin" or "custom") || !ValidId(parts[1])) return null;
        string directory = Path.Combine(Root, parts[0], parts[1]);
        try
        {
            ThemeScheme scheme = Read(parts[0] == "builtin" ? directory : ResolveCustom(directory), key);
            return scheme.Manifest.Id == parts[1] ? scheme : null;
        }
        catch (Exception ex) when (IsPackageFailure(ex)) { return null; }
    }

    public ThemeScheme Read(string directory, string key = "preview", CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        directory = Path.GetFullPath(directory);
        EnsureNoLinks(directory);
        string[] files = EnumerateSafeFiles(directory);
        ThemeManifest manifest = ReadJson<ThemeManifest>(Resolve(directory, "manifest.json"));
        ValidateManifest(manifest);
        var light = ReadJson<ThemeVariant>(Resolve(directory, manifest.Light));
        var dark = ReadJson<ThemeVariant>(Resolve(directory, manifest.Dark));
        ValidateVariant(directory, light);
        ValidateVariant(directory, dark);
        string source = Resolve(directory, manifest.Source);
        if (!File.Exists(source) || new FileInfo(source).Length > 64 * 1024) throw new InvalidDataException("Theme source notice is missing or too large.");
        long totalPixels = 0;
        foreach (string file in files.Where(IsImage))
        {
            cancellationToken.ThrowIfCancellationRequested();
            (int width, int height) = ReadImageSize(file);
            totalPixels = checked(totalPixels + (long)width * height);
            if (width > 8192 || height > 8192 || totalPixels > 64_000_000)
                throw new InvalidDataException("Theme images exceed the pixel limit.");
            _decodeImage?.Invoke(file);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new ThemeScheme(key, directory, manifest, light, dark);
    }

    public StagedTheme StageImport(string archivePath, CancellationToken cancellationToken = default)
    {
        if (new FileInfo(archivePath).Length > MaxArchiveBytes) throw new InvalidDataException("Theme archive exceeds 64 MB.");
        EnsureWritableRoot();
        string stage = Path.Combine(Root, ".staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            if (archive.Entries.Count > MaxFiles) throw new InvalidDataException("Too many theme package entries.");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relative = entry.FullName;
                bool isDirectory = relative.EndsWith('/');
                if (isDirectory) relative = relative[..^1];
                ValidateRelative(relative);
                if (!seen.Add(relative)) throw new InvalidDataException("Duplicate or case-colliding theme entry.");
                int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
                if (unixType is not (0 or 0x8000 or 0x4000) || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Theme links and special files are not supported.");
                string destination = Resolve(stage, relative);
                if (isDirectory) { ValidateAllowedDirectory(relative); Directory.CreateDirectory(destination); continue; }
                ValidateAllowedFile(relative);
                long limit = IsImage(relative) ? 24L * 1024 * 1024 : 256L * 1024;
                if (entry.Length > limit) throw new InvalidDataException("Theme entry is too large.");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using Stream input = entry.Open();
                using FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                byte[] buffer = new byte[81920];
                long written = 0;
                int count;
                while ((count = input.Read(buffer)) != 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    written += count; total += count;
                    if (written > limit || total > MaxExpandedBytes) throw new InvalidDataException("Expanded theme data is too large.");
                    output.Write(buffer, 0, count);
                }
            }
            ThemeScheme scheme = Read(stage, cancellationToken: cancellationToken);
            return new StagedTheme(stage, scheme);
        }
        catch { DeleteOwnedStage(stage); throw; }
    }

    public ThemeScheme Install(StagedTheme staged, bool replaceExisting, bool installAsCopy = false)
    {
        EnsureWritableRoot();
        ThemeScheme verified = Read(staged.Directory);
        if (installAsCopy)
        {
            verified.Manifest.Id = verified.Manifest.Id[..Math.Min(verified.Manifest.Id.Length, 45)] + "-" + Guid.NewGuid().ToString("N")[..12];
            verified.Manifest.Name = verified.Manifest.Name[..Math.Min(verified.Manifest.Name.Length, 85)] + " (copy)";
            File.WriteAllText(Path.Combine(staged.Directory, "manifest.json"), JsonSerializer.Serialize(verified.Manifest, Json));
        }
        string directory = Path.Combine(Root, "custom", verified.Manifest.Id);
        EnsureNoLinks(directory);
        string pointer = Path.Combine(directory, "current.json");
        if (File.Exists(pointer))
        {
            ThemeScheme existing = Read(ResolveCustom(directory), "custom:" + verified.Manifest.Id);
            if (existing.Manifest.Version == verified.Manifest.Version)
            {
                if (SameFiles(existing.Directory, verified.Directory)) return existing;
                throw new IOException("This theme ID/version already contains different files; install a separate copy.");
            }
            if (!replaceExisting) throw new IOException("A different version of this theme already exists.");
        }
        string version = "versions/" + verified.Manifest.Version + "-" + Guid.NewGuid().ToString("N");
        string target = Resolve(directory, version);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        // The validated staging and versions directories share the same volume.
        Directory.Move(staged.Directory, target);
        staged.Transferred = true;
        string temporary = pointer + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new CurrentTheme { Path = version }, Json));
            if (File.Exists(pointer)) File.Replace(temporary, pointer, null);
            else File.Move(temporary, pointer);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        // A crash before pointer replacement leaves the old version active. An
        // unreferenced immutable version is harmless and is never loaded automatically.
        // Everything was validated before publication. No decoder or extra
        // file reads may report failure after the active pointer was replaced.
        return new ThemeScheme("custom:" + verified.Manifest.Id, target, verified.Manifest, verified.Light, verified.Dark);
    }

    public void Export(ThemeScheme scheme, string destination, ThemeExportOverrides? overrides = null)
    {
        ThemeScheme verified = Read(scheme.Directory, scheme.Key);
        string output = Path.GetFullPath(destination);
        if (output.StartsWith(verified.Directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Export the archive outside its source theme directory.");
        string temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            ThemeVariant light = Clone(verified.Light), dark = Clone(verified.Dark);
            ThemeManifest manifest = Clone(verified.Manifest);
            if (overrides is not null)
            {
                // Edited built-ins become distinct portable copies, never an
                // ambiguous same-ID/same-version replacement of their source.
                manifest.Id = manifest.Id[..Math.Min(manifest.Id.Length, 45)] + "-" + Guid.NewGuid().ToString("N")[..12];
                manifest.Name = manifest.Name[..Math.Min(manifest.Name.Length, 85)] + " (custom)";
                manifest.Version = "1.0.0";
            }
            string? externalImage = overrides?.BackgroundPath;
            string? packagedImage = null;
            if (!string.IsNullOrEmpty(externalImage))
            {
                if (!overrides!.AllowBackgroundRedistribution) throw new InvalidOperationException("Background redistribution must be confirmed explicitly.");
                EnsureNoLinks(externalImage);
                ReadImageSize(externalImage); _decodeImage?.Invoke(externalImage);
                packagedImage = "assets/user-background" + Path.GetExtension(externalImage).ToLowerInvariant();
                if (!IsImage(packagedImage)) throw new InvalidDataException("Unsupported background image.");
                var existingPaths = EnumerateSafeFiles(verified.Directory)
                    .Select(path => Path.GetRelativePath(verified.Directory, path).Replace('\\', '/'))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (existingPaths.Contains(packagedImage))
                    packagedImage = "assets/user-background-" + Guid.NewGuid().ToString("N") + Path.GetExtension(externalImage).ToLowerInvariant();
            }
            if (overrides is not null && (!InRange(overrides.OverlayOpacity, 0, 1) || !InRange(overrides.BlurRadius, 0, 18)))
                throw new InvalidDataException("Exported background parameters are outside supported ranges.");
            ApplyOverrides(light, overrides, packagedImage); ApplyOverrides(dark, overrides, packagedImage);
            ValidateAppearance(light); ValidateAppearance(dark);
            long pixels = 0;
            foreach (string asset in EnumerateSafeFiles(verified.Directory).Where(IsImage).Concat(externalImage is null ? [] : new[] { externalImage }))
            {
                (int width, int height) = ReadImageSize(asset); pixels += (long)width * height;
                if (pixels > 64_000_000) throw new InvalidDataException("Exported images exceed the pixel limit.");
            }
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                var referencedImages = light.Backgrounds.Values.Concat(dark.Backgrounds.Values)
                    .Select(background => background.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (string file in EnumerateSafeFiles(verified.Directory))
                {
                    string relative = Path.GetRelativePath(verified.Directory, file).Replace('\\', '/');
                    if (relative == "manifest.json" || relative == verified.Manifest.Light || relative == verified.Manifest.Dark || relative == packagedImage) continue;
                    if (relative.StartsWith("assets/", StringComparison.Ordinal) && !referencedImages.Contains(relative)) continue;
                    archive.CreateEntryFromFile(file, relative, CompressionLevel.Optimal);
                }
                WriteJsonEntry(archive, "manifest.json", manifest);
                WriteJsonEntry(archive, verified.Manifest.Light, light);
                WriteJsonEntry(archive, verified.Manifest.Dark, dark);
                if (packagedImage is not null) archive.CreateEntryFromFile(externalImage!, packagedImage, CompressionLevel.Optimal);
            }
            using (var check = ZipFile.OpenRead(temporary))
                if (check.Entries.Count > MaxFiles || check.Entries.Sum(entry => entry.Length) > MaxExpandedBytes)
                    throw new InvalidDataException("Exported theme data exceeds package limits.");
            if (new FileInfo(temporary).Length > MaxArchiveBytes) throw new InvalidDataException("Exported theme exceeds the archive limit.");
            // Validate the actual portable output, not just the source/override
            // models. A missing or colliding asset can never publish a success.
            string validationRoot = Path.Combine(Path.GetTempPath(), "KeeStashDock-theme-export-check-" + Guid.NewGuid().ToString("N"));
            try
            {
                var checker = new ThemePackageStore(validationRoot, _decodeImage, _appVersion);
                using var validated = checker.StageImport(temporary);
            }
            finally { DeleteOwnedStage(validationRoot); }
            if (File.Exists(output)) File.Move(temporary, output, overwrite: true);
            else File.Move(temporary, output);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void ExportDefault(ThemeVariant light, ThemeVariant dark, string destination, ThemeExportOverrides overrides)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "KeeStashDock-theme-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temporary, "variants"));
        try
        {
            var manifest = new ThemeManifest { Id = "custom-appearance", Name = "KeeStashDock appearance" };
            File.WriteAllText(Path.Combine(temporary, "manifest.json"), JsonSerializer.Serialize(manifest, Json));
            File.WriteAllText(Path.Combine(temporary, "variants", "light.json"), JsonSerializer.Serialize(light, Json));
            File.WriteAllText(Path.Combine(temporary, "variants", "dark.json"), JsonSerializer.Serialize(dark, Json));
            File.WriteAllText(Path.Combine(temporary, "SOURCE.md"), "# KeeStashDock appearance\n\nPalette derived from KeeStashDock. Any included user background was explicitly approved for redistribution by the exporter.\n");
            Export(Read(temporary), destination, overrides);
        }
        finally { DeleteOwnedStage(temporary); }
    }

    public static bool IsPackageFailure(Exception ex) => ex is IOException or UnauthorizedAccessException or JsonException or
        ArgumentException or NotSupportedException or OverflowException or FormatException;

    public static string Resolve(string directory, string relative)
    {
        ValidateRelative(relative);
        string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Theme path escapes its directory.");
        EnsureNoLinks(path);
        return path;
    }

    public static void ValidateRelative(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Length > 180 || relative.Contains('\\') || relative.Contains(':') ||
            relative.StartsWith('/') || relative.Any(char.IsControl)) throw new InvalidDataException("Invalid theme path.");
        foreach (string segment in relative.Split('/'))
            if (segment is "" or "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ') ||
                segment.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0 ||
                Regex.IsMatch(segment.Split('.')[0], @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new InvalidDataException("Invalid theme path component.");
    }

    public static void EnsureNoLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Theme paths must not contain links or junctions.");
    }

    private static string ResolveCustom(string directory)
    {
        CurrentTheme pointer = ReadJson<CurrentTheme>(Resolve(directory, "current.json"));
        if (!Regex.IsMatch(pointer.Path, @"^versions/[0-9]+\.[0-9]+\.[0-9]+-[a-f0-9]{32}$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("Invalid theme version pointer.");
        return Resolve(directory, pointer.Path);
    }

    private void ValidateManifest(ThemeManifest manifest)
    {
        if (manifest.FormatVersion != 1 || !ValidId(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Name) ||
            manifest.Name.Length > 100 || manifest.Name.Any(char.IsControl) || !IsThreePartVersion(manifest.Version) ||
            !Version.TryParse(manifest.MinimumAppVersion, out Version? minimum) || minimum > _appVersion)
            throw new InvalidDataException("Unsupported theme identity, format, or application version.");
        if (manifest.Light != "variants/light.json" || manifest.Dark != "variants/dark.json" || manifest.Source != "SOURCE.md")
            throw new InvalidDataException("Theme definition paths must use the supported layout.");
    }

    private static bool IsThreePartVersion(string value) => Regex.IsMatch(value, @"^\d{1,4}\.\d{1,4}\.\d{1,4}$", RegexOptions.CultureInvariant);
    private static bool ValidId(string id) => Regex.IsMatch(id, @"^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant);

    private static void ValidateVariant(string directory, ThemeVariant variant)
    {
        if (variant.Colors is null || RequiredColors.Any(key => !variant.Colors.ContainsKey(key)) ||
            variant.Colors.Any(pair => !RequiredColors.Contains(pair.Key) && !OptionalColors.Contains(pair.Key) || !ValidColor(pair.Value)))
            throw new InvalidDataException("Theme color definitions are incomplete or invalid.");
        foreach ((string text, string surface) in new[] { ("B.TextPrimary", "B.WindowBg"), ("B.TextMuted", "B.WindowBg"),
            ("B.TextPrimary", "B.CardBg"), ("B.TextMuted", "B.CardBg"),
            ("B.CardTextPrimary", "B.CardBg"), ("B.CardTextMuted", "B.CardBg"), ("B.InputText", "B.InputBg"),
            ("B.InputTextMuted", "B.InputBg"), ("B.OnAccent", "B.Accent") })
            if (!OpaqueColor(variant.Colors[text]) || !OpaqueColor(variant.Colors[surface]) || Contrast(variant.Colors[text], variant.Colors[surface]) < 4.5)
                throw new InvalidDataException("Theme text colors do not meet the minimum readable contrast.");
        if (variant.Backgrounds is null || variant.Backgrounds.Count > 3 || variant.Backgrounds.Keys.Any(role => role is not ("quickPanel" or "workspace" or "smallWindow")))
            throw new InvalidDataException("Unsupported theme background role.");
        foreach (ThemeBackground background in variant.Backgrounds.Values)
        {
            if (background is null) throw new InvalidDataException("Empty theme background definition.");
            string path = Resolve(directory, background.Path);
            if (!background.Path.StartsWith("assets/", StringComparison.Ordinal) || !IsImage(path) || !File.Exists(path) ||
                !InRange(background.Opacity, 0, 1) || !InRange(background.OverlayOpacity, 0, 1) ||
                !InRange(background.BlurRadius, 0, 18) || !InRange(background.FocalX, 0, 1) || !InRange(background.FocalY, 0, 1))
                throw new InvalidDataException("Theme background is missing or invalid.");
        }
        ValidateAppearance(variant);
    }

    private static void ValidateAppearance(ThemeVariant variant)
    {
        if (!InRange(variant.CardOpacity, 0.1, 1) || !InRange(variant.SettingsSurfaceOpacity, 0.1, 1) ||
            !InRange(variant.SettingsFrostedGlassRadius, 1, 18) ||
            !InRange(variant.BubbleOpacity, 0.5, 1) ||
            variant.BubbleStyle is not ("Default" or "FollowTheme" or "Ivory" or "Graphite" or "Amber" or "Mint" or "Sky" or "Rose" or "Lavender" or "Coral" or "Teal" or "Slate") ||
            variant.PanelTextColorMode is not ("Auto" or "Ivory" or "Graphite" or "Amber" or "Mint" or "Sky" or "Rose" or "Lavender" or "Coral" or "Teal" or "Slate" or "Custom") || !ValidColor(variant.PanelTextCustomColor) ||
            (variant.PanelTextColorMode == "Custom" && !OpaqueColor(variant.PanelTextCustomColor)))
            throw new InvalidDataException("Theme appearance parameters are outside supported ranges.");
    }

    private static bool ValidColor(string? color) => color is not null && Regex.IsMatch(color, @"^#(?:[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.CultureInvariant);
    private static bool OpaqueColor(string color) => color.Length == 7 || color[1..3].Equals("FF", StringComparison.OrdinalIgnoreCase);
    private static bool InRange(double value, double minimum, double maximum) => double.IsFinite(value) && value >= minimum && value <= maximum;
    private static double Contrast(string foreground, string background)
    {
        static double Luminance(string color)
        {
            string rgb = color[^6..];
            static double Linear(int value) { double channel = value / 255d; return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4); }
            return 0.2126 * Linear(Convert.ToInt32(rgb[..2], 16)) + 0.7152 * Linear(Convert.ToInt32(rgb[2..4], 16)) + 0.0722 * Linear(Convert.ToInt32(rgb[4..6], 16));
        }
        double a = Luminance(foreground), b = Luminance(background);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
    private static bool IsImage(string file) => Path.GetExtension(file).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg";

    private static void ValidateAllowedFile(string relative)
    {
        bool allowed = relative is "manifest.json" or "SOURCE.md" or "PROMPTS.md" or "variants/light.json" or "variants/dark.json" ||
            ((relative.StartsWith("assets/", StringComparison.Ordinal) || relative.StartsWith("previews/", StringComparison.Ordinal)) && IsImage(relative));
        if (!allowed) throw new InvalidDataException("Unsupported file in theme package.");
    }

    private static void ValidateAllowedDirectory(string relative)
    {
        if (relative is not ("assets" or "previews" or "variants") &&
            !relative.StartsWith("assets/", StringComparison.Ordinal) && !relative.StartsWith("previews/", StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported directory in theme package.");
    }

    private static string[] EnumerateSafeFiles(string directory)
    {
        var files = new List<string>();
        var pending = new Stack<string>(); pending.Push(directory);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        int count = 0;
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            foreach (string entry in Directory.EnumerateFileSystemEntries(current))
            {
                if (++count > MaxFiles) throw new InvalidDataException("Too many theme entries.");
                EnsureNoLinks(entry);
                string relative = Path.GetRelativePath(directory, entry).Replace('\\', '/');
                ValidateRelative(relative);
                if (!names.Add(relative)) throw new InvalidDataException("Case-colliding theme entries.");
                if (Directory.Exists(entry)) { ValidateAllowedDirectory(relative); pending.Push(entry); continue; }
                ValidateAllowedFile(relative);
                long length = new FileInfo(entry).Length;
                total += length;
                if (length > (IsImage(entry) ? 24L * 1024 * 1024 : 256L * 1024) || total > MaxExpandedBytes)
                    throw new InvalidDataException("Theme files exceed supported limits.");
                files.Add(entry);
            }
        }
        return files.ToArray();
    }

    private static T ReadJson<T>(string path)
    {
        if (new FileInfo(path).Length > 256 * 1024) throw new InvalidDataException("Theme JSON is too large.");
        string text = File.ReadAllText(path);
        using JsonDocument document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 12 });
        static void RejectDuplicateKeys(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate theme JSON property.");
                    RejectDuplicateKeys(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array) foreach (JsonElement item in element.EnumerateArray()) RejectDuplicateKeys(item);
        }
        RejectDuplicateKeys(document.RootElement);
        if (typeof(T) == typeof(ThemeVariant) && document.RootElement.ValueKind == JsonValueKind.Object)
        {
            // These retired settings are inert when reading older packages. Keep
            // strict validation for every other property and omit them on export.
            JsonObject variant = JsonNode.Parse(text)!.AsObject();
            variant.Remove("bubbleFrostedGlassEnabled");
            variant.Remove("bubbleTextGlow");
            variant.Remove("bubbleShadow");
            return variant.Deserialize<T>(Json) ?? throw new InvalidDataException("Empty theme JSON.");
        }
        return JsonSerializer.Deserialize<T>(text, Json) ?? throw new InvalidDataException("Empty theme JSON.");
    }

    private static (int Width, int Height) ReadImageSize(string path)
    {
        using FileStream stream = File.OpenRead(path);
        if (stream.Length > 24L * 1024 * 1024) throw new InvalidDataException("Theme image is too large.");
        Span<byte> header = stackalloc byte[24]; stream.ReadExactly(header);
        if (Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            if (!header[..8].SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) || !header[12..16].SequenceEqual("IHDR"u8))
                throw new InvalidDataException("Invalid PNG background.");
            int width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[16..20]);
            int height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[20..24]);
            return CheckSize(width, height);
        }
        if (!IsImage(path) || header[0] != 0xff || header[1] != 0xd8) throw new InvalidDataException("Invalid JPEG background.");
        stream.Position = 2;
        while (stream.Position < stream.Length)
        {
            int marker = stream.ReadByte(); if (marker != 0xff) throw new InvalidDataException("Invalid JPEG marker.");
            do { marker = stream.ReadByte(); } while (marker == 0xff);
            if (marker is 0xd9 or 0xda or -1) break;
            if (marker is 0x01 or >= 0xd0 and <= 0xd8) continue;
            int length = (stream.ReadByte() << 8) | stream.ReadByte();
            if (length < 2 || stream.Position + length - 2 > stream.Length) break;
            if (marker is >= 0xc0 and <= 0xc3 or >= 0xc5 and <= 0xc7 or >= 0xc9 and <= 0xcb or >= 0xcd and <= 0xcf)
            {
                stream.ReadByte(); int height = (stream.ReadByte() << 8) | stream.ReadByte();
                int width = (stream.ReadByte() << 8) | stream.ReadByte();
                return CheckSize(width, height);
            }
            stream.Position += length - 2;
        }
        throw new InvalidDataException("JPEG dimensions are missing.");
    }

    private static (int Width, int Height) CheckSize(int width, int height)
    {
        if (width < 1 || height < 1 || width > 8192 || height > 8192 || (long)width * height > 32_000_000)
            throw new InvalidDataException("Theme image dimensions are outside supported limits.");
        return (width, height);
    }

    private void EnsureWritableRoot()
    {
        EnsureNoLinks(Root);
        Directory.CreateDirectory(Path.Combine(Root, ".staging"));
        EnsureNoLinks(Path.Combine(Root, ".staging"));
    }

    private static void DeleteOwnedStage(string directory)
    {
        if (!Directory.Exists(directory)) return;
        EnsureNoLinks(directory);
        Directory.Delete(directory, recursive: true);
    }

    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json)!;
    private static bool SameFiles(string first, string second)
    {
        string[] a = EnumerateSafeFiles(first).Select(path => Path.GetRelativePath(first, path)).Order(StringComparer.Ordinal).ToArray();
        string[] b = EnumerateSafeFiles(second).Select(path => Path.GetRelativePath(second, path)).Order(StringComparer.Ordinal).ToArray();
        if (!a.SequenceEqual(b, StringComparer.Ordinal)) return false;
        foreach (string relative in a)
        {
            using var x = File.OpenRead(Path.Combine(first, relative)); using var y = File.OpenRead(Path.Combine(second, relative));
            if (!System.Security.Cryptography.SHA256.HashData(x).SequenceEqual(System.Security.Cryptography.SHA256.HashData(y))) return false;
        }
        return true;
    }
    private static void WriteJsonEntry<T>(ZipArchive archive, string name, T value)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open()); writer.Write(JsonSerializer.Serialize(value, Json));
    }

    private static void ApplyOverrides(ThemeVariant variant, ThemeExportOverrides? settings, string? background)
    {
        if (settings is null) return;
        variant.CardOpacity = settings.CardOpacity;
        variant.SettingsSurfaceOpacity = settings.SettingsSurfaceOpacity;
        variant.SettingsFrostedGlassEnabled = settings.SettingsFrostedGlassEnabled;
        variant.SettingsFrostedGlassRadius = settings.SettingsFrostedGlassRadius;
        variant.BubbleOpacity = settings.BubbleOpacity;
        variant.BubbleStyle = settings.BubbleStyle;
        variant.PanelTextColorMode = settings.PanelTextColorMode;
        variant.PanelTextCustomColor = settings.PanelTextCustomColor;
        if (background is not null) variant.Backgrounds["quickPanel"] = new ThemeBackground
        {
            Path = background, OverlayOpacity = settings.OverlayOpacity, BlurRadius = settings.BlurRadius,
        };
        else if (settings.OverrideQuickPanelBackground) variant.Backgrounds.Remove("quickPanel");
        else if (variant.Backgrounds.TryGetValue("quickPanel", out ThemeBackground? original))
        { original.OverlayOpacity = settings.OverlayOpacity; original.BlurRadius = settings.BlurRadius; }
    }

    private sealed class CurrentTheme { public string Path { get; set; } = ""; }

    public sealed class StagedTheme : IDisposable
    {
        internal StagedTheme(string directory, ThemeScheme scheme) { Directory = directory; Scheme = scheme; }
        public string Directory { get; }
        public ThemeScheme Scheme { get; }
        internal bool Transferred { get; set; }
        public void Dispose() { if (!Transferred) DeleteOwnedStage(Directory); }
    }
}

/// <summary>Only deliberately chosen appearance values can enter a portable export.</summary>
public sealed record ThemeExportOverrides(double CardOpacity, double BubbleOpacity,
    string PanelTextColorMode, string PanelTextCustomColor, string? BackgroundPath,
    double OverlayOpacity, double BlurRadius, bool AllowBackgroundRedistribution,
    string BubbleStyle = "FollowTheme", bool OverrideQuickPanelBackground = false,
    double SettingsSurfaceOpacity = 1.0, bool SettingsFrostedGlassEnabled = false,
    double SettingsFrostedGlassRadius = 8);
