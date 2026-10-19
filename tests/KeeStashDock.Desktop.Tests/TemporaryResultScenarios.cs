using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using KeeStashDock.Core.Services;
using KeeStashDock.Desktop.Views;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

internal static class TemporaryResultScenarios
{
    internal static void Verify(string isolatedRoot)
    {
        VerifyOptionalTools(isolatedRoot);
        var settings = new SettingsStore(Path.Combine(isolatedRoot, "result-targets.json"));
        settings.Current.Language = "en-US";
        settings.Current.TranslationTargetLanguage = "zh-CN";
        int settingWrites = 0, notifications = 0, saves = 0;
        settings.Changed += () => settingWrites++;
        var window = new TranslationResultWindow(settings, _ => notifications++);
        string? requestedTarget = null, savedText = null;
        long? savedGroup = null;
        bool fail = true;
        try
        {
            window.SetRecognizedSource("retained original");
            window.ShowError();
            window.SetResultContext([new(null, "Uncategorized"), new(7, "Test group")], 7, "zh-CN",
                target => requestedTarget = target, (text, group) =>
                {
                    saves++;
                    if (fail) throw new IOException("Simulated storage failure");
                    savedText = text;
                    savedGroup = group;
                    return 12;
                });
            Assert.IsFalse(Complete(window.SaveResultAsync(ResultTextKind.Translation)));
            Assert.AreEqual(0, saves, "Errors must not be saved as translated content.");
            Assert.IsFalse(Complete(window.SaveResultAsync(ResultTextKind.Source)));
            Assert.AreEqual(0, notifications, "Failed persistence must never show a success toast.");
            string? source = null;
            Assert.IsTrue(window.TryCopySource(value => source = value));
            Assert.AreEqual("retained original", source);
            fail = false;
            Assert.IsTrue(Complete(window.SaveResultAsync(ResultTextKind.Source)));
            Assert.AreEqual("retained original", savedText);
            Assert.AreEqual(7L, savedGroup);
            Assert.AreEqual(1, notifications);
            Assert.IsFalse(Complete(window.SaveResultAsync(ResultTextKind.Source)));
            Assert.AreEqual(2, saves, "Repeat clicks after a successful save must be coalesced.");

            var targetCombo = Descendants(window).OfType<ComboBox>().Single(combo => combo.Name == "ResultTargetCombo");
            targetCombo.SelectedItem = targetCombo.Items.OfType<ComboBoxItem>().Single(item => (string)item.Tag == "de-DE");
            Assert.AreEqual("de-DE", requestedTarget);
            Assert.AreEqual("zh-CN", settings.Current.TranslationTargetLanguage);
            Assert.AreEqual(0, settingWrites, "Temporary target selection must not persist global preferences.");

            window.ShowResult("translated result");
            Assert.IsTrue(Complete(window.SaveResultAsync(ResultTextKind.Both)));
            Assert.AreEqual("Source:\nretained original\n\nTranslation:\ntranslated result", savedText);
            window.HideCard();
            Assert.IsFalse(Complete(window.SaveResultAsync(ResultTextKind.Source)), "Dismissal must release storage and source actions.");
            window.ShowRecognizedText("OCR only");
            window.SetResultContext([new(null, "Uncategorized")], null, "zh-CN", _ => { }, (text, _) =>
            { savedText = text; return 13; });
            Assert.IsFalse(Complete(window.SaveResultAsync(ResultTextKind.Translation)));
            Assert.IsTrue(Complete(window.SaveResultAsync(ResultTextKind.Source)));
            Assert.AreEqual("OCR only", savedText);

            window.HideCard();
            window.SetRecognizedSource("pending source");
            using var release = new ManualResetEventSlim();
            int uiThread = Environment.CurrentManagedThreadId;
            window.SetResultContext([new(null, "Uncategorized"), new(7, "Group")], 7, "en-US", _ => { }, (text, group) =>
            {
                Assert.AreNotEqual(uiThread, Environment.CurrentManagedThreadId, "Storage must not wait on the UI thread.");
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
                return 14;
            });
            var groupCombo = Descendants(window).OfType<ComboBox>().Single(combo => combo.Name == "ResultGroupCombo");
            groupCombo.SelectedIndex = 0;
            Assert.IsTrue(window.HasResultContext);
            Assert.IsNull(window.SelectedGroupId, "Explicit Uncategorized selection must not fall back to the original group.");
            Task<bool> pending = window.SaveResultAsync(ResultTextKind.Source);
            Assert.IsFalse(pending.IsCompleted, "A blocked storage operation must yield to the UI.");
            Assert.IsFalse(Complete(window.SaveResultAsync(ResultTextKind.Source)));
            Task draining = window.SaveCompletion;
            Assert.IsFalse(draining.IsCompleted, "Shutdown must be able to await in-flight persistence.");
            settings.Current.TranslationResultShowSavePanel = false;
            settings.Save();
            settings.Current.TranslationResultShowSavePanel = true;
            settings.Save();
            Assert.IsFalse(draining.IsCompleted, "Hiding the save tools must not cancel a save already requested.");
            Assert.IsFalse(groupCombo.IsEnabled, "Restoring the tools must not unlock an in-flight save.");
            int beforeStale = notifications;
            window.HideCard();
            window.ShowRecognizedText("new result");
            release.Set();
            Assert.IsFalse(Complete(pending), "Late completion must not change the next result.");
            Complete(draining.ContinueWith(_ => true));
            Assert.AreEqual(beforeStale, notifications);
        }
        finally { window.Close(); }
    }

