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

    [STATestMethod]
    public void SameDpiSharesFrozenOutlineButSnapshotsMutableInput()
    {
        var outline = new RectangleGeometry(new Rect(10, 20, 80, 60), 8, 8);
        Geometry snapshot = PanelShadowWindow.ScaleProjectedOutline(outline, 1, 1)!;
        Assert.IsFalse(outline.IsFrozen, "A caller-owned geometry must remain editable.");
        Assert.IsTrue(snapshot.IsFrozen);
        outline.Rect = new Rect(20, 30, 80, 60);
        Assert.AreEqual(new Rect(10, 20, 80, 60), snapshot.Bounds,
            "Changing the source cannot change a shadow's retained snapshot.");
        outline.Freeze();
        Assert.AreSame(outline, PanelShadowWindow.ScaleProjectedOutline(outline, 1, 1),
            "Normal pointer frames must share the already-frozen silhouette without copying its points.");
    }

    [STATestMethod]
    public void ProjectedThicknessIsThinThemedAndHollowAndDoesNotChangeClassic()
    {
        var outline = new RectangleGeometry(new Rect(10, 10, 100, 90), 8, 8);
        outline.Freeze();
        var themeBrush = new SolidColorBrush(Colors.SteelBlue);
        var shadow = new SurfaceShadow
        {
            Outline = outline, Strength = 0, Depth = 2.5, DepthBrush = themeBrush,
        };
        byte[] pixels = Render(shadow);
        Assert.IsTrue(Alpha(pixels, 65, 101) > 80, "The bottom edge should have a visible thin side wall.");
        Assert.IsTrue(Alpha(pixels, 110, 50) > 0, "The right edge should show the slight depth offset.");
        Assert.AreEqual((byte)0, Alpha(pixels, 65, 105), "Thickness must not become a large second panel.");
        for (int y = 20; y < 90; y++)
            for (int x = 20; x < 100; x++)
                Assert.AreEqual((byte)0, Alpha(pixels, x, y), "Depth must leave the translucent face entirely hollow.");
        Assert.IsFalse(shadow.IsHitTestVisible);
        Assert.IsFalse(themeBrush.IsFrozen, "Drawing must not freeze an externally owned theme brush.");
        int side = PixelIndex(65, 101);
        Assert.IsTrue(pixels[side] > pixels[side + 2], "The side wall should use the theme's color.");
        themeBrush.Color = Colors.White;
        byte[] changedTheme = Render(shadow);
        Assert.AreEqual(changedTheme[side], changedTheme[side + 2], "Live theme changes must update the side wall.");

        shadow.Depth = 0;
        Assert.IsTrue(Render(shadow).All(value => value == 0), "Disabling depth should clear it completely.");
        shadow.Depth = 2.5;
        shadow.Outline = null;
        Assert.IsTrue(Render(shadow).All(value => value == 0), "Classic shadows must not gain the projected side wall.");
    }

    [STATestMethod]
    public void ReusedShadowRefreshesFalloffAndTracksTheNewSilhouette()
    {
        var outline = new RectangleGeometry(new Rect(20, 20, 80, 70), 8, 8);
        outline.Freeze();
        var shadow = new SurfaceShadow { Outline = outline, Spread = 20, ShadowOffset = 4, Strength = 0.3 };
        byte strong = Alpha(Render(shadow), 108, 55);
        Assert.IsTrue(strong > 0);
        shadow.Strength = 0.1;
        Assert.IsTrue(Alpha(Render(shadow), 108, 55) < strong, "Changing strength must invalidate cached falloff paints.");
        shadow.Spread = 4;
        Assert.AreEqual((byte)0, Alpha(Render(shadow), 108, 55), "Changing spread must not retain wider cached strokes.");
        shadow.Spread = 20;
        shadow.Strength = 0.3;
        var moved = new RectangleGeometry(new Rect(30, 20, 80, 70), 8, 8);
        moved.Freeze();
        shadow.Outline = moved;
        byte[] pixels = Render(shadow);
        Assert.AreEqual((byte)0, Alpha(pixels, 108, 55), "The old edge must become transparent inside the new face.");
        Assert.IsTrue(Alpha(pixels, 118, 55) > 0, "The retained paints must follow the new projected edge.");
    }

    private const int RenderPadding = 32, RenderWidth = 184, RenderHeight = 174;

    private static int PixelIndex(int x, int y)
        => ((y + RenderPadding) * RenderWidth + x + RenderPadding) * 4;

    private static byte Alpha(byte[] pixels, int x, int y) => pixels[PixelIndex(x, y) + 3];

    private static byte[] Render(SurfaceShadow shadow)
    {
        var host = new Grid { Width = RenderWidth, Height = RenderHeight };
        shadow.Margin = new Thickness(RenderPadding);
        host.Children.Add(shadow);
        try
        {
            host.Measure(new Size(RenderWidth, RenderHeight));
            host.Arrange(new Rect(0, 0, RenderWidth, RenderHeight));
            host.UpdateLayout();
            var bitmap = new RenderTargetBitmap(RenderWidth, RenderHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(host);
            var pixels = new byte[RenderWidth * RenderHeight * 4];
            bitmap.CopyPixels(pixels, RenderWidth * 4, 0);
            return pixels;
        }
        finally { host.Children.Remove(shadow); }
    }
}
