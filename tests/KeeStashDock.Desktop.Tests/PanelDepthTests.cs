using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KeeStashDock.Desktop.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

[TestClass]
public sealed class PanelDepthTests
{
    [STATestMethod]
    public void FloatingChromePreservesCardOpacityAndHasNoSolidBackplate()
    {
        var host = new Grid { Width = 160, Height = 100 };
        host.Children.Add(new FloatingFrame { Backplate = true, Margin = new Thickness(5, 5, 0, 0), Stroke = Brushes.Gray });
        host.Children.Add(new FloatingFrame { Margin = new Thickness(0, 0, 5, 5),
            Fill = new SolidColorBrush(Color.FromArgb(80, 200, 200, 200)), Stroke = Brushes.White });
        host.Measure(new Size(160, 100));
        host.Arrange(new Rect(0, 0, 160, 100));
        host.UpdateLayout();
        var bitmap = new RenderTargetBitmap(160, 100, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);
        var pixels = new byte[160 * 100 * 4];
        bitmap.CopyPixels(pixels, 160 * 4, 0);
        Assert.AreEqual((byte)80, pixels[(50 * 160 + 80) * 4 + 3],
            "The offset backing must not increase the configured card opacity.");
        Assert.AreEqual((byte)0, pixels[(2 * 160 + 153) * 4 + 3],
            "The upper corner is genuinely cut away, not painted over with an opaque patch.");
        Assert.IsTrue(host.Children.Cast<FloatingFrame>().All(frame => !frame.IsHitTestVisible));
    }

    [STATestMethod]
    public void HollowShadowLeavesTranslucentSurfaceInteriorUntouched()
    {
        var host = new Grid { Width = 160, Height = 140 };
        var shadow = new SurfaceShadow
        {
            Margin = new Thickness(30), Spread = 24, ShadowOffset = 4, Strength = 0.3,
        };
        host.Children.Add(shadow);
        host.Measure(new Size(160, 140));
        host.Arrange(new Rect(0, 0, 160, 140));
        host.UpdateLayout();
        var bitmap = new RenderTargetBitmap(160, 140, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);
        var pixels = new byte[160 * 140 * 4];
        bitmap.CopyPixels(pixels, 160 * 4, 0);

        for (int y = 40; y < 100; y++)
            for (int x = 40; x < 120; x++)
                Assert.AreEqual((byte)0, pixels[(y * 160 + x) * 4 + 3],
                    "The shadow must not darken the interior of a translucent card.");
        Assert.IsTrue(pixels[(70 * 160 + 26) * 4 + 3] > 0,
            "The exterior must contain a visible soft shadow.");
        Assert.AreEqual((byte)0, pixels[(70 * 160 + 1) * 4 + 3],
            "Reserved drawing space must contain the complete shadow.");
        Assert.IsFalse(shadow.IsHitTestVisible);
        Assert.IsNull(shadow.Effect, "Drawing the silhouette must not blur a text-bearing ancestor.");
    }

    internal static void VerifyFiniteMotionReleasesItsClockAndCancellationWins(FrameworkElement visual)
    {
        // Animation timing needs a presented visual connected to WPF's render
        // host. The shared isolated Application scenario supplies that host.
        Transform previous = visual.RenderTransform;
        var transform = new TranslateTransform();
        visual.RenderTransform = transform;
        var motion = new MotionValue(transform, TranslateTransform.YProperty);
        try
        {
            motion.To(-2);
            Assert.IsTrue(transform.HasAnimatedProperties);
            PumpUntil(() => !transform.HasAnimatedProperties);
            Assert.AreEqual(-2, transform.Y);

            motion.To(3);
            motion.To(0, animate: false);
            Assert.IsFalse(transform.HasAnimatedProperties);
            Assert.AreEqual(0, transform.Y);
            var finished = DateTime.UtcNow.AddMilliseconds(250);
            PumpUntil(() => DateTime.UtcNow >= finished);
            Assert.AreEqual(0, transform.Y, "A replaced animation cannot restore stale hover state.");
        }
        finally { motion.To(0, animate: false); visual.RenderTransform = previous; }
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
        timer.Tick += (_, _) => { if (condition() || DateTime.UtcNow >= deadline) frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Assert.IsTrue(condition());
    }
}
