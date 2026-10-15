using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KeeStashDock.Desktop.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

[TestClass]
public sealed class CardDepthTests
{
    [TestMethod]
    public void ProximityUsesTheProjectedPolygonRatherThanItsBoundingRectangle()
    {
        Point[] tiltedCard = [new(100, 0), new(200, 0), new(300, 100), new(0, 100)];
        Assert.AreEqual(1, FloatingCardMotion.ProximityActivity(new Point(150, 50), tiltedCard));
        Assert.AreEqual(0, FloatingCardMotion.ProximityActivity(new Point(30, 5), tiltedCard),
            "An empty corner of the projected bounding rectangle is not part of the card.");
        Assert.AreEqual(1, FloatingCardMotion.ProximityActivity(new Point(150, 0), tiltedCard));
        Assert.AreEqual(0.5, FloatingCardMotion.ProximityActivity(new Point(150, -20), tiltedCard), 0.0001);
        Assert.AreEqual(0, FloatingCardMotion.ProximityActivity(new Point(150, -40), tiltedCard));
    }

    [TestMethod]
    public void ProximityFallsOffSmoothlyInEveryDirectionAndRejectsInvalidInput()
    {
        Point[] card = [new(0, 0), new(100, 0), new(100, 80), new(0, 80)];
        double previous = 1;
        for (int distance = 0; distance <= 45; distance++)
        {
            double strength = FloatingCardMotion.ProximityActivity(new Point(50, -distance), card);
            Assert.IsTrue(strength is >= 0 and <= 1 && strength <= previous);
            Assert.AreEqual(strength, FloatingCardMotion.ProximityActivity(new Point(-distance, 40), card), 0.0001);
            Assert.AreEqual(strength, FloatingCardMotion.ProximityActivity(new Point(100 + distance, 40), card), 0.0001);
            Assert.AreEqual(strength, FloatingCardMotion.ProximityActivity(new Point(50, 80 + distance), card), 0.0001);
            previous = strength;
        }
        Assert.AreEqual(0, FloatingCardMotion.ProximityActivity(new Point(double.NaN, 0), card));
        Assert.AreEqual(0, FloatingCardMotion.ProximityActivity(new Point(0, double.PositiveInfinity), card));
        Assert.AreEqual(0, FloatingCardMotion.ProximityActivity(new Point(0, 0), []));
    }

    [STATestMethod]
    public void LayeredDecorationIsHollowAndNonInteractiveAtRestAndOnApproach()
    {
        var decoration = new CardDepthDecoration { OutlineBrush = Brushes.SlateGray, Margin = new Thickness(20) };
        var host = new Grid();
        host.Children.Add(decoration);
        foreach (double activity in new[] { 0.0, 1.0 })
        {
            decoration.Activity = activity;
            byte[] pixels = Render(host);
            for (int y = 30; y < 110; y++)
                for (int x = 30; x < 150; x++)
                    Assert.AreEqual((byte)0, Alpha(pixels, x, y), "Depth plates must not back a translucent card with black.");
            Assert.IsTrue(Alpha(pixels, 165, 70) > 0, "The rear plates must extend visibly beyond the card edge.");
            Assert.IsTrue(Alpha(pixels, 80, 126) > 0, "The second offset layer must remain visible below the card.");
            Assert.AreEqual((byte)0, Alpha(pixels, 2, 70), "Depth drawing must stay within the reserved margin.");
        }
        Assert.IsFalse(decoration.IsHitTestVisible);
        Assert.IsNull(decoration.Effect);
    }

    [STATestMethod]
    public void HighlightIsSubtleAndClearsCompletelyWhenProximityEnds()
    {
        var decoration = new CardDepthDecoration { IsHighlight = true, Margin = new Thickness(20) };
        var host = new Grid();
        host.Children.Add(decoration);
        Assert.AreEqual((byte)0, Alpha(Render(host), 80, 70));
        decoration.Activity = 1;
        byte centerAlpha = Alpha(Render(host), 80, 70);
        Assert.IsTrue(centerAlpha is >= 10 and <= 16, "The wash is about 5.5%, preserving the original theme and readable text.");
        decoration.Activity = 0;
        Assert.AreEqual((byte)0, Alpha(Render(host), 80, 70));
        decoration.Activity = double.NaN;
        Assert.AreEqual(0, decoration.Activity);
        decoration.Activity = 2;
        Assert.AreEqual(1, decoration.Activity);
        Assert.IsFalse(decoration.IsHitTestVisible);
    }

    [STATestMethod]
    public void CollapsedDecorationDoesNotChangeClassicLayoutOrPixels()
    {
        var decoration = new CardDepthDecoration { Visibility = Visibility.Collapsed };
        var face = new Border { Width = 140, Height = 100, Background = Brushes.Silver, CornerRadius = new CornerRadius(10) };
        var grid = new Grid();
        grid.Children.Add(face);
        byte[] ordinary = Render(grid);
        grid.Children.Insert(0, decoration);
        byte[] withDisabledDecoration = Render(grid);
        CollectionAssert.AreEqual(ordinary, withDisabledDecoration);
        Assert.AreEqual(140, face.ActualWidth);
        Assert.AreEqual(100, face.ActualHeight);
        Assert.AreEqual(new Size(), decoration.RenderSize);
    }

