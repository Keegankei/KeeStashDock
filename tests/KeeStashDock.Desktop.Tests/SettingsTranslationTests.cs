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
    public void DesktopWindowWorkflows_PreserveSettingsDraftsAndVisibility()
    {
        SynchronizationContext? previousContext = SynchronizationContext.Current;
        string parent = Path.Combine(Path.GetTempPath(), "KeeStashDock.Tests", "SettingsTranslation");
        string root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        string logs = Path.Combine(root, "Logs");
        Directory.CreateDirectory(logs);
        SettingsWindow? window = null;
        Application? application = null;
        FileLogger? logger = null;
        var registeredKeys = new Dictionary<int, (uint Modifiers, uint Key)>();
        bool Register(int id, uint modifiers, uint key)
        {
            if (registeredKeys.Values.Contains((modifiers, key))) return false;
            registeredKeys.Add(id, (modifiers, key));
            return true;
        }
        using var hotkey = new HotkeyManager(Register, id => registeredKeys.Remove(id));
        using var screenshotHotkey = new HotkeyManager(Register, id => registeredKeys.Remove(id));
        Assert.IsTrue(hotkey.Initialize(1, 0x51));
        Assert.IsTrue(screenshotHotkey.Initialize(1, 0x57));

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

            // Load WPF resources without product App startup, global hooks or real
            // user data. The nested panel scenario briefly shows its own window
            // and claims an empty, test-only portable draft directory.
            application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(application.Dispatcher));
            LocalizationService.Instance.SetLanguage("en-US");
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
            string? testedTarget = null;
            int shutdownPreparations = 0;
            var pendingShutdown = new TaskCompletionSource<bool>();
            var componentInstaller = new ComponentSettingsScenarios();
            window = new SettingsWindow(
                settings, backup, importer, restarter, hotkey,
                displayDataRoot: root, isAppDataFallback: false, screenshotHotkey: screenshotHotkey,
                prepareForShutdown: () => { shutdownPreparations++; return pendingShutdown.Task; },
                components: componentInstaller.Actions,
                translate: (text, cfg, token) =>
                {
                    testedProvider = cfg.TranslationEngine;
                    testedSample = text;
                    testedTarget = KeeStashDock.Domain.Entities.TranslationTargets.Resolve(cfg);
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

            var panelMode = Named<ComboBox>(window, "PanelModeCombo");
            var floatingMotion = Named<ComboBox>(window, "FloatingMotionCombo");
            Assert.IsFalse(floatingMotion.IsEnabled, "Classic must disable the floating-only presentation choice.");
            Assert.AreEqual("Follow", ((ComboBoxItem)floatingMotion.SelectedItem).Tag);
            panelMode.SelectedIndex = 1;
            Assert.IsTrue(floatingMotion.IsEnabled);
            floatingMotion.SelectedIndex = 1;
            Assert.IsFalse(settings.Current.FloatingPanelFollowPointer);
            var savedAppearance = new SettingsStore(settingsFile);
            savedAppearance.Load();
            Assert.IsTrue(savedAppearance.Current.FloatingPanelEnabled);
            Assert.IsFalse(savedAppearance.Current.FloatingPanelFollowPointer);
            panelMode.SelectedIndex = 0;
            Assert.IsFalse(floatingMotion.IsEnabled);
            panelMode.SelectedIndex = 1;
            Assert.AreEqual("Fixed", ((ComboBoxItem)floatingMotion.SelectedItem).Tag,
                "Switching through Classic must preserve the floating presentation preference.");
            Assert.IsFalse(settings.Current.FloatingPanelFollowPointer);

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

            ComboBox targetCombo = Named<ComboBox>(window, "TranslationTargetCombo");
            targetCombo.SelectedItem = targetCombo.Items.OfType<ComboBoxItem>().Single(item => (string?)item.Tag == "zh-CN");
            PumpUntilCompleted(window.TestTranslationAsync());
            Assert.AreEqual("zh-CN", testedTarget);
            Assert.AreEqual("en-US", settings.Current.Language, "Changing the target must preserve the interface language.");
            Assert.AreEqual("This is a translation test.", testedSample, "The Settings test must use the independent target.");
            var targetReload = new SettingsStore(settingsFile);
            targetReload.Load();
            Assert.AreEqual("zh-CN", targetReload.Current.TranslationTargetLanguage);

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
            Assert.IsTrue(key.IsEnabled, "Screenshot translation must remain configurable with the selection bubble disabled.");
            Assert.IsTrue(appId.IsEnabled);
            Assert.IsTrue(aliyunPanel.IsEnabled);
            Assert.IsTrue(Named<Button>(window, "TranslationTestButton").IsEnabled);
            var selectionAction = Named<ComboBox>(window, "SelectionActionCombo");
            Assert.IsFalse(selectionAction.IsEnabled);
            Named<CheckBox>(window, "SelectionPopupCheck").IsChecked = true;
            selectionAction.SelectedItem = selectionAction.Items.OfType<ComboBoxItem>().Single(item => (string?)item.Tag == "Both");
            var bothSettings = new SettingsStore(settingsFile);
            bothSettings.Load();
            Assert.AreEqual("Both", bothSettings.Current.SelectionAction);
            Assert.IsTrue(Named<Grid>(window, "TranslateEngineRow").IsVisible || window.Visibility != Visibility.Visible);
            Named<CheckBox>(window, "SelectionPopupCheck").IsChecked = false;

            // Both actions use an in-memory registrar: no global shortcut or
            // actual user's settings/registry are changed by this test.
            CheckBox captureEnabled = Named<CheckBox>(window, "ScreenshotHotkeyCheck");
            Assert.IsTrue(captureEnabled.IsChecked);
            Assert.IsFalse(window.TryApplyHotkey(true, 1, 0x51));
            Assert.AreEqual(0x57u, settings.Current.ScreenshotHotkeyVirtualKey);
            Assert.IsFalse(window.TryApplyHotkey(false, 1, 0x57));
            Assert.AreEqual(0x51u, settings.Current.HotkeyVirtualKey);
            Assert.IsTrue(window.TryApplyHotkey(true, 3, 0x54));
            Assert.AreEqual(0x54u, settings.Current.ScreenshotHotkeyVirtualKey);
            captureEnabled.IsChecked = false;
            Assert.IsFalse(screenshotHotkey.IsRegistered);
            Assert.IsFalse(settings.Current.ScreenshotHotkeyEnabled);
            Assert.IsTrue(window.TryApplyHotkey(true, 3, 0x55));
            Assert.IsFalse(screenshotHotkey.IsRegistered, "Editing a disabled shortcut must not activate it.");
            captureEnabled.IsChecked = true;
            Assert.IsTrue(settings.Current.ScreenshotHotkeyEnabled);
            Assert.IsTrue(screenshotHotkey.IsRegistered);
            var hotkeyReload = new SettingsStore(settingsFile);
            hotkeyReload.Load();
            Assert.AreEqual(0x55u, hotkeyReload.Current.ScreenshotHotkeyVirtualKey);
            Assert.IsTrue(hotkeyReload.Current.ScreenshotHotkeyEnabled);
            HotkeySettingsScenarios.Verify(window, settings, hotkey, screenshotHotkey);
            Button restart = Named<Button>(window, "RestartButton");
            restart.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.AreEqual(1, shutdownPreparations);
            Assert.IsFalse(window.IsEnabled);
            restart.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.AreEqual(1, shutdownPreparations, "A pending editor decision must block duplicate restart actions.");
            pendingShutdown.SetResult(false);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.IsTrue(window.IsEnabled, "Cancelling a draft decision must abort restart and restore Settings.");
            componentInstaller.Verify(window);
            new MigrationRunner(database, logger).Run();
            BackupSettingsScenarios.Verify(root, logger);
            using (var lockedExport = new FileStream(Path.Combine(root, "locked.ksdpkg"), FileMode.Create,
                FileAccess.ReadWrite, FileShare.None))
            {
                Assert.IsFalse(window.TryExportPackage(lockedExport.Name),
                    "A locked export destination must be a recoverable failure instead of an unhandled UI exception.");
            }
            ImageEntryWindowScenarios.Verify(root);
            TemporaryResultScenarios.Verify(root);
            UpdateWindowScenarios.Verify(root);
            StickyNoteWindowScenarios.Verify(root);
            PerspectiveSurfaceTests.VerifyLiveControls();
            EdgeDockScenarios.VerifySettings(window, settings);
            FloatingPerformanceScenarios.RecordIfRequested(root);
            EdgeDockScenarios.Verify(root);
            SettingsPresentationScenarios.Verify(window, settings, settingsFile);
            WorkbenchPresentationScenarios.Verify(root);
            ThemePresentationScenarios.Verify(root, settings);
            SettingsOpacityScenarios.Verify(window, settings, settingsFile);
            BubbleBackdropScenarios.Verify(window, settings);
            BubblePresentationScenarios.Verify(root, settings);
            ThemePreviewWindowScenarios.Verify(root, settings);
            SearchAndBatchScenarios.Verify(root);
            TemplateFillWindowScenarios.Verify();
            DocumentationPreviewScenarios.RenderIfRequested(root);
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
            SynchronizationContext.SetSynchronizationContext(previousContext);
            logger?.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
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
