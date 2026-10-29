using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KeeStashDock.Core.Services;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Desktop.ViewModels;
using KeeStashDock.Desktop.Views;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Database;
using KeeStashDock.Infrastructure.Logging;
using KeeStashDock.Infrastructure.Repositories;
using KeeStashDock.Infrastructure.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

/// <summary>Opt-in real WPF renders. Uses isolated sample data and never shows a native window.</summary>
internal static class ThemePreviewRenderScenarios
{
    internal static void RenderIfRequested(string isolatedRoot, SettingsStore previousSettings)
    {
        string? output = Environment.GetEnvironmentVariable("KEESTASHDOCK_THEME_PREVIEWS");
        if (string.IsNullOrWhiteSpace(output)) return;
        string destination = Path.GetFullPath(output);
        if (destination.Replace('\\', '/').Contains("/docs/images", StringComparison.OrdinalIgnoreCase))
            throw new AssertFailedException("Theme verification must use a new artifact directory, not existing documentation images.");
        Directory.CreateDirectory(destination);
        string root = Path.Combine(isolatedRoot, "theme-rendering"); Directory.CreateDirectory(root);
        string previousThemeRoot = ThemeManager.Store.Root;
        string language = LocalizationService.Instance.CurrentLanguage;
        int fontSize = TypographyManager.Percent;
        string source = Environment.GetEnvironmentVariable("KEESTASHDOCK_THEME_SOURCE") ?? Path.Combine(AppContext.BaseDirectory, "theme");
        using var log = new FileLogger(Path.Combine(root, "Logs"));
        var maintenance = new DatabaseMaintenanceCoordinator();
        var db = new Db(Path.Combine(root, "examples.db"), maintenance); new MigrationRunner(db, log).Run();
        var repository = new SnippetRepository(db);
        var snippets = new SnippetService(repository, log);
        var categories = new CategoryRepository(db);
        var tags = new TagRepository(db);
        long writing = categories.Insert(new Category { Name = "Writing", SortOrder = 0 });
        categories.Insert(new Category { Name = "Reference", SortOrder = 1 });
        categories.Insert(new Category { Name = "Screenshots", SortOrder = 2 });
        long selected = snippets.Add("Hello {{name}},\n\nHere is a short update for {{project}}.\n\nPrepared on {{date}} at {{time}}.",
            SnippetSource.Manual, title: "A thoughtful follow-up", categoryId: writing, isTemplate: true);
        tags.SetForSnippet(selected, ["writing", "templates"]);
        snippets.TogglePin(snippets.GetById(selected)!);
        snippets.Add("A quiet workspace makes room for the next idea.", SnippetSource.Manual, title: "A thought worth keeping", categoryId: writing);
        snippets.Add("Prepare the draft.\nReview the details.\nShare when ready.", SnippetSource.Manual, title: "A simple checklist", categoryId: writing);
        var settings = new SettingsStore(Path.Combine(root, "settings.json"));
        Assert.AreEqual("", settings.Current.ThemePresetId);
        Assert.IsNull(settings.Current.BackgroundImagePath);
        Assert.AreEqual("en-US", settings.Current.Language);
        using var hotkey = new HotkeyManager((_, _, _) => true, _ => { });
        using var screenshot = new HotkeyManager((_, _, _) => true, _ => { });
        hotkey.Initialize(1, 0x51); screenshot.Initialize(1, 0x57);
        var backup = new BackupService(db, log);
        var restarter = new NoRestart();
        var importer = new DatabaseImportService(db, maintenance, backup,
            new DatabaseSchemaValidator(), new PhysicalDatabaseFileOperations(), restarter, log);
        var components = new ComponentInstallerActions(_ => ComponentStatus.Missing,
            (_, _, _) => throw new AssertFailedException("Theme renders cannot install components."),
            _ => throw new AssertFailedException("Theme renders cannot start components."),
            _ => throw new AssertFailedException("Theme renders cannot stop components."));
        SettingsWindow? options = null; WorkbenchWindow? workbench = null;
        try
        {
            LocalizationService.Instance.SetLanguage("en-US");
            ThemeManager.Initialize(settings, source);
            settings.Current.ThemePresetId = Environment.GetEnvironmentVariable("KEESTASHDOCK_THEME_PRESET") ?? "builtin:wisteria-atelier";
            settings.Current.ThemeUseCustomBackground = false;
            settings.Current.SelectionBubbleStyle = "FollowTheme";
            settings.Save();
            Assert.IsNotNull(ThemeManager.ActiveScheme, "Build output must contain the approved built-in theme, or set KEESTASHDOCK_THEME_SOURCE to its root.");
            workbench = new WorkbenchWindow(snippets, new SearchService(repository), categories, tags, settings,
                new GroupIoService(snippets, categories));
            var model = (WorkbenchViewModel)workbench.DataContext;
            var selection = model.RequestSelectionChangeAsync(model.Items.Single(item => item.Id == selected));
            Assert.IsTrue(selection.IsCompletedSuccessfully && selection.Result);
            FrameworkElement editor = (FrameworkElement)workbench.FindName("ContentEditor");
            ThemeWindowSurface.Track(workbench, "workspace");
            Assert.AreSame(editor, workbench.FindName("ContentEditor"), "The theme wrapper must preserve the XAML name scope.");
            options = new SettingsWindow(settings, backup, importer, restarter, hotkey,
                displayDataRoot: "[Path hidden for privacy]", isAppDataFallback: false,
                screenshotHotkey: screenshot, components: components,
                translate: (_, _, _) => throw new AssertFailedException("Theme renders cannot contact a translation provider."));
            ThemeWindowSurface.Track(options, "workspace");
            AwaitCatalog(options.ThemeCatalogRefresh);
            Assert.IsNotNull(options.FindName("ThemePresetCombo"));
            // Opening settings may inspect autostart. Replace only the fixture's
            // visible check state while suppressing the OS-writing handler.
            FieldInfo loading = typeof(SettingsWindow).GetField("_loading", BindingFlags.Instance | BindingFlags.NonPublic)!;
            foreach (string mode in new[] { "Light", "Dark" })
            foreach (int percentage in new[] { 100, 150 })
            {
                settings.Current.Theme = mode; settings.Current.FontSizePercent = percentage; settings.Save();
                TypographyManager.Apply(percentage);
                loading.SetValue(options, true);
                try { ((CheckBox)options.FindName("AutoStartCheck")).IsChecked = false; }
                finally { loading.SetValue(options, false); }
                string suffix = mode.ToLowerInvariant() + "-" + percentage;
                Render(workbench, destination, "v130-theme-workbench-" + suffix + ".png", new Size(1120, 760));
                Assert.AreEqual(13 * percentage / 100d, ((TextBox)workbench.FindName("ContentEditor")).FontSize, .001);
                options.SelectSettingsPage("General");
                ((ScrollViewer)options.FindName("GeneralPage")).ScrollToHome();
                MaskPaths(options);
                Render(options, destination, "v130-theme-settings-general-" + suffix + ".png", new Size(960, 780));
                options.SelectSettingsPage("Appearance");
                ((ScrollViewer)options.FindName("AppearancePage")).ScrollToHome();
                // Refresh the real settings controls to show the same active variant/font.
                loading.SetValue(options, true);
                try
                {
                    typeof(SettingsWindow).GetMethod("PopulateFromSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(options, null);
                    ((CheckBox)options.FindName("AutoStartCheck")).IsChecked = false;
                }
                finally { loading.SetValue(options, false); }
                MaskPaths(options);
                Render(options, destination, "v130-theme-settings-appearance-" + suffix + ".png", new Size(960, 780));
                ((Slider)options.FindName("SettingsSurfaceOpacitySlider")).Value = 60;
                Render(options, destination, "v130-theme-settings-opacity60-" + suffix + ".png", new Size(960, 780),
                    "SettingsSurfaceOpacitySlider");
                var backgroundTarget = (ComboBox)options.FindName("BackgroundTargetCombo");
                backgroundTarget.SelectedItem = backgroundTarget.Items.OfType<ComboBoxItem>().Single(item => (string)item.Tag == "settings");
                ((CheckBox)options.FindName("SettingsFrostCheck")).IsChecked = true;
                ((Slider)options.FindName("SettingsFrostSlider")).Value = 8;
                Render(options, destination, "v130-theme-settings-opacity60-frost8-" + suffix + ".png", new Size(960, 780),
                    "SettingsFrostSlider");
                ((CheckBox)options.FindName("SettingsFrostCheck")).IsChecked = false;
                ((Slider)options.FindName("SettingsSurfaceOpacitySlider")).Value = 100;
                backgroundTarget.SelectedIndex = 0;
                settings.Current.ThemeAppearanceCustomized = false;
                settings.Save();
            }
            File.WriteAllText(Path.Combine(destination, "theme-preview-notes.txt"),
                "KeeStashDock 1.3.0 theme verification\nActual WPF window content rendered offscreen with fictitious sample entries.\n" +
                ThemeManager.ActiveScheme!.Manifest.Name + ": Light/Dark, global interface text 100%/150%.\n" +
                "Additional appearance views show 60% Settings surface opacity; text and popup surfaces remain opaque.\n" +
                "Frosted Settings views use an 8px artwork-only blur at 60% surface opacity.\n" +
                "Theme rendering uses the same shared background wrapper as the application. No desktop capture or user data.\n" +
                "Native title bars, per-monitor DPI and pointer interaction require separate desktop validation.\n" +
                "Default configuration remains unchanged: no preset selected and no wallpaper.\n");
        }
        finally
        {
            Close(options); Close(workbench);
            ThemeManager.Initialize(previousSettings, previousThemeRoot);
            TypographyManager.Apply(fontSize); LocalizationService.Instance.SetLanguage(language);
        }
    }

    private static void MaskPaths(SettingsWindow window)
    {
        foreach (string name in new[] { "OcrPathText", "OfflinePathText", "BackupPathText", "DataPathText", "BgPathText" })
            switch (window.FindName(name))
            {
                case TextBlock text: text.Text = "[Path hidden for privacy]"; break;
                case TextBox box: box.Text = "[Path hidden for privacy]"; break;
            }
        if (window.FindName("BackupStatusText") is TextBlock status)
        {
            int start = status.Text.LastIndexOf("\nLocation:", StringComparison.Ordinal);
            if (start >= 0) status.Text = status.Text[..start] + "\nLocation: [Path hidden for privacy]";
        }
    }

    private static void AwaitCatalog(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            timer.Tick += (_, _) => { if (task.IsCompleted || DateTime.UtcNow >= deadline) frame.Continue = false; };
            timer.Start();
            try { Dispatcher.PushFrame(frame); }
            finally { timer.Stop(); }
        }
        Assert.IsTrue(task.IsCompleted, "The isolated theme catalog did not finish within 30 seconds.");
        task.GetAwaiter().GetResult();
    }