    internal static void VerifyProjectedScrollClippingAndFreeze()
    {
        if (!FloatingCardMotion.AnimationsAllowed) return;
        var host = new Grid { Height = 80, Margin = new Thickness(8), Background = Brushes.Transparent };
        var rear = new CardDepthDecoration();
        var target = new Grid();
        var front = new CardDepthDecoration { IsHighlight = true };
        target.Children.Add(new Border { Background = Brushes.AliceBlue, CornerRadius = new CornerRadius(10) });
        target.Children.Add(front);
        host.Children.Add(rear);
        host.Children.Add(target);
        FloatingCardMotion.SetTarget(host, target);
        FloatingCardMotion.SetDecoration(host, rear);
        FloatingCardMotion.SetHighlight(host, front);
        FloatingCardMotion.SetEnabled(host, true);
        Transform ordinaryTransform = target.RenderTransform;
        var stack = new StackPanel();
        stack.Children.Add(host);
        stack.Children.Add(new Border { Height = 500 });
        var scroll = new ScrollViewer
        {
            Content = stack, Margin = new Thickness(30, 60, 30, 60), CanContentScroll = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var content = new Grid { Background = Brushes.White };
        content.Children.Add(scroll);
        var surface = new PerspectiveSurface { Child = content, IsPerspectiveEnabled = true, Yaw = -8, Pitch = 5 };
        var window = new Window
        {
            Width = 360, Height = 260, Content = surface, ShowActivated = false, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Left = 80, Top = 80
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            PumpUntil(() => host.IsLoaded && host.IsVisible);
            Point center = host.TransformToAncestor(window).Transform(new Point(70, 35));
            FloatingCardMotion.UpdateProximity(host, center, window);
            PumpUntil(() => rear.Activity >= 0.99 && !rear.HasAnimatedProperties);
            var transforms = (TransformGroup)target.RenderTransform;
            var lift = transforms.Children.OfType<TranslateTransform>().Single();
            Assert.IsTrue(lift.Y < -1);

            FloatingCardMotion.FreezeProximity(host);
            Point frozen = new(lift.X, lift.Y);
            host.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
            { RoutedEvent = Mouse.MouseMoveEvent, Source = host });
            Assert.IsFalse(lift.HasAnimatedProperties,
                "A separately routed MouseMove must not restart a card during an editor/menu freeze.");
            Assert.AreEqual(frozen, new Point(lift.X, lift.Y));

            scroll.ScrollToVerticalOffset(110);
            window.UpdateLayout();
            PumpUntil(() => scroll.VerticalOffset >= 109);
            // The lower part remains inside the whole projected plane while
            // the complete card is clipped above the scrolling viewport.
            Point hiddenPoint = host.TransformToAncestor(window).Transform(new Point(70, 75));
            FloatingCardMotion.UpdateProximity(host, hiddenPoint, window);
            PumpUntil(() => rear.Activity <= 0.001 && !rear.HasAnimatedProperties);
            Assert.AreEqual(0, front.Activity);

            scroll.ScrollToVerticalOffset(0);
            window.UpdateLayout();
            PumpUntil(() => scroll.VerticalOffset == 0);
            FloatingCardMotion.UpdateProximity(host, center, window);
            PumpUntil(() => rear.Activity >= 0.99 && !rear.HasAnimatedProperties);
            FloatingCardMotion.SetEnabled(host, false);
            Assert.AreSame(ordinaryTransform, target.RenderTransform);
            Assert.AreEqual(0, rear.Activity);
            Assert.AreEqual(0, front.Activity);
            Assert.IsFalse(rear.HasAnimatedProperties);
            Assert.IsFalse(front.HasAnimatedProperties);
        }
        finally { window.Close(); }
    }

    private static void PumpUntil(Func<bool> complete)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        DateTime deadline = DateTime.UtcNow.AddSeconds(4);
        timer.Tick += (_, _) => { if (complete() || DateTime.UtcNow >= deadline) frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Assert.IsTrue(complete(), "Synthetic projected card feedback did not settle.");
    }

    private static byte[] Render(FrameworkElement element)
    {
        // Directly rendering the live vector visual avoids screenshot scaling
        // or a VisualBrush masking alpha/color defects in the decoration.
        element.Measure(new Size(180, 140));
        element.Arrange(new Rect(0, 0, 180, 140));
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(180, 140, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        byte[] pixels = new byte[180 * 140 * 4];
        bitmap.CopyPixels(pixels, 180 * 4, 0);
        return pixels;
    }

    private static byte Alpha(byte[] pixels, int x, int y) => pixels[(y * 180 + x) * 4 + 3];
}
