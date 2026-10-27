using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using KeeStashDock.Desktop.Views;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

internal static class SettingsOpacityScenarios
{
    internal static void Verify(SettingsWindow window, SettingsStore settings, string settingsPath)
    {
        string previousRoot = ThemeManager.Store.Root;
        ThemeAppearanceSnapshot previous = ThemeAppearanceSnapshot.Capture(settings.Current);
        string source = Environment.GetEnvironmentVariable("KEESTASHDOCK_THEME_SOURCE") ?? Path.Combine(AppContext.BaseDirectory, "theme");
        var slider = (Slider)window.FindName("SettingsSurfaceOpacitySlider");
        try
        {
            ThemeManager.Initialize(settings, source);
            settings.Current.ThemePresetId = "builtin:wisteria-atelier";
            settings.Current.ThemeUseCustomBackground = false;
            settings.Current.ThemeAppearanceCustomized = true;
            settings.Current.SettingsSurfaceOpacity = 1;
            settings.Current.CardOpacity = .83;
            settings.Current.Theme = "Light";
            settings.Save();
            ReloadControls(window);
            ThemeWindowSurface.Track(window, "workspace");
            Assert.IsNotNull(ThemeManager.ActiveScheme);
            Assert.IsTrue(slider.IsEnabled);
            Assert.AreEqual(10d, slider.Minimum);
            Assert.AreEqual(100d, slider.Maximum);
            Assert.AreEqual(5d, slider.TickFrequency);

            foreach (string mode in new[] { "Light", "Dark" })
            {
                settings.Current.Theme = mode;
                settings.Save();
                Brush globalInput = (Brush)Application.Current.FindResource("B.InputBg");
                Brush globalCard = (Brush)Application.Current.FindResource("B.CardBg");
                slider.Value = 60;
                Assert.AreEqual(.6, settings.Current.SettingsSurfaceOpacity, .0001);
                Assert.AreEqual(.83, settings.Current.CardOpacity, .0001, "Settings opacity must not change quick-panel opacity.");
                Assert.AreEqual("60%", ((TextBlock)window.FindName("SettingsSurfaceOpacityValue")).Text);
                var loaded = new SettingsStore(settingsPath); loaded.Load();
                Assert.AreEqual(.6, loaded.Current.SettingsSurfaceOpacity, .0001);
                Assert.AreEqual(.83, loaded.Current.CardOpacity, .0001);
                Assert.AreSame(globalInput, Application.Current.FindResource("B.InputBg"), "Editing settings must not replace the shared input brush.");
                Assert.AreSame(globalCard, window.FindResource("B.CardBg"), "Popup surfaces must keep the opaque shared card brush.");
                Assert.AreEqual(1d, globalInput.Opacity);
                Assert.AreEqual(1d, globalCard.Opacity);
                VerifySurfaces(window, .6);

                slider.Value = 100;
                VerifySurfaces(window, 1);
                slider.Value = 60;
            }

            VerifySettingsFrost(window, settings, settingsPath);

            ThemeAppearanceSnapshot snapshot = ThemeAppearanceSnapshot.Capture(settings.Current);
            settings.Current.SettingsSurfaceOpacity = .2;
            settings.Current.SettingsFrostedGlassEnabled = false;
            settings.Current.SettingsFrostedGlassRadius = 2;
            snapshot.Restore(settings.Current);
            Assert.AreEqual(.6, settings.Current.SettingsSurfaceOpacity, .0001, "Failed theme application must restore the settings surface opacity.");
            Assert.IsTrue(settings.Current.SettingsFrostedGlassEnabled);
            Assert.AreEqual(8d, settings.Current.SettingsFrostedGlassRadius);

            // Preview ignores custom overrides; cancelling resumes the saved
            // opacity without writing it back to the appearance-only theme.
            ThemeScheme active = ThemeManager.ActiveScheme!;
            ThemeVariant previewLight = ThemeManager.CreateDefaultVariant(false);
            ThemeVariant previewDark = ThemeManager.CreateDefaultVariant(true);
            previewLight.SettingsSurfaceOpacity = .45;
            previewDark.SettingsSurfaceOpacity = .75;
            previewDark.SettingsFrostedGlassEnabled = true;
            previewDark.SettingsFrostedGlassRadius = 12;
            previewLight.Backgrounds["workspace"] = active.Light.Backgrounds["workspace"];
            previewDark.Backgrounds["workspace"] = active.Dark.Backgrounds["workspace"];
            var preview = new ThemeScheme("preview", active.Directory, active.Manifest, previewLight, previewDark);
            ThemeManager.Preview(preview, "Light");
            VerifySurfaces(window, .45);
            Assert.AreEqual(45d, slider.Value, .0001);
            Assert.AreEqual(previewLight.Backgrounds["workspace"].BlurRadius, BackgroundRadius(window));
            Assert.AreEqual(Visibility.Collapsed, ((Grid)window.FindName("SettingsFrostRadiusRow")).Visibility);
            ThemeManager.Preview(preview, "Dark");
            VerifySurfaces(window, .75);
            Assert.AreEqual(75d, slider.Value, .0001);
            Assert.AreEqual(12d, BackgroundRadius(window));
            ThemeManager.EndPreview();
            VerifySurfaces(window, .6);
            Assert.AreEqual(60d, slider.Value, .0001, "Cancelling preview must restore the saved slider value as well as the brushes.");
            Assert.AreEqual(.6, settings.Current.SettingsSurfaceOpacity, .0001);
            Assert.AreEqual(8d, BackgroundRadius(window));

            settings.Current.ThemePresetId = "";
            settings.Save();
            Assert.IsFalse(slider.IsEnabled, "A settings window without artwork must remain readable and opaque.");
            VerifySurfaces(window, 1);
            Assert.AreEqual(.6, settings.Current.SettingsSurfaceOpacity, .0001, "Hiding the background preserves the user's opacity preference.");
            Assert.IsFalse(((CheckBox)window.FindName("SettingsFrostCheck")).IsEnabled);
            Assert.AreEqual(Visibility.Collapsed, ((Grid)window.FindName("SettingsFrostRadiusRow")).Visibility);
            Assert.AreEqual(0d, BackgroundRadius(window));

            previewLight.Backgrounds.Clear(); previewDark.Backgrounds.Clear();
            ThemeManager.Preview(preview, "Light");
            Assert.IsFalse(slider.IsEnabled, "A palette-only theme must not enable background transparency.");
            VerifySurfaces(window, 1);
        }
        finally
        {
            ThemeManager.EndPreview();
            previous.Restore(settings.Current);
            ThemeManager.Initialize(settings, previousRoot);
            settings.Save();
            ReloadControls(window);
            window.SelectSettingsPage("General");
        }
    }