    private static void VerifyOptionalTools(string isolatedRoot)
    {
        string path = Path.Combine(isolatedRoot, "result-options.json");
        File.WriteAllText(path, "{\"translationTargetLanguage\":\"zh-CN\"}");
        var settings = new SettingsStore(path);
        settings.Load();
        Assert.IsTrue(settings.Current.TranslationResultShowTargetSelector);
        Assert.IsTrue(settings.Current.TranslationResultShowSavePanel,
            "Existing settings without the new switches must retain their result tools.");
        int settingsWrites = 0, targetRequests = 0;
        string? requestedTarget = null;
        settings.Changed += () => settingsWrites++;
        var window = new TranslationResultWindow(settings, _ => { });
        StackPanel Panel(string name) => Descendants(window).OfType<StackPanel>().Single(panel => panel.Name == name);
        ComboBox Combo(string name) => Descendants(window).OfType<ComboBox>().Single(combo => combo.Name == name);
        void SetContext() => window.SetResultContext([new(null, "Uncategorized"), new(7, "Group")], 7, "zh-CN",
            target => { requestedTarget = target; targetRequests++; }, (_, _) => 12);
        void SetOptions(bool target, bool save)
        {
            int before = settingsWrites;
            settings.Current.TranslationResultShowTargetSelector = target;
            settings.Current.TranslationResultShowSavePanel = save;
            settings.Save();
            Assert.AreEqual(before + 1, settingsWrites, "Applying result visibility must not write settings again.");
        }
        void AssertTools(Visibility target, Visibility save)
        {
            Assert.AreEqual(target, Panel("ResultTargetTools").Visibility);
            Assert.AreEqual(save, Panel("ResultSaveTools").Visibility);
            Assert.AreEqual(target == Visibility.Visible || save == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed,
                Panel("ResultTools").Visibility, "Hidden tools must collapse their container without leaving an empty section.");
        }
        try
        {
            foreach (Button button in Descendants(window).OfType<Button>())
                Assert.AreSame(window.FindResource("S.Button"), button.Style,
                    "Result actions must use the themed rounded buttons rather than platform chrome.");
            window.ShowTranslating(20, 20);
            window.SetRecognizedSource("retained source");
            SetContext();
            AssertTools(Visibility.Collapsed, Visibility.Collapsed);
            SetOptions(false, false);
            window.ShowResult("completed translation");
            AssertTools(Visibility.Collapsed, Visibility.Collapsed);
            Assert.IsTrue(window.HasResultContext);
            Assert.AreEqual(7L, window.SelectedGroupId);
            string? copied = null;
            Assert.IsTrue(window.TryCopySource(text => copied = text));
            Assert.AreEqual("retained source", copied);
            Assert.IsTrue(window.TryCopyResult(text => copied = text));
            Assert.AreEqual("completed translation", copied);

            SetOptions(false, true);
            AssertTools(Visibility.Collapsed, Visibility.Visible);
            SetOptions(true, false);
            AssertTools(Visibility.Visible, Visibility.Collapsed);
            var targetCombo = Combo("ResultTargetCombo");
            targetCombo.SelectedItem = targetCombo.Items.OfType<ComboBoxItem>().Single(item => (string)item.Tag == "de-DE");
            Combo("ResultGroupCombo").SelectedIndex = 0;
            int beforeToggle = settingsWrites;
            SetOptions(false, false);
            SetOptions(true, true);
            AssertTools(Visibility.Visible, Visibility.Visible);
            Assert.AreEqual("de-DE", ((ComboBoxItem)targetCombo.SelectedItem).Tag);
            Assert.IsNull(window.SelectedGroupId, "Visibility changes must preserve an explicit Uncategorized choice.");
            Assert.AreEqual("de-DE", requestedTarget);
            Assert.AreEqual(1, targetRequests, "Visibility changes must not restart a translation.");
            Assert.AreEqual(beforeToggle + 2, settingsWrites);
            Assert.AreEqual("zh-CN", settings.Current.TranslationTargetLanguage);

            window.ShowTranslating(20, 20);
            window.SetRecognizedSource("failed translation source");
            SetContext();
            SetOptions(false, false);
            SetOptions(true, true);
            AssertTools(Visibility.Collapsed, Visibility.Collapsed);
            window.ShowError();
            AssertTools(Visibility.Visible, Visibility.Visible);
            Assert.IsTrue(window.TryCopySource(text => copied = text));
            Assert.AreEqual("failed translation source", copied);
            Assert.IsFalse(window.TryCopyResult(_ => Assert.Fail("Error messages must not be copied as translations.")));
            Assert.IsTrue(Complete(window.SaveResultAsync(ResultTextKind.Source)),
                "A translation failure must preserve source saving.");

            window.ShowRecognizing(20, 20);
            SetContext();
            AssertTools(Visibility.Collapsed, Visibility.Collapsed);
            window.ShowError("Screenshot.OcrNoText");
            AssertTools(Visibility.Collapsed, Visibility.Collapsed);
            window.ShowRecognizedText("recognized source");
            AssertTools(Visibility.Visible, Visibility.Visible);
            Assert.IsFalse(Complete(window.SaveResultAsync(ResultTextKind.Translation)));
            Assert.IsTrue(Complete(window.SaveResultAsync(ResultTextKind.Source)));

            SetOptions(false, false);
            var reloaded = new SettingsStore(path);
            reloaded.Load();
            Assert.IsFalse(reloaded.Current.TranslationResultShowTargetSelector);
            Assert.IsFalse(reloaded.Current.TranslationResultShowSavePanel);
        }
        finally { window.Close(); }
    }

    private static bool Complete(Task<bool> task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            DateTime deadline = DateTime.UtcNow.AddSeconds(8);
            timer.Tick += (_, _) => { if (task.IsCompleted || DateTime.UtcNow >= deadline) frame.Continue = false; };
            timer.Start();
            try { Dispatcher.PushFrame(frame); }
            finally { timer.Stop(); }
        }
        Assert.IsTrue(task.IsCompleted, "Temporary result persistence timed out.");
        return task.GetAwaiter().GetResult();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        foreach (object child in LogicalTreeHelper.GetChildren(parent))
            if (child is DependencyObject dependency)
            {
                yield return dependency;
                foreach (var item in Descendants(dependency)) yield return item;
            }
    }
}
