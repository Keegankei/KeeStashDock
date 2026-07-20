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
              "edgeGlowStrength": 1000,
              "edgeGlowBrightness": 1000,
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
        Assert.AreEqual(30.0, store.Current.EdgeGlowStrength);
        Assert.AreEqual(1.0, store.Current.EdgeGlowBrightness);
        Assert.AreEqual(1.0, store.Current.CardOpacity);
        Assert.AreEqual(1.0, store.Current.BubbleOpacity);
        Assert.IsNull(store.Current.PanelPosX);
        Assert.IsNull(store.Current.PanelPosY);
        Assert.IsNull(store.Current.PanelUserWidth);
        Assert.AreEqual(4000.0, store.Current.PanelUserHeight);
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
