using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using KeeStashDock.Core.Services;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Desktop.Views;
using KeeStashDock.Infrastructure.Database;
using KeeStashDock.Infrastructure.Logging;
using KeeStashDock.Infrastructure.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

[TestClass]
[DoNotParallelize]
public sealed class SettingsTranslationTests
{
    [STATestMethod]
    public void SavedDeepLSelection_ProviderSwitchesPreserveIndependentCredentialsAndVisibility()
    {
        string parent = Path.Combine(Path.GetTempPath(), "KeeStashDock.Tests", "SettingsTranslation");
        string root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        string logs = Path.Combine(root, "Logs");
        Directory.CreateDirectory(logs);
        SettingsWindow? window = null;
        Application? application = null;
        FileLogger? logger = null;
        using var hotkey = new HotkeyManager();

        try
        {
            string settingsFile = Path.Combine(root, "settings.json");
            var saved = new SettingsStore(settingsFile);
            saved.Current.SelectionPopupEnabled = true;
            saved.Current.SelectionAction = "Translate";
            saved.Current.TranslationEngine = "DeepL";
            saved.Current.TranslationApiKey = "test-deepl-original:fx";
            saved.Current.TranslationAppId = "test-baidu-app-original";
            saved.Current.BaiduTranslationApiKey = "test-baidu-original";
            saved.Current.AliyunAccessKeyId = "test-aliyun-id";
            saved.Current.AliyunAccessKeySecret = "test-aliyun-secret";
            saved.Save();
            var settings = new SettingsStore(settingsFile);
            settings.Load();

            // Load only WPF resources. The product App, its startup hooks and
            // PortablePaths are never initialized, and no window is shown.
            application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            application.Resources.MergedDictionaries.Add(LoadResource("Theme.Light.xaml"));
            application.Resources.MergedDictionaries.Add(LoadResource("Styles.xaml"));
            logger = new FileLogger(logs);
            var maintenance = new DatabaseMaintenanceCoordinator();
            var database = new Db(Path.Combine(root, "snippets.db"), maintenance);
            var backup = new BackupService(database, logger);
            var restarter = new UnexpectedRestarter();
            var importer = new DatabaseImportService(
                database, maintenance, backup, new DatabaseSchemaValidator(),
                new PhysicalDatabaseFileOperations(), restarter, logger);
            int saves = 0;
            settings.Changed += () => saves++;
            TaskCompletionSource<TranslationResult>? pendingTest = null;
            CancellationToken testToken = default;
            string? testedProvider = null;
            string? testedSample = null;
            window = new SettingsWindow(
                settings, backup, importer, restarter, hotkey,
                displayDataRoot: root, isAppDataFallback: false,
                translate: (text, cfg, token) =>
                {
                    testedProvider = cfg.TranslationEngine;
                    testedSample = text;
                    testToken = token;
                    return pendingTest?.Task ?? Task.FromResult(new TranslationResult(cfg.TranslationEngine, "test result"));
                });

            ComboBox engine = Named<ComboBox>(window, "TranslateEngineCombo");
            TextBox key = Named<TextBox>(window, "ApiKeyBox");
            TextBox appId = Named<TextBox>(window, "TranslationAppIdBox");
            Grid keyRow = Named<Grid>(window, "ApiKeyRow");
            Grid appIdRow = Named<Grid>(window, "TranslationAppIdRow");

            Assert.AreEqual("DeepL", ((ComboBoxItem)engine.SelectedItem).Tag);
            Assert.AreEqual("test-deepl-original:fx", key.Text);
            Assert.AreEqual(Visibility.Visible, keyRow.Visibility);
            Assert.AreEqual(Visibility.Collapsed, appIdRow.Visibility);
            Assert.IsTrue(key.IsEnabled);
            Assert.AreEqual(0, saves, "Opening settings must not overwrite saved provider credentials.");

            SelectProvider(engine, "Baidu");

            Assert.AreEqual("Baidu", settings.Current.TranslationEngine);
            Assert.AreEqual("test-baidu-original", key.Text);
            Assert.AreEqual("test-baidu-app-original", appId.Text);
            Assert.AreEqual(Visibility.Visible, keyRow.Visibility);
            Assert.AreEqual(Visibility.Visible, appIdRow.Visibility);
            Assert.IsTrue(appId.IsEnabled);
            Assert.AreEqual(1, saves, "Refreshing the credential controls must not trigger extra saves.");

            key.Text = "test-baidu-updated";
            appId.Text = "test-baidu-app-updated";
            SelectProvider(engine, "DeepL");

            Assert.AreEqual("test-deepl-original:fx", key.Text);
            Assert.AreEqual(Visibility.Visible, keyRow.Visibility);
            Assert.AreEqual(Visibility.Collapsed, appIdRow.Visibility);
            Assert.AreEqual("test-baidu-updated", settings.Current.BaiduTranslationApiKey);
            key.Text = "test-deepl-updated:fx";

            var reloaded = new SettingsStore(settingsFile);
            reloaded.Load();
            Assert.AreEqual("DeepL", reloaded.Current.TranslationEngine);
            Assert.AreEqual("test-deepl-updated:fx", reloaded.Current.TranslationApiKey);
            Assert.AreEqual("test-baidu-updated", reloaded.Current.BaiduTranslationApiKey);
            Assert.AreEqual("test-baidu-app-updated", reloaded.Current.TranslationAppId);

            SelectProvider(engine, "Bing");

            Assert.AreEqual(Visibility.Collapsed, keyRow.Visibility);
            Assert.AreEqual(Visibility.Collapsed, appIdRow.Visibility);
            Assert.AreEqual(string.Empty, key.Text);
            Assert.AreEqual("test-deepl-updated:fx", settings.Current.TranslationApiKey);
            Assert.AreEqual("test-baidu-updated", settings.Current.BaiduTranslationApiKey);

            SelectProvider(engine, "Baidu");
            Assert.AreEqual("test-baidu-updated", key.Text);
            Assert.AreEqual("test-baidu-app-updated", appId.Text);

            SelectProvider(engine, "Aliyun");
            TextBox aliyunId = Named<TextBox>(window, "AliyunAccessKeyIdBox");
            PasswordBox aliyunSecret = Named<PasswordBox>(window, "AliyunAccessKeySecretBox");
            StackPanel aliyunPanel = Named<StackPanel>(window, "AliyunSettingsPanel");
            StackPanel offlinePanel = Named<StackPanel>(window, "OfflineSettingsPanel");
            TextBox offlineEndpoint = Named<TextBox>(window, "OfflineEndpointBox");
            Assert.AreEqual(Visibility.Visible, aliyunPanel.Visibility);
            Assert.AreEqual(Visibility.Collapsed, offlinePanel.Visibility);
            Assert.AreEqual(Visibility.Collapsed, keyRow.Visibility);
            Assert.AreEqual("test-aliyun-id", aliyunId.Text);
            Assert.AreEqual("test-aliyun-secret", aliyunSecret.Password);
            aliyunId.Text = "test-aliyun-id-updated";
            aliyunSecret.Password = "test-aliyun-secret-updated";

            SelectProvider(engine, "Offline");
            Assert.AreEqual(Visibility.Collapsed, aliyunPanel.Visibility);
            Assert.AreEqual(Visibility.Visible, offlinePanel.Visibility);
            Assert.AreEqual("http://127.0.0.1:5000", offlineEndpoint.Text);
            offlineEndpoint.Text = "http://127.0.0.1:5010";
            Task successfulTest = window.TestTranslationAsync();
            PumpUntilCompleted(successfulTest);
            Assert.AreEqual("Offline", testedProvider);
            Assert.AreEqual("\u8FD9\u662F\u4E00\u4E2A\u7FFB\u8BD1\u6D4B\u8BD5\u3002", testedSample);
            Assert.AreEqual(LocalizationService.Instance["Settings.TranslationTestSuccess"],
                Named<TextBlock>(window, "TranslationTestStatus").Text);

            pendingTest = new TaskCompletionSource<TranslationResult>();
            Task staleTest = window.TestTranslationAsync();
            Assert.IsFalse(Named<Button>(window, "TranslationTestButton").IsEnabled);
            SelectProvider(engine, "Aliyun");
            Assert.IsTrue(testToken.IsCancellationRequested);
            pendingTest.SetResult(new TranslationResult("Offline", "stale result"));
            PumpUntilCompleted(staleTest);
            Assert.AreEqual(string.Empty, Named<TextBlock>(window, "TranslationTestStatus").Text);
            Assert.IsTrue(Named<Button>(window, "TranslationTestButton").IsEnabled);
            Assert.AreEqual("test-aliyun-secret-updated", aliyunSecret.Password);
            Assert.AreEqual("test-deepl-updated:fx", settings.Current.TranslationApiKey);
            Assert.AreEqual("test-baidu-updated", settings.Current.BaiduTranslationApiKey);

            var finalSettings = new SettingsStore(settingsFile);
            finalSettings.Load();
            Assert.AreEqual("Aliyun", finalSettings.Current.TranslationEngine);
            Assert.AreEqual("test-aliyun-id-updated", finalSettings.Current.AliyunAccessKeyId);
            Assert.AreEqual("test-aliyun-secret-updated", finalSettings.Current.AliyunAccessKeySecret);
            Assert.AreEqual("http://127.0.0.1:5010", finalSettings.Current.OfflineTranslationEndpoint);
            Named<CheckBox>(window, "SelectionPopupCheck").IsChecked = false;
            Assert.IsFalse(key.IsEnabled);
            Assert.IsFalse(appId.IsEnabled);
            Assert.IsFalse(aliyunPanel.IsEnabled);
            Assert.IsFalse(Named<Button>(window, "TranslationTestButton").IsEnabled);
        }
        finally
        {
            if (window is not null)
            {
                // Settings normally hides on Close. Permit final destruction
                // in this isolated test so Application keeps no test window.
                window.Closing += (_, args) => args.Cancel = false;
                window.Close();
            }
            application?.Shutdown();
            logger?.Dispose();
            string resolvedRoot = Path.GetFullPath(root);
            Assert.AreEqual(Path.GetFullPath(parent), Path.GetDirectoryName(resolvedRoot));
            Assert.IsTrue(Guid.TryParseExact(Path.GetFileName(resolvedRoot), "N", out _));
            if (Directory.Exists(resolvedRoot))
                Directory.Delete(resolvedRoot, recursive: true);
        }
    }

    private static ResourceDictionary LoadResource(string name) => new()
    {
        Source = new Uri($"/{typeof(SettingsWindow).Assembly.GetName().Name};component/Themes/{name}", UriKind.Relative),
    };

    private static void PumpUntilCompleted(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            timer.Tick += (_, _) => { if (task.IsCompleted || DateTime.UtcNow >= deadline) frame.Continue = false; };
            timer.Start();
            try { Dispatcher.PushFrame(frame); }
            finally { timer.Stop(); }
        }
        Assert.IsTrue(task.IsCompleted, "The isolated settings test did not complete.");
        task.GetAwaiter().GetResult();
    }

    private static T Named<T>(SettingsWindow window, string name) where T : FrameworkElement
    {
        object control = window.FindName(name);
        Assert.IsInstanceOfType<T>(control);
        return (T)control;
    }

    private static void SelectProvider(ComboBox combo, string provider)
        => combo.SelectedItem = combo.Items.OfType<ComboBoxItem>()
            .Single(item => (string?)item.Tag == provider);

    private sealed class UnexpectedRestarter : IApplicationRestarter
    {
        public bool TryRestart() => throw new AssertFailedException("Settings editing must not restart the app.");
    }
}
