using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using KeeStashDock.Desktop.Views;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

/// <summary>Independent background choices on the actual Settings controls, without native dialogs.</summary>
internal static class SettingsBackgroundScenarios
{
    internal static void Verify(SettingsWindow window, SettingsStore settings, string settingsPath, string isolatedRoot)
    {
        string previousRoot = ThemeManager.Store.Root;
        ThemeAppearanceSnapshot previous = ThemeAppearanceSnapshot.Capture(settings.Current);
        string? previousPanelImage = settings.Current.BackgroundImagePath;
        string previousTarget = (string)((ComboBoxItem)Named<ComboBox>(window, "BackgroundTargetCombo").SelectedItem).Tag;
        string source = Environment.GetEnvironmentVariable("KEESTASHDOCK_THEME_SOURCE") ?? Path.Combine(AppContext.BaseDirectory, "theme");
        string panelImage = Path.Combine(isolatedRoot, "panel-background.png");
        string settingsImage = Path.Combine(isolatedRoot, "settings-background.png");
        WriteImage(panelImage, Colors.SteelBlue);
        WriteImage(settingsImage, Colors.Thistle);
        var other = new Window { Content = new Border() };
        try
        {
            ThemeManager.Initialize(settings, source);
            var cfg = settings.Current;
            cfg.ThemePresetId = "";
            cfg.Theme = "Light";
            cfg.BackgroundImagePath = null;
            cfg.SettingsBackgroundImagePath = null;
            cfg.ThemeUseCustomBackground = true;
            cfg.ThemeUseCustomSettingsBackground = false;
            cfg.ThemeAppearanceCustomized = true;
            cfg.SettingsAppearanceCustomized = false;
            cfg.BackgroundOverlayOpacity = .45;
            cfg.SettingsBackgroundOverlayOpacity = .2;
            cfg.SettingsBackgroundOverlayCustomized = false;
            cfg.FrostedGlassEnabled = true;
            cfg.FrostedGlassRadius = 6;
            cfg.SettingsFrostedGlassEnabled = false;
            settings.Save();
            ReloadControls(window);
            window.SelectSettingsPage("Appearance");
            ThemeWindowSurface.Track(window, "settings");
            ThemeWindowSurface.Track(other, "workspace");

            SelectTarget(window, "quickPanel");
            window.SetSelectedBackground(panelImage);
            Assert.AreEqual(panelImage, ThemeManager.CreatePanelAppearanceSettings(cfg).BackgroundImagePath);
            Assert.IsNull(ThemeManager.GetBackgroundImage("settings"), "A personal panel image must not silently become a Settings background.");
            VerifyCategoryNavigation(window, settings, settingsPath);

            SelectTarget(window, "settings");
            Assert.IsTrue(window.IsSettingsBackgroundSelected);
            window.SetSelectedBackground(settingsImage);
            Assert.AreEqual(panelImage, cfg.BackgroundImagePath);
            Assert.AreEqual(settingsImage, cfg.SettingsBackgroundImagePath);
            Assert.IsTrue(cfg.ThemeUseCustomSettingsBackground);
            Assert.AreEqual(settingsImage, ThemeManager.CreatePanelAppearanceSettings(cfg).SettingsBackgroundImagePath);
            Assert.AreEqual(Visibility.Visible, Named<FrameworkElement>(window, "SettingsBackgroundOptions").Visibility);
            Assert.AreEqual(Visibility.Collapsed, Named<FrameworkElement>(window, "QuickPanelFrostOptions").Visibility);
            Assert.IsTrue(Named<Slider>(window, "SettingsSurfaceOpacitySlider").IsEnabled,
                "A personal Settings background must enable its surface-opacity control even without a theme preset.");
            Assert.IsNull(BackgroundLayer(other).Background, "Settings artwork must not affect the workbench role.");
            ImageBrush settingsBrush = (ImageBrush)BackgroundLayer(window).Background;
            Assert.AreSame(ThemeManager.GetBackgroundImage("settings"), settingsBrush.ImageSource);
            Assert.IsTrue(settingsBrush.ImageSource.IsFrozen);

            Named<Slider>(window, "BgOpacitySlider").Value = 35;
            Assert.AreEqual(.35, cfg.SettingsBackgroundOverlayOpacity, .0001);
            Assert.IsTrue(cfg.SettingsBackgroundOverlayCustomized);
            Assert.AreEqual(.45, cfg.BackgroundOverlayOpacity, .0001);
            Assert.AreEqual(.35, Scrim(window).Opacity, .0001);
            Named<CheckBox>(window, "SettingsFrostCheck").IsChecked = true;
            Named<Slider>(window, "SettingsFrostSlider").Value = 9;
            Assert.AreEqual(9d, ((BlurEffect)BackgroundLayer(window).Effect).Radius);
            Assert.IsTrue(cfg.FrostedGlassEnabled);
            Assert.AreEqual(6d, cfg.FrostedGlassRadius, "Settings blur must not alter panel blur.");
            Assert.AreSame(settingsBrush, BackgroundLayer(window).Background,
                "Changing opacity and blur must reuse the decoded artwork and its brush.");

            var loaded = new SettingsStore(settingsPath); loaded.Load();
            Assert.AreEqual(panelImage, loaded.Current.BackgroundImagePath);
            Assert.AreEqual(settingsImage, loaded.Current.SettingsBackgroundImagePath);
            Assert.IsTrue(loaded.Current.ThemeUseCustomSettingsBackground);
            Assert.AreEqual(.35, loaded.Current.SettingsBackgroundOverlayOpacity, .0001);
            Assert.IsTrue(loaded.Current.SettingsBackgroundOverlayCustomized);
            Assert.AreEqual(.45, loaded.Current.BackgroundOverlayOpacity, .0001);
            Assert.IsTrue(loaded.Current.SettingsFrostedGlassEnabled);
            Assert.AreEqual(9d, loaded.Current.SettingsFrostedGlassRadius);
            Assert.IsTrue(loaded.Current.SettingsAppearanceCustomized);

            VerifySaveFailureRollsBack(window, settings, settingsPath, panelImage, settingsImage);
            VerifyInvalidImageKeepsPreferences(window, settings, settingsPath, isolatedRoot, settingsImage);
            SelectTarget(window, "quickPanel");
            window.SetSelectedBackground(null);
            Assert.IsNull(cfg.BackgroundImagePath);
            Assert.AreEqual(settingsImage, ThemeManager.GetBackgroundPath("settings"),
                "Clearing the panel must not clear the independently chosen Settings image.");
            window.SetSelectedBackground(panelImage);
            SelectTarget(window, "settings");
            window.SetSelectedBackground(null);
            Assert.IsTrue(cfg.ThemeUseCustomSettingsBackground);
            Assert.IsNull(ThemeManager.GetBackgroundImage("settings"));
            Assert.IsNull(BackgroundLayer(window).Background);
            Assert.AreEqual(panelImage, cfg.BackgroundImagePath);
            Assert.IsFalse(Named<Slider>(window, "SettingsSurfaceOpacitySlider").IsEnabled);
            Assert.AreEqual(Visibility.Collapsed, Named<FrameworkElement>(window, "BackgroundOptions").Visibility);
            Assert.AreEqual(1d, ((SolidColorBrush)window.Resources["B.SettingsSurfaceBg"]).Opacity);

            VerifyThemeFallbackAndPreview(window, settings, settingsPath, panelImage, settingsImage);
            VerifySettingsChangesLeaveOtherAreasUnchanged(window, settings, settingsImage);
        }
        finally
        {
            other.Close();
            ThemeManager.EndPreview();
            previous.Restore(settings.Current);
            settings.Current.BackgroundImagePath = previousPanelImage;
            ThemeManager.Initialize(settings, previousRoot);
            settings.Save();
            ReloadControls(window);
            SelectTarget(window, previousTarget);
            window.SelectSettingsPage("General");
        }
    }

