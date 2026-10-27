using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using KeeStashDock.Desktop.Views;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

internal static class BubbleBackdropScenarios
{
    internal static void Verify(SettingsWindow settingsWindow, SettingsStore settings)
    {
        var cfg = new AppSettings { BubbleOpacity = .55, BubbleFrostedGlassEnabled = true };
        var surface = new Border { Child = new TextBlock { Text = "Readable sample" } };
        var window = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true,
            ShowActivated = false, Content = surface };
        try
        {
            BubblePalette palette = BubbleChrome.Apply(surface, cfg);
            Assert.AreEqual(1d, palette.Opacity, "No native compositor must mean an opaque fallback, not unreadable translucency.");
            Assert.AreEqual((nint)0, new WindowInteropHelper(window).Handle,
                "Styling an offscreen bubble must not create a native window or activate the desktop.");
            Assert.IsTrue(((GradientBrush)surface.Background).GradientStops.All(stop => stop.Color.A == 255));
            Assert.AreEqual(1d, surface.Opacity);
            Assert.IsNull(((TextBlock)surface.Child).Effect, "Frost must never blur text.");
            Assert.IsFalse(window.ShowActivated);

            cfg.BubbleFrostedGlassEnabled = false;
            palette = BubbleChrome.Apply(surface, cfg);
            Assert.AreEqual(.55, palette.Opacity, .0001);
            Assert.IsTrue(((GradientBrush)surface.Background).GradientStops.Any(stop => stop.Color.A < 255));
            Assert.AreEqual(.55, BubbleBackdrop.ReadablePalette(palette, true, true).Opacity, .0001);
            Assert.AreEqual(1d, BubbleBackdrop.ReadablePalette(palette, true, false).Opacity);
            Assert.AreEqual(.55, cfg.BubbleOpacity, "Native fallback must not overwrite the selected opacity.");
            // Exercise HWND hook cleanup without showing or activating a window.
            Assert.AreNotEqual((nint)0, new WindowInteropHelper(window).EnsureHandle());
            cfg.BubbleFrostedGlassEnabled = true;
            Assert.AreEqual(1d, BubbleChrome.Apply(surface, cfg).Opacity);
            Assert.IsFalse(window.IsVisible);
        }
        finally { window.Close(); }

        ThemeAppearanceSnapshot before = ThemeAppearanceSnapshot.Capture(settings.Current);
        var check = (CheckBox)settingsWindow.FindName("BubbleFrostCheck");
        try
        {
            check.IsChecked = true;
            Assert.IsTrue(settings.Current.BubbleFrostedGlassEnabled);
            settings.Current.SettingsFrostedGlassEnabled = true;
            settings.Current.SettingsFrostedGlassRadius = 11;
            ThemeAppearanceSnapshot saved = ThemeAppearanceSnapshot.Capture(settings.Current);
            settings.Current.SettingsFrostedGlassEnabled = settings.Current.BubbleFrostedGlassEnabled = false;
            settings.Current.SettingsFrostedGlassRadius = 2;
            saved.Restore(settings.Current);
            Assert.IsTrue(settings.Current.SettingsFrostedGlassEnabled);
            Assert.IsTrue(settings.Current.BubbleFrostedGlassEnabled);
            Assert.AreEqual(11d, settings.Current.SettingsFrostedGlassRadius);
        }
        finally
        {
            check.IsChecked = before.BubbleFrostedGlassEnabled;
            before.Restore(settings.Current);
            settings.Save();
        }
    }
}
