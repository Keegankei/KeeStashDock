using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Desktop.ViewModels;
using KeeStashDock.Desktop.Views;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

/// <summary>Real HWND/layout checks inside the existing isolated WPF Application.</summary>
internal static class StickyNoteWindowScenarios
{
    internal static void Verify(string isolatedRoot)
    {
        string language = LocalizationService.Instance.CurrentLanguage;
        LocalizationService.Instance.SetLanguage("en-US");
        try
        {
            foreach (bool floating in new[] { false, true })
            {
                VerifyShownGeometry(isolatedRoot, floating, "A detached note stays readable.\nMove it without shrinking the content.");
                VerifyShownGeometry(isolatedRoot, floating, string.Join('\n', Enumerable.Repeat("Long notes scroll within the card.", 45)));
            }
            VerifyCancelledFirstShow();
        }
        finally { LocalizationService.Instance.SetLanguage(language); }
    }

    private static void VerifyShownGeometry(string root, bool floating, string content)
    {
        var settings = new SettingsStore(Path.Combine(root, "sticky-fixture-settings.json"));
        settings.Current.FloatingPanelEnabled = floating;
        var note = new StickyNoteWindow(Item(content), 0.85, floating, settings)
        {
            Left = SystemParameters.WorkArea.Left + 80,
            Top = SystemParameters.WorkArea.Top + 80,
        };
        PanelShadowWindow? shadow = null;
        try
        {
            // ShowTornOutAt uses this same first Show/SizeToContent path. Direct
            // Show avoids taking over the user's actual mouse in an automated test.
            note.Show();
            AssertReadableGeometry(note);
            PumpUntil(() => note.IsLoaded && note.OwnedWindows.OfType<PanelShadowWindow>().Any() &&
                (SystemParameters.HighContrast || note.OwnedWindows.OfType<PanelShadowWindow>().Single().IsVisible));
            shadow = note.OwnedWindows.OfType<PanelShadowWindow>().Single();
            double height = note.ActualHeight;
            AssertReadableGeometry(note);
            AssertShadowTracksOwner(note, shadow);

            // Native movement follows the same geometry events used by DragMove,
            // without synthesizing global mouse input or writing the clipboard.
            for (int move = 0; move < 3; move++)
            {
                note.Left += 24; note.Top += 12;
                settings.Current.FloatingPanelEnabled = !settings.Current.FloatingPanelEnabled;
                settings.Save();
                note.UpdateLayout();
                Drain();
                AssertReadableGeometry(note);
                Assert.AreEqual(height, note.ActualHeight, 0.5, "Movement/mode changes cannot resize the note.");
                AssertShadowTracksOwner(note, shadow);
            }
            note.Hide();
            Assert.IsFalse(shadow.IsVisible);
            note.Show();
            PumpUntil(() => SystemParameters.HighContrast || shadow.IsVisible);
            Assert.AreSame(shadow, note.OwnedWindows.OfType<PanelShadowWindow>().Single());
            AssertReadableGeometry(note);
            AssertShadowTracksOwner(note, shadow);
        }
        finally { note.Close(); Drain(); }
        Assert.IsFalse(Application.Current.Windows.Cast<Window>().Contains(note));
        if (shadow is not null) Assert.IsFalse(Application.Current.Windows.Cast<Window>().Contains(shadow));
        settings.Current.FloatingPanelEnabled = !floating;
        settings.Save(); // Closed notes must have detached their settings handler.
    }

    private static void VerifyCancelledFirstShow()
    {
        var note = new StickyNoteWindow(Item("Queued decoration must not survive its note."));
        note.Show();
        note.Hide(); // Cancel the queued decoration before its first dispatcher turn.
        Drain();
        Assert.IsTrue(note.OwnedWindows.Cast<Window>().All(window => !window.IsVisible));
        note.Show();
        note.Close();
        Drain();
        Assert.IsFalse(Application.Current.Windows.OfType<PanelShadowWindow>().Any(window => window.Owner == note),
            "Closing before a queued shadow show must not create an orphan HWND.");
    }

    private static SnippetItemViewModel Item(string content) => new(new Snippet
    {
        Title = "Detached note fixture", Content = content,
        CreatedAt = DateTime.UtcNow, Type = SnippetType.PlainText,
    });

    private static void AssertReadableGeometry(StickyNoteWindow note)
    {
        Assert.AreEqual(280, note.Width, 0.5, "Showing a shadow must never overwrite the note's explicit width.");
        Assert.AreEqual(280, note.ActualWidth, 0.5);
        Assert.IsTrue(note.ActualHeight is >= 70 and <= 340);
        var card = (Border)note.Content;
        Assert.AreEqual(280, card.ActualWidth, 0.5, "The visible card must fill its normal-width HWND.");
        Assert.IsTrue(card.Child.RenderSize.Width >= 250, "Text must retain a readable content width.");
        Assert.IsNull(card.Effect, "The shadow must not rasterize the text layer.");
        Rect native = NativeBounds(note);
        DpiScale dpi = VisualTreeHelper.GetDpi(note);
        Assert.AreEqual(280 * dpi.DpiScaleX, native.Width, 2);
        Assert.AreEqual(note.ActualHeight * dpi.DpiScaleY, native.Height, 2);
    }

    private static void AssertShadowTracksOwner(Window note, PanelShadowWindow shadow)
    {
        if (SystemParameters.HighContrast) { Assert.IsFalse(shadow.IsVisible); return; }
        Rect owner = NativeBounds(note), decoration = NativeBounds(shadow);
        DpiScale dpi = VisualTreeHelper.GetDpi(note);
        double padX = Math.Ceiling(32 * dpi.DpiScaleX), padY = Math.Ceiling(32 * dpi.DpiScaleY);
        Assert.AreEqual(owner.Left - padX, decoration.Left, 1);
        Assert.AreEqual(owner.Top - padY, decoration.Top, 1);
        Assert.AreEqual(owner.Width + 2 * padX, decoration.Width, 1);
        Assert.AreEqual(owner.Height + 2 * padY, decoration.Height, 1);
    }

    private static Rect NativeBounds(Window window)
    {
        Assert.IsTrue(GetWindowRect(new WindowInteropHelper(window).Handle, out NativeRect bounds));
        return new(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
    }
    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void PumpUntil(Func<bool> condition)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        DateTime limit = DateTime.UtcNow.AddSeconds(3);
        timer.Tick += (_, _) => { if (condition() || DateTime.UtcNow >= limit) frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Assert.IsTrue(condition(), "The sticky note layout did not settle.");
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hwnd, out NativeRect rectangle);
}
