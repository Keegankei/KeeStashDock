using KeeStashDock.Core.Services;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Storage;

namespace KeeStashDock.Tests;

[TestClass]
public sealed class TranslationTargetAndResultTests
{
    [TestMethod]
    public void IndependentAndTemporaryTargetsNeverChangeTheInterfaceOrGlobalDefault()
    {
        var cfg = new AppSettings { Language = "en-US" };
        Assert.AreEqual("en-US", TranslationTargets.Resolve(cfg));
        cfg.Language = "ja-JP";
        Assert.AreEqual("ja-JP", TranslationTargets.Resolve(cfg));
        cfg.TranslationTargetLanguage = "zh-CN";
        Assert.AreEqual("zh-CN", TranslationTargets.Resolve(cfg));
        Assert.AreEqual("de-DE", TranslationTargets.Resolve(cfg, "de-DE"));
        Assert.AreEqual("zh-CN", cfg.TranslationTargetLanguage);
        Assert.AreEqual("ja-JP", cfg.Language);
        Assert.AreEqual("zh-CN", TranslationTargets.Resolve(cfg));
    }

    [TestMethod]
    public void ManagedOfflineCatalogDoesNotPromiseUninstalledLanguageDirections()
    {
        Assert.IsTrue(TranslationTargets.IsOffered("Offline", "zh-CN"));
        Assert.IsTrue(TranslationTargets.IsOffered("Offline", "en-US"));
        Assert.IsFalse(TranslationTargets.IsOffered("Offline", "ja-JP"));
        Assert.IsFalse(TranslationTargets.IsOffered("Offline", "zh-TW"));
        Assert.IsTrue(TranslationTargets.IsOffered("Offline", "ja-JP", managedOffline: false));
        Assert.IsFalse(TranslationTargets.IsOffered("Google", "xx-XX"));
    }

    [TestMethod]
    [DataRow("{}", "FollowInterface")]
    [DataRow("{\"translationTargetLanguage\":null}", "FollowInterface")]
    [DataRow("{\"translationTargetLanguage\":\"xx-XX\"}", "FollowInterface")]
    [DataRow("{\"translationTargetLanguage\":\" ZH-cn \"}", "zh-CN")]
    [DataRow("{\"translationTargetLanguage\":\"ja-JP\",\"translationEngine\":\"Offline\"}", "ja-JP")]
    public void SettingsUpgradePreservesExplicitTargetsAndNormalizesInvalidValues(string json, string expected)
    {
        string file = Path.Combine(Path.GetTempPath(), "KeeStashDock-target-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(file, json);
            var store = new SettingsStore(file);
            store.Load();
            Assert.AreEqual(expected, store.Current.TranslationTargetLanguage);
            store.Save();
            var reloaded = new SettingsStore(file);
            reloaded.Load();
            Assert.AreEqual(expected, reloaded.Current.TranslationTargetLanguage);
        }
        finally { File.Delete(file); File.Delete(file + ".bak"); }
    }

    [TestMethod]
    public void FailedTranslationPreservesSourceAndNeverSavesAnErrorAsTranslation()
    {
        var result = new TemporaryTextResult();
        result.SetSource("recognized text");
        Assert.IsNotNull(result.Content(ResultTextKind.Source));
        Assert.IsNull(result.Content(ResultTextKind.Translation));
        Assert.IsNull(result.Content(ResultTextKind.Both));
        var request = result.BeginSave(ResultTextKind.Source, 5)!;
        Assert.IsNull(result.BeginSave(ResultTextKind.Source, 5), "A pending save must block repeat clicks.");
        Assert.IsTrue(result.CompleteSave(request, success: false));
        var retry = result.BeginSave(ResultTextKind.Source, 5)!;
        Assert.AreEqual(request.Text, retry.Text);
        Assert.IsTrue(result.CompleteSave(retry, success: true));
        Assert.IsNull(result.BeginSave(ResultTextKind.Source, 5), "Successful identical actions must not save twice.");
        Assert.IsNotNull(result.BeginSave(ResultTextKind.Source, 6), "Choosing another group is a new explicit action.");
    }

    [TestMethod]
    public void NewSourceRejectsStaleTranslationAndSaveCompletion()
    {
        var result = new TemporaryTextResult();
        result.SetSource("first");
        long firstRevision = result.Revision;
        var pending = result.BeginSave(ResultTextKind.Source, null)!;
        result.Reset();
        result.SetSource("second");
        Assert.IsFalse(result.SetTranslation(firstRevision, "late translation"));
        Assert.IsFalse(result.CompleteSave(pending, success: true));
        Assert.AreEqual("second", result.Content(ResultTextKind.Source));
        Assert.IsNull(result.Content(ResultTextKind.Translation));
        Assert.IsTrue(result.SetTranslation(result.Revision, "second translated"));
        Assert.AreEqual("Source:\nsecond\n\nTranslation:\nsecond translated", result.Content(ResultTextKind.Both));
    }
}
