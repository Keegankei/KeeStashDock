using System.IO;
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

/// <summary>Exercises the real workbench controls inside the existing isolated STA Application.</summary>
internal static class WorkbenchPresentationScenarios
{
    internal static void Verify(string isolatedRoot)
    {
        string root = Path.Combine(isolatedRoot, "workbench-presentation");
        Directory.CreateDirectory(root);
        using var log = new FileLogger(Path.Combine(root, "Logs"));
        var db = new Db(Path.Combine(root, "snippets.db"));
        new MigrationRunner(db, log).Run();
        var repository = new SnippetRepository(db);
        var snippets = new SnippetService(repository, log);
        var categories = new CategoryRepository(db);
        var tags = new TagRepository(db);
        long writing = categories.Insert(new Category { Name = "Writing", SortOrder = 1 });
        long empty = categories.Insert(new Category { Name = "An empty group", SortOrder = 2 });
        long textId = snippets.Add("Keep useful things within reach.\n\nAlt + Q — Quick panel\nAlt + W — Capture a region\n\nCollect a thought, organize it, and return when you need it.",
            SnippetSource.Manual, title: "Your ideas, ready when you are", categoryId: writing);
        tags.SetForSnippet(textId, ["ideas", "getting-started"]);
        snippets.TogglePin(snippets.GetById(textId)!);
        snippets.Add("https://github.com/Keegankei/KeeStashDock", SnippetSource.Manual,
            title: "KeeStashDock on GitHub", categoryId: writing);
        byte[] picture = CreateImage();
        long imageId = snippets.AddImage(picture, picture, writing,
            title: "A useful idea, captured", ocrText: "Capture an idea. Keep the original alongside your notes.");
        string language = LocalizationService.Instance.CurrentLanguage;
        bool wasDark = ThemeManager.IsDark;
        int previousTextSize = TypographyManager.Percent;
        long? openedImage = null;
        var window = new WorkbenchWindow(snippets, new SearchService(repository), categories, tags,
            new SettingsStore(Path.Combine(root, "settings.json")), new GroupIoService(snippets, categories),
            onImageRequested: id => openedImage = id);
        var model = (WorkbenchViewModel)window.DataContext;
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        var frame = new Border
        {
            DataContext = model,
            Child = content,
            UseLayoutRounding = window.UseLayoutRounding,
            SnapsToDevicePixels = window.SnapsToDevicePixels,
        };
        // Keep view styles/converters intact; a separate palette slot makes
        // off-screen theme invalidation deterministic without changing Window.Resources.
        frame.Resources.MergedDictionaries.Add(window.Resources);
        frame.SetResourceReference(Border.BackgroundProperty, "B.WindowBg");
        // These are client-area sizes: leave room for native borders and caption
        // so the minimum-window test is not accidentally more generous than the UI.
        var regularSize = new Size(window.Width - 16, window.Height - 40);
        var minimumSize = new Size(window.MinWidth - 16, window.MinHeight - 40);
        try
        {
            Select(model, textId);
            foreach (string locale in LocalizationService.Instance.DiscoverLanguages().Select(item => item.Tag))
            {
                LocalizationService.Instance.SetLanguage(locale);
                foreach (string theme in new[] { "Light", "Dark" })
                {
                    ApplyTheme(frame, theme);
                    frame.FlowDirection = window.FlowDirection;
                    foreach ((Size size, string label) in new[] { (regularSize, "default"), (minimumSize, "minimum") })
                    {
                        Arrange(frame, size);
                        AssertActionsFit(frame, $"{locale}/{theme}/{label}");
                        AssertEditorUsable(window, $"{locale}/{theme}/{label}");
                        if (locale is "en-US" or "zh-CN" or "de-DE" or "ar-SA")
                            SavePreview(frame, $"workbench-{locale}-{theme.ToLowerInvariant()}-{label}.png");
                    }
                }
            }

            LocalizationService.Instance.SetLanguage("en-US");
            frame.FlowDirection = window.FlowDirection;
            ApplyTheme(frame, "Dark");
            var contentEditor = Named<TextBox>(window, "ContentEditor");
            const string draft = "An unsaved idea remains available when a search hides its entry.";
            contentEditor.Text = draft;
            Assert.AreEqual(draft, model.EditContent);
            Assert.IsTrue(model.IsDirty);
            SetSearch(window, model, "no-matching-entry-42af");
            Arrange(frame, minimumSize);
            Assert.AreEqual(0, model.Items.Count);
            Assert.IsNull(model.SelectedItem);
            Assert.IsTrue(model.IsDirty);
            Assert.AreEqual(draft, contentEditor.Text);
            Assert.IsTrue(IsPresented(Named<FrameworkElement>(window, "EmptyState"), frame));
            Assert.IsTrue(IsPresented(contentEditor, frame) && contentEditor.IsEnabled,
                "An empty result list must not cover or disable the unsaved editor buffer.");
            AssertActionsFit(frame, "dirty draft with empty search");
            AssertEditorUsable(window, "dirty draft with empty search");
            SavePreview(frame, "workbench-dark-empty-search-draft.png");
            var save = Named<Button>(window, "SaveButton");
            Assert.AreSame(model.SaveCommand, save.Command, "The visible Save action must still persist the editor buffer.");
            model.SaveCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            Assert.IsFalse(model.IsDirty);
            Assert.AreEqual(draft, snippets.GetById(textId)!.Content);

            SetSearch(window, model, string.Empty);
            Select(model, imageId);
            Arrange(frame, minimumSize);
            var openImage = Named<Button>(window, "OpenImageButton");
            Assert.IsTrue(IsPresented(openImage, frame));
            AssertActionsFit(frame, "image entry at minimum size");
            AssertEditorUsable(window, "image entry at minimum size");
            openImage.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.AreEqual(imageId, openedImage, "The redesigned image preview must retain its viewer action.");
            SavePreview(frame, "workbench-dark-image-minimum.png");

            snippets.MoveToTrash(textId);
            model.SelectedNav = model.Nav.Single(item => item.Id == -3);
            LocalizationService.Instance.SetLanguage("de-DE");
            frame.FlowDirection = window.FlowDirection;
            Arrange(frame, minimumSize);
            Assert.IsTrue(IsPresented(Named<Button>(window, "RestoreButton"), frame));
            Assert.IsTrue(IsPresented(Named<Button>(window, "DeleteForeverButton"), frame));
            Assert.IsFalse(IsPresented(Named<Button>(window, "DeleteButton"), frame));
            AssertActionsFit(frame, "German trash at minimum size");
            AssertEditorUsable(window, "German trash at minimum size");
            SavePreview(frame, "workbench-de-DE-dark-trash-minimum.png");
            Assert.AreSame(model.RestoreCommand, Named<Button>(window, "RestoreButton").Command);
            model.RestoreCommand.Execute(null);
            Assert.IsFalse(snippets.GetById(textId)!.IsDeleted);

            model.SelectedNav = model.Nav.Single(item => item.Id == empty);
            Arrange(frame, regularSize);
            Assert.AreEqual(0, model.Items.Count);
            Assert.IsTrue(IsPresented(Named<FrameworkElement>(window, "EmptyState"), frame));
            AssertActionsFit(frame, "empty group");

            model.SelectedNav = model.Nav.Single(item => item.Id == -1);
            Select(model, textId);
            foreach (int percentage in new[] { 90, 150 })
            {
                TypographyManager.Apply(percentage);
                // Detached fixtures need the same resource invalidation that
                // the Application normally delivers to a live window.
                foreach (string key in Application.Current.Resources.Keys.OfType<string>().Where(key => key.StartsWith("T.", StringComparison.Ordinal)))
                    frame.Resources[key] = Application.Current.Resources[key];
                var scaledMinimum = new Size(window.MinWidth - 16, window.MinHeight - 40);
                foreach (string locale in new[] { "en-US", "zh-CN", "de-DE", "ar-SA" })
                {
                    LocalizationService.Instance.SetLanguage(locale);
                    frame.FlowDirection = window.FlowDirection;
                    ApplyTheme(frame, "Dark");
                    foreach ((Size size, string label) in new[] { (regularSize, "default"), (scaledMinimum, "minimum") })
                    {
                        Arrange(frame, size);
                        string scenario = $"{locale}/{percentage}%/{label}";
                        SavePreview(frame, $"workbench-{locale}-text-{percentage}-{label}.png");
                        AssertActionsFit(frame, scenario);
                        AssertEditorUsable(window, scenario);
                        if (label == "default")
                        {
                            var metadata = Named<ScrollViewer>(window, "MetadataViewport");
                            Assert.IsTrue(metadata.ExtentHeight <= metadata.ViewportHeight + 1,
                                $"Title, expiry and tags must fit without a partial row in {scenario}: {metadata.ExtentHeight:0.##}/{metadata.ViewportHeight:0.##}. " +
                                $"Limit {metadata.MaxHeight:0.##}; pane {Named<Border>(window, "EditorPane").ActualHeight:0.##}; " +
                                $"chrome {Named<TextBlock>(window, "EditorHeading").ActualHeight:0.##}, {Named<WrapPanel>(window, "EditorActions").ActualHeight:0.##}, " +
                                $"{Named<StackPanel>(window, "EditorStatus").ActualHeight:0.##}, {Named<DockPanel>(window, "ContentToolbar").ActualHeight:0.##}.");
                        }
                        Assert.AreEqual(13 * percentage / 100d, Named<TextBox>(window, "ContentEditor").FontSize, 0.001,
                            "An existing editor must update when global text size changes.");
                    }
                }
            }

            LocalizationService.Instance.SetLanguage("de-DE");
            frame.FlowDirection = window.FlowDirection;
            Select(model, imageId);
            Arrange(frame, minimumSize);
            AssertActionsFit(frame, "150% image at minimum size");
            AssertEditorUsable(window, "150% image at minimum size");
            snippets.MoveToTrash(textId);
            model.SelectedNav = model.Nav.Single(item => item.Id == -3);
            Arrange(frame, minimumSize);
            SavePreview(frame, "workbench-de-DE-text-150-trash-minimum.png");
            AssertActionsFit(frame, "150% German trash at minimum size");
            AssertEditorUsable(window, "150% German trash at minimum size");
        }
        finally
        {
            // Never let a failing layout assertion display a real unsaved-draft
            // prompt when the isolated workbench is destroyed.
            if (model.IsDirty)
                model.SaveCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            frame.Child = null;
            window.Content = content;
            window.Closing += (_, args) => args.Cancel = false;
            window.Close();
            LocalizationService.Instance.SetLanguage(language);
            ThemeManager.Apply(wasDark ? "Dark" : "Light");
            TypographyManager.Apply(previousTextSize);
        }
    }

