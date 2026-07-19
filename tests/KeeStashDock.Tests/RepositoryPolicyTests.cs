using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace KeeStashDock.Tests;

[TestClass]
public sealed class RepositoryPolicyTests
{
    private static readonly Regex PlaceholderRegex =
        new(@"\{\d+\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [TestMethod]
    public void LocaleFiles_HaveMatchingKeysAndPlaceholders()
    {
        string root = FindRepositoryRoot();
        string localeDirectory = Path.Combine(
            root, "src", "KeeStashDock.Desktop", "Locales");
        string[] files = Directory.GetFiles(localeDirectory, "*.json")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.IsTrue(files.Length >= 20, $"Expected at least 20 locale files, found {files.Length}.");

        var english = ReadLocale(Path.Combine(localeDirectory, "en-US.json"));
        string[] englishKeys = english.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();

        foreach (string file in files)
        {
            var locale = ReadLocale(file);
            string[] localeKeys = locale.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
            CollectionAssert.AreEqual(
                englishKeys,
                localeKeys,
                $"Localization keys differ in {Path.GetFileName(file)}.");

            foreach (string key in englishKeys)
            {
                string[] expected = GetPlaceholders(english[key]);
                string[] actual = GetPlaceholders(locale[key]);
                CollectionAssert.AreEqual(
                    expected,
                    actual,
                    $"Format placeholders differ for '{key}' in {Path.GetFileName(file)}.");
            }
        }
    }

    [TestMethod]
    public void LocaleFiles_HaveValidMetadata()
    {
        string root = FindRepositoryRoot();
        string localeDirectory = Path.Combine(
            root, "src", "KeeStashDock.Desktop", "Locales");

        foreach (string file in Directory.GetFiles(localeDirectory, "*.json"))
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file));
            JsonElement metadata = document.RootElement.GetProperty("$meta");

            string? nativeName = metadata.GetProperty("nativeName").GetString();
            string? direction = metadata.GetProperty("direction").GetString();

