using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

internal static class SettingsThemeScenarios
{
    internal static void VerifyBackupList(ListBox list)
    {
        // Exercise the actual Settings list and generated DisplayMemberPath text.
        // Keep this within the shared STA Application; no user window is shown.
        var parent = (Panel)list.Parent;
        int index = parent.Children.IndexOf(list);
        int originalSelection = list.SelectedIndex;
        bool wasDark = ThemeManager.IsDark;
        ResourceDictionary[] dictionaries = Application.Current.Resources.MergedDictionaries.ToArray();
        parent.Children.Remove(list);
        var frame = new Border { Child = list };
        try
        {
            // Switching repeatedly catches cached brushes as well as the original
            // white system background with dark-theme text.
            foreach (string theme in new[] { "Dark", "Light", "Dark" })
            {
                ThemeManager.Apply(theme);
                // An off-screen tree has no Application window to receive the
                // resource invalidation, so replace its local palette as well.
                frame.Resources.MergedDictionaries.Clear();
                frame.Resources.MergedDictionaries.Add(Application.Current.Resources.MergedDictionaries[0]);
                frame.Background = (Brush)Application.Current.FindResource("B.CardBg");
                list.SelectedIndex = -1;
                Layout(frame);
                var surface = (Border)list.Template.FindName("ListFace", list);
                Assert.AreEqual(ColorOf(Application.Current.FindResource("B.InputBg")), ColorOf(surface.Background));
                var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                Assert.IsNotNull(item);
                var text = Descendants<TextBlock>(item).First(block => !string.IsNullOrWhiteSpace(block.Text));
                AssertReadable(text.Foreground, surface.Background, $"{theme} unselected backup");

                list.SelectedIndex = 0;
                Layout(frame);
                var itemFace = (Border)item.Template.FindName("ListItemFace", item);
                Color selectedBackground = Composite(ColorOf(itemFace.Background), ColorOf(surface.Background));
                Assert.IsGreaterThanOrEqualTo(4.5, Contrast(ColorOf(text.Foreground), selectedBackground),
                    $"{theme} selected backup must retain readable text.");
                Assert.AreNotEqual(ColorOf(surface.Background), selectedBackground,
                    "Selection must remain visible after keyboard focus leaves the list.");
                RenderIfRequested(frame, theme);

                list.IsEnabled = false;
                Layout(frame);
                Assert.AreEqual(ColorOf(Application.Current.FindResource("B.InputBg")), ColorOf(surface.Background),
                    "Disabling the list must not restore light system chrome.");
                list.IsEnabled = true;
            }
        }
        finally
        {
            list.IsEnabled = true;
            list.SelectedIndex = originalSelection;
            ThemeManager.Apply(wasDark ? "Dark" : "Light");
            Application.Current.Resources.MergedDictionaries.Clear();
            foreach (ResourceDictionary dictionary in dictionaries)
                Application.Current.Resources.MergedDictionaries.Add(dictionary);
            frame.Child = null;
            parent.Children.Insert(index, list);
        }
    }

    private static void Layout(FrameworkElement element)
    {
        element.Measure(new Size(640, 150));
        element.Arrange(new Rect(0, 0, 640, 150));
        element.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
    }

    private static void RenderIfRequested(FrameworkElement frame, string theme)
    {
        string? directory = Environment.GetEnvironmentVariable("KEESTASHDOCK_DOC_IMAGES");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap(640, 150, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(frame);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, $"settings-backup-list-{theme.ToLowerInvariant()}.png"));
        encoder.Save(output);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject element) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(element, i);
            if (child is T match) yield return match;
            foreach (T descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static Color ColorOf(object value) => ((SolidColorBrush)value).Color;

    private static void AssertReadable(Brush foreground, Brush background, string state)
        => Assert.IsGreaterThanOrEqualTo(4.5, Contrast(ColorOf(foreground), ColorOf(background)),
            $"{state} must meet normal-text contrast.");

    private static Color Composite(Color foreground, Color background)
    {
        double alpha = foreground.A / 255d;
        byte Blend(byte front, byte back) => (byte)Math.Round(front * alpha + back * (1 - alpha));
        return Color.FromRgb(Blend(foreground.R, background.R), Blend(foreground.G, background.G),
            Blend(foreground.B, background.B));
    }

    private static double Contrast(Color first, Color second)
    {
        static double Channel(byte channel)
        {
            double value = channel / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color color)
            => 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        double a = Luminance(first);
        double b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
}
