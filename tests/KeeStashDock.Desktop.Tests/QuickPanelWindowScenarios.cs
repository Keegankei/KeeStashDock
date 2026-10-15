using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using KeeStashDock.Core.Services;
using KeeStashDock.Desktop.Views;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Infrastructure.Database;
using KeeStashDock.Infrastructure.Logging;
using KeeStashDock.Infrastructure.Repositories;
using KeeStashDock.Infrastructure.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

internal static class QuickPanelWindowScenarios
{
    internal static void Verify(string isolatedRoot)
    {
        // Draft recovery uses the executable's portable directory. Claim an empty,
        // test-only directory before constructing the panel; never inspect old data.
        string draftRoot = Path.Combine(AppContext.BaseDirectory, "Data");
        Assert.IsFalse(Directory.Exists(draftRoot), "The UI fixture requires an unused test output directory.");
        Directory.CreateDirectory(draftRoot);
        QuickPanelWindow? panel = null;
        string previousLanguage = LocalizationService.Instance.CurrentLanguage;
        try
        {
            LocalizationService.Instance.SetLanguage("en-US");
            Assert.AreEqual(draftRoot, PortablePaths.DataRoot);
            using var log = new FileLogger(Path.Combine(isolatedRoot, "panel-logs"));
            var db = new Db(Path.Combine(isolatedRoot, "panel.db"), new DatabaseMaintenanceCoordinator());
            new MigrationRunner(db, log).Run();
            var repository = new SnippetRepository(db);
            var snippets = new SnippetService(repository, log);
            snippets.Add("Ready when you need it\nSave useful text without breaking your flow.", Domain.Entities.SnippetSource.Manual);
            var settings = new SettingsStore(Path.Combine(isolatedRoot, "panel-settings.json"));
            settings.Current.PanelUserWidth = 400;
            settings.Current.PanelUserHeight = 460;
            settings.Current.CollapseDelaySeconds = 60;
            var openedImages = new List<long>();
            Point pointer = new(-1000, -1000);
            panel = new QuickPanelWindow(snippets, new SearchService(repository), settings,
                new CategoryRepository(db), () => { }, () => { }, onImageRequested: openedImages.Add,
                perspectivePointer: () => pointer)
            { Title = "KeeStashDock isolated UI smoke" };

            panel.ShowPanel();
            PumpUntil(() => panel.IsVisible && panel.Opacity >= 0.99);
            nint handle = new WindowInteropHelper(panel).Handle;
            Assert.AreNotEqual(nint.Zero, handle, "Summoning must create a native panel window.");
            Assert.IsTrue(IsWindowVisible(handle), "The panel must be visible to Windows even without a taskbar button.");
            Assert.IsFalse(panel.ShowInTaskbar);
            Assert.IsFalse(panel.ShowActivated);
            VerifyDepthModes(panel, settings, handle, openedImages);
            VerifyPerspectiveMotion(panel, settings, handle, value => pointer = value);
            CaptureDocumentationPreviews(panel, settings, snippets, value => pointer = value);
            panel.ShowPanel();
            PumpUntil(() => panel.IsVisible && panel.Opacity >= 0.99);
            Assert.AreEqual(handle, new WindowInteropHelper(panel).Handle);
            Assert.IsTrue(IsWindowVisible(handle), "Repeated launch requests must keep an already visible panel visible.");
            panel.HidePanel();
            panel.ShowPanel();
            PumpUntil(() => panel.IsVisible && panel.Opacity >= 0.99);
            Assert.IsTrue(IsWindowVisible(handle), "A summon during fade-out must cancel the pending hide.");

            panel.HidePanel();
            PumpUntil(() => !panel.IsVisible);
            Assert.IsFalse(IsWindowVisible(handle));
            Assert.IsTrue(panel.OwnedWindows.Cast<Window>().All(window => !window.IsVisible),
                "Hiding the panel must also hide its separate decoration.");
            panel.ShowPanel();
            PumpUntil(() => panel.IsVisible && panel.Opacity >= 0.99);
            Assert.AreEqual(handle, new WindowInteropHelper(panel).Handle);
            Assert.IsTrue(IsWindowVisible(handle), "A second summon must reuse and reveal the panel.");
            panel.HidePanel();
            panel.Close();
            panel = null;
            DateTime afterFade = DateTime.UtcNow.AddMilliseconds(250);
            PumpUntil(() => DateTime.UtcNow >= afterFade);
            Assert.IsFalse(IsWindowVisible(handle), "Closing during fade-out must leave no late callback acting on the destroyed window.");
            Assert.IsFalse(Application.Current.Windows.OfType<PanelShadowWindow>().Any(),
                "Closing the panel must release its shadow window and event subscriptions.");
            settings.Current.FloatingPanelEnabled = !settings.Current.FloatingPanelEnabled;
            settings.Save(); // A closed panel must no longer observe live settings.
        }
        finally
        {
            panel?.Close();
            LocalizationService.Instance.SetLanguage(previousLanguage);
            // This fixture never edits quick input. Non-recursive deletion fails
            // safely if anything unexpected appeared in its empty draft directory.
            Directory.Delete(draftRoot, recursive: false);
        }
    }

