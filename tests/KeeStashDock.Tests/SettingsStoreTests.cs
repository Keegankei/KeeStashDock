using System.Text.Json;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Storage;

namespace KeeStashDock.Tests;

[TestClass]
[DoNotParallelize]
public sealed class SettingsStoreTests
{
    private static readonly string TempParent = Path.Combine(
        Path.GetTempPath(),
        "KeeStashDock.Tests",
        "SettingsStore");

    private string _root = null!;
    private string _settingsFile = null!;

    [TestInitialize]
    public void Initialize()
    {
        Directory.CreateDirectory(TempParent);
        _root = Path.Combine(TempParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _settingsFile = Path.Combine(_root, "settings.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(0)]
    [DataRow(int.MaxValue)]
    public void IntegerLimits_AreNormalizedOnLoad(int value)
    {
        File.WriteAllText(_settingsFile, $$"""
            {
              "trashRetentionDays": {{value}},
              "backupKeepCount": {{value}},
              "autoBackupIntervalHours": {{value}}
            }
            """);
        var store = new SettingsStore(_settingsFile);

        store.Load();

        if (value <= 0)
        {
            Assert.AreEqual(
                AppSettings.DefaultTrashRetentionDays,
                store.Current.TrashRetentionDays);
            Assert.AreEqual(
                AppSettings.DefaultBackupKeepCount,
                store.Current.BackupKeepCount);
            Assert.AreEqual(
                AppSettings.DefaultAutoBackupIntervalHours,
                store.Current.AutoBackupIntervalHours);
        }
        else
        {
            Assert.AreEqual(
                AppSettings.MaxTrashRetentionDays,
                store.Current.TrashRetentionDays);
            Assert.AreEqual(
                AppSettings.MaxBackupKeepCount,
                store.Current.BackupKeepCount);
            Assert.AreEqual(
                AppSettings.MaxAutoBackupIntervalHours,
                store.Current.AutoBackupIntervalHours);
        }
    }

    [TestMethod]
    public void CorruptJson_FallsBackToSafeDefaults()
    {
        File.WriteAllText(_settingsFile, "{ definitely not json");
        var store = new SettingsStore(_settingsFile);

        store.Load();

        AssertFreshInstallDefaults(store.Current);
    }

    [TestMethod]
    public void FontSize_MissingPreferencePreservesOriginalTypographyAndRoundTrips()
    {
        File.WriteAllText(_settingsFile, """{"language":"zh-CN","theme":"Dark"}""");
        var store = new SettingsStore(_settingsFile);
        store.Load();
        Assert.AreEqual(AppSettings.DefaultFontSizePercent, store.Current.FontSizePercent);

        store.Current.FontSizePercent = 130;
        store.Save();
        var reloaded = new SettingsStore(_settingsFile);
        reloaded.Load();
        Assert.AreEqual(130, reloaded.Current.FontSizePercent);
        Assert.AreEqual("zh-CN", reloaded.Current.Language);
        Assert.AreEqual("Dark", reloaded.Current.Theme);
    }

    [TestMethod]
    [DataRow(-1, 90)]
    [DataRow(0, 90)]
    [DataRow(90, 90)]
    [DataRow(125, 125)]
    [DataRow(150, 150)]
    [DataRow(int.MaxValue, 150)]
    public void FontSize_ClampsOnLoadAndSave(int value, int expected)
    {
        File.WriteAllText(_settingsFile, $$"""{"fontSizePercent":{{value}},"theme":"Dark"}""");
        var store = new SettingsStore(_settingsFile);
        store.Load();
        Assert.AreEqual(expected, store.Current.FontSizePercent);
        store.Current.FontSizePercent = value;
        store.Save();
        var reloaded = new SettingsStore(_settingsFile);
        reloaded.Load();
        Assert.AreEqual(expected, reloaded.Current.FontSizePercent);
        Assert.AreEqual("Dark", reloaded.Current.Theme);
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("\"large\"")]
    [DataRow("true")]
    [DataRow("125.5")]
    [DataRow("999999999999")]
    [DataRow("{}")]
    public void FontSize_MalformedPreferenceKeepsUnrelatedPreferences(string value)
    {
        File.WriteAllText(_settingsFile, $$"""{"fontSizePercent":{{value}},"theme":"Dark","language":"zh-CN"}""");
        var store = new SettingsStore(_settingsFile);
        store.Load();
        Assert.AreEqual(AppSettings.DefaultFontSizePercent, store.Current.FontSizePercent);
        Assert.AreEqual("Dark", store.Current.Theme);
        Assert.AreEqual("zh-CN", store.Current.Language);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("{}")]
    [DataRow("null")]
    public void FreshInstallOrEmptySettings_LoadTheReleaseDefaults(string? json)
    {
        if (json is not null) File.WriteAllText(_settingsFile, json);
        var store = new SettingsStore(_settingsFile);
        store.Load();

        AssertFreshInstallDefaults(store.Current);
        store.Save();
        var reloaded = new SettingsStore(_settingsFile);
        reloaded.Load();
        AssertFreshInstallDefaults(reloaded.Current);
    }

    [TestMethod]
    public void ExistingPreferences_ArePreservedWhenReleaseDefaultsChange()
    {
        const string json = """
            {
              "language": "zh-CN",
              "hotkeyModifiers": 3,
              "hotkeyVirtualKey": 81,
              "screenshotHotkeyEnabled": false,
              "screenshotHotkeyModifiers": 3,
              "screenshotHotkeyVirtualKey": 83,
              "autoStart": true,
              "selectionPopupEnabled": false,
              "selectionAction": "Both",
              "translationEngine": "Bing",
              "selectionBubbleStyle": "FollowTheme",
              "clipboardWatchEnabled": true,
              "directPasteEnabled": true,
              "panelEdgeAutoHideEnabled": false,
              "collapseDelaySeconds": 5,
              "expandDelaySeconds": 0.5,
              "theme": "Dark",
              "floatingPanelEnabled": true,
              "floatingPanelFollowPointer": false,
              "floatingInspectionGesture": "Middle",
              "panelTextColorMode": "Ivory",
              "cardOpacity": 0.8,
              "backgroundImagePath": "C:\\Wallpapers\\existing.png",
              "backgroundOverlayOpacity": 0.3,
              "frostedGlassEnabled": true,
              "frostedGlassRadius": 8,
              "autoBackupEnabled": false,
              "autoBackupIntervalHours": 48
            }
            """;
        File.WriteAllText(_settingsFile, json);
        var store = new SettingsStore(_settingsFile);
        store.Load();
        store.Save();
        var reloaded = new SettingsStore(_settingsFile);
        reloaded.Load();
        reloaded.Save();

        using JsonDocument original = JsonDocument.Parse(json);
        using JsonDocument persisted = JsonDocument.Parse(File.ReadAllText(_settingsFile));
        foreach (JsonProperty preference in original.RootElement.EnumerateObject())
        {
            Assert.IsTrue(JsonElement.DeepEquals(preference.Value,
                persisted.RootElement.GetProperty(preference.Name)),
                $"The release defaults must not replace the saved {preference.Name} preference.");
        }
    }

    [TestMethod]
    public void MissingFields_KeepPropertyDefaults()
    {
        File.WriteAllText(_settingsFile, """
            {
              "theme": "Dark"
            }
            """);
        var store = new SettingsStore(_settingsFile);

        store.Load();

        Assert.AreEqual("Dark", store.Current.Theme);
        AssertSafeDataDefaults(store.Current);
        Assert.AreEqual(2.0, store.Current.CollapseDelaySeconds);
        Assert.AreEqual(0.1, store.Current.ExpandDelaySeconds);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FloatingPanel_LegacySettingsKeepModeAndUseSafeInspectionDefaults(bool floatingEnabled)
    {
        File.WriteAllText(_settingsFile, $$"""
            { "theme": "Dark", "floatingPanelEnabled": {{floatingEnabled.ToString().ToLowerInvariant()}} }
            """);
        var store = new SettingsStore(_settingsFile);

        store.Load();

        Assert.AreEqual(floatingEnabled, store.Current.FloatingPanelEnabled);
        Assert.IsTrue(store.Current.FloatingPanelFollowPointer,
            "The existing floating preference remains compatible with deliberate drag inspection.");
        Assert.AreEqual("Right", store.Current.FloatingInspectionGesture);
        Assert.AreEqual("Left", store.Current.FloatingInspectionButton);
        Assert.AreEqual("Alt", store.Current.FloatingInspectionModifiers);
        Assert.IsTrue(store.Current.PanelEdgeAutoHideEnabled);
        Assert.IsNull(store.Current.PanelDevicePosX);
        Assert.IsNull(store.Current.PanelDevicePosY);
        Assert.AreEqual("Dark", store.Current.Theme);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FloatingPanel_PerspectiveChoiceRoundTripsAcrossClassicMode(bool followPointer)
    {
        var store = new SettingsStore(_settingsFile);
        store.Current.FloatingPanelEnabled = true;
        store.Current.FloatingPanelFollowPointer = followPointer;
        store.Save();

        var reloaded = new SettingsStore(_settingsFile);
        reloaded.Load();
        Assert.IsTrue(reloaded.Current.FloatingPanelEnabled);
        Assert.AreEqual(followPointer, reloaded.Current.FloatingPanelFollowPointer);

        reloaded.Current.FloatingPanelEnabled = false;
        reloaded.Save();
        var classic = new SettingsStore(_settingsFile);
        classic.Load();
        Assert.IsFalse(classic.Current.FloatingPanelEnabled);
        Assert.AreEqual(followPointer, classic.Current.FloatingPanelFollowPointer,
            "Switching to Classic must retain the preferred floating perspective mode.");
    }


    [TestMethod]
    public void InspectionGestures_AllSupportedCustomCombinationsRoundTrip()
    {
        var store = new SettingsStore(_settingsFile);
        store.Current.FloatingInspectionGesture = "Custom";
        store.Current.PanelEdgeAutoHideEnabled = true;
        foreach (string button in new[] { "Left", "Right", "Middle", "XButton1", "XButton2" })
        foreach (string modifiers in new[] { "None", "Control", "Alt", "Shift", "Control+Alt", "Control+Shift", "Alt+Shift", "Control+Alt+Shift" })
        {
            store.Current.FloatingInspectionButton = button;
            store.Current.FloatingInspectionModifiers = modifiers;
            store.Save();
            var reloaded = new SettingsStore(_settingsFile);
            reloaded.Load();
            Assert.AreEqual("Custom", reloaded.Current.FloatingInspectionGesture);
            Assert.AreEqual(button, reloaded.Current.FloatingInspectionButton);
            Assert.AreEqual(modifiers, reloaded.Current.FloatingInspectionModifiers);
            Assert.IsTrue(reloaded.Current.PanelEdgeAutoHideEnabled);
        }
        foreach (string preset in new[] { "Right", "Middle" })
        {
            store.Current.FloatingInspectionGesture = preset;
            store.Save();
            var reloaded = new SettingsStore(_settingsFile);
            reloaded.Load();
            Assert.AreEqual(preset, reloaded.Current.FloatingInspectionGesture);
            Assert.AreEqual("XButton2", reloaded.Current.FloatingInspectionButton);
            Assert.AreEqual("Control+Alt+Shift", reloaded.Current.FloatingInspectionModifiers,
                "A preset must preserve the user's saved custom combination.");
        }
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("unknown")]
    [DataRow("right")]
    public void InvalidInspectionFields_LoadSafeDefaultsWithoutResettingOtherSettings(string? invalid)
    {
        File.WriteAllText(_settingsFile, JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["theme"] = "Dark",
            ["floatingPanelFollowPointer"] = false,
            ["floatingInspectionGesture"] = invalid,
            ["floatingInspectionButton"] = invalid,
            ["floatingInspectionModifiers"] = invalid,
        }));
        var store = new SettingsStore(_settingsFile);
        store.Load();
        Assert.AreEqual("Right", store.Current.FloatingInspectionGesture);
        Assert.AreEqual("Left", store.Current.FloatingInspectionButton);
        Assert.AreEqual("Alt", store.Current.FloatingInspectionModifiers);
        Assert.AreEqual("Dark", store.Current.Theme);
        Assert.IsFalse(store.Current.FloatingPanelFollowPointer);
    }

    [TestMethod]
    [DataRow(-100_000, 100_000)]
    [DataRow(-3840, -240)]
    [DataRow(0, 0)]
    public void ValidPhysicalMonitorCoordinates_RoundTrip(int x, int y)
    {
        var store = new SettingsStore(_settingsFile);
        store.Current.PanelDevicePosX = x;
        store.Current.PanelDevicePosY = y;
        store.Save();
        var reloaded = new SettingsStore(_settingsFile);
        reloaded.Load();
        Assert.AreEqual(x, reloaded.Current.PanelDevicePosX);
        Assert.AreEqual(y, reloaded.Current.PanelDevicePosY);
    }

    [TestMethod]
    [DataRow(100_001, 0)]
    [DataRow(0, -100_001)]
    [DataRow(int.MinValue, int.MaxValue)]
    public void InvalidPhysicalMonitorCoordinates_LoadAsMissingPair(int x, int y)
    {
        File.WriteAllText(_settingsFile, $$"""
            { "panelDevicePosX": {{x}}, "panelDevicePosY": {{y}}, "panelPosX": 400, "panelPosY": 120 }
            """);
        var store = new SettingsStore(_settingsFile);
        store.Load();
        Assert.IsNull(store.Current.PanelDevicePosX);
        Assert.IsNull(store.Current.PanelDevicePosY);
        Assert.AreEqual(400d, store.Current.PanelPosX);
        Assert.AreEqual(120d, store.Current.PanelPosY);
    }

    [TestMethod]
    public void Save_NormalizesInvalidInspectionAndIncompletePhysicalLocation()
    {
        var store = new SettingsStore(_settingsFile);
        store.Current.FloatingInspectionGesture = null!;
        store.Current.FloatingInspectionButton = "Back";
        store.Current.FloatingInspectionModifiers = "Win";
        store.Current.PanelDevicePosX = -3840;
        store.Current.PanelDevicePosY = null;
        store.Save();
        var reloaded = new SettingsStore(_settingsFile);
        reloaded.Load();
        Assert.AreEqual("Right", reloaded.Current.FloatingInspectionGesture);
        Assert.AreEqual("Left", reloaded.Current.FloatingInspectionButton);
        Assert.AreEqual("Alt", reloaded.Current.FloatingInspectionModifiers);
        Assert.IsNull(reloaded.Current.PanelDevicePosX);
        Assert.IsNull(reloaded.Current.PanelDevicePosY);
        Assert.IsFalse(File.Exists(_settingsFile + ".tmp"));
    }

    [TestMethod]
    public void ExtremeFloatingPointAndGeometryValues_AreBounded()
    {
        File.WriteAllText(_settingsFile, """
            {
              "collapseDelaySeconds": 1e300,
              "expandDelaySeconds": -5,
              "panelWidth": 1e300,
              "backgroundOverlayOpacity": 50,
              "frostedGlassRadius": 1000,
              "cardOpacity": 1000,
              "bubbleOpacity": 1000,
              "panelPosX": 1e300,
              "panelPosY": -1e300,
              "panelUserWidth": -1,
              "panelUserHeight": 1e300
            }
            """);
        var store = new SettingsStore(_settingsFile);

        store.Load();

        Assert.AreEqual(60.0, store.Current.CollapseDelaySeconds);
        Assert.AreEqual(0.1, store.Current.ExpandDelaySeconds);
        Assert.AreEqual(2000.0, store.Current.PanelWidth);
        Assert.AreEqual(1.0, store.Current.BackgroundOverlayOpacity);
        Assert.AreEqual(18.0, store.Current.FrostedGlassRadius);
        Assert.AreEqual(1.0, store.Current.CardOpacity);
        Assert.AreEqual(1.0, store.Current.BubbleOpacity);
        Assert.IsNull(store.Current.PanelPosX);
        Assert.IsNull(store.Current.PanelPosY);
        Assert.IsNull(store.Current.PanelUserWidth);
        Assert.AreEqual(4000.0, store.Current.PanelUserHeight);
    }

    [TestMethod]
    public void SettingsSurfaceOpacityPreservesExistingAppearanceAndRoundTripsIndependently()
    {
        File.WriteAllText(_settingsFile, """{ "cardOpacity": 0.65 }""");
        var store = new SettingsStore(_settingsFile);
        store.Load();
        Assert.AreEqual(1.0, store.Current.SettingsSurfaceOpacity,
            "Older settings must retain opaque Settings surfaces.");
        store.Current.SettingsSurfaceOpacity = .55;
        store.Save();
        var reloaded = new SettingsStore(_settingsFile);
        reloaded.Load();
        Assert.AreEqual(.55, reloaded.Current.SettingsSurfaceOpacity);
        Assert.AreEqual(.65, reloaded.Current.CardOpacity);
    }

    [TestMethod]
    [DataRow(double.NaN, 1.0)]
    [DataRow(double.PositiveInfinity, 1.0)]
    [DataRow(-1.0, 1.0)]
    [DataRow(0.0, 1.0)]
    [DataRow(0.05, 0.1)]
    [DataRow(1.5, 1.0)]
    public void SettingsSurfaceOpacityNormalizesBeforeSaving(double value, double expected)
    {
        var store = new SettingsStore(_settingsFile);
        store.Load();
        store.Current.SettingsSurfaceOpacity = value;
        store.Save();
        var reloaded = new SettingsStore(_settingsFile);
        reloaded.Load();
        Assert.AreEqual(expected, reloaded.Current.SettingsSurfaceOpacity);
    }

    [TestMethod]
    public void FrostedGlassSettingsDefaultOffAndPersistIndependently()
    {
        File.WriteAllText(_settingsFile, """{ "frostedGlassEnabled": true, "frostedGlassRadius": 4 }""");
        var store = new SettingsStore(_settingsFile); store.Load();
        Assert.IsFalse(store.Current.SettingsFrostedGlassEnabled);
        Assert.AreEqual(8d, store.Current.SettingsFrostedGlassRadius);
        store.Current.SettingsFrostedGlassEnabled = true;
        store.Current.SettingsFrostedGlassRadius = 12;
        store.Save();
        var loaded = new SettingsStore(_settingsFile); loaded.Load();
        Assert.IsTrue(loaded.Current.SettingsFrostedGlassEnabled);
        Assert.AreEqual(12d, loaded.Current.SettingsFrostedGlassRadius);
        Assert.IsTrue(loaded.Current.FrostedGlassEnabled);
        Assert.AreEqual(4d, loaded.Current.FrostedGlassRadius);
    }

    [TestMethod]
    public void RetiredBubbleEffects_AreIgnoredWithoutResettingSupportedAppearance()
    {
        File.WriteAllText(_settingsFile, """
            {
              "theme": "Dark",
              "selectionBubbleStyle": "Lavender",
              "bubbleTextGlow": true,
              "bubbleShadow": true,
              "bubbleFrostedGlassEnabled": true,
              "bubbleOpacity": 0.75,
              "cardOpacity": 0.65,
              "settingsSurfaceOpacity": 0.6,
              "settingsFrostedGlassEnabled": true,
              "settingsFrostedGlassRadius": 12,
              "frostedGlassEnabled": true,
              "frostedGlassRadius": 5
            }
            """);
        var store = new SettingsStore(_settingsFile);
        store.Load();
        Assert.AreEqual("Dark", store.Current.Theme);
        Assert.AreEqual("Lavender", store.Current.SelectionBubbleStyle);
        Assert.AreEqual(.75, store.Current.BubbleOpacity);
        Assert.AreEqual(.65, store.Current.CardOpacity);
        Assert.AreEqual(.6, store.Current.SettingsSurfaceOpacity);
        Assert.IsTrue(store.Current.SettingsFrostedGlassEnabled);
        Assert.AreEqual(12d, store.Current.SettingsFrostedGlassRadius);
        Assert.IsTrue(store.Current.FrostedGlassEnabled);
        Assert.AreEqual(5d, store.Current.FrostedGlassRadius);

        store.Save();
        using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(_settingsFile));
        foreach (string retired in new[] { "bubbleTextGlow", "bubbleShadow", "bubbleFrostedGlassEnabled" })
            Assert.IsFalse(saved.RootElement.TryGetProperty(retired, out _),
                "Retired effects must not be carried into newly saved settings.");
        var reloaded = new SettingsStore(_settingsFile);
        reloaded.Load();
        Assert.AreEqual(.75, reloaded.Current.BubbleOpacity);
        Assert.AreEqual(.65, reloaded.Current.CardOpacity);
        Assert.AreEqual(.6, reloaded.Current.SettingsSurfaceOpacity);
        Assert.IsTrue(reloaded.Current.SettingsFrostedGlassEnabled);
        Assert.AreEqual(12d, reloaded.Current.SettingsFrostedGlassRadius);
        Assert.IsTrue(reloaded.Current.FrostedGlassEnabled);
        Assert.AreEqual(5d, reloaded.Current.FrostedGlassRadius);
    }

    [TestMethod]
    [DataRow(double.NaN, 8d)]
    [DataRow(double.PositiveInfinity, 8d)]
    [DataRow(0d, 8d)]
    [DataRow(.5d, 1d)]
    [DataRow(30d, 18d)]
    public void SettingsFrostRadiusIsNormalizedBeforeSerialization(double value, double expected)
    {
        var store = new SettingsStore(_settingsFile); store.Load();
        store.Current.SettingsFrostedGlassRadius = value;
        store.Save();
        var loaded = new SettingsStore(_settingsFile); loaded.Load();
        Assert.AreEqual(expected, loaded.Current.SettingsFrostedGlassRadius);
    }

    [TestMethod]
    public void RetiredEdgeGlowFields_DoNotResetExistingSettings()
    {
        File.WriteAllText(_settingsFile, """
            {
              "theme": "Dark",
              "edgeGlowEnabled": true,
              "edgeGlowStrength": 24,
              "edgeGlowBrightness": 0.8,
              "bubbleTextGlow": true,
              "cardOpacity": 0.65
            }
            """);
        var store = new SettingsStore(_settingsFile);
        store.Load();
        store.Save();

        Assert.AreEqual("Dark", store.Current.Theme);
        Assert.AreEqual(0.65, store.Current.CardOpacity);
        using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(_settingsFile));
        Assert.IsFalse(saved.RootElement.TryGetProperty("edgeGlowEnabled", out _));
        Assert.IsFalse(saved.RootElement.TryGetProperty("edgeGlowStrength", out _));
        Assert.IsFalse(saved.RootElement.TryGetProperty("edgeGlowBrightness", out _));
        Assert.IsFalse(saved.RootElement.TryGetProperty("bubbleTextGlow", out _));
    }

    [TestMethod]
    public void TranslationDefaultsAndCredentials_PreserveExistingProviderChoice()
    {
        var fresh = new SettingsStore(_settingsFile);
        fresh.Load();
        Assert.AreEqual("Google", fresh.Current.TranslationEngine);
        Assert.AreEqual(AppSettings.DefaultOfflineTranslationEndpoint, fresh.Current.OfflineTranslationEndpoint);
        Assert.AreEqual(string.Empty, fresh.Current.AliyunAccessKeyId);
        Assert.AreEqual(string.Empty, fresh.Current.AliyunAccessKeySecret);

        File.WriteAllText(_settingsFile, """
            { "translationEngine": "DeepL", "translationApiKey": "test-deepl:fx" }
            """);
        var existing = new SettingsStore(_settingsFile);
        existing.Load();
        Assert.AreEqual("DeepL", existing.Current.TranslationEngine);
        Assert.AreEqual(string.Empty, existing.Current.BaiduTranslationApiKey);
        existing.Current.TranslationAppId = "test-app";
        existing.Current.BaiduTranslationApiKey = "test-baidu";
        existing.Current.TranslationEngine = "Baidu";
        existing.Save();

        var reloaded = new SettingsStore(_settingsFile);
        reloaded.Load();
        Assert.AreEqual("Baidu", reloaded.Current.TranslationEngine);
        Assert.AreEqual("test-app", reloaded.Current.TranslationAppId);
        Assert.AreEqual("test-baidu", reloaded.Current.BaiduTranslationApiKey);
        Assert.AreEqual("test-deepl:fx", reloaded.Current.TranslationApiKey);

        reloaded.Current.AliyunAccessKeyId = "test-aliyun-id";
        reloaded.Current.AliyunAccessKeySecret = "test-aliyun-secret";
        reloaded.Current.OfflineTranslationEndpoint = "http://127.0.0.1:5010";
        reloaded.Current.TranslationEngine = "Offline";
        reloaded.Save();
        var offline = new SettingsStore(_settingsFile);
        offline.Load();
        Assert.AreEqual("Offline", offline.Current.TranslationEngine);
        Assert.AreEqual("test-aliyun-id", offline.Current.AliyunAccessKeyId);
        Assert.AreEqual("test-aliyun-secret", offline.Current.AliyunAccessKeySecret);
        Assert.AreEqual("http://127.0.0.1:5010", offline.Current.OfflineTranslationEndpoint);
        Assert.AreEqual("test-deepl:fx", offline.Current.TranslationApiKey);
        Assert.AreEqual("test-baidu", offline.Current.BaiduTranslationApiKey);
    }

    [TestMethod]
    public void Save_NormalizesMutatedValuesBeforeAtomicWrite()
    {
        var store = new SettingsStore(_settingsFile);
        store.Load();
        store.Current.TrashRetentionDays = 0;
        store.Current.BackupKeepCount = -10;
        store.Current.AutoBackupIntervalHours = int.MaxValue;
        store.Current.CollapseDelaySeconds = double.NaN;
        store.Current.BackgroundOverlayOpacity = double.NaN;
        store.Current.TranslationEngine = null!;
        store.Current.SelectionBubbleStyle = " ";
        store.Current.TranslationApiKey = null!;
        store.Current.AliyunAccessKeyId = null!;
        store.Current.AliyunAccessKeySecret = null!;
        store.Current.OfflineTranslationEndpoint = null!;

        store.Save();

        Assert.IsTrue(File.Exists(_settingsFile));
        Assert.IsFalse(File.Exists(_settingsFile + ".tmp"));
        using JsonDocument persisted = JsonDocument.Parse(File.ReadAllText(_settingsFile));
        Assert.AreEqual(
            AppSettings.DefaultTrashRetentionDays,
            persisted.RootElement.GetProperty("trashRetentionDays").GetInt32());
        Assert.AreEqual(
            AppSettings.DefaultBackupKeepCount,
            persisted.RootElement.GetProperty("backupKeepCount").GetInt32());
        Assert.AreEqual(
            AppSettings.MaxAutoBackupIntervalHours,
            persisted.RootElement.GetProperty("autoBackupIntervalHours").GetInt32());
        Assert.AreEqual(
            2.0,
            persisted.RootElement.GetProperty("collapseDelaySeconds").GetDouble());
        Assert.AreEqual(
            string.Empty,
            persisted.RootElement.GetProperty("translationApiKey").GetString());
        Assert.AreEqual(string.Empty, store.Current.AliyunAccessKeyId);
        Assert.AreEqual(string.Empty, store.Current.AliyunAccessKeySecret);
        Assert.AreEqual(AppSettings.DefaultOfflineTranslationEndpoint, store.Current.OfflineTranslationEndpoint);
        Assert.AreEqual(0.2, store.Current.BackgroundOverlayOpacity);
        Assert.AreEqual("Google", store.Current.TranslationEngine);
        Assert.AreEqual("Default", store.Current.SelectionBubbleStyle);
    }

    [TestMethod]
    public void ScreenshotShortcut_DefaultsAndDisabledChoiceRoundTrip()
    {
        File.WriteAllText(_settingsFile, """{"theme":"Dark"}""");
        var store = new SettingsStore(_settingsFile);
        store.Load();
        Assert.IsTrue(store.Current.ScreenshotHotkeyEnabled);
        Assert.AreEqual(1u, store.Current.ScreenshotHotkeyModifiers);
        Assert.AreEqual(0x57u, store.Current.ScreenshotHotkeyVirtualKey);
        store.Current.ScreenshotHotkeyEnabled = false;
        store.Current.ScreenshotHotkeyModifiers = 6;
        store.Current.ScreenshotHotkeyVirtualKey = 0x54;
        store.Save();
        var reload = new SettingsStore(_settingsFile);
        reload.Load();
        Assert.IsFalse(reload.Current.ScreenshotHotkeyEnabled);
        Assert.AreEqual(6u, reload.Current.ScreenshotHotkeyModifiers);
        Assert.AreEqual(0x54u, reload.Current.ScreenshotHotkeyVirtualKey);
    }

    [TestMethod]
    public void ScreenshotShortcut_InvalidOrDuplicateSettingsAreMadeSafe()
    {
        var store = new SettingsStore(_settingsFile);
        store.Current.ScreenshotHotkeyModifiers = uint.MaxValue;
        store.Current.ScreenshotHotkeyVirtualKey = 0;
        store.ValidateAndNormalize();
        Assert.AreEqual(1u, store.Current.ScreenshotHotkeyModifiers);
        Assert.AreEqual(0x57u, store.Current.ScreenshotHotkeyVirtualKey);
        store.Current.HotkeyModifiers = 1;
        store.Current.HotkeyVirtualKey = 0x57;
        store.ValidateAndNormalize();
        Assert.IsFalse(store.Current.ScreenshotHotkeyEnabled, "Keep the user's existing panel shortcut when the new default collides.");
        Assert.AreEqual(1u, store.Current.HotkeyModifiers);
        Assert.AreEqual(0x57u, store.Current.HotkeyVirtualKey);
    }

    private static void AssertFreshInstallDefaults(AppSettings settings)
    {
        AssertSafeDataDefaults(settings);
        Assert.AreEqual("en-US", settings.Language);
        Assert.AreEqual(AppSettings.DefaultFontSizePercent, settings.FontSizePercent);
        Assert.AreEqual(1u, settings.HotkeyModifiers);
        Assert.AreEqual(0x51u, settings.HotkeyVirtualKey);
        Assert.IsTrue(settings.ScreenshotHotkeyEnabled);
        Assert.AreEqual(1u, settings.ScreenshotHotkeyModifiers);
        Assert.AreEqual(0x57u, settings.ScreenshotHotkeyVirtualKey);
        Assert.IsFalse(settings.AutoStart);
        Assert.IsTrue(settings.SelectionPopupEnabled);
        Assert.AreEqual("Capture", settings.SelectionAction);
        Assert.AreEqual("Google", settings.TranslationEngine);
        Assert.AreEqual("Default", settings.SelectionBubbleStyle);
        Assert.IsFalse(settings.ClipboardWatchEnabled);
        Assert.IsFalse(settings.DirectPasteEnabled);
        Assert.IsTrue(settings.PanelEdgeAutoHideEnabled);
        Assert.AreEqual(2.0, settings.CollapseDelaySeconds);
        Assert.AreEqual(0.1, settings.ExpandDelaySeconds);
        Assert.AreEqual("System", settings.Theme);
        Assert.IsFalse(settings.FloatingPanelEnabled);
        Assert.IsTrue(settings.FloatingPanelFollowPointer);
        Assert.AreEqual("Right", settings.FloatingInspectionGesture);
        Assert.AreEqual("Auto", settings.PanelTextColorMode);
        Assert.AreEqual(0.9, settings.CardOpacity);
        Assert.AreEqual(1.0, settings.SettingsSurfaceOpacity);
        Assert.IsNull(settings.BackgroundImagePath);
        Assert.AreEqual(0.2, settings.BackgroundOverlayOpacity);
        Assert.IsFalse(settings.FrostedGlassEnabled);
        Assert.AreEqual(4.0, settings.FrostedGlassRadius);
        Assert.IsTrue(settings.AutoBackupEnabled);
        Assert.AreEqual(24, settings.AutoBackupIntervalHours);
    }

    private static void AssertSafeDataDefaults(AppSettings settings)
    {
        Assert.AreEqual(
            AppSettings.DefaultTrashRetentionDays,
            settings.TrashRetentionDays);
        Assert.AreEqual(
            AppSettings.DefaultBackupKeepCount,
            settings.BackupKeepCount);
        Assert.AreEqual(
            AppSettings.DefaultAutoBackupIntervalHours,
            settings.AutoBackupIntervalHours);
    }
}