    private static void VerifyCategoryNavigation(SettingsWindow window, SettingsStore settings, string settingsPath)
    {
        string saved = File.ReadAllText(settingsPath);
        int writes = 0;
        void Changed() => writes++;
        settings.Changed += Changed;
        try
        {
            foreach (string target in new[] { "settings", "quickPanel", "settings", "quickPanel" })
            {
                SelectTarget(window, target);
                Assert.AreEqual(target == "settings", window.IsSettingsBackgroundSelected);
                Assert.AreEqual(target == "settings" ? 20d : 45d, Named<Slider>(window, "BgOpacitySlider").Value);
            }
            Assert.AreEqual(0, writes, "Browsing background categories must not persist settings or copy values between areas.");
            Assert.AreEqual(saved, File.ReadAllText(settingsPath));
        }
        finally { settings.Changed -= Changed; }
    }

    private static void VerifyInvalidImageKeepsPreferences(SettingsWindow window, SettingsStore settings,
        string settingsPath, string isolatedRoot, string settingsImage)
    {
        string invalid = Path.Combine(isolatedRoot, "invalid-background.png");
        File.WriteAllText(invalid, "This is not a supported image.");
        string saved = File.ReadAllText(settingsPath);
        Assert.Throws<Exception>(() => window.SetSelectedBackground(invalid));
        Assert.AreEqual(saved, File.ReadAllText(settingsPath), "Rejected image choices must not persist partial appearance changes.");
        Assert.AreEqual(settingsImage, settings.Current.SettingsBackgroundImagePath);
        Assert.AreEqual(settingsImage, ThemeManager.GetBackgroundPath("settings"));
    }

