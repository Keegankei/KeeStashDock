using System.IO.Compression;
using System.Text.Json;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Tests;

[TestClass]
public sealed class ThemePackageStoreTests
{
    private string _root = "";
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly byte[] Pixel = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");

    [TestInitialize]
    public void Start()
    {
        _root = Path.Combine(Path.GetTempPath(), "KeeStashDock-theme-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (_root.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(_root).StartsWith("KeeStashDock-theme-tests-", StringComparison.Ordinal) && Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    [TestMethod]
    public void FullThemeRoundTripPreservesBothVariantsAndRelativeArt()
    {
        ThemePackageStore source = Store("source");
        ThemeScheme original = source.Read(CreateDefinition("definition"));
        string zip = Path.Combine(_root, "portable.zip"); source.Export(original, zip);
        ThemePackageStore target = Store("target");
        using var staged = target.StageImport(zip);
        ThemeScheme installed = target.Install(staged, false);
        Assert.AreEqual("custom:example", installed.Key);
        Assert.AreEqual("#FFFFFF", installed.Light.Colors["B.WindowBg"]);
        Assert.AreEqual("#101010", installed.Dark.Colors["B.WindowBg"]);
        CollectionAssert.AreEqual(Pixel, File.ReadAllBytes(Path.Combine(installed.Directory, "assets", "picture.png")));
        Assert.AreEqual(installed.Directory, target.Find(installed.Key)!.Directory);
        Assert.IsFalse(Directory.Exists(Path.Combine(target.Root, "builtin")));
    }

    [TestMethod]
    public void OptionalPromptProvenanceRoundTripsWithoutBecomingRequired()
    {
        ThemePackageStore store = Store("source");
        string directory = CreateDefinition("prompt-provenance");
        _ = store.Read(directory);
        const string prompts = "# Generation prompts\n\nOriginal example composition with directional light.\n";
        File.WriteAllText(Path.Combine(directory, "PROMPTS.md"), prompts);
        string archive = Path.Combine(_root, "prompt-provenance.zip");
        store.Export(store.Read(directory), archive);
        using var staged = Store("target").StageImport(archive);
        Assert.AreEqual(prompts, File.ReadAllText(Path.Combine(staged.Directory, "PROMPTS.md")),
            "Optional generation provenance must travel with the exported theme.");
        Assert.IsTrue(File.Exists(Path.Combine(staged.Directory, "SOURCE.md")));
    }

    [TestMethod]
    public void OptionalBubbleColorsRoundTripWithoutBecomingRequired()
    {
        string directory = CreateDefinition("bubble-colors");
        ThemePackageStore store = Store("source");
        Assert.IsFalse(store.Read(directory).Light.Colors.ContainsKey("B.BubbleAccent"),
            "Existing theme files without the new semantic colors remain valid.");
        string[] keys = ["B.BubbleAccent", "B.BubbleAccentSoft", "B.BubbleBorder", "B.BubbleMuted", "B.BubbleHighlight"];
        foreach (string variantName in new[] { "light", "dark" })
        {
            string path = Path.Combine(directory, "variants", variantName + ".json");
            ThemeVariant variant = JsonSerializer.Deserialize<ThemeVariant>(File.ReadAllText(path), Json)!;
            foreach (string key in keys) variant.Colors[key] = variantName == "light" ? "#704899" : "#D0B4F5";
            Write(path, variant);
        }
        string zip = Path.Combine(_root, "bubble-colors.zip");
        store.Export(store.Read(directory), zip);
        using var stage = Store("target").StageImport(zip);
        foreach (string key in keys)
        {
            Assert.AreEqual("#704899", stage.Scheme.Light.Colors[key]);
            Assert.AreEqual("#D0B4F5", stage.Scheme.Dark.Colors[key]);
        }
    }

    [TestMethod]
    public void OptionalSettingsSurfaceOpacityDefaultsAndRoundTripsPerVariant()
    {
        string directory = CreateDefinition("settings-opacity");
        ThemePackageStore store = Store("source");
        foreach (string variantName in new[] { "light", "dark" })
        {
            string path = Path.Combine(directory, "variants", variantName + ".json");
            var oldDefinition = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            oldDefinition.Remove("settingsSurfaceOpacity");
            oldDefinition.Remove("settingsFrostedGlassEnabled");
            oldDefinition.Remove("settingsFrostedGlassRadius");
            File.WriteAllText(path, oldDefinition.ToJsonString());
        }
        ThemeScheme oldScheme = store.Read(directory);
        Assert.AreEqual(1.0, oldScheme.Light.SettingsSurfaceOpacity);
        Assert.AreEqual(1.0, oldScheme.Dark.SettingsSurfaceOpacity);
        foreach (ThemeVariant variant in new[] { oldScheme.Light, oldScheme.Dark })
        {
            Assert.IsFalse(variant.SettingsFrostedGlassEnabled);
            Assert.AreEqual(8d, variant.SettingsFrostedGlassRadius);
        }
        oldScheme.Light.SettingsSurfaceOpacity = .6;
        oldScheme.Dark.SettingsSurfaceOpacity = .75;
        oldScheme.Light.SettingsFrostedGlassEnabled = true;
        oldScheme.Light.SettingsFrostedGlassRadius = 6;
        Write(Path.Combine(directory, "variants", "light.json"), oldScheme.Light);
        Write(Path.Combine(directory, "variants", "dark.json"), oldScheme.Dark);
        string zip = Path.Combine(_root, "settings-opacity.zip");
        store.Export(store.Read(directory), zip);
        using var stage = Store("target").StageImport(zip);
        Assert.AreEqual(.6, stage.Scheme.Light.SettingsSurfaceOpacity);
        Assert.AreEqual(.75, stage.Scheme.Dark.SettingsSurfaceOpacity);
        Assert.IsTrue(stage.Scheme.Light.SettingsFrostedGlassEnabled);
        Assert.AreEqual(6d, stage.Scheme.Light.SettingsFrostedGlassRadius);
        Assert.IsFalse(stage.Scheme.Dark.SettingsFrostedGlassEnabled);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RetiredBubbleEffectsImportWithoutChangingSupportedAppearanceAndAreOmittedOnExport(bool customized)
    {
        string definition = CreateDefinition("legacy-bubble-effects");
        foreach (string name in new[] { "light", "dark" })
        {
            string path = Path.Combine(definition, "variants", name + ".json");
            var variant = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            variant["bubbleFrostedGlassEnabled"] = true;
            variant["bubbleTextGlow"] = true;
            variant["bubbleShadow"] = true;
            variant["bubbleOpacity"] = .72;
            variant["settingsSurfaceOpacity"] = .63;
            variant["settingsFrostedGlassEnabled"] = true;
            variant["settingsFrostedGlassRadius"] = 11;
            File.WriteAllText(path, variant.ToJsonString());
        }
        ThemePackageStore store = Store("target");
        using var legacy = store.StageImport(Archive(definition, "legacy.zip"));
        ThemeScheme installed = store.Install(legacy, false);
        foreach (ThemeVariant variant in new[] { installed.Light, installed.Dark })
        {
            Assert.AreEqual(.72, variant.BubbleOpacity);
            Assert.AreEqual(.63, variant.SettingsSurfaceOpacity);
            Assert.IsTrue(variant.SettingsFrostedGlassEnabled);
            Assert.AreEqual(11d, variant.SettingsFrostedGlassRadius);
        }
        string output = Path.Combine(_root, "supported-appearance.zip");
        ThemeExportOverrides? appearance = customized
            ? new(.9, .72, "Auto", "#FFFFFF", null, .5, 0, false,
                SettingsSurfaceOpacity: .63, SettingsFrostedGlassEnabled: true, SettingsFrostedGlassRadius: 11)
            : null;
        store.Export(installed, output, appearance);
        using var archive = ZipFile.OpenRead(output);
        foreach (string name in new[] { "light", "dark" })
        {
            using Stream stream = archive.GetEntry("variants/" + name + ".json")!.Open();
            using JsonDocument json = JsonDocument.Parse(stream);
            foreach (string retired in new[] { "bubbleFrostedGlassEnabled", "bubbleTextGlow", "bubbleShadow" })
                Assert.IsFalse(json.RootElement.TryGetProperty(retired, out _), "Exports must discard " + retired + ".");
            Assert.AreEqual(.72, json.RootElement.GetProperty("bubbleOpacity").GetDouble());
            Assert.AreEqual(.63, json.RootElement.GetProperty("settingsSurfaceOpacity").GetDouble());
            Assert.IsTrue(json.RootElement.GetProperty("settingsFrostedGlassEnabled").GetBoolean());
            Assert.AreEqual(11d, json.RootElement.GetProperty("settingsFrostedGlassRadius").GetDouble());
        }
        using var roundTrip = Store("round-trip").StageImport(output);
        Assert.AreEqual(installed.Light.Colors["B.WindowBg"], roundTrip.Scheme.Light.Colors["B.WindowBg"]);
        Assert.AreEqual(installed.Dark.Colors["B.WindowBg"], roundTrip.Scheme.Dark.Colors["B.WindowBg"]);
    }

    [TestMethod]
    public void RetiredBubbleCompatibilityDoesNotPermitUnknownThemeProperties()
    {
        string definition = CreateDefinition("unknown-bubble-effect");
        string path = Path.Combine(definition, "variants", "light.json");
        var variant = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        variant["bubbleShadow"] = true;
        variant["unsupportedEffect"] = true;
        File.WriteAllText(path, variant.ToJsonString());
        Assert.ThrowsExactly<JsonException>(() => Store("target").Read(definition));
    }

    [TestMethod]
    [DataRow(0.0)]
    [DataRow(0.05)]
    [DataRow(1.1)]
    public void InvalidSettingsSurfaceOpacityIsRejectedInThemesAndExports(double value)
    {
        string directory = CreateDefinition("invalid-settings-opacity");
        ThemePackageStore store = Store("source");
        ThemeScheme scheme = store.Read(directory);
        string output = Path.Combine(_root, "bad-opacity.zip");
        Assert.ThrowsExactly<InvalidDataException>(() => store.Export(scheme, output,
            new ThemeExportOverrides(.9, 1, "Auto", "#FFFFFF", null, .5, 0, false,
                SettingsSurfaceOpacity: value)));
        Assert.IsFalse(File.Exists(output));
        scheme.Light.SettingsSurfaceOpacity = value;
        Write(Path.Combine(directory, "variants", "light.json"), scheme.Light);
        Assert.ThrowsExactly<InvalidDataException>(() => store.Read(directory));
    }

    [TestMethod]
    [DataRow(0d)]
    [DataRow(.5d)]
    [DataRow(19d)]
    public void InvalidSettingsFrostRadiusIsRejectedInThemesAndExports(double value)
    {
        string directory = CreateDefinition("invalid-settings-frost");
        ThemePackageStore store = Store("source");
        ThemeScheme scheme = store.Read(directory);
        string output = Path.Combine(_root, "bad-frost.zip");
        Assert.ThrowsExactly<InvalidDataException>(() => store.Export(scheme, output,
            new ThemeExportOverrides(.9, 1, "Auto", "#FFFFFF", null, .5, 0, false,
                SettingsFrostedGlassRadius: value)));
        Assert.IsFalse(File.Exists(output));
        scheme.Light.SettingsFrostedGlassRadius = value;
        Write(Path.Combine(directory, "variants", "light.json"), scheme.Light);
        Assert.ThrowsExactly<InvalidDataException>(() => store.Read(directory));
    }

    [TestMethod]
    public void InstallingNewVersionPreservesOldUntilPointerIsReplaced()
    {
        ThemePackageStore store = Store("target");
        string definition = CreateDefinition("definition");
        string firstZip = Archive(definition, "first.zip");
        ThemeScheme first;
        using (var stage = store.StageImport(firstZip)) first = store.Install(stage, false);
        var manifest = JsonSerializer.Deserialize<ThemeManifest>(File.ReadAllText(Path.Combine(definition, "manifest.json")), Json)!;
        manifest.Version = "1.1.0"; Write(Path.Combine(definition, "manifest.json"), manifest);
        string nextZip = Archive(definition, "next.zip");
        using (var stage = store.StageImport(nextZip))
            Assert.ThrowsExactly<IOException>(() => store.Install(stage, false));
        Assert.AreEqual(first.Directory, store.Find("custom:example")!.Directory);
        using (var stage = store.StageImport(nextZip)) store.Install(stage, true);
        Assert.IsTrue(Directory.Exists(first.Directory));
        Assert.AreEqual("1.1.0", store.Find("custom:example")!.Manifest.Version);
    }

    [TestMethod]
    public void CancelledImportDoesNotCreateCustomSelectionOrLeaveStage()
    {
        ThemePackageStore store = Store("target");
        string zip = Archive(CreateDefinition("definition"), "cancel.zip");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => store.StageImport(zip, cancellation.Token));
        Assert.AreEqual(0, store.Discover().Count);
        Assert.AreEqual(0, Directory.GetDirectories(Path.Combine(store.Root, ".staging")).Length);
    }

    [TestMethod]
    public void DamagedNewVersionCannotReplaceInstalledTheme()
    {
        ThemePackageStore store = Store("target");
        string definition = CreateDefinition("definition");
        using (var stage = store.StageImport(Archive(definition, "good.zip"))) store.Install(stage, false);
        string before = File.ReadAllText(Path.Combine(store.Root, "custom", "example", "current.json"));
        File.WriteAllText(Path.Combine(definition, "variants", "dark.json"), "{\"colors\":{}}");
        Assert.ThrowsExactly<InvalidDataException>(() => store.StageImport(Archive(definition, "bad.zip")));
        Assert.AreEqual(before, File.ReadAllText(Path.Combine(store.Root, "custom", "example", "current.json")));
    }

    [TestMethod]
    [DataRow("../escape.png")]
    [DataRow("/absolute.png")]
    [DataRow("C:/outside.png")]
    [DataRow("assets/a.png:payload")]
    [DataRow("assets/NUL.png")]
    [DataRow("assets/a./test.png")]
    [DataRow("assets/a /test.png")]
    [DataRow("assets\\path.png")]
    [DataRow("assets/../../escape.png")]
    public void ImportRejectsUnsafeEntryNames(string path)
    {
        string zip = Path.Combine(_root, "unsafe.zip");
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create)) archive.CreateEntry(path);
        Assert.ThrowsExactly<InvalidDataException>(() => Store("target").StageImport(zip));
    }

