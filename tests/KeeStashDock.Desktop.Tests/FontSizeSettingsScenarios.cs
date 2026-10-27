using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KeeStashDock.Desktop.Views;
using KeeStashDock.Infrastructure.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

internal static class FontSizeSettingsScenarios
{
    internal static void Verify(SettingsWindow window, SettingsStore settings, string settingsFile)
    {
        int original = settings.Current.FontSizePercent;
        int writes = 0;
        void Changed() => writes++;
        settings.Changed += Changed;
        var slider = (Slider)window.FindName("FontSizeSlider");
        var preview = (TextBlock)window.FindName("FontSizePreview");
        var reset = (Button)window.FindName("FontSizeResetButton");
        try
        {
            window.SelectSettingsPage("Appearance");
            Assert.AreEqual((double)original, slider.Value);
            foreach (int percent in new[] { 150, 90 })
            {
                slider.Value = percent;
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.AreEqual(percent, settings.Current.FontSizePercent);
                var reloaded = new SettingsStore(settingsFile);
                reloaded.Load();
                Assert.AreEqual(percent, reloaded.Current.FontSizePercent,
                    "Changing text size must persist immediately for the next launch.");
                Assert.AreEqual(12d * percent / 100d, preview.FontSize, 0.001,
                    "The visible preview must use the current text size without reopening Settings.");
                Assert.AreEqual(percent.ToString() + "%", ((TextBlock)window.FindName("FontSizeValue")).Text);
                if (percent == 150) VerifyExpandedLayout(window);
            }
            reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.AreEqual(100, settings.Current.FontSizePercent);
            Assert.AreEqual(100d, slider.Value);
            Assert.AreEqual(12d, preview.FontSize, 0.001);
            int beforeReset = writes;
            reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.AreEqual(beforeReset, writes, "Reset at the default size should not rewrite settings.");

            // Exercise the same population path used by the constructor while
            // suppressing persistence. Opening Settings must not replace a saved size.
            FieldInfo loading = typeof(SettingsWindow).GetField("_loading", BindingFlags.Instance | BindingFlags.NonPublic)!;
            loading.SetValue(window, true);
            try
            {
                settings.Current.FontSizePercent = 140;
                typeof(SettingsWindow).GetMethod("PopulateFontSize", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(window, null);
                Assert.AreEqual(140d, slider.Value);
                Assert.AreEqual(beforeReset, writes);
                Assert.AreEqual(140, settings.Current.FontSizePercent);
            }
            finally { loading.SetValue(window, false); }
        }
        finally
        {
            slider.Value = original;
            settings.Current.FontSizePercent = original;
            TypographyManager.Apply(original);
            settings.Changed -= Changed;
        }
    }

    private static void VerifyExpandedLayout(SettingsWindow window)
    {
        // Render the real Settings content without opening or capturing a user window.
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        var frame = new Border
        {
            Child = content,
            Resources = window.Resources,
            DataContext = window.DataContext,
            FlowDirection = window.FlowDirection,
        };
        frame.SetResourceReference(Border.BackgroundProperty, "B.WindowBg");
        try
        {
            var size = new Size(window.MinWidth - 16, window.MinHeight - 40);
            frame.Measure(size);
            frame.Arrange(new Rect(size));
            frame.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            frame.Measure(size);
            frame.Arrange(new Rect(size));
            frame.UpdateLayout();
            var brand = (TextBlock)window.FindName("SettingsBrandText");
            Assert.AreEqual(TextTrimming.CharacterEllipsis, brand.TextTrimming);
            Assert.AreEqual("KeeStashDock", brand.ToolTip);
            var section = (FrameworkElement)window.FindName("FontSizeSection");
            Rect bounds = section.TransformToAncestor(frame).TransformBounds(new Rect(section.RenderSize));
            Assert.IsTrue(bounds.Left >= 0 && bounds.Right <= frame.ActualWidth + 1,
                "The text-size section must fit the minimum settings window at 150%.");
            var slider = (Slider)window.FindName("FontSizeSlider");
            var preview = (TextBlock)window.FindName("FontSizePreview");
            Assert.IsTrue(slider.ActualWidth >= 100, "The enlarged setting must retain a usable slider.");
            Assert.IsTrue(preview.ActualWidth > 100 && preview.ActualHeight >= preview.FontSize,
                "The preview must wrap instead of clipping enlarged text.");

            foreach (string page in new[] { "General", "Translation", "Components", "Data", "About" })
            {
                window.SelectSettingsPage(page);
                frame.Measure(size);
                frame.Arrange(new Rect(size));
                frame.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                frame.Measure(size);
                frame.Arrange(new Rect(size));
                frame.UpdateLayout();
                var pageContent = (ScrollViewer)window.FindName(page + "Page");
                foreach (TextBox box in Descendants<TextBox>(pageContent))
                {
                    if (!IsPresented(box, pageContent) || box.ActualWidth <= 0) continue;
                    Assert.IsTrue(box.ActualHeight >= box.FontSize + 2,
                        $"The {page} input {box.Name} must retain a full text line at 150%.");
                }
            }
            window.SelectSettingsPage("Appearance");
            frame.Measure(size);
            frame.Arrange(new Rect(size));
            frame.UpdateLayout();

            string? directory = Environment.GetEnvironmentVariable("KEESTASHDOCK_DOC_IMAGES");
            if (string.IsNullOrWhiteSpace(directory)) return;
            Directory.CreateDirectory(directory);
            var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(frame);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(directory, "settings-font-size-150.png"));
            encoder.Save(output);
        }
        finally
        {
            frame.Child = null;
            window.Content = content;
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (T descendant in Descendants<T>(child)) yield return descendant;
        }
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
}
