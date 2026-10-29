using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Desktop.Views;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

/// <summary>Actual theme-dialog layout with isolated settings and no native windows.</summary>
internal static class ThemePreviewWindowScenarios
{
    internal static void Verify(string isolatedRoot, SettingsStore previousSettings)
    {
        string previousThemeRoot = ThemeManager.Store.Root;
        string previousLanguage = LocalizationService.Instance.CurrentLanguage;
        int previousFontSize = TypographyManager.Percent;
        string source = Environment.GetEnvironmentVariable("KEESTASHDOCK_THEME_SOURCE") ?? Path.Combine(AppContext.BaseDirectory, "theme");
        string? destination = Environment.GetEnvironmentVariable("KEESTASHDOCK_THEME_DIALOG_PREVIEWS");
        if (!string.IsNullOrWhiteSpace(destination)) Directory.CreateDirectory(Path.GetFullPath(destination));
        string settingsPath = Path.Combine(isolatedRoot, "theme-dialog-preview.json");
        var settings = new SettingsStore(settingsPath);
        settings.Current.Theme = "Light";
        settings.Current.ThemePresetId = "";
        settings.Current.PanelPosX = 137;
        settings.Current.HotkeyModifiers = 0x0001;
        settings.Current.HotkeyVirtualKey = 0x51;
        int writes = 0;
        settings.Changed += () => writes++;
        try
        {
            ThemeManager.Initialize(settings, source);
            ThemeScheme? scheme = ThemeManager.Store.Find("builtin:wisteria-atelier");
            Assert.IsNotNull(scheme, "The shipped theme must be available for dialog preview coverage.");
            foreach (bool themed in new[] { false, true })
                foreach (string mode in new[] { "Light", "Dark" })
                    foreach (int percent in new[] { 100, 150 })
                        VerifyDialog(themed ? scheme : null, settings, mode, percent, "en-US", false,
                            new Size(760, 780), destination);
            VerifyDialog(scheme, settings, "Light", 150, "zh-CN", false, new Size(420, 600), destination);
            VerifyDialog(scheme, settings, "Dark", 150, "ar-SA", false, new Size(420, 600), destination);
            VerifyDialog(scheme, settings, "Dark", 150, "en-US", true, new Size(420, 600), destination);
            Assert.AreEqual(0, writes, "Opening, changing and closing previews must not persist preferences.");
            Assert.IsFalse(File.Exists(settingsPath), "Previewing must not create a settings file.");
            Assert.AreEqual("", settings.Current.ThemePresetId);
            Assert.AreEqual("Light", settings.Current.Theme);
            Assert.AreEqual(137d, settings.Current.PanelPosX);
            Assert.AreEqual(0x0001u, settings.Current.HotkeyModifiers);
            Assert.AreEqual(0x51u, settings.Current.HotkeyVirtualKey);
            if (!string.IsNullOrWhiteSpace(destination))
                File.WriteAllText(Path.Combine(destination, "theme-dialog-preview-notes.txt"),
                    "Actual KeeStashDock theme preview/apply controls rendered offscreen.\n" +
                    "Default and Wisteria Light/Dark; 100%/150% text; narrow Chinese/Arabic RTL and import-only layouts.\n" +
                    "Isolated preferences, no desktop capture, no native dialog shown and no settings saved.\n" +
                    "Native title bars and physical mixed-DPI interaction require separate desktop acceptance.\n");
        }
        finally
        {
            ThemeManager.EndPreview();
            ThemeManager.Initialize(previousSettings, previousThemeRoot);
            LocalizationService.Instance.SetLanguage(previousLanguage);
            TypographyManager.Apply(previousFontSize);
        }
    }

    private static void VerifyDialog(ThemeScheme? scheme, SettingsStore settings, string mode, int percent,
        string language, bool installOnly, Size size, string? destination)
    {
        LocalizationService.Instance.SetLanguage(language);
        TypographyManager.Apply(percent);
        var dialog = new ThemePreviewWindow(scheme, settings, installOnly);
        try
        {
            var root = Named<Grid>(dialog, "PreviewRoot");
            var modes = Named<ComboBox>(dialog, "PreviewModeCombo");
            var bubble = Named<CheckBox>(dialog, "PreviewBubbleCheck");
            var installCopy = Named<CheckBox>(dialog, "PreviewInstallCopyCheck");
            Assert.AreEqual(installOnly ? Visibility.Collapsed : Visibility.Visible, bubble.Visibility);
            Assert.AreEqual(installOnly ? Visibility.Visible : Visibility.Collapsed, installCopy.Visibility);
            if (installOnly)
            {
                installCopy.IsChecked = true;
                Assert.IsTrue(dialog.InstallAsCopy);
                installCopy.IsChecked = false;
                Assert.IsFalse(dialog.InstallAsCopy);
            }
            modes.SelectedItem = modes.Items.OfType<ComboBoxItem>().Single(item => (string)item.Tag == mode);
            Assert.AreEqual(mode == "Dark", ThemeManager.IsDark);
            Assert.AreEqual("Light", settings.Current.Theme, "Changing the preview mode must not change the saved theme.");
            if (language == "ar-SA") Assert.AreEqual(FlowDirection.RightToLeft, dialog.FlowDirection);
            ThemeWindowSurface.Track(dialog, "smallWindow");
            Assert.AreSame(root, dialog.FindName("PreviewRoot"), "Background composition must preserve the dialog's XAML namescope.");
            string name = (scheme is null ? "default" : "wisteria") + "-" + mode.ToLowerInvariant() + "-" + percent +
                (size.Width < 600 ? "-narrow-" + language : "") + (installOnly ? "-import" : "") + ".png";
            RenderAndCheck(dialog, size, scheme is not null && !SystemParameters.HighContrast,
                string.IsNullOrWhiteSpace(destination) ? null : Path.Combine(destination, name));
        }
        finally
        {
            dialog.Close();
            Assert.IsFalse(ThemeManager.IsDark, "Closing a preview must restore the saved Light appearance.");
            Assert.IsNull(ThemeManager.ActiveScheme, "Closing a preview must restore the saved default scheme.");
        }
    }

