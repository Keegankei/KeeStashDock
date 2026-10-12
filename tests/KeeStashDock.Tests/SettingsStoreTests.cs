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

        AssertSafeDataDefaults(store.Current);
        Assert.AreEqual("en-US", store.Current.Language);
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
        Assert.IsTrue(store.Current.BubbleTextGlow);
        Assert.AreEqual(0.65, store.Current.CardOpacity);
        using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(_settingsFile));
        Assert.IsFalse(saved.RootElement.TryGetProperty("edgeGlowEnabled", out _));
        Assert.IsFalse(saved.RootElement.TryGetProperty("edgeGlowStrength", out _));
        Assert.IsFalse(saved.RootElement.TryGetProperty("edgeGlowBrightness", out _));
    }

    [TestMethod]
    public void TranslationDefaultsAndCredentials_PreserveExistingProviderChoice()
    {
        var fresh = new SettingsStore(_settingsFile);
        fresh.Load();
        Assert.AreEqual("Bing", fresh.Current.TranslationEngine);
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
    }

    [TestMethod]
    public void ScreenshotShortcut_DefaultsAndDisabledChoiceRoundTrip()
    {
        File.WriteAllText(_settingsFile, """{"theme":"Dark"}""");
        var store = new SettingsStore(_settingsFile);
        store.Load();
        Assert.IsTrue(store.Current.ScreenshotHotkeyEnabled);
        Assert.AreEqual(3u, store.Current.ScreenshotHotkeyModifiers);
        Assert.AreEqual(0x53u, store.Current.ScreenshotHotkeyVirtualKey);
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
        Assert.AreEqual(3u, store.Current.ScreenshotHotkeyModifiers);
        Assert.AreEqual(0x53u, store.Current.ScreenshotHotkeyVirtualKey);
        store.Current.HotkeyModifiers = 3;
        store.Current.HotkeyVirtualKey = 0x53;
        store.ValidateAndNormalize();
        Assert.IsFalse(store.Current.ScreenshotHotkeyEnabled, "Keep the user's existing panel shortcut when the new default collides.");
        Assert.AreEqual(3u, store.Current.HotkeyModifiers);
        Assert.AreEqual(0x53u, store.Current.HotkeyVirtualKey);
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