    private static void Select(WorkbenchViewModel model, long id)
    {
        Task<bool> task = model.RequestSelectionChangeAsync(model.Items.Single(item => item.Id == id));
        Assert.IsTrue(task.IsCompletedSuccessfully && task.Result);
    }

    private static void SetSearch(WorkbenchWindow window, WorkbenchViewModel model, string text)
    {
        Named<TextBox>(window, "SearchBox").Text = text;
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
        timer.Tick += (_, _) => frame.Continue =
            (model.SearchText != text || model.IsSearching || !model.QueryCompletion.IsCompleted) && DateTime.UtcNow < deadline;
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Assert.AreEqual(text, model.SearchText, "The search field must continue updating its binding.");
        Assert.IsTrue(model.QueryCompletion.IsCompletedSuccessfully && !model.IsSearching,
            "The asynchronously debounced query must finish before its results are inspected.");
    }

    private static void Arrange(FrameworkElement frame, Size size)
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        frame.Measure(size);
        frame.Arrange(new Rect(size));
        frame.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        // Resource and ActualHeight bindings can invalidate a detached tree
        // after its first layout; there is no presentation source to arrange it.
        frame.Measure(size);
        frame.Arrange(new Rect(size));
        frame.UpdateLayout();
    }

    private static void ApplyTheme(FrameworkElement frame, string theme)
    {
        ThemeManager.Apply(theme);
        // A detached visual tree does not receive Application window resource
        // invalidation. Replace only this fixture's local palette, not its styles.
        if (frame.Resources.MergedDictionaries.Count > 1)
            frame.Resources.MergedDictionaries.RemoveAt(1);
        frame.Resources.MergedDictionaries.Add(Application.Current.Resources.MergedDictionaries[0]);
    }

    private static void AssertEditorUsable(WorkbenchWindow window, string scenario)
    {
        var editor = Named<TextBox>(window, "ContentEditor");
        Assert.IsTrue(editor.ActualWidth >= 180 && editor.ActualHeight >= 70,
            $"The writing area is too small in {scenario}: {editor.ActualWidth:0.##} × {editor.ActualHeight:0.##}; expected at least 180 × 70. " +
            $"Editor pane: {Named<Border>(window, "EditorPane").ActualHeight}/{Named<Border>(window, "EditorPane").MinHeight}; " +
            $"details: {string.Join(", ", Descendants<ScrollViewer>(Named<Border>(window, "EditorPane")).Select(view => $"{view.ActualHeight}/{view.MaxHeight}"))}; font: {editor.FontSize}.");
        Assert.AreEqual(((SolidColorBrush)Application.Current.FindResource("B.InputBg")).Color,
            ((SolidColorBrush)editor.Background).Color,
            $"The editor must use the active theme background in {scenario}.");
        Assert.IsTrue(Contrast(editor.Foreground, editor.Background) >= 4.5,
            $"Editor text must remain readable after switching the active palette in {scenario}.");
    }

    private static void AssertActionsFit(FrameworkElement root, string scenario)
    {
        Rect viewport = new(0, 0, root.ActualWidth, root.ActualHeight);
        viewport.Inflate(1, 1); // Layout rounding can differ by a device pixel.
        foreach (Button button in Descendants<Button>(root).Where(button => IsPresented(button, root)))
        {
            // At large text sizes the editor can scroll independently. Check
            // that each action can actually be brought into the visible area.
            if (Ancestors(button).OfType<ScrollViewer>().Any(view => view.Name == "EditorViewport"))
            {
                button.BringIntoView();
                Arrange(root, new Size(root.ActualWidth, root.ActualHeight));
            }
            Rect bounds = button.TransformToAncestor(root).TransformBounds(new Rect(button.RenderSize));
            Assert.IsTrue(bounds.Width > 0 && bounds.Height > 0 && viewport.Contains(bounds),
                $"An action is outside the available window in {scenario}: {button.Name}, {button.Content}, {bounds}; viewport {viewport}.");
        }
        foreach (ScrollViewer view in Descendants<ScrollViewer>(root).Where(view => view.Name == "EditorViewport"))
            view.ScrollToTop();
        Arrange(root, new Size(root.ActualWidth, root.ActualHeight));
    }

    private static IEnumerable<DependencyObject> Ancestors(DependencyObject element)
    {
        for (DependencyObject? node = VisualTreeHelper.GetParent(element); node is not null; node = VisualTreeHelper.GetParent(node))
            yield return node;
    }

    private static bool IsPresented(DependencyObject element, DependencyObject root)
    {
        for (DependencyObject? node = element; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is UIElement view && view.Visibility != Visibility.Visible) return false;
            if (ReferenceEquals(node, root)) return true;
        }
        return false;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (T descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static double Contrast(Brush foreground, Brush background)
    {
        Assert.IsInstanceOfType<SolidColorBrush>(foreground);
        Assert.IsInstanceOfType<SolidColorBrush>(background);
        static double Luminance(Color color)
        {
            static double Linear(byte value)
            {
                double channel = value / 255d;
                return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        }
        double a = Luminance(((SolidColorBrush)foreground).Color);
        double b = Luminance(((SolidColorBrush)background).Color);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static T Named<T>(WorkbenchWindow window, string name) where T : FrameworkElement =>
        (T)(window.FindName(name) ?? throw new AssertFailedException($"Missing workbench control: {name}"));

    private static byte[] CreateImage()
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(232, 240, 251)),
            Padding = new Thickness(24),
            Child = new TextBlock
            {
                Text = "Capture an idea\n\nSave a useful moment.\nReturn to it later.",
                FontSize = 20,
                Foreground = new SolidColorBrush(Color.FromRgb(40, 64, 96)),
            },
        };
        Arrange(card, new Size(360, 180));
        using var stream = new MemoryStream();
        Encode(card).Save(stream);
        return stream.ToArray();
    }

    private static void SavePreview(FrameworkElement root, string name)
    {
        string? destination = Environment.GetEnvironmentVariable("KEESTASHDOCK_DOC_IMAGES");
        if (string.IsNullOrWhiteSpace(destination)) return;
        Directory.CreateDirectory(destination);
        using FileStream output = File.Create(Path.Combine(destination, name));
        Encode(root).Save(output);
    }

    private static PngBitmapEncoder Encode(FrameworkElement root)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth),
            (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        return encoder;
    }
}
