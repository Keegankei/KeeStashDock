using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using KeeStashDock.Core.Services;
using KeeStashDock.Desktop.ViewModels;
using KeeStashDock.Desktop.Views;
using KeeStashDock.Infrastructure.Database;
using KeeStashDock.Infrastructure.Logging;
using KeeStashDock.Infrastructure.Repositories;
using KeeStashDock.Infrastructure.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace KeeStashDock.Desktop.Tests;

internal static class EdgeDockScenarios
{
    internal static void VerifySettings(SettingsWindow window, SettingsStore settings)
    {
        var mode = (ComboBox)window.FindName("PanelModeCombo");
        var motion = (ComboBox)window.FindName("FloatingMotionCombo");
        var gesture = (ComboBox)window.FindName("InspectionGestureCombo");
        var button = (ComboBox)window.FindName("InspectionButtonCombo");
        var modifiers = (ComboBox)window.FindName("InspectionModifiersCombo");
        var custom = (Grid)window.FindName("InspectionCustomRow");
        var edge = (CheckBox)window.FindName("PanelEdgeAutoHideCheck");
        mode.SelectedIndex = 1;
        motion.SelectedIndex = 0;
        Assert.IsTrue(((StackPanel)window.FindName("InspectionGesturePanel")).IsEnabled);
        gesture.SelectedIndex = 2;
        Assert.AreEqual(Visibility.Visible, custom.Visibility);
        Assert.AreEqual(8, modifiers.Items.Count);
        modifiers.SelectedIndex = 7;
        button.SelectedIndex = 4;
        Assert.AreEqual("Custom", settings.Current.FloatingInspectionGesture);
        Assert.AreEqual("Control+Alt+Shift", settings.Current.FloatingInspectionModifiers);
        Assert.AreEqual("XButton2", settings.Current.FloatingInspectionButton);
        gesture.SelectedIndex = 1;
        Assert.AreEqual(Visibility.Collapsed, custom.Visibility);
        Assert.AreEqual("Middle", settings.Current.FloatingInspectionGesture);
        Assert.AreEqual("XButton2", settings.Current.FloatingInspectionButton,
            "Choosing a preset must keep the saved custom combination.");
        motion.SelectedIndex = 1;
        Assert.IsFalse(((StackPanel)window.FindName("InspectionGesturePanel")).IsEnabled);
        edge.IsChecked = true;
        Assert.IsTrue(settings.Current.PanelEdgeAutoHideEnabled);
        edge.IsChecked = false;
        Assert.IsFalse(settings.Current.PanelEdgeAutoHideEnabled);
    }