            Assert.IsFalse(
                string.IsNullOrWhiteSpace(nativeName),
                $"$meta.nativeName is empty in {Path.GetFileName(file)}.");
            CollectionAssert.Contains(
                new[] { "ltr", "rtl" },
                direction,
                $"$meta.direction is invalid in {Path.GetFileName(file)}.");
        }
    }


    [TestMethod]
    public void LocaleHelp_DoesNotDocumentRetiredSummonOrPlainPasteBehavior()
    {
        string root = FindRepositoryRoot();
        string localeDirectory = Path.Combine(
            root, "src", "KeeStashDock.Desktop", "Locales");

        foreach (string file in Directory.GetFiles(localeDirectory, "*.json"))
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file));
            string help = document.RootElement.GetProperty("Help.Content").GetString()
                ?? string.Empty;
            Assert.IsFalse(
                help.Contains("Ctrl+Alt+V", StringComparison.OrdinalIgnoreCase),
                $"Retired plain-text paste guidance remains in {Path.GetFileName(file)}.");
        }

        string english = ReadHelp(Path.Combine(localeDirectory, "en-US.json"));
        Assert.IsFalse(english.Contains("screen edge", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(english.Contains("exempt from expiry", StringComparison.OrdinalIgnoreCase));

        string chinese = ReadHelp(Path.Combine(localeDirectory, "zh-CN.json"));
        Assert.IsFalse(chinese.Contains("\u5C4F\u5E55\u8FB9\u7F18", StringComparison.Ordinal));
        Assert.IsFalse(chinese.Contains("\u514D\u4E8E\u8FC7\u671F", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Repository_UsesApache20LicenseAndMetadata()
    {
        string root = FindRepositoryRoot();
        string license = File.ReadAllText(Path.Combine(root, "LICENSE"));
        Assert.IsTrue(
            license.Contains("Apache License", StringComparison.Ordinal),
            "LICENSE does not contain the Apache License title.");
        Assert.IsTrue(
            license.Contains("Version 2.0, January 2004", StringComparison.Ordinal),
            "LICENSE is not the Apache License 2.0 text.");

        string noticePath = Path.Combine(root, "NOTICE");
        Assert.IsTrue(File.Exists(noticePath), "NOTICE is required for project attribution.");
        Assert.IsTrue(
            File.ReadAllText(noticePath).Contains(
                "Copyright 2026 KeeganKei", StringComparison.Ordinal),
            "NOTICE does not contain the project copyright attribution.");

        XDocument properties = XDocument.Load(Path.Combine(root, "Directory.Build.props"));
        string? expression = properties
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "PackageLicenseExpression")
            ?.Value;
        Assert.AreEqual("Apache-2.0", expression);
    }

    [TestMethod]
    public void Repository_HasNoUserSpecificIdeFiles()
    {
        string root = FindRepositoryRoot();
        string[] forbidden = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".user", StringComparison.OrdinalIgnoreCase)
                        || path.EndsWith(".suo", StringComparison.OrdinalIgnoreCase))
            .Where(path => !IsUnderIgnoredBuildDirectory(root, path))
            .ToArray();

        Assert.AreEqual(0, forbidden.Length, string.Join(Environment.NewLine, forbidden));
    }

    [TestMethod]
    public void NuGetPackages_UseCentralExactVersions()
    {
        string root = FindRepositoryRoot();
        string centralPath = Path.Combine(root, "Directory.Packages.props");
        XDocument centralDocument = XDocument.Load(centralPath);
        var centralVersions = centralDocument
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageVersion")
            .ToDictionary(
                element => RequiredAttribute(element, "Include"),
                element => RequiredAttribute(element, "Version"),
                StringComparer.OrdinalIgnoreCase);

        Assert.IsTrue(centralVersions.Count > 0, "No centrally managed package versions were found.");
        foreach ((string package, string version) in centralVersions)
        {
            Assert.IsFalse(
                version.Contains('*') || version.Contains('[') || version.Contains('('),
                $"Package '{package}' has a floating or ranged version '{version}'.");
        }

        foreach (string projectPath in Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories))
        {
            if (IsUnderIgnoredBuildDirectory(root, projectPath))
                continue;

            XDocument project = XDocument.Load(projectPath);
            foreach (XElement reference in project.Descendants()
                         .Where(element => element.Name.LocalName == "PackageReference"))
            {
                string package = RequiredAttribute(reference, "Include");
                XAttribute? localVersion = reference.Attribute("Version");
                Assert.IsNull(
                    localVersion,
                    $"Package '{package}' in {Path.GetRelativePath(root, projectPath)} must use central version management.");
                Assert.IsTrue(
                    centralVersions.ContainsKey(package),
                    $"Package '{package}' in {Path.GetRelativePath(root, projectPath)} has no central version.");
            }
        }
    }

    [TestMethod]
    public void SourceAndGeneralDocumentation_UseEnglishScriptOutsideLocalizationExceptions()
    {
        string root = FindRepositoryRoot();
        var violations = new List<string>();

        foreach (string file in EnumeratePolicyTextFiles(root))
        {
            string relative = NormalizeRelative(root, file);
            if (relative.Equals("README.zh-CN.md", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith(
                    "src/KeeStashDock.Desktop/Locales/",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int lineNumber = 0;
            foreach (string line in File.ReadLines(file))
            {
                lineNumber++;
                char? character = line.FirstOrDefault(IsDisallowedScriptCharacter);
                if (character is null || character == '\0')
                    continue;

                violations.Add(
                    $"{relative}:{lineNumber} contains U+{(int)character.Value:X4} outside a localization exception.");
                if (violations.Count >= 20)
                    break;
            }

            if (violations.Count >= 20)
                break;
        }

        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    public void Readmes_DoNotContainPlaceholderSections()
    {
        string root = FindRepositoryRoot();
        foreach (string name in new[] { "README.md", "README.zh-CN.md" })
        {
            string content = File.ReadAllText(Path.Combine(root, name));
            Assert.IsFalse(
                Regex.IsMatch(content, @"(?i)\b(?:xxx|todo|tbd)\b"),
                $"{name} still contains a placeholder marker.");
            Assert.IsTrue(content.Length > 2_000, $"{name} is unexpectedly incomplete.");
        }
    }


    private static string ReadHelp(string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("Help.Content").GetString()
            ?? string.Empty;
    }

    private static Dictionary<string, string> ReadLocale(string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.AreEqual(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.IsTrue(document.RootElement.TryGetProperty("$meta", out _));

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            if (property.NameEquals("$meta"))
                continue;

            Assert.AreEqual(
                JsonValueKind.String,
                property.Value.ValueKind,
                $"Localization value '{property.Name}' in {Path.GetFileName(path)} must be a string.");
            values.Add(property.Name, property.Value.GetString() ?? string.Empty);
        }

        return values;
    }

    private static string[] GetPlaceholders(string value) => PlaceholderRegex
        .Matches(value)
        .Select(match => match.Value)
        .OrderBy(item => item, StringComparer.Ordinal)
        .ToArray();

    private static string RequiredAttribute(XElement element, string name) =>
        element.Attribute(name)?.Value
        ?? throw new AssertFailedException($"Missing '{name}' on {element.Name.LocalName}.");

    private static IEnumerable<string> EnumeratePolicyTextFiles(string root)
    {
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".bat", ".cs", ".csproj", ".gitattributes", ".gitignore", ".json",
            ".manifest", ".md", ".props", ".sln", ".targets", ".txt", ".xaml",
            ".xml", ".yaml", ".yml",
        };
        var extensionlessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "LICENSE", "NOTICE", ".editorconfig", ".gitattributes", ".gitignore",
        };

        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !IsUnderIgnoredBuildDirectory(root, path))
            .Where(path => extensions.Contains(Path.GetExtension(path))
                        || extensionlessNames.Contains(Path.GetFileName(path)));
    }

    private static bool IsUnderIgnoredBuildDirectory(string root, string path)
    {
        string relative = NormalizeRelative(root, path);
        string[] parts = relative.Split('/');
        return parts.Any(part => part.Equals(".git", StringComparison.OrdinalIgnoreCase)
                              || part.Equals("bin", StringComparison.OrdinalIgnoreCase)
                              || part.Equals("obj", StringComparison.OrdinalIgnoreCase)
                              || part.Equals("artifacts", StringComparison.OrdinalIgnoreCase)
                              || part.Equals("TestResults", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDisallowedScriptCharacter(char character)
    {
        int value = character;
        return IsBetween(value, 0x0400, 0x052F)  // Cyrillic
            || IsBetween(value, 0x0590, 0x05FF)  // Hebrew
            || IsBetween(value, 0x0600, 0x06FF)  // Arabic
            || IsBetween(value, 0x0900, 0x097F)  // Devanagari
            || IsBetween(value, 0x0E00, 0x0E7F)  // Thai
            || IsBetween(value, 0x1100, 0x11FF)  // Hangul Jamo
            || IsBetween(value, 0x3000, 0x303F)  // CJK symbols and punctuation
            || IsBetween(value, 0x3040, 0x30FF)  // Hiragana and Katakana
            || IsBetween(value, 0x3400, 0x4DBF)  // CJK Extension A
            || IsBetween(value, 0x4E00, 0x9FFF)  // CJK unified ideographs
            || IsBetween(value, 0xAC00, 0xD7AF)  // Hangul syllables
            || IsBetween(value, 0xF900, 0xFAFF)  // CJK compatibility ideographs
            || IsBetween(value, 0xFF00, 0xFFEF); // Full-width forms
    }

    private static bool IsBetween(int value, int minimum, int maximum) =>
        value >= minimum && value <= maximum;

    private static string NormalizeRelative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KeeStashDock.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the KeeStashDock repository root.");
    }
}