    [TestMethod]
    public void ImportRejectsDuplicateAndCaseCollidingPaths()
    {
        string zip = Path.Combine(_root, "duplicate.zip");
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        { archive.CreateEntry("assets/a.png"); archive.CreateEntry("assets/A.png"); }
        Assert.ThrowsExactly<InvalidDataException>(() => Store("target").StageImport(zip));
    }

    [TestMethod]
    public void ImportRejectsScriptAndSymlinkEntries()
    {
        foreach (bool symbolic in new[] { false, true })
        {
            string zip = Path.Combine(_root, symbolic ? "link.zip" : "script.zip");
            using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry(symbolic ? "assets/a.png" : "assets/run.ps1");
                if (symbolic) entry.ExternalAttributes = unchecked((int)0xa1ff0000);
            }
            Assert.ThrowsExactly<InvalidDataException>(() => Store("target").StageImport(zip));
        }
    }

    [TestMethod]
    public void UnknownFormatMissingDarkAndOversizedPixelsAreRejected()
    {
        string definition = CreateDefinition("definition");
        string manifestPath = Path.Combine(definition, "manifest.json");
        ThemeManifest manifest = JsonSerializer.Deserialize<ThemeManifest>(File.ReadAllText(manifestPath), Json)!;
        manifest.FormatVersion = 2; Write(manifestPath, manifest);
        Assert.ThrowsExactly<InvalidDataException>(() => Store("target").Read(definition));
        manifest.FormatVersion = 1; Write(manifestPath, manifest);
        string dark = Path.Combine(definition, "variants", "dark.json"); File.Delete(dark);
        Assert.ThrowsExactly<FileNotFoundException>(() => Store("target").Read(definition));
        Write(dark, Variant("#101010"));
        byte[] oversized = Pixel.ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(oversized.AsSpan(16, 4), 10000);
        File.WriteAllBytes(Path.Combine(definition, "assets", "picture.png"), oversized);
        Assert.ThrowsExactly<InvalidDataException>(() => Store("target").Read(definition));
    }

    [TestMethod]
    public void ExternalBackgroundRequiresConsentAndExportsNoOriginalPath()
    {
        ThemePackageStore store = Store("source"); ThemeScheme scheme = store.Read(CreateDefinition("definition"));
        string image = Path.Combine(_root, "private-owner-wallpaper.png"); File.WriteAllBytes(image, Pixel);
        string output = Path.Combine(_root, "custom.zip"); File.WriteAllText(output, "preserve me");
        var options = new ThemeExportOverrides(.9, 1, "Auto", "#FFFFFF", image, .7, 0, false);
        Assert.ThrowsExactly<InvalidOperationException>(() => store.Export(scheme, output, options));
        Assert.AreEqual("preserve me", File.ReadAllText(output));
        store.Export(scheme, output, options with { AllowBackgroundRedistribution = true });
        using var stage = Store("target").StageImport(output);
        Assert.AreNotEqual(scheme.Manifest.Id, stage.Scheme.Manifest.Id);
        Assert.AreEqual("assets/user-background.png", stage.Scheme.Light.Backgrounds["quickPanel"].Path);
        Assert.AreEqual(stage.Scheme.Light.Backgrounds["quickPanel"].Path, stage.Scheme.Dark.Backgrounds["quickPanel"].Path);
        using ZipArchive archive = ZipFile.OpenRead(output);
        foreach (ZipArchiveEntry entry in archive.Entries.Where(entry => entry.FullName.EndsWith(".json")))
        {
            using var reader = new StreamReader(entry.Open()); string text = reader.ReadToEnd();
            Assert.IsFalse(text.Contains(_root) || text.Contains("private-owner") || text.Contains("translationApiKey"));
        }
    }

    [TestMethod]
    public void DuplicateJsonKeysAndNullBackgroundCannotCrashCatalog()
    {
        string definition = CreateDefinition("definition");
        string lightPath = Path.Combine(definition, "variants", "light.json");
        string saved = File.ReadAllText(lightPath);
        File.WriteAllText(lightPath, saved.Replace("\"cardOpacity\":0.9", "\"cardOpacity\":0.9,\"CardOpacity\":0.8"));
        Assert.ThrowsExactly<InvalidDataException>(() => Store("target").Read(definition));
        File.WriteAllText(lightPath, saved.Replace("\"quickPanel\":{", "\"unused\":null,\"quickPanel\":{"));
        Assert.ThrowsExactly<InvalidDataException>(() => Store("target").Read(definition));
    }

    private ThemePackageStore Store(string name) => new(Path.Combine(_root, name));

    [TestMethod]
    public void SameVersionDifferentContentNeedsSeparateCopyAndDoesNotOverwrite()
    {
        ThemePackageStore store = Store("target");
        string definition = CreateDefinition("definition");
        ThemeScheme original;
        using (var stage = store.StageImport(Archive(definition, "first.zip"))) original = store.Install(stage, false);
        File.WriteAllText(Path.Combine(definition, "SOURCE.md"), "Updated attribution.");
        string different = Archive(definition, "different.zip");
        using (var stage = store.StageImport(different)) Assert.ThrowsExactly<IOException>(() => store.Install(stage, true));
        using (var stage = store.StageImport(different))
        {
            ThemeScheme copy = store.Install(stage, false, installAsCopy: true);
            Assert.AreNotEqual(original.Key, copy.Key);
            Assert.AreEqual(original.Directory, store.Find(original.Key)!.Directory);
            Assert.AreEqual(2, store.Discover().Count);
        }
    }

    [TestMethod]
    public void AppearanceExportKeepsBothPalettesButExcludesClearedWallpaper()
    {
        ThemePackageStore store = Store("source"); ThemeScheme scheme = store.Read(CreateDefinition("definition"));
        string output = Path.Combine(_root, "appearance.zip");
        var appearance = new ThemeExportOverrides(.8, .85, "Ivory", "#FFFFFF", null, .45, 2, false,
            "Graphite", OverrideQuickPanelBackground: true, SettingsSurfaceOpacity: .65,
            SettingsFrostedGlassEnabled: true, SettingsFrostedGlassRadius: 9);
        store.Export(scheme, output, appearance);
        using var stage = Store("target").StageImport(output);
        foreach (ThemeVariant variant in new[] { stage.Scheme.Light, stage.Scheme.Dark })
        {
            Assert.AreEqual("Graphite", variant.BubbleStyle);
            Assert.IsFalse(variant.Backgrounds.ContainsKey("quickPanel"));
            Assert.AreEqual(.8, variant.CardOpacity);
            Assert.AreEqual(.65, variant.SettingsSurfaceOpacity);
            Assert.IsTrue(variant.SettingsFrostedGlassEnabled);
            Assert.AreEqual(9d, variant.SettingsFrostedGlassRadius);
        }
        Assert.AreNotEqual(stage.Scheme.Light.Colors["B.WindowBg"], stage.Scheme.Dark.Colors["B.WindowBg"]);
    }

    [TestMethod]
    public void TransparentTextAndUnsafeOverrideParametersAreRejected()
    {
        string definition = CreateDefinition("definition");
        ThemeVariant light = Variant("#FFFFFF"); light.Colors["B.TextPrimary"] = "#00101010";
        Write(Path.Combine(definition, "variants", "light.json"), light);
        ThemePackageStore store = Store("target");
        Assert.ThrowsExactly<InvalidDataException>(() => store.Read(definition));
        Write(Path.Combine(definition, "variants", "light.json"), Variant("#FFFFFF"));
        ThemeScheme scheme = store.Read(definition);
        string output = Path.Combine(_root, "bad.zip");
        Assert.ThrowsExactly<InvalidDataException>(() => store.Export(scheme, output,
            new ThemeExportOverrides(.9, 1, "Auto", "#FFFFFF", null, double.NaN, 0, false)));
        Assert.IsFalse(File.Exists(output));
    }

    [TestMethod]
    public void ExportRetainsCaseInsensitiveReferencesAndDoesNotReplaceOtherRoleArtwork()
    {
        string definition = CreateDefinition("definition");
        File.Move(Path.Combine(definition, "assets", "picture.png"), Path.Combine(definition, "assets", "user-background.png"));
        foreach (string name in new[] { "light", "dark" })
        {
            var variant = Variant(name == "light" ? "#FFFFFF" : "#101010");
            variant.Backgrounds["quickPanel"].Path = "assets/USER-BACKGROUND.png";
            variant.Backgrounds["workspace"] = new ThemeBackground { Path = "assets/user-background.png" };
            Write(Path.Combine(definition, "variants", name + ".json"), variant);
        }
        ThemePackageStore store = Store("source"); ThemeScheme scheme = store.Read(definition);
        string first = Path.Combine(_root, "original.zip"); store.Export(scheme, first);
        using (var original = Store("target").StageImport(first))
            Assert.IsTrue(File.Exists(Path.Combine(original.Directory, "assets", "user-background.png")));
        string customImage = Path.Combine(_root, "replacement.png"); File.WriteAllBytes(customImage, Pixel);
        string second = Path.Combine(_root, "replacement.zip");
        store.Export(scheme, second, new ThemeExportOverrides(.9, 1, "Auto", "#FFFFFF", customImage, .5, 0, true));
        using var exported = Store("next-target").StageImport(second);
        string quick = exported.Scheme.Light.Backgrounds["quickPanel"].Path;
        string workspace = exported.Scheme.Light.Backgrounds["workspace"].Path;
        Assert.AreNotEqual(workspace, quick);
        Assert.AreEqual("assets/user-background.png", workspace);
        Assert.IsTrue(File.Exists(ThemePackageStore.Resolve(exported.Directory, quick)));
        Assert.IsTrue(File.Exists(ThemePackageStore.Resolve(exported.Directory, workspace)));
    }

    [TestMethod]
    public void DamagedDecodeIsSkippedAndMalformedNewSettingsPreserveOtherPreferences()
    {
        string root = Path.Combine(_root, "catalog"); Directory.CreateDirectory(Path.Combine(root, "builtin"));
        Directory.Move(CreateDefinition("definition"), Path.Combine(root, "builtin", "example"));
        var store = new ThemePackageStore(root, _ => throw new FormatException("Invalid image stream."));
        Assert.AreEqual(0, store.Discover().Count);
        string settingsFile = Path.Combine(_root, "settings.json");
        File.WriteAllText(settingsFile, "{\"themePresetId\":{},\"themeUseCustomBackground\":\"bad\",\"themeAppearanceCustomized\":null,\"language\":\"ja-JP\",\"fontSizePercent\":125}");
        var settings = new SettingsStore(settingsFile); settings.Load();
        Assert.AreEqual("", settings.Current.ThemePresetId);
        Assert.IsTrue(settings.Current.ThemeUseCustomBackground);
        Assert.IsFalse(settings.Current.ThemeAppearanceCustomized);
        Assert.AreEqual(125, settings.Current.FontSizePercent);
        Assert.AreEqual("ja-JP", settings.Current.Language);
    }

    [TestMethod]
    public void BuiltInCatalogUsesStableIdOrderWithoutSelectingAPreset()
    {
        string root = Path.Combine(_root, "ordered-catalog");
        foreach (string id in new[] { "wisteria-reverie", "wisteria-atelier", "twilight-study", "sunlit-gallery" })
        {
            string directory = CreateDefinition("ordered-catalog/builtin/" + id);
            Write(Path.Combine(directory, "manifest.json"), new ThemeManifest { Id = id, Name = id });
        }
        var store = new ThemePackageStore(root);
        CollectionAssert.AreEqual(new[] { "builtin:sunlit-gallery", "builtin:twilight-study", "builtin:wisteria-atelier", "builtin:wisteria-reverie" },
            store.Discover().Select(scheme => scheme.Key).ToArray());
        Assert.AreEqual("", new AppSettings().ThemePresetId,
            "Adding or reordering bundled themes must not apply one on a fresh installation.");
    }

    [TestMethod]
    public void CatalogCancellationStopsImageValidationBeforePublishingResults()
    {
        string directory = CreateDefinition("catalog/builtin/example");
        File.WriteAllBytes(Path.Combine(directory, "assets", "unused.png"), Pixel);
        using var cancellation = new CancellationTokenSource();
        int decoded = 0;
        var store = new ThemePackageStore(Path.Combine(_root, "catalog"), _ =>
        {
            decoded++;
            cancellation.Cancel();
        });
        Assert.ThrowsExactly<OperationCanceledException>(() => store.Discover(cancellation.Token));
        Assert.AreEqual(1, decoded, "Cancellation must stop before decoding the next image.");
        Assert.ThrowsExactly<OperationCanceledException>(() => store.Discover(cancellation.Token));
        Assert.AreEqual(1, decoded);
    }

    private string CreateDefinition(string name)
    {
        string directory = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(directory, "variants")); Directory.CreateDirectory(Path.Combine(directory, "assets"));
        Write(Path.Combine(directory, "manifest.json"), new ThemeManifest { Id = "example", Name = "Example" });
        Write(Path.Combine(directory, "variants", "light.json"), Variant("#FFFFFF"));
        Write(Path.Combine(directory, "variants", "dark.json"), Variant("#101010"));
        File.WriteAllBytes(Path.Combine(directory, "assets", "picture.png"), Pixel);
        File.WriteAllText(Path.Combine(directory, "SOURCE.md"), "Test fixture artwork.");
        return directory;
    }

    private static ThemeVariant Variant(string color) => new()
    {
        Colors = ThemePackageStore.RequiredColors.ToDictionary(key => key, key =>
            key == "B.Accent" ? "#444444" : key == "B.OnAccent" ? "#FFFFFF" :
            key.Contains("Text", StringComparison.Ordinal) ? (color == "#FFFFFF" ? "#101010" : "#FFFFFF") : color),
        Backgrounds = new Dictionary<string, ThemeBackground> { ["quickPanel"] = new() { Path = "assets/picture.png" } },
    };

    private string Archive(string directory, string name)
    {
        string zip = Path.Combine(_root, name); ZipFile.CreateFromDirectory(directory, zip); return zip;
    }
    private static void Write<T>(string path, T value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
}