    private static void VerifySaveFailureRollsBack(SettingsWindow window, SettingsStore settings,
        string settingsPath, string panelImage, string settingsImage)
    {
        string temporary = settingsPath + ".tmp";
        Assert.IsFalse(File.Exists(temporary));
        Assert.IsFalse(Directory.Exists(temporary));
        string saved = File.ReadAllText(settingsPath);
        ThemeAppearanceSnapshot appearance = ThemeAppearanceSnapshot.Capture(settings.Current);
        string? panelPath = settings.Current.BackgroundImagePath;
        int notifications = 0;
        void Changed() => notifications++;
        settings.Changed += Changed;
        // An empty directory at the store's temporary-file path makes the real
        // atomic save fail without changing file ACLs or requiring privileges.
        Directory.CreateDirectory(temporary);
        try
        {
            SelectTarget(window, "settings");
            foreach (Action change in new Action[]
            {
                () => window.SetSelectedBackground(panelImage),
                () => window.SetSelectedBackground(null),
                () => window.UseThemeBackground(),
                () => window.CommitBackgroundChange(cfg =>
                {
                    cfg.SettingsBackgroundOverlayOpacity = .85;
                    cfg.SettingsBackgroundOverlayCustomized = true;
                    cfg.SettingsSurfaceOpacity = .25;
                    cfg.SettingsFrostedGlassEnabled = false;
                    cfg.SettingsFrostedGlassRadius = 3;
                    cfg.SettingsAppearanceCustomized = true;
                }),
            })
            {
                Exception error = Assert.Throws<Exception>(change);
                Assert.IsTrue(ThemePackageStore.IsPackageFailure(error), "The fixture must fail on the real settings write.");
                Assert.AreEqual(appearance, ThemeAppearanceSnapshot.Capture(settings.Current),
                    "A rejected save must restore all appearance preferences in memory.");
                Assert.AreEqual(panelPath, settings.Current.BackgroundImagePath);
                Assert.AreEqual(saved, File.ReadAllText(settingsPath));
                Assert.AreEqual(settingsImage, ThemeManager.GetBackgroundPath("settings"));
                Assert.AreEqual(35d, Named<Slider>(window, "BgOpacitySlider").Value,
                    "The editor must return to the saved overlay after rollback.");
            }

            SelectTarget(window, "quickPanel");
            Exception panelError = Assert.Throws<Exception>(() => window.SetSelectedBackground(settingsImage));
            Assert.IsTrue(ThemePackageStore.IsPackageFailure(panelError));
            Assert.AreEqual(panelPath, settings.Current.BackgroundImagePath,
                "The separate quick-panel path must also roll back after a rejected save.");
            Assert.AreEqual(appearance, ThemeAppearanceSnapshot.Capture(settings.Current));
            Assert.AreEqual(saved, File.ReadAllText(settingsPath));
            Assert.AreEqual(0, notifications, "Failed writes must not announce changed settings.");
        }
        finally
        {
            settings.Changed -= Changed;
            Directory.Delete(temporary);
            SelectTarget(window, "settings");
        }
        settings.Save();
        Assert.AreEqual(saved, File.ReadAllText(settingsPath),
            "A later successful save must not silently persist a previously rejected choice.");
    }

