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

/// <summary>
/// Opt-in documentation renders of the actual release views. No desktop pixels,
/// global hooks, clipboard, network requests or existing databases are used.
/// Runs inside the existing STA test Application, without showing these windows.
/// </summary>
internal static class DocumentationPreviewScenarios
{
    internal static void RenderIfRequested(string isolatedRoot)
    {
        string? destination = Environment.GetEnvironmentVariable("KEESTASHDOCK_DOC_IMAGES");
        if (string.IsNullOrWhiteSpace(destination)) return;
        string directory = Path.GetFullPath(destination);
        Directory.CreateDirectory(directory);
        isolatedRoot = Path.Combine(isolatedRoot, "documentation");
        Directory.CreateDirectory(isolatedRoot);
        string previousLanguage = LocalizationService.Instance.CurrentLanguage;
        LocalizationService.Instance.SetLanguage("en-US");
        using var log = new FileLogger(Path.Combine(isolatedRoot, "documentation-logs"));
        var maintenance = new DatabaseMaintenanceCoordinator();
        var db = new Db(Path.Combine(isolatedRoot, "documentation.db"), maintenance);
        new MigrationRunner(db, log).Run();
        var repository = new SnippetRepository(db);
        var snippets = new SnippetService(repository, log);
        var categories = new CategoryRepository(db);
        var tags = new TagRepository(db);
        var settings = new SettingsStore(Path.Combine(isolatedRoot, "documentation-settings.json"));
        Assert.AreEqual("en-US", settings.Current.Language);
        Assert.IsFalse(settings.Current.FloatingPanelEnabled);
        Assert.AreEqual("Google", settings.Current.TranslationEngine);
        var backup = new BackupService(db, log);
        var restarter = new NoRestart();
        var importer = new DatabaseImportService(db, maintenance, backup,
            new DatabaseSchemaValidator(), new PhysicalDatabaseFileOperations(), restarter, log);
        using var hotkey = new HotkeyManager((_, _, _) => true, _ => { });
        using var captureHotkey = new HotkeyManager((_, _, _) => true, _ => { });
        Assert.IsTrue(hotkey.Initialize(1, 0x51));
        Assert.IsTrue(captureHotkey.Initialize(1, 0x57));
        var components = new ComponentInstallerActions(
            _ => ComponentStatus.Missing,
            (_, _, _) => throw new AssertFailedException("Documentation must not install components."),
            _ => throw new AssertFailedException("Documentation must not start components."),
            _ => throw new AssertFailedException("Documentation must not stop components."));
        Window? workbench = null;
        ImageEntryWindow? imageEntry = null;
        SettingsWindow? settingsWindow = null;
        try
        {
            long writing = categories.Insert(new Category { Name = "Writing", SortOrder = 1 });
            long reference = categories.Insert(new Category { Name = "Reference", SortOrder = 2 });
            long captures = categories.Insert(new Category { Name = "Screenshots", SortOrder = 3 });
            byte[] noteImage = CreateExampleNote();
            const string noteText = "Capture an idea\n\nSave a region as an image.\nExtract text when you need it.\nKeep the original alongside your notes.";
            long imageId = snippets.AddImage(noteImage, ImagePresentation.Thumbnail(noteImage), captures,
                title: "A useful idea, captured", ocrText: noteText);
            snippets.Add("https://github.com/Keegankei/KeeStashDock", SnippetSource.Manual,
                title: "KeeStashDock on GitHub", categoryId: reference);
            snippets.Add("A small idea today can become tomorrow's best work.", SnippetSource.Manual,
                title: "A thought worth keeping", categoryId: writing);
            long welcomeId = snippets.Add("Keep useful things within reach.\n\nAlt + Q  —  Open the quick panel\nAlt + W  —  Capture a screen region\n\nOrganize text, links and screenshots into groups. Pin what matters, find it later, and reuse it anywhere.",
                SnippetSource.Manual, title: "Your ideas, ready when you are", categoryId: writing);
            snippets.TogglePin(snippets.GetById(welcomeId)!);
            tags.SetForSnippet(welcomeId, ["ideas", "getting-started"]);

            workbench = new WorkbenchWindow(snippets, new SearchService(repository), categories, tags,
                settings, new GroupIoService(snippets, categories));
            var model = (WorkbenchViewModel)workbench.DataContext;
            Task<bool> selection = model.RequestSelectionChangeAsync(model.Items.Single(item => item.Id == welcomeId));
            Assert.IsTrue(selection.IsCompletedSuccessfully && selection.Result);
            RenderWindow(workbench, directory, "main-window.png", new Size(1060, 650));

            imageEntry = new ImageEntryWindow(snippets,
                (_, _) => throw new AssertFailedException("Documentation uses prefilled example text, not OCR."),
                snippets.GetById(imageId)!, noteImage);
            RenderWindow(imageEntry, directory, "screenshot-entry.png", new Size(1000, 590));

            settingsWindow = new SettingsWindow(settings, backup, importer, restarter, hotkey,
                displayDataRoot: @"C:\Example\KeeStashDock\Data", isAppDataFallback: false,
                screenshotHotkey: captureHotkey, components: components,
                translate: (_, _, _) => throw new AssertFailedException("Documentation must not request translation."));
            // Use the actual idle folder-availability logic with isolated component
            // paths. The injected actions above still reject installs and processes.
            var componentService = new ComponentInstallService(Path.Combine(isolatedRoot, "application"),
                Path.Combine(isolatedRoot, "local-components"), TimeSpan.FromMinutes(30), settings: settings);
            typeof(SettingsWindow).GetField("_componentService", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(settingsWindow, componentService);
            typeof(SettingsWindow).GetMethod("RefreshFolderPaths", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(settingsWindow, null);
            // Autostart is a machine property, not a serialized setting. Render
            // the fresh-install unchecked state while suppressing its registry
            // handler; never change the current user's startup registration.
            FieldInfo loading = typeof(SettingsWindow).GetField("_loading", BindingFlags.Instance | BindingFlags.NonPublic)!;
            loading.SetValue(settingsWindow, true);
            try { ((CheckBox)settingsWindow.FindName("AutoStartCheck")).IsChecked = false; }
            finally { loading.SetValue(settingsWindow, false); }
            RenderSettings(settingsWindow, directory);
            RenderSettingsVariants(settingsWindow, directory);
            RenderResultVariants(settings, directory);
        }
        finally
        {
            Close(settingsWindow);
            Close(imageEntry);
            Close(workbench);
            LocalizationService.Instance.SetLanguage(previousLanguage);
        }
    }

    private static void RenderSettings(SettingsWindow window, string directory)
    {
        (string Page, string FileName)[] pages =
        [
            ("General", "default-settings.png"),
            ("Translation", "translation-settings.png"),
            ("Appearance", "default-appearance.png"),
            ("Components", "component-setup.png"),
            ("Data", "backup-settings.png"),
            ("About", "about-settings.png"),
        ];
        foreach ((string page, string fileName) in pages)
        {
            window.SelectSettingsPage(page);
            var scroll = (ScrollViewer)window.FindName(page + "Page");
            scroll.ScrollToHome();
            var backupStatus = (TextBlock)window.FindName("BackupStatusText");
            string originalStatus = backupStatus.Text;
            // Replace fixture path text before layout. An overlay on an offscreen
            // field could otherwise paint over another card or the window footer.
            int location = originalStatus.LastIndexOf("\nLocation:", StringComparison.Ordinal);
            if (location >= 0) backupStatus.Text = originalStatus[..location] + "\nLocation: [path hidden for privacy]";
            string[] privateFields = page switch
            {
                "Components" => ["OcrPathText", "OfflinePathText"],
                "Data" => ["BackupPathText"],
                "About" => ["DataPathText"],
                _ => [],
            };
            var restorePaths = new List<Action>();
            try
            {
                foreach (string name in privateFields)
                {
                    switch (window.FindName(name))
                    {
                        case TextBox box:
                            string originalBox = box.Text;
                            restorePaths.Add(() => box.Text = originalBox);
                            box.Text = "[Path hidden for privacy]";
                            break;
                        case TextBlock text:
                            string originalText = text.Text;
                            restorePaths.Add(() => text.Text = originalText);
                            text.Text = "[Path hidden for privacy]";
                            break;
                        default:
                            throw new AssertFailedException("The settings path field could not be located.");
                    }
                }
                RenderWindow(window, directory, fileName, new Size(960, 760));
                string? detailFileName = page switch
                {
                    "Components" => "component-folders.png",
                    "Data" => "backup-status.png",
                    _ => null,
                };
                if (detailFileName is not null)
                {
                    // Apply the real page scroll while its arranged viewport is
                    // available, retaining the same path redactions for both views.
                    scroll.ScrollToEnd();
                    scroll.UpdateLayout();
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
                    RenderWindow(window, directory, detailFileName, new Size(960, 760));
                }
            }
            finally
            {
                foreach (Action restore in restorePaths) restore();
                backupStatus.Text = originalStatus;
            }
        }
        window.SelectSettingsPage("General");
    }

    private static void RenderSettingsVariants(SettingsWindow window, string directory)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        ResourceDictionary[] originalDictionaries = dictionaries.ToArray();
        string previousLanguage = LocalizationService.Instance.CurrentLanguage;
        bool wasDark = ThemeManager.IsDark;
        try
        {
            LocalizationService.Instance.SetLanguage("en-US");
            ThemeManager.Apply("Dark");
            window.SelectSettingsPage("General");
            ((ScrollViewer)window.FindName("GeneralPage")).ScrollToHome();
            RenderWindow(window, directory, "settings-dark.png", new Size(960, 760));

            ThemeManager.Apply("Light");
            LocalizationService.Instance.SetLanguage("zh-CN");
            window.SelectSettingsPage("Translation");
            ((ScrollViewer)window.FindName("TranslationPage")).ScrollToHome();
            RenderWindow(window, directory, "settings-zh-CN.png", new Size(760, 620));

            LocalizationService.Instance.SetLanguage("ar-SA");
            window.SelectSettingsPage("General");
            ((ScrollViewer)window.FindName("GeneralPage")).ScrollToHome();
            Assert.AreEqual(FlowDirection.RightToLeft, window.FlowDirection);
            RenderWindow(window, directory, "settings-ar-SA.png", new Size(760, 620));
        }
        finally
        {
            ThemeManager.Apply(wasDark ? "Dark" : "Light");
            dictionaries.Clear();
            foreach (ResourceDictionary dictionary in originalDictionaries) dictionaries.Add(dictionary);
            LocalizationService.Instance.SetLanguage(previousLanguage);
            window.SelectSettingsPage("General");
        }
    }

    private static void RenderResultVariants(SettingsStore settings, string directory)
    {
        bool previousTarget = settings.Current.TranslationResultShowTargetSelector;
        bool previousSave = settings.Current.TranslationResultShowSavePanel;
        var result = new TranslationResultWindow(settings, _ => { });
        void SetContext() => result.SetResultContext([new(null, "Uncategorized"), new(7, "Writing")], 7, "de-DE",
            _ => throw new AssertFailedException("Documentation must not request translation."),
            (_, _) => throw new AssertFailedException("Documentation must not persist result entries."));
        try
        {
            settings.Current.TranslationResultShowTargetSelector = true;
            settings.Current.TranslationResultShowSavePanel = true;
            result.SetRecognizedSource("Keep useful things within reach.");
            result.ShowResult("Nützliche Dinge immer griffbereit.");
            SetContext();
            RenderWindow(result, directory, "translation-result-tools.png", new Size(420, double.PositiveInfinity));

            settings.Current.TranslationResultShowTargetSelector = false;
            settings.Current.TranslationResultShowSavePanel = false;
            SetContext();
            RenderWindow(result, directory, "translation-result-compact.png", new Size(420, double.PositiveInfinity));
        }
        finally
        {
            settings.Current.TranslationResultShowTargetSelector = previousTarget;
            settings.Current.TranslationResultShowSavePanel = previousSave;
            result.Close();
        }
    }

    private static void RenderWindow(Window window, string directory, string fileName, Size size)
    {
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        var frame = CreateFrame(window, content);
        try
        {
            Arrange(frame, size);
            Save(Snapshot(frame), Path.Combine(directory, fileName));
        }
        finally
        {
            frame.Child = null;
            window.Content = content;
        }
    }

    private static Border CreateFrame(Window window, FrameworkElement content) => new()
    {
        Background = (Brush)Application.Current.FindResource("B.WindowBg"),
        Resources = window.Resources,
        DataContext = window.DataContext,
        FlowDirection = window.FlowDirection,
        Child = content,
    };

    private static void Arrange(FrameworkElement element, Size size)
    {
        element.Measure(size);
        element.Arrange(new Rect(new Size(size.Width,
            double.IsPositiveInfinity(size.Height) ? element.DesiredSize.Height : size.Height)));
        element.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
    }

    private static BitmapSource Snapshot(FrameworkElement element)
    {
        int width = (int)Math.Ceiling(element.ActualWidth);
        int height = (int)Math.Ceiling(element.ActualHeight);
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        image.Render(element);
        return image;
    }

    private static void Save(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using FileStream output = File.Create(path);
        encoder.Save(output);
    }

    private static byte[] CreateExampleNote()
    {
        var text = new StackPanel { Margin = new Thickness(32) };
        text.Children.Add(new TextBlock { Text = "CAPTURE / KEEP / REUSE", FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(68, 98, 141)), Margin = new Thickness(0, 0, 0, 22) });
        text.Children.Add(new TextBlock { Text = "Capture an idea", FontSize = 32, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(29, 48, 76)), Margin = new Thickness(0, 0, 0, 24) });
        text.Children.Add(new TextBlock { Text = "Save a region as an image.\nExtract text when you need it.\nKeep the original alongside your notes.",
            FontSize = 18, LineHeight = 31, TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(63, 79, 102)) });
        var note = new Border { Background = new SolidColorBrush(Color.FromRgb(241, 246, 252)), Child = text };
        Arrange(note, new Size(540, 360));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Snapshot(note)));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void Close(Window? window)
    {
        if (window is null) return;
        window.Closing += (_, args) => args.Cancel = false;
        window.Close();
    }

    private sealed class NoRestart : IApplicationRestarter
    {
        public bool TryRestart() => throw new AssertFailedException("Documentation must not restart the app.");
    }
}
