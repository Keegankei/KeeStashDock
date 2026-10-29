using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KeeStashDock.Desktop.Views;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

internal static class ThemePresentationScenarios
{
    internal static void Verify(string isolatedRoot, SettingsStore settings)
    {
        var appearance = ThemeAppearanceSnapshot.Capture(settings.Current);
        string? previousWallpaper = settings.Current.BackgroundImagePath;
        double? previousPosition = settings.Current.PanelPosX;
        string themeRoot = Path.Combine(isolatedRoot, "theme-fixtures");
        ThemeManager.Initialize(settings, themeRoot);
        Window? first = null, second = null;
        try
        {
            settings.Current.ThemePresetId = ""; settings.Current.Theme = "Light";
            settings.Current.BackgroundImagePath = Path.Combine(isolatedRoot, "private-wallpaper.png");
            settings.Current.ThemeUseCustomBackground = true; settings.Save();
            ThemeManager.Preview(null, "Dark");
            Assert.IsNull(ThemeManager.CreatePanelAppearanceSettings(settings.Current).BackgroundImagePath,
                "The default appearance preview must hide a saved custom background without deleting its reference.");
            Assert.IsNotNull(settings.Current.BackgroundImagePath);
            settings.Current.PanelPosX = 123;
            ThemeManager.EndPreview();
            Assert.IsFalse(ThemeManager.IsDark);
            Assert.AreEqual(123d, settings.Current.PanelPosX);

            Directory.CreateDirectory(Path.Combine(themeRoot, "assets"));
            string imagePath = Path.Combine(themeRoot, "assets", "fixture.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
                new byte[] { 20, 40, 60, 255, 20, 40, 60, 255, 20, 40, 60, 255, 20, 40, 60, 255 }, 8)));
            using (var file = File.Create(imagePath)) encoder.Save(file);
            byte[] originalImage = File.ReadAllBytes(imagePath);
            ThemeVariant light = ThemeManager.CreateDefaultVariant(false), dark = ThemeManager.CreateDefaultVariant(true);
            foreach (ThemeVariant variant in new[] { light, dark })
                variant.Backgrounds["workspace"] = new ThemeBackground { Path = "assets/fixture.png", OverlayOpacity = .8 };
            light.CardOpacity = .94; dark.CardOpacity = .82;
            var scheme = new ThemeScheme("preview", themeRoot, new ThemeManifest { Id = "preview", Name = "Preview" }, light, dark);
            first = new Window { Content = new Border { Height = 40, Width = 100 } }; second = new Window { Content = new Border() };
            ThemeWindowSurface.Track(first, "workspace"); ThemeWindowSurface.Track(second, "workspace");
            ThemeManager.Preview(scheme, "Light");
            Border a = (Border)((Grid)((Grid)first.Content).Children[0]).Children[0];
            Border b = (Border)((Grid)((Grid)second.Content).Children[0]).Children[0];
            ImageSource firstImage = ((ImageBrush)a.Background).ImageSource;
            Assert.IsNotNull(firstImage); Assert.AreSame(firstImage, ((ImageBrush)b.Background).ImageSource);
            Assert.IsTrue(firstImage.IsFrozen, "Shared theme images must not be decoded for every card or window.");
            ((Grid)first.Content).Measure(new Size(100, double.PositiveInfinity));
            Assert.AreEqual(40d, ((Grid)first.Content).DesiredSize.Height, "Backgrounds must not enlarge SizeToContent windows.");
            ImageSource cached = firstImage;
            settings.Current.PanelPosX = 456; settings.Save();
            Assert.AreSame(cached, ((ImageBrush)a.Background).ImageSource, "Unrelated geometry saves must not decode or replace artwork.");
            ThemeManager.Preview(scheme, "Dark");
            Assert.IsTrue(ThemeManager.IsDark);
            Assert.AreEqual(.82, ThemeManager.CreatePanelAppearanceSettings(settings.Current).CardOpacity);
            int accessibilityRefreshes = 0;
            void CountRefresh() => accessibilityRefreshes++;
            ThemeManager.Changed += CountRefresh;
            try
            {
                // Exercise the notification path without changing Windows
                // accessibility settings or relying on the current OS theme.
                typeof(ThemeManager).GetMethod("OnSystemPropertyChanged",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(null, [null, new System.ComponentModel.PropertyChangedEventArgs(nameof(SystemParameters.HighContrast))]);
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Assert.AreEqual(1, accessibilityRefreshes, "High-contrast notifications refresh even a fixed Dark appearance.");
                Assert.IsTrue(ThemeManager.IsDark);
            }
            finally { ThemeManager.Changed -= CountRefresh; }
            CollectionAssert.AreEqual(originalImage, File.ReadAllBytes(imagePath), "Theme composition must never alter image pixels on disk.");

            ThemeAppearanceSnapshot snapshot = ThemeAppearanceSnapshot.Capture(settings.Current);
            settings.Current.Theme = "Dark"; settings.Current.CardOpacity = .2; settings.Current.PanelPosX = 789;
            snapshot.Restore(settings.Current);
            Assert.AreEqual(789d, settings.Current.PanelPosX, "Appearance rollback must preserve movement during preview.");
            Assert.AreEqual(snapshot.CardOpacity, settings.Current.CardOpacity);
            ThemeManager.EndPreview();
            settings.Current.ThemePresetId = "custom:missing"; settings.Save();
            Assert.AreEqual("Theme.Unavailable", ThemeManager.LoadError);
            Assert.AreEqual("custom:missing", settings.Current.ThemePresetId, "Missing themes retain the recoverable selection.");
            Assert.IsNull(ThemeManager.ActiveScheme);
        }
        finally
        {
            first?.Close(); second?.Close(); ThemeManager.EndPreview();
            appearance.Restore(settings.Current);
            settings.Current.BackgroundImagePath = previousWallpaper; settings.Current.PanelPosX = previousPosition;
            settings.Save();
        }
        ThemePreviewRenderScenarios.RenderIfRequested(isolatedRoot, settings);
    }
}
