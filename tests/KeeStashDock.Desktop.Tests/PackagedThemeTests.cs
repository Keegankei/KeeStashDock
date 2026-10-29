using System.IO;
using System.Text.Json;
using System.Windows.Media;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

[TestClass]
public sealed class PackagedThemeTests
{
    private string _root = "";
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [TestInitialize]
    public void Start()
    {
        _root = Path.Combine(Path.GetTempPath(), "KeeStashDock-packaged-theme-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Path.GetDirectoryName(_root) == Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar) &&
            Path.GetFileName(_root).StartsWith("KeeStashDock-packaged-theme-", StringComparison.Ordinal))
            Directory.Delete(_root, true);
    }

    [STATestMethod]
    public void EveryBundledThemeDecodesBothVariantsAndKeepsTextReadable()
    {
        string source = Path.Combine(AppContext.BaseDirectory, "theme");
        string[] ids = PackageVerification.VerifyBuiltInThemes(source);
        foreach (string id in new[] { "sunlit-gallery", "twilight-study", "wisteria-atelier", "wisteria-reverie" })
            CollectionAssert.Contains(ids, id);
        Assert.AreEqual(Directory.GetDirectories(Path.Combine(source, "builtin")).Length, ids.Length,
            "Release verification must validate every bundled theme, including additional presets.");
        var store = new ThemePackageStore(source, ThemeManager.ValidateImage);
        foreach (string directory in Directory.GetDirectories(Path.Combine(source, "builtin")))
        {
            ThemeScheme scheme = store.Read(directory);
            foreach (ThemeVariant variant in new[] { scheme.Light, scheme.Dark })
            foreach ((string text, string background) in new[]
            {
                ("B.TextPrimary", "B.WindowBg"), ("B.TextMuted", "B.PanelBg"),
                ("B.CardTextPrimary", "B.CardBg"), ("B.CardTextMuted", "B.CardBg"),
                ("B.InputText", "B.InputBg"), ("B.InputTextMuted", "B.InputBg"),
                ("B.OnAccent", "B.Accent"), ("B.BubbleText", "B.BubbleBg"),
                ("B.BubbleMuted", "B.BubbleBg")
            })
                Assert.IsTrue(Contrast(variant.Colors[text], variant.Colors[background]) >= 4.5,
                    $"{scheme.Manifest.Name}: {text} must remain readable over {background}.");

            string archive = Path.Combine(_root, scheme.Manifest.Id + ".zip");
            store.Export(scheme, archive);
            var imported = new ThemePackageStore(Path.Combine(_root, scheme.Manifest.Id), ThemeManager.ValidateImage);
            using var staged = imported.StageImport(archive);
            Assert.AreEqual(scheme.Manifest.Id, staged.Scheme.Manifest.Id);
            foreach (string surface in new[] { "quickPanel", "workspace", "smallWindow" })
            {
                Assert.AreEqual(scheme.Light.Backgrounds[surface].Path, staged.Scheme.Light.Backgrounds[surface].Path);
                Assert.AreEqual(scheme.Dark.Backgrounds[surface].Path, staged.Scheme.Dark.Backgrounds[surface].Path);
            }
        }
    }

    [STATestMethod]
    [DataRow("sunlit-gallery")]
    [DataRow("twilight-study")]
    [DataRow("wisteria-atelier")]
    [DataRow("wisteria-reverie")]
    public void MissingRequiredThemeFailsReleaseVerification(string themeId)
    {
        CopyThemes();
        Directory.Move(Path.Combine(_root, "builtin", themeId), Path.Combine(_root, "removed-theme"));
        Assert.ThrowsExactly<InvalidDataException>(() => PackageVerification.VerifyBuiltInThemes(_root));
    }

    [STATestMethod]
    public void BrokenAdditionalThemeCannotBeSilentlySkipped()
    {
        CopyThemes();
        string extra = Path.Combine(_root, "builtin", "extra-theme");
        Directory.CreateDirectory(extra);
        File.WriteAllText(Path.Combine(extra, "manifest.json"), "{broken");
        Assert.Throws<JsonException>(() => PackageVerification.VerifyBuiltInThemes(_root));
    }

    [STATestMethod]
    public void MissingVariantBackgroundFailsReleaseVerification()
    {
        CopyThemes();
        string file = Path.Combine(_root, "builtin", "wisteria-atelier", "variants", "dark.json");
        ThemeVariant variant = JsonSerializer.Deserialize<ThemeVariant>(File.ReadAllText(file), Json)!;
        variant.Backgrounds.Remove("workspace");
        File.WriteAllText(file, JsonSerializer.Serialize(variant, Json));
        Assert.ThrowsExactly<InvalidDataException>(() => PackageVerification.VerifyBuiltInThemes(_root));
    }

    private void CopyThemes()
    {
        string source = Path.Combine(AppContext.BaseDirectory, "theme", "builtin");
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(_root, "builtin", Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static double Contrast(string foreground, string background)
    {
        static double Luminance(string value)
        {
            var color = (Color)ColorConverter.ConvertFromString(value);
            static double Linear(byte channel)
            {
                double value = channel / 255d;
                return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
            }
            return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        }
        double first = Luminance(foreground), second = Luminance(background);
        return (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05);
    }
}