    private static void VerifySettingsChangesLeaveOtherAreasUnchanged(SettingsWindow window,
        SettingsStore settings, string settingsImage)
    {
        var cfg = settings.Current;
        ThemeAppearanceSnapshot previous = ThemeAppearanceSnapshot.Capture(cfg);
        string? previousPanelImage = cfg.BackgroundImagePath;
        try
        {
            cfg.ThemePresetId = "builtin:wisteria-atelier";
            cfg.Theme = "Light";
            cfg.ThemeUseCustomBackground = false;
            cfg.ThemeUseCustomSettingsBackground = false;
            cfg.ThemeAppearanceCustomized = false;
            cfg.SettingsAppearanceCustomized = false;
            cfg.SettingsBackgroundOverlayCustomized = false;
            cfg.SelectionBubbleStyle = "FollowTheme";
            cfg.SettingsBackgroundOverlayOpacity = .2;
            settings.Save();
            ThemeVariant light = ThemeManager.ActiveVariant!;
            cfg.BackgroundOverlayOpacity = light.Backgrounds["quickPanel"].OverlayOpacity;
            cfg.CardOpacity = light.CardOpacity;
            cfg.BubbleOpacity = light.BubbleOpacity;
            cfg.SettingsSurfaceOpacity = light.SettingsSurfaceOpacity;
            cfg.SettingsFrostedGlassEnabled = light.SettingsFrostedGlassEnabled;
            cfg.SettingsFrostedGlassRadius = light.SettingsFrostedGlassRadius;
            cfg.Theme = "Dark";
            settings.Save();
            ReloadControls(window);
            SelectTarget(window, "settings");
            AppSettings expected = ThemeManager.CreatePanelAppearanceSettings(cfg);
            Assert.AreNotEqual(cfg.BackgroundOverlayOpacity, expected.BackgroundOverlayOpacity,
                "The fixture needs distinct stored Light and active Dark values to expose global override leaks.");

            void VerifyOtherAreas()
            {
                Assert.IsFalse(cfg.ThemeAppearanceCustomized,
                    "Changing only Settings must not enable the global appearance override.");
                AppSettings current = ThemeManager.CreatePanelAppearanceSettings(cfg);
                Assert.AreEqual(expected.BackgroundImagePath, current.BackgroundImagePath);
                Assert.AreEqual(expected.BackgroundOverlayOpacity, current.BackgroundOverlayOpacity);
                Assert.AreEqual(expected.FrostedGlassEnabled, current.FrostedGlassEnabled);
                Assert.AreEqual(expected.FrostedGlassRadius, current.FrostedGlassRadius);
                Assert.AreEqual(expected.CardOpacity, current.CardOpacity);
                Assert.AreEqual(expected.BubbleOpacity, current.BubbleOpacity);
                Assert.AreEqual(expected.PanelTextColorMode, current.PanelTextColorMode);
                Assert.AreEqual(expected.PanelTextCustomColor, current.PanelTextCustomColor);
            }

            window.SetSelectedBackground(settingsImage);
            VerifyOtherAreas();
            Named<Slider>(window, "BgOpacitySlider").Value = 35;
            Assert.IsTrue(cfg.SettingsBackgroundOverlayCustomized);
            VerifyOtherAreas();
            Named<Slider>(window, "SettingsSurfaceOpacitySlider").Value = 60;
            Assert.IsTrue(cfg.SettingsAppearanceCustomized);
            VerifyOtherAreas();
            Named<CheckBox>(window, "SettingsFrostCheck").IsChecked = true;
            Named<Slider>(window, "SettingsFrostSlider").Value = 9;
            Assert.IsTrue(cfg.SettingsAppearanceCustomized);
            VerifyOtherAreas();
            window.SetSelectedBackground(null);
            VerifyOtherAreas();
            window.UseThemeBackground();
            VerifyOtherAreas();

            var customized = ThemeAppearanceSnapshot.Capture(cfg);
            cfg.SettingsAppearanceCustomized = false;
            customized.Restore(cfg);
            Assert.IsTrue(cfg.SettingsAppearanceCustomized,
                "Theme rollback must restore the independent Settings appearance override.");
        }
        finally
        {
            previous.Restore(cfg);
            cfg.BackgroundImagePath = previousPanelImage;
            settings.Save();
            ReloadControls(window);
        }
    }