    private static void VerifySettingsFrost(SettingsWindow window, SettingsStore settings, string settingsPath)
    {
        var check = (CheckBox)window.FindName("SettingsFrostCheck");
        var strength = (Slider)window.FindName("SettingsFrostSlider");
        var row = (Grid)window.FindName("SettingsFrostRadiusRow");
        Assert.IsTrue(check.IsEnabled);
        Assert.AreEqual(1d, strength.Minimum);
        Assert.AreEqual(18d, strength.Maximum);
        Border image = BackgroundLayer(window);
        Brush cachedBrush = image.Background;
        double presetBlur = ThemeManager.ActiveVariant!.Backgrounds["workspace"].BlurRadius;
        var other = new Window { Content = new Border() };
        ThemeWindowSurface.Track(other, "workspace");
        try
        {
            check.IsChecked = true;
            strength.Value = 8;
            Assert.IsTrue(settings.Current.SettingsFrostedGlassEnabled);
            Assert.AreEqual(8d, settings.Current.SettingsFrostedGlassRadius);
            Assert.AreEqual(Visibility.Visible, row.Visibility);
            Assert.AreEqual(8d, BackgroundRadius(window));
            Assert.AreSame(cachedBrush, image.Background, "Blur changes must reuse the artwork brush and decoded image.");
            Assert.IsTrue(image.Effect!.IsFrozen);
            Assert.AreEqual(presetBlur, BackgroundRadius(other), "Settings blur must not change other windows.");
            Assert.AreEqual(.6, settings.Current.SettingsSurfaceOpacity);
            var reloaded = new SettingsStore(settingsPath); reloaded.Load();
            Assert.IsTrue(reloaded.Current.SettingsFrostedGlassEnabled);
            Assert.AreEqual(8d, reloaded.Current.SettingsFrostedGlassRadius);

            strength.Value = 14;
            Assert.AreEqual(14d, BackgroundRadius(window));
            Assert.AreSame(cachedBrush, image.Background);
            check.IsChecked = false;
            Assert.AreEqual(presetBlur, BackgroundRadius(window));
            Assert.AreEqual(Visibility.Collapsed, row.Visibility);
            check.IsChecked = true;
            strength.Value = 8;
        }
        finally { other.Close(); }
    }

