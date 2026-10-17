using System.Globalization;
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
            // Autostart is a machine property, not a serialized setting. Render
            // the fresh-install unchecked state while suppressing its registry
            // handler; never change the current user's startup registration.
            FieldInfo loading = typeof(SettingsWindow).GetField("_loading", BindingFlags.Instance | BindingFlags.NonPublic)!;
            loading.SetValue(settingsWindow, true);
            try { ((CheckBox)settingsWindow.FindName("AutoStartCheck")).IsChecked = false; }
            finally { loading.SetValue(settingsWindow, false); }
            RenderSettings(settingsWindow, directory);
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
        var viewer = (ScrollViewer)window.Content;
        var page = (StackPanel)viewer.Content;
        viewer.Content = null;
        var frame = CreateFrame(window, page);
        try
        {
            Arrange(frame, new Size(630, double.PositiveInfinity));
            var components = (FrameworkElement)window.FindName("ComponentsSection");
            var behavior = SectionOf((FrameworkElement)window.FindName("SelectionPopupCheck"), page);
            var appearance = SectionOf((FrameworkElement)window.FindName("PanelModeCombo"), page);
            var backup = SectionOf((FrameworkElement)window.FindName("AutoBackupCheck"), page);
            var about = (TextBlock)window.FindName("AboutText");
            Rect aboutBounds = BoundsIn(about, frame);
            // Every path is synthetic already. Additionally remove the entire
            // path line from the exported pixels, with an explicit privacy label.
            var pathMask = new Rect(aboutBounds.X, aboutBounds.Y + aboutBounds.Height * 2 / 3,
                aboutBounds.Width, Math.Max(20, aboutBounds.Height / 3));
            BitmapSource full = Snapshot(frame, pathMask);
            Rect componentBounds = BoundsIn(components, frame);
            SaveCrop(full, new Rect(0, 0, full.PixelWidth, componentBounds.Bottom + 8), directory, "default-settings.png");
            SaveCrop(full, componentBounds, directory, "component-setup.png", 8);
            SaveCrop(full, BoundsIn(behavior, frame), directory, "behavior-settings.png", 8);
            SaveCrop(full, BoundsIn(appearance, frame), directory, "default-appearance.png", 8);
            Rect backupBounds = BoundsIn(backup, frame);
            backupBounds.Union(BoundsIn(SectionOf(about, page), frame));
            SaveCrop(full, backupBounds, directory, "backup-settings.png", 8);
        }
        finally
        {
            frame.Child = null;
            viewer.Content = page;
        }
    }

    private static Border SectionOf(FrameworkElement element, StackPanel page)
    {
        DependencyObject? current = element;
        while (current is not null)
        {
            if (current is Border border && ReferenceEquals(border.Parent, page))
                return border;
            current = LogicalTreeHelper.GetParent(current);
        }
        throw new AssertFailedException("The settings section could not be located.");
    }

    private static Rect BoundsIn(FrameworkElement element, Visual ancestor)
        => element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));

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
        FlowDirection = FlowDirection.LeftToRight,
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

    private static BitmapSource Snapshot(FrameworkElement element, Rect? pathMask = null)
    {
        int width = (int)Math.Ceiling(element.ActualWidth);
        int height = (int)Math.Ceiling(element.ActualHeight);
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        image.Render(element);
        if (pathMask is not { } mask) return image;
        var overlay = new DrawingVisual();
        using (DrawingContext drawing = overlay.RenderOpen())
        {
            drawing.DrawImage(image, new Rect(0, 0, width, height));
            drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(223, 229, 239)), null, mask, 3, 3);
            var label = new FormattedText("Data: [path hidden for privacy]", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11,
                new SolidColorBrush(Color.FromRgb(65, 78, 99)), 1);
            drawing.DrawText(label, new Point(mask.X + 5, mask.Y + 2));
        }
        var redacted = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        redacted.Render(overlay);
        return redacted;
    }

    private static void SaveCrop(BitmapSource source, Rect bounds, string directory, string fileName, int padding = 0)
    {
        int left = Math.Max(0, (int)Math.Floor(bounds.Left) - padding);
        int top = Math.Max(0, (int)Math.Floor(bounds.Top) - padding);
        int right = Math.Min(source.PixelWidth, (int)Math.Ceiling(bounds.Right) + padding);
        int bottom = Math.Min(source.PixelHeight, (int)Math.Ceiling(bounds.Bottom) + padding);
        Save(new CroppedBitmap(source, new Int32Rect(left, top, right - left, bottom - top)), Path.Combine(directory, fileName));
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
