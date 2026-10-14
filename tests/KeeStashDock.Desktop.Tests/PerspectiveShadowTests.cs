using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KeeStashDock.Desktop.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

[TestClass]
public sealed class PerspectiveShadowTests
{
    [STATestMethod]
    public void ProjectedShadowFollowsTiltedEdgeAndKeepsItsInteriorTransparent()
    {
        const int padding = 32, width = 184, height = 174;
        var outline = Geometry.Parse("M 20,10 L 110,25 L 105,95 L 10,90 Z");
        outline.Freeze();
        var shadow = new SurfaceShadow
        {
            Margin = new Thickness(padding), Outline = outline,
            Spread = 20, ShadowOffset = 4, Strength = 0.3,
        };
        var host = new Grid { Width = width, Height = height };
        host.Children.Add(shadow);
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        byte Alpha(int x, int y) => pixels[((y + padding) * width + x + padding) * 4 + 3];

        for (int y = 35; y < 75; y++)
            for (int x = 30; x < 90; x++)
                Assert.AreEqual((byte)0, Alpha(x, y),
                    "A projected shadow must never darken the translucent panel face.");
        Assert.IsTrue(Alpha(114, 55) > 0,
            "The shadow must follow the tilted right edge inside the native rectangle.");
        Assert.AreEqual((byte)0, Alpha(-25, 55),
            "No stationary rectangular shadow should remain outside the tilted surface.");
        Assert.AreEqual((byte)0, Alpha(150, 55),
            "The reserved drawing space must contain the full soft shadow.");
        Assert.IsFalse(shadow.IsHitTestVisible);
    }

    [STATestMethod]
    public void MixedDpiScalingPreservesProjectedCoordinatesAndSourceGeometry()
    {
        var outline = new RectangleGeometry(new Rect(10, 20, 80, 60), 8, 8)
        {
            Transform = new TranslateTransform(4, 6),
        };
        Rect original = outline.Bounds;
        Geometry scaled = PanelShadowWindow.ScaleProjectedOutline(outline, 1.5, 0.8)!;
        Assert.AreEqual(21, scaled.Bounds.X, 0.0001);
        Assert.AreEqual(20.8, scaled.Bounds.Y, 0.0001);
        Assert.AreEqual(120, scaled.Bounds.Width, 0.0001);
        Assert.AreEqual(48, scaled.Bounds.Height, 0.0001);
        Assert.AreEqual(original, outline.Bounds, "DPI conversion must not mutate the owner silhouette.");
        Assert.IsTrue(scaled.IsFrozen);
        Assert.IsNull(PanelShadowWindow.ScaleProjectedOutline(null, 1.5, 0.8),
            "Classic mode must restore the ordinary rounded-rectangle shadow.");
    }
}