    private static void VerifyThemeFallbackAndPreview(SettingsWindow window, SettingsStore settings,
        string settingsPath, string panelImage, string settingsImage)
    {
        var cfg = settings.Current;
        cfg.ThemePresetId = "builtin:wisteria-atelier";
        settings.Save();
        Assert.IsNotNull(ThemeManager.ActiveScheme);
        Assert.IsNull(ThemeManager.GetBackgroundImage("settings"),
            "An explicit Settings clear must suppress a preset's inherited workspace background.");
        Assert.IsNotNull(ThemeManager.GetBackgroundImage("workspace"));
        window.UseThemeBackground();
        Assert.IsFalse(cfg.ThemeUseCustomSettingsBackground);
        Assert.AreEqual(ThemeManager.GetBackgroundPath("workspace"), ThemeManager.GetBackgroundPath("settings"),
            "Existing theme packages without a Settings role must retain their workspace artwork.");
        Assert.AreEqual(panelImage, cfg.BackgroundImagePath);
        Assert.IsTrue(cfg.ThemeUseCustomBackground);

        cfg.ThemeAppearanceCustomized = true;
        cfg.SettingsBackgroundOverlayOpacity = .2;
        cfg.SettingsBackgroundOverlayCustomized = false;
        settings.Save();
        double presetOverlay = ThemeManager.ActiveVariant!.Backgrounds["workspace"].OverlayOpacity;
        Assert.AreEqual(presetOverlay, ThemeManager.GetSettingsBackground(cfg)!.OverlayOpacity, .0001,
            "Legacy appearance customization must not replace the theme's Settings overlay with a newly added default.");
        Assert.AreEqual(presetOverlay, ThemeManager.CreatePanelAppearanceSettings(cfg).SettingsBackgroundOverlayOpacity, .0001);
        Assert.AreEqual(presetOverlay, Scrim(window).Opacity, .0001);
        Assert.AreEqual(presetOverlay * 100, Named<Slider>(window, "BgOpacitySlider").Value, .0001);
        Named<Slider>(window, "BgOpacitySlider").Value = 35;
        Assert.IsTrue(cfg.SettingsBackgroundOverlayCustomized);
        Assert.AreEqual(.35, ThemeManager.GetSettingsBackground(cfg)!.OverlayOpacity, .0001);

        window.SetSelectedBackground(settingsImage);
        AppSettings detached = cfg.Copy();
        detached.SettingsBackgroundImagePath = panelImage;
        Assert.AreEqual(panelImage, ThemeManager.CreatePanelAppearanceSettings(detached).SettingsBackgroundImagePath,
            "Resolving a disposable appearance snapshot must use that snapshot's image, not the current global Settings image.");
        Assert.AreEqual(panelImage, ThemeManager.GetSettingsBackground(detached)!.Path);
        Assert.AreEqual(settingsImage, ThemeManager.GetBackgroundPath("settings"));
        detached.ThemeUseCustomSettingsBackground = false;
        Assert.AreEqual(ThemeManager.GetBackgroundPath("workspace"),
            ThemeManager.CreatePanelAppearanceSettings(detached).SettingsBackgroundImagePath,
            "A snapshot using the preset must ignore another active personal Settings image.");
        var snapshot = ThemeAppearanceSnapshot.Capture(cfg);
        cfg.ThemeUseCustomSettingsBackground = false;
        cfg.SettingsBackgroundImagePath = null;
        cfg.SettingsBackgroundOverlayOpacity = .9;
        cfg.SettingsBackgroundOverlayCustomized = false;
        snapshot.Restore(cfg);
        Assert.IsTrue(cfg.ThemeUseCustomSettingsBackground);
        Assert.AreEqual(settingsImage, cfg.SettingsBackgroundImagePath);
        Assert.AreEqual(.35, cfg.SettingsBackgroundOverlayOpacity, .0001,
            "Failed theme application must restore the Settings background override and its overlay.");
        Assert.IsTrue(cfg.SettingsBackgroundOverlayCustomized);

        ThemeScheme active = ThemeManager.ActiveScheme!;
        string saved = File.ReadAllText(settingsPath);
        ThemeManager.Preview(active, "Dark");
        Assert.AreEqual(ThemeManager.GetBackgroundPath("workspace"), ThemeManager.GetBackgroundPath("settings"));
        Assert.AreNotEqual(settingsImage, ThemeManager.GetBackgroundPath("settings"));
        Assert.AreNotEqual(panelImage, ThemeManager.CreatePanelAppearanceSettings(cfg).BackgroundImagePath);
        ThemeManager.EndPreview();
        Assert.AreEqual(settingsImage, ThemeManager.GetBackgroundPath("settings"));
        Assert.AreEqual(panelImage, ThemeManager.CreatePanelAppearanceSettings(cfg).BackgroundImagePath);
        Assert.AreEqual(saved, File.ReadAllText(settingsPath), "Cancelling a preview must leave both original background choices unchanged on disk.");

        ThemeVariant light = ThemeManager.CreateDefaultVariant(false), dark = ThemeManager.CreateDefaultVariant(true);
        foreach (ThemeVariant variant in new[] { light, dark })
        {
            variant.Backgrounds["workspace"] = active.Light.Backgrounds["workspace"];
            variant.Backgrounds["settings"] = active.Light.Backgrounds["quickPanel"];
        }
        var separate = new ThemeScheme("preview", active.Directory, active.Manifest, light, dark);
        ThemeManager.Preview(separate, "Light");
        Assert.AreEqual(ThemePackageStore.Resolve(active.Directory, light.Backgrounds["settings"].Path),
            ThemeManager.GetBackgroundPath("settings"), "An explicit Settings role must take precedence over the workspace fallback.");
        Assert.AreNotEqual(ThemeManager.GetBackgroundPath("workspace"), ThemeManager.GetBackgroundPath("settings"));
        light.HideSettingsBackground = true;
        ThemeManager.Preview(separate, "Light");
        Assert.IsNull(ThemeManager.GetBackgroundImage("settings"));
        Assert.IsNotNull(ThemeManager.GetBackgroundImage("workspace"), "A theme's Settings-only clear must keep the workbench background.");
        ThemeManager.EndPreview();
        Assert.AreEqual(settingsImage, ThemeManager.GetBackgroundPath("settings"));

        cfg.SettingsBackgroundImagePath = settingsImage + ".missing";
        settings.Save();
        Assert.IsNull(ThemeManager.GetBackgroundImage("settings"));
        Assert.IsFalse(Named<Slider>(window, "SettingsSurfaceOpacitySlider").IsEnabled);
        Assert.AreEqual(1d, ((SolidColorBrush)window.Resources["B.SettingsSurfaceBg"]).Opacity,
            "Missing personal artwork must use readable opaque Settings surfaces.");
        Assert.AreEqual(panelImage, ThemeManager.CreatePanelAppearanceSettings(cfg).BackgroundImagePath);
    }

    private static void SelectTarget(SettingsWindow window, string tag)
    {
        var combo = Named<ComboBox>(window, "BackgroundTargetCombo");
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().Single(item => (string)item.Tag == tag);
    }

    private static void WriteImage(string path, Color color)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
            Enumerable.Range(0, 4).SelectMany(_ => new[] { color.B, color.G, color.R, color.A }).ToArray(), 8)));
        using var stream = File.Create(path); encoder.Save(stream);
    }

    private static Border BackgroundLayer(Window window) => (Border)((Grid)((Grid)window.Content).Children[0]).Children[0];
    private static Border Scrim(Window window) => (Border)((Grid)((Grid)window.Content).Children[0]).Children[1];
    private static T Named<T>(SettingsWindow window, string name) where T : FrameworkElement => (T)window.FindName(name);
    private static void ReloadControls(SettingsWindow window) => typeof(SettingsWindow)
        .GetMethod("ReloadAppearanceControls", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
}