    private static void VerifyDepthModes(QuickPanelWindow panel, SettingsStore settings, nint handle, List<long> openedImages)
    {
        var shadow = panel.OwnedWindows.OfType<PanelShadowWindow>().Single();
        Assert.AreEqual(!SystemParameters.HighContrast, shadow.IsVisible);
        Assert.IsFalse(shadow.ShowActivated);
        Assert.IsFalse(shadow.ShowInTaskbar);
        if (shadow.IsVisible)
        {
            nint shadowHandle = new WindowInteropHelper(shadow).Handle;
            long style = GetWindowLongPtr(shadowHandle, -20).ToInt64();
            Assert.AreEqual(0x08000020L, style & 0x08000020L,
                "Visible decoration must be non-activating and click-through across processes.");
            Assert.AreEqual(new nint(-1), SendMessage(shadowHandle, 0x0084, nint.Zero, nint.Zero));
        }
        Rect initial = new(panel.Left, panel.Top, panel.ActualWidth, panel.ActualHeight);
        var cardList = (ListBox)panel.FindName("CardList");
        panel.UpdateLayout();
        var card = Descendants(cardList).OfType<Border>()
            .Single(border => border.Tag is ViewModels.SnippetItemViewModel);
        Assert.IsNull(card.Effect, "Card text must remain outside the shadow renderer.");
        PanelDepthTests.VerifyFiniteMotionReleasesItsClockAndCancellationWins(card);
        for (int i = 0; i < 6; i++)
        {
            settings.Current.FloatingPanelEnabled = i % 2 == 0;
            settings.Save();
            panel.UpdateLayout();
            Assert.AreEqual(settings.Current.FloatingPanelEnabled && FloatingCardMotion.AnimationsAllowed,
                panel.FloatingDepthEnabled);
            Assert.AreEqual(panel.FloatingDepthEnabled, ((PerspectiveSurface)panel.FindName("SpatialSurface")).IsPerspectiveEnabled);
            Assert.AreEqual(panel.FindResource("B.CardBg"), card.Background);
            Assert.AreEqual(new CornerRadius(10), card.CornerRadius,
                "Floating changes the spatial presentation, never the original card styling.");
            Assert.AreEqual(initial, new Rect(panel.Left, panel.Top, panel.ActualWidth, panel.ActualHeight),
                "Switching appearance cannot add shadow padding to the visible panel geometry.");
            Assert.AreSame(shadow, panel.OwnedWindows.OfType<PanelShadowWindow>().Single());
            if (panel.FloatingDepthEnabled)
            {
                // Release on the fixed hit area, outside the displaced front
                // surface, still opens the card. Use an image callback so this
                // regression never copies to the real system clipboard.
                var hitArea = Descendants(cardList).OfType<Grid>().Single(grid => grid.Tag is ViewModels.SnippetItemViewModel);
                var item = (ViewModels.SnippetItemViewModel)hitArea.Tag;
                var originalType = item.Model.Type;
                try
                {
                    item.Model.Type = Domain.Entities.SnippetType.Image;
                    int before = openedImages.Count;
                    hitArea.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                        { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
                    Assert.AreEqual(before + 1, openedImages.Count);
                    Assert.AreEqual(item.Id, openedImages[^1]);
                    Descendants(card).OfType<Button>().First().RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,
                        Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
                    Assert.AreEqual(before + 1, openedImages.Count, "Pin/delete releases must not open their card.");
                }
                finally { item.Model.Type = originalType; }
            }
        }
        ((Button)panel.FindName("ShrinkExpandBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        panel.UpdateLayout();
        Assert.AreEqual(44, panel.ActualHeight, 0.01, "The collapsed bar must remain exactly 44 DIPs.");
        settings.Current.FloatingPanelEnabled = true;
        settings.Save();
        panel.UpdateLayout();
        Assert.AreEqual(44, panel.ActualHeight, 0.01);
        Assert.IsFalse(((PerspectiveSurface)panel.FindName("SpatialSurface")).IsPerspectiveEnabled);
        Assert.IsFalse(panel.IsDepthPointerPolling);
        SendMessage(handle, 0x0232, nint.Zero, nint.Zero); // Complete a native move/resize.
        Assert.AreEqual(initial.Width, settings.Current.PanelUserWidth);
        Assert.AreEqual(initial.Height, settings.Current.PanelUserHeight,
            "A collapsed resize must not overwrite the expanded height with decoration dimensions.");
        ((Button)panel.FindName("ShrinkExpandBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        panel.UpdateLayout();
        Assert.AreEqual(initial.Size, panel.RenderSize);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (DependencyObject descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void VerifyPerspectiveMotion(QuickPanelWindow panel, SettingsStore settings, nint handle, Action<Point> movePointer)
    {
        if (!panel.FloatingDepthEnabled) return;
        var surface = (PerspectiveSurface)panel.FindName("SpatialSurface");
        Rect nativeEnvelope = new(panel.Left, panel.Top, panel.ActualWidth, panel.ActualHeight);
        Point first = new(panel.ActualWidth + 20, 65);
        movePointer(first);
        panel.UpdatePerspectivePointer();
        PerspectivePose expected = PerspectivePose.FromPointer(first, panel.RenderSize);
        PumpUntil(() => !surface.HasAnimatedProperties && Math.Abs(surface.Yaw - expected.Yaw) < 0.01);
        Assert.IsTrue(panel.IsDepthPointerPolling, "A nearby desktop pointer must drive the panel even outside its HWND.");
        Assert.AreEqual(nativeEnvelope, new Rect(panel.Left, panel.Top, panel.ActualWidth, panel.ActualHeight));

        SendMessage(handle, 0x0231, nint.Zero, nint.Zero); // Native move/resize starts.
        double frozenYaw = surface.Yaw;
        Point second = new(-20, panel.ActualHeight - 60);
        movePointer(second);
        panel.UpdatePerspectivePointer();
        Assert.AreEqual(frozenYaw, surface.Yaw, "Dragging must retain the current pose without snapping the hit target away.");
        Assert.IsFalse(panel.IsDepthPointerPolling);
        SendMessage(handle, 0x0232, nint.Zero, nint.Zero);
        expected = PerspectivePose.FromPointer(second, panel.RenderSize);
        PumpUntil(() => !surface.HasAnimatedProperties && Math.Abs(surface.Yaw - expected.Yaw) < 0.01);

        var combo = (ComboBox)panel.FindName("ExpireCombo");
        try
        {
            combo.IsDropDownOpen = true;
            frozenYaw = surface.Yaw;
            movePointer(first);
            panel.UpdatePerspectivePointer();
            Assert.AreEqual(frozenYaw, surface.Yaw, "The separate popup must not chase a moving placement target.");
            Assert.IsFalse(panel.IsDepthPointerPolling);
        }
        finally { combo.IsDropDownOpen = false; }
        movePointer(new Point(-1000, -1000));
        panel.UpdatePerspectivePointer();
        PumpUntil(() => !surface.HasAnimatedProperties && Math.Abs(surface.Yaw) < 0.01);
        Assert.IsFalse(panel.IsDepthPointerPolling, "Idle/far-away panels must release the fast cursor sampler.");

        settings.Current.FloatingPanelFollowPointer = false;
        settings.Save();
        PumpUntil(() => !surface.HasAnimatedProperties && Math.Abs(surface.Yaw - PerspectivePose.Fixed.Yaw) < 0.01);
        Point[] corners = [new(0, 0), new(panel.ActualWidth, 0), new(0, panel.ActualHeight),
            new(panel.ActualWidth, panel.ActualHeight)];
        Point[] fixedProjection = corners.Select(surface.ProjectPoint).ToArray();
        foreach (Point cursor in new[] { first, second, new Point(200, 180), new Point(-1000, -1000) })
        {
            movePointer(cursor);
            panel.UpdatePerspectivePointer();
            Assert.AreEqual(PerspectivePose.Fixed.Yaw, surface.Yaw, 0.01);
            Assert.AreEqual(PerspectivePose.Fixed.Pitch, surface.Pitch, 0.01);
            CollectionAssert.AreEqual(fixedProjection, corners.Select(surface.ProjectPoint).ToArray(),
                "Fixed perspective must retain exactly the same projected panel while the pointer moves.");
            Assert.AreEqual(nativeEnvelope, new Rect(panel.Left, panel.Top, panel.ActualWidth, panel.ActualHeight));
        }
        Assert.IsFalse(panel.IsDepthPointerPolling, "Fixed mode must also stop card sampling when the pointer is far away.");
        VerifyCardProximity(panel, handle, movePointer);
        settings.Current.FloatingPanelFollowPointer = true;
        settings.Save();
        PumpUntil(() => !surface.HasAnimatedProperties && Math.Abs(surface.Yaw) < 0.01);
    }

    private static void VerifyCardProximity(QuickPanelWindow panel, nint handle, Action<Point> movePointer)
    {
        var list = (ListBox)panel.FindName("CardList");
        var host = Descendants(list).OfType<FrameworkElement>().First(FloatingCardMotion.GetEnabled);
        CardDepthDecoration decoration = FloatingCardMotion.GetDecoration(host)!;
        Assert.IsNotNull(decoration);
        GeneralTransform projection = host.TransformToAncestor(panel);
        // WPF maps points on the live 3D face; extrapolate the nearby desktop
        // point after projection instead of asking it to hit outside the mesh.
        movePointer(projection.Transform(new Point(host.ActualWidth - 1, host.ActualHeight * 0.4)) + new Vector(13, 0));
        panel.UpdatePerspectivePointer();
        PumpUntil(() => decoration.Activity > 0.1 && !decoration.HasAnimatedProperties);
        Assert.IsTrue(decoration.Activity < 1, "A nearby card responds before the pointer enters its stationary hit area.");

        movePointer(projection.Transform(new Point(host.ActualWidth * 0.35, host.ActualHeight * 0.4)));
        panel.UpdatePerspectivePointer();
        PumpUntil(() => decoration.Activity >= 0.99 && !decoration.HasAnimatedProperties);
        var surface = (PerspectiveSurface)panel.FindName("SpatialSurface");
        Assert.AreEqual(PerspectivePose.Fixed.Yaw, surface.Yaw, 0.01,
            "Card feedback must work independently while the whole panel stays fixed.");

        FrameworkElement target = FloatingCardMotion.GetTarget(host)!;
        var transforms = (TransformGroup)target.RenderTransform;
        var lift = transforms.Children.OfType<TranslateTransform>().Single();
        Assert.IsTrue(lift.Y < -1, "The hovered card face must separate visibly from its resting layers.");
        var pin = Descendants(target).OfType<Button>().First();
        movePointer(pin.TransformToAncestor(panel).Transform(new Point(pin.ActualWidth / 2, pin.ActualHeight / 2)));
        Point beforeButton = new(lift.X, lift.Y);
        panel.UpdatePerspectivePointer();
        Assert.AreEqual(beforeButton, new Point(lift.X, lift.Y),
            "Entering pin/delete must freeze the existing face, not move the button away from the pointer.");

        SendMessage(handle, 0x0231, nint.Zero, nint.Zero);
        double frozenActivity = decoration.Activity;
        movePointer(new Point(-1000, -1000));
        panel.UpdatePerspectivePointer();
        Assert.AreEqual(frozenActivity, decoration.Activity, 0.001,
            "Native dragging must freeze card depth as well as the panel's pose.");
        SendMessage(handle, 0x0232, nint.Zero, nint.Zero);
        PumpUntil(() => decoration.Activity < 0.01 && !decoration.HasAnimatedProperties);
        Assert.IsFalse(panel.IsDepthPointerPolling);
        CardDepthTests.VerifyProjectedScrollClippingAndFreeze();
    }

    private static void CaptureDocumentationPreviews(QuickPanelWindow panel, SettingsStore settings, SnippetService snippets, Action<Point> movePointer)
    {
        string? destination = Environment.GetEnvironmentVariable("KEESTASHDOCK_DOC_IMAGES");
        if (string.IsNullOrWhiteSpace(destination)) return;
        string directory = Path.GetFullPath(destination);
        Directory.CreateDirectory(directory);
        // These fixtures use this test's disposable database. No system clipboard,
        // desktop pixels, user settings, or captured application content is read.
        snippets.Add("Capture, read, translate\nUse local OCR and your chosen translation provider.", Domain.Entities.SnippetSource.Manual);
        snippets.Add("Keep ideas close\nUseful notes, screenshots and links, ready when you need them.", Domain.Entities.SnippetSource.Manual);
        double originalHeight = settings.Current.PanelUserHeight ?? 460;
        bool originalMode = settings.Current.FloatingPanelEnabled;
        bool originalFollow = settings.Current.FloatingPanelFollowPointer;
        bool originalDark = ThemeManager.IsDark;
        try
        {
            settings.Current.PanelUserHeight = 600;
            foreach (var (floating, dark, follow, name) in new[]
            {
                (false, false, true, "classic-panel.png"),
                (true, false, true, "floating-panel.png"),
                (true, true, true, "floating-panel-dark.png"),
                (true, false, false, "floating-panel-fixed.png"),
            })
            {
                ThemeManager.Apply(dark ? "Dark" : "Light");
                settings.Current.FloatingPanelEnabled = floating;
                settings.Current.FloatingPanelFollowPointer = follow;
                settings.Save();
                panel.ShowPanel();
                PumpUntil(() => panel.IsVisible && panel.Opacity >= 0.99);
                panel.UpdateLayout();
                movePointer(floating ? new Point(panel.ActualWidth + 10, 50) : new Point(-1000, -1000));
                panel.UpdatePerspectivePointer();
                if (floating && panel.FloatingDepthEnabled)
                {
                    FrameworkElement? card = Descendants((ListBox)panel.FindName("CardList"))
                        .OfType<FrameworkElement>().FirstOrDefault(FloatingCardMotion.GetEnabled);
                    if (card is not null)
                    {
                        movePointer(card.TransformToAncestor(panel).Transform(new Point(card.ActualWidth * 0.7, card.ActualHeight * 0.5)));
                        panel.UpdatePerspectivePointer();
                    }
                }
                DateTime finish = DateTime.UtcNow.AddMilliseconds(250);
                PumpUntil(() => DateTime.UtcNow >= finish);
                var shadow = panel.OwnedWindows.OfType<PanelShadowWindow>().Single();
                const int padding = 32;
                int width = (int)Math.Ceiling(panel.ActualWidth) + 2 * padding;
                int height = (int)Math.Ceiling(panel.ActualHeight) + 2 * padding;
                var frame = new DrawingVisual();
                using (DrawingContext drawing = frame.RenderOpen())
                {
                    // A neutral fixture backdrop makes the real external shadow
                    // visible; it is not a capture of the maintainer's desktop.
                    var backdrop = new SolidColorBrush(ThemeManager.IsDark
                        ? Color.FromRgb(35, 40, 50) : Color.FromRgb(230, 235, 243));
                    drawing.DrawRectangle(backdrop, null, new Rect(0, 0, width, height));
                    if (shadow.IsVisible)
                        drawing.DrawImage(PerspectivePreviewRecorder.Snapshot(shadow), new Rect(0, 0, width, height));
                    drawing.DrawImage(PerspectivePreviewRecorder.Snapshot(panel),
                        new Rect(padding, padding, panel.ActualWidth, panel.ActualHeight));
                }
                var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                image.Render(frame);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using (var output = File.Create(Path.Combine(directory, name))) encoder.Save(output);
                if (floating && !dark && follow) PerspectivePreviewRecorder.Record(panel, movePointer, directory);
            }
        }
        finally
        {
            movePointer(new Point(-1000, -1000));
            ThemeManager.Apply(originalDark ? "Dark" : "Light");
            settings.Current.PanelUserHeight = originalHeight;
            settings.Current.FloatingPanelEnabled = originalMode;
            settings.Current.FloatingPanelFollowPointer = originalFollow;
            settings.Save();
            panel.ShowPanel();
            PumpUntil(() => panel.IsVisible && panel.Opacity >= 0.99);
        }
    }

    private static void PumpUntil(Func<bool> complete)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        timer.Tick += (_, _) => { if (complete() || DateTime.UtcNow >= deadline) frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Assert.IsTrue(complete(), "The panel did not finish its visibility transition.");
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
}