    private static void Render(Window window, string destination, string name, Size size, string? revealControl = null)
    {
        if (window is SettingsWindow settingsWindow) MaskPaths(settingsWindow);
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        var frame = new Border { Child = content, DataContext = window.DataContext, FlowDirection = window.FlowDirection,
            UseLayoutRounding = true, SnapsToDevicePixels = true, Background = (Brush)Application.Current.FindResource("B.WindowBg") };
        frame.Resources.MergedDictionaries.Add(Application.Current.Resources.MergedDictionaries[0]);
        frame.Resources.MergedDictionaries.Add(window.Resources);
        foreach (string key in Application.Current.Resources.Keys.OfType<string>().Where(key => key.StartsWith("T.", StringComparison.Ordinal)))
            frame.Resources[key] = Application.Current.Resources[key];
        try
        {
            frame.Measure(size); frame.Arrange(new Rect(size)); frame.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
            if (revealControl is not null)
            {
                ((FrameworkElement)window.FindName(revealControl)).BringIntoView();
                frame.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
            }
            Assert.AreEqual(size.Width, frame.ActualWidth); Assert.AreEqual(size.Height, frame.ActualHeight);
            var image = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32); image.Render(frame);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var output = File.Create(Path.Combine(destination, name)); encoder.Save(output);
        }
        finally { frame.Child = null; window.Content = content; }
    }

    private static void Close(Window? window)
    {
        if (window is null) return;
        window.Closing += (_, e) => e.Cancel = false; window.Close();
    }

    private sealed class NoRestart : IApplicationRestarter
    { public bool TryRestart() => throw new AssertFailedException("Theme renders cannot restart the app."); }
}