    private static void RenderAndCheck(ThemePreviewWindow dialog, Size size, bool artworkExpected, string? destination)
    {
        var content = (FrameworkElement)dialog.Content;
        var footer = Named<Border>(dialog, "PreviewFooter");
        var scroll = Named<ScrollViewer>(dialog, "PreviewScroll");
        var imageFrame = Named<Border>(dialog, "PreviewImageFrame");
        var image = Named<Image>(dialog, "PreviewImage");
        var apply = Named<Button>(dialog, "PreviewApplyButton");
        var cancel = Named<Button>(dialog, "PreviewCancelButton");
        var sample = Named<Button>(dialog, "PreviewSampleButton");
        var toggle = Named<CheckBox>(dialog, "PreviewBubbleCheck");
        if (toggle.Visibility == Visibility.Collapsed) toggle = Named<CheckBox>(dialog, "PreviewInstallCopyCheck");
        dialog.Content = null;
        var frame = new Border { Child = content, FlowDirection = dialog.FlowDirection,
            UseLayoutRounding = true, SnapsToDevicePixels = true,
            Background = (Brush)Application.Current.FindResource("B.WindowBg") };
        frame.Resources.MergedDictionaries.Add(Application.Current.Resources.MergedDictionaries[0]);
        frame.Resources.MergedDictionaries.Add(dialog.Resources);
        foreach (string key in Application.Current.Resources.Keys.OfType<string>().Where(key => key.StartsWith("T.", StringComparison.Ordinal)))
            frame.Resources[key] = Application.Current.Resources[key];
        try
        {
            Layout(frame, size);
            Assert.AreEqual(artworkExpected ? Visibility.Visible : Visibility.Collapsed, imageFrame.Visibility);
            if (artworkExpected)
            {
                Assert.IsNotNull(image.Source);
                Assert.IsTrue(image.Source.IsFrozen, "Preview artwork must reuse immutable decoded images.");
                Assert.IsTrue(image.Clip is RectangleGeometry || imageFrame.Clip is RectangleGeometry,
                    "Artwork needs a rounded clip, not just a rounded border behind square pixels.");
            }
            Rect footerBefore = Bounds(footer, frame);
            AssertInside(footerBefore, size, "The footer must remain inside the visible dialog.");
            Assert.IsTrue(Bounds(scroll, frame).Bottom <= footerBefore.Top + .5,
                "The scroll viewport must end above the fixed footer.");
            foreach (Button button in new[] { apply, cancel })
            {
                AssertInside(Bounds(button, frame), size, "Footer actions must remain fully visible without scrolling.");
                Assert.IsTrue(button.ActualWidth > 0 && button.ActualHeight >= 30);
            }
            Color onAccent = ColorOf(Application.Current.FindResource("B.OnAccent"));
            foreach (Button button in new[] { apply, sample })
            {
                Assert.AreEqual(onAccent, ColorOf(button.Foreground));
                TextBlock label = Descendants(button).OfType<TextBlock>().First(text => !string.IsNullOrWhiteSpace(text.Text));
                Assert.AreEqual(onAccent, ColorOf(label.Foreground), "Generated action labels must use the button's contrast color.");
            }
            if (destination is not null)
            {
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(frame);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(destination); encoder.Save(output);
            }
            scroll.ScrollToEnd(); Layout(frame, size);
            Assert.AreEqual(footerBefore, Bounds(footer, frame), "Scrolling content must not move the footer.");
            Rect toggleBounds = Bounds(toggle, frame);
            foreach (TextBlock text in Descendants(toggle).OfType<TextBlock>().Where(text => !string.IsNullOrWhiteSpace(text.Text)))
            {
                Rect label = Bounds(text, frame);
                Assert.IsTrue(label.Left >= toggleBounds.Left - .5 && label.Right <= toggleBounds.Right + .5,
                    "Long translated toggle text must wrap within its row.");
            }
        }
        finally { frame.Child = null; dialog.Content = content; }
    }

    private static T Named<T>(Window window, string name) where T : FrameworkElement =>
        window.FindName(name) as T ?? throw new AssertFailedException("Missing theme preview control: " + name);

    private static void Layout(FrameworkElement frame, Size size)
    {
        frame.Measure(size); frame.Arrange(new Rect(size)); frame.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
        frame.UpdateLayout();
    }

    private static Rect Bounds(FrameworkElement element, Visual ancestor) =>
        element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));

    private static void AssertInside(Rect bounds, Size size, string message) =>
        Assert.IsTrue(bounds.Left >= -.5 && bounds.Top >= -.5 && bounds.Right <= size.Width + .5 && bounds.Bottom <= size.Height + .5, message);

    private static Color ColorOf(object brush) => ((SolidColorBrush)brush).Color;

    private static IEnumerable<DependencyObject> Descendants(DependencyObject element)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(element, index);
            yield return child;
            foreach (DependencyObject descendant in Descendants(child)) yield return descendant;
        }
    }
}