    private static Border BackgroundLayer(Window window) =>
        (Border)((Grid)((Grid)window.Content).Children[0]).Children[0];

    private static double BackgroundRadius(Window window) => (BackgroundLayer(window).Effect as BlurEffect)?.Radius ?? 0;

    private static void VerifySurfaces(SettingsWindow window, double opacity)
    {
        foreach (string key in new[] { "B.SettingsSurfaceBg", "B.InputBg" })
        {
            var brush = (SolidColorBrush)window.Resources[key];
            Assert.IsTrue(brush.IsFrozen);
            Assert.AreEqual(opacity, brush.Opacity, .0001);
        }
        window.SelectSettingsPage("Appearance");
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        var frame = new Border { Child = content };
        frame.Resources.MergedDictionaries.Add(Application.Current.Resources.MergedDictionaries[0]);
        frame.Resources.MergedDictionaries.Add(window.Resources);
        try
        {
            frame.Measure(new Size(960, 780)); frame.Arrange(new Rect(0, 0, 960, 780)); frame.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
            var navigation = (Border)window.FindName("SettingsNavigationSurface");
            Assert.AreEqual(opacity, navigation.Background.Opacity, .0001);
            Border section = Descendants<Border>(content).First(border => ReferenceEquals(border.Style, window.Resources["S.Section"]));
            Assert.AreEqual(opacity, section.Background.Opacity, .0001);
            var combo = (ComboBox)window.FindName("ThemeCombo");
            combo.ApplyTemplate();
            var input = (SolidColorBrush)window.Resources["B.InputBg"];
            Assert.IsTrue(Descendants<Border>(combo).Any(border => ReferenceEquals(border.Background, input)),
                "The live ComboBox face must use the scoped input brush.");
            var value = (TextBlock)window.FindName("SettingsSurfaceOpacityValue");
            Assert.AreEqual(1d, value.Foreground.Opacity);
            for (DependencyObject? ancestor = value; ancestor is not null; ancestor = VisualTreeHelper.GetParent(ancestor))
                if (ancestor is UIElement element)
                {
                    Assert.AreEqual(1d, element.Opacity, "Surface transparency must not fade the text tree.");
                    Assert.IsFalse(element.Effect is BlurEffect, "Frosted glass must not blur the text tree.");
                }
        }
        finally { frame.Child = null; window.Content = content; }
    }

    private static void ReloadControls(SettingsWindow window) => typeof(SettingsWindow)
        .GetMethod("ReloadAppearanceControls", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);

    private static IEnumerable<T> Descendants<T>(DependencyObject element) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(element, i);
            if (child is T match) yield return match;
            foreach (T descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
