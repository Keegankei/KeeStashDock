using System.Text.Json;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Storage;

namespace KeeStashDock.Tests;

[TestClass]
public sealed class BackupSettingsMigrationTests
{
    [TestMethod]
    [DataRow(1, 60)]
    [DataRow(3, 180)]
    [DataRow(24, 1440)]
    [DataRow(48, 2880)]
    [DataRow(720, 43200)]
    [DataRow(0, 1440)]
    [DataRow(-3, 1440)]
    [DataRow(int.MaxValue, 1440)]
    public void AbsentMinutesMigratesLegacyHoursExactlyOrUsesDefault(int hours, int expected)
        => WithSettings($$"""{"autoBackupIntervalHours":{{hours}},"backupKeepCount":17,"theme":"Dark"}""", store =>
        {
            Assert.AreEqual(expected, store.Current.AutoBackupIntervalMinutes);
            Assert.AreEqual(17, store.Current.BackupKeepCount);
            Assert.AreEqual("Dark", store.Current.Theme);
        });

    [TestMethod]
    [DataRow("15", 15)]
    [DataRow("30", 30)]
    [DataRow("60", 60)]
    [DataRow("75", 75)]
    [DataRow("0", 1440)]
    [DataRow("-1", 1440)]
    [DataRow("2147483647", 1440)]
    [DataRow("null", 1440)]
    [DataRow("\"invalid\"", 1440)]
    public void ExplicitMinutesWinsIncludingInvalidValuesWithoutResettingOtherPreferences(string value, int expected)
        => WithSettings($$"""{"autoBackupIntervalHours":48,"autoBackupIntervalMinutes":{{value}},"theme":"Dark"}""", store =>
        {
            Assert.AreEqual(expected, store.Current.AutoBackupIntervalMinutes);
            Assert.AreEqual("Dark", store.Current.Theme);
        });

    [TestMethod]
    public void FreshInstallKeeps24HoursAndSevenSnapshots()
        => WithSettings("{}", store =>
        {
            Assert.IsTrue(store.Current.AutoBackupEnabled);
            Assert.AreEqual(AppSettings.DefaultAutoBackupIntervalMinutes, store.Current.AutoBackupIntervalMinutes);
            Assert.AreEqual(7, store.Current.BackupKeepCount);
        });

    private static void WithSettings(string json, Action<SettingsStore> assertion)
    {
        string root = Path.Combine(Path.GetTempPath(), "KeeStashDock.Tests", "BackupMigration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "settings.json");
        try
        {
            File.WriteAllText(file, json);
            var store = new SettingsStore(file);
            store.Load();
            assertion(store);
            store.Save();
            using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(file));
            Assert.IsTrue(saved.RootElement.TryGetProperty("autoBackupIntervalMinutes", out _));
            var reloaded = new SettingsStore(file);
            reloaded.Load();
            assertion(reloaded);
        }
        finally { Directory.Delete(root, true); }
    }
}