    internal static void Verify(string isolatedRoot)
    {
        string draftRoot = Path.Combine(AppContext.BaseDirectory, "Data");
        Assert.IsFalse(Directory.Exists(draftRoot));
        Directory.CreateDirectory(draftRoot);
        QuickPanelWindow? panel = null;
        try
        {
            using var log = new FileLogger(Path.Combine(isolatedRoot, "edge-logs"));
            var db = new Db(Path.Combine(isolatedRoot, "edge.db"), new DatabaseMaintenanceCoordinator());
            new MigrationRunner(db, log).Run();
            var repository = new SnippetRepository(db);
            var snippets = new SnippetService(repository, log);
            snippets.Add("Synthetic edge dock card", Domain.Entities.SnippetSource.Manual);
            var settings = new SettingsStore(Path.Combine(isolatedRoot, "edge-settings.json"));
            settings.Current.PanelUserWidth = 400;
            settings.Current.PanelUserHeight = 460;
            settings.Current.CollapseDelaySeconds = 60;
            settings.Current.PanelEdgeAutoHideEnabled = true;
            settings.Current.FloatingPanelEnabled = true;
            Point pointer = new(-1000, -1000);
            panel = new QuickPanelWindow(snippets, new SearchService(repository), settings,
                new CategoryRepository(db), () => { }, () => { }, perspectivePointer: () => pointer);
            panel.ShowPanel();
            Pump();
            nint hwnd = new WindowInteropHelper(panel).Handle;
            Drawing.Rectangle work = Forms.Screen.FromHandle(hwnd).WorkingArea;
            var viewModel = (QuickPanelViewModel)panel.DataContext;
            foreach (PanelDockSide side in new[] { PanelDockSide.Left, PanelDockSide.Top, PanelDockSide.Right, PanelDockSide.Bottom })
            {
                Drawing.Rectangle normal = Bounds(hwnd);
                normal.X = work.Left + (work.Width - normal.Width) / 2;
                normal.Y = work.Top + (work.Height - normal.Height) / 2;
                normal = EdgeDockGeometry.RestoreBounds(normal, work, side);
                Place(hwnd, normal);
                SendMessage(hwnd, 0x0232, nint.Zero, nint.Zero);
                if (side == PanelDockSide.Left && panel.FloatingDepthEnabled)
                    VerifyOutsideCardProximity(panel, value => pointer = value);
                int? savedX = settings.Current.PanelDevicePosX, savedY = settings.Current.PanelDevicePosY;
                panel.RetractEdgeDock();
                Assert.IsTrue(panel.IsEdgeDockHidden, $"{side} edge must retract.");
                Assert.IsTrue(work.Contains(Bounds(hwnd)),
                    $"{side} hidden handle bounds {Bounds(hwnd)} must fit work area {work}; WPF size {panel.Width} x {panel.Height}, actual {panel.ActualWidth} x {panel.ActualHeight}.");
                Assert.IsTrue(panel.OwnedWindows.Cast<Window>().All(window => !window.IsVisible));
                Assert.AreEqual(savedX, settings.Current.PanelDevicePosX);
                Assert.AreEqual(savedY, settings.Current.PanelDevicePosY);
                Assert.AreEqual(400d, settings.Current.PanelUserWidth);
                Assert.AreEqual(460d, settings.Current.PanelUserHeight);
                var handle = (Border)panel.FindName("EdgeDockHandle");
                handle.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
                    { RoutedEvent = UIElement.MouseEnterEvent });
                Assert.IsFalse(panel.IsEdgeDockHidden, "Hovering the edge handle must restore the normal panel.");
                Assert.AreEqual(normal.Size, Bounds(hwnd).Size);
                Assert.IsTrue(work.Contains(Bounds(hwnd)), $"{side} restored bounds {Bounds(hwnd)} must fit {work}.");

                viewModel.IsLocked = true;
                panel.RetractEdgeDock();
                Assert.IsFalse(panel.IsEdgeDockHidden, "Lock must pin the currently visible state.");
                viewModel.IsLocked = false;
                panel.RetractEdgeDock();
                Assert.IsTrue(panel.IsEdgeDockHidden);
                panel.ShowPanel(); // The app's hotkey routes a hidden handle here.
                Assert.IsFalse(panel.IsEdgeDockHidden);
                Assert.AreEqual(normal.Size, Bounds(hwnd).Size);
            }

            panel.RetractEdgeDock();
            settings.Current.PanelEdgeAutoHideEnabled = false;
            settings.Save();
            Assert.IsFalse(panel.IsEdgeDockHidden, "Disabling edge docking must restore a hidden panel immediately.");
            settings.Current.PanelEdgeAutoHideEnabled = true;
            settings.Save();
            Drawing.Rectangle center = Bounds(hwnd);
            center.X = work.Left + (work.Width - center.Width) / 2;
            center.Y = work.Top + (work.Height - center.Height) / 2;
            Place(hwnd, center);
            panel.UpdateEdgeDockAfterMove();
            panel.RetractEdgeDock();
            Assert.IsFalse(panel.IsEdgeDockHidden, "Dragging away must cancel edge docking.");
            settings.Current.PanelEdgeAutoHideEnabled = false;
            settings.Save();

            var shrink = (Button)panel.FindName("ShrinkExpandBtn");
            shrink.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            panel.UpdateLayout();
            Assert.AreEqual(Visibility.Collapsed, ((TextBlock)panel.FindName("TitleText")).Visibility);
            Assert.AreEqual(6, ((StackPanel)panel.FindName("TopBarButtons")).Children.Count);
            Assert.IsTrue(panel.ActualWidth < 230, "The compact bar contains only its six buttons.");
            Assert.AreEqual(44, panel.ActualHeight, 0.01);
            Assert.AreEqual(panel.MinWidth, panel.MaxWidth);
            SendMessage(hwnd, 0x0232, nint.Zero, nint.Zero);
            Assert.AreEqual(400d, settings.Current.PanelUserWidth);
            shrink.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            panel.UpdateLayout();
            Assert.AreEqual(400, panel.ActualWidth, 0.01);
            Assert.AreEqual(460, panel.ActualHeight, 0.01);
            Assert.AreEqual(Visibility.Visible, ((TextBlock)panel.FindName("TitleText")).Visibility);
        }
        finally
        {
            panel?.Close();
            Directory.Delete(draftRoot, recursive: false);
        }
    }

    private static void VerifyOutsideCardProximity(QuickPanelWindow panel, Action<Point> movePointer)
    {
        var list = (ListBox)panel.FindName("CardList");
        FrameworkElement host = Descendants(list).OfType<FrameworkElement>().First(FloatingCardMotion.GetEnabled);
        CardDepthDecoration decoration = FloatingCardMotion.GetDecoration(host)!;
        GeneralTransform projection = host.TransformToAncestor(panel);
        Point[] quad =
        [
            projection.Transform(new Point(0, 0)),
            projection.Transform(new Point(host.ActualWidth, 0)),
            projection.Transform(new Point(host.ActualWidth, host.ActualHeight)),
            projection.Transform(new Point(0, host.ActualHeight)),
        ];
        Point[] outside =
        [
            new(-1, (quad[0].Y + quad[3].Y) / 2),
            new(panel.ActualWidth + 1, (quad[1].Y + quad[2].Y) / 2),
            new((quad[0].X + quad[1].X) / 2, -1),
            new((quad[2].X + quad[3].X) / 2, panel.ActualHeight + 1),
        ];
        Point nearby = outside.MaxBy(point => FloatingCardMotion.ProximityActivity(point, quad));
        Assert.IsTrue(FloatingCardMotion.ProximityActivity(nearby, quad) > 0,
            "The fixture must place the card within proximity reach of the window boundary.");
        movePointer(new Point(-1000, -1000));
        panel.UpdatePerspectivePointer();
        Pump();
        Assert.IsFalse(panel.IsDepthPointerPolling);

        // No MouseMove is raised: only the normal 200-ms state timer can
        // discover this virtual pointer outside the docked panel's HWND.
        movePointer(nearby);
        Pump();
        Pump();
        Assert.IsTrue(decoration.Activity > 0 && decoration.Activity < 1,
            "A docked, visible panel must retain outside-card approach feedback.");
        movePointer(new Point(-1000, -1000));
        Pump();
        Pump();
        Assert.IsTrue(decoration.Activity < 0.01);
        Assert.IsFalse(panel.IsDepthPointerPolling);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (DependencyObject nested in Descendants(child)) yield return nested;
        }
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromMilliseconds(220) };
        timer.Tick += (_, _) => frame.Continue = false;
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
    }

    private static Drawing.Rectangle Bounds(nint hwnd)
    {
        Assert.IsTrue(GetWindowRect(hwnd, out var value));
        return Drawing.Rectangle.FromLTRB(value.Left, value.Top, value.Right, value.Bottom);
    }

    private static void Place(nint hwnd, Drawing.Rectangle value)
        => Assert.IsTrue(SetWindowPos(hwnd, nint.Zero, value.X, value.Y, value.Width, value.Height, 0x0004 | 0x0010));

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out NativeRect rectangle);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd, int message, nint wParam, nint lParam);
}
