using System.Windows;
using System.Windows.Controls;
using KeeStashDock.Desktop.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

[TestClass]
public sealed class PerspectivePoseTests
{
    [TestMethod]
    public void NearbyDesktopMovementTiltsInOppositeDirections()
    {
        var size = new Size(400, 600);
        PerspectivePose left = PerspectivePose.FromPointer(new Point(-60, 300), size);
        PerspectivePose right = PerspectivePose.FromPointer(new Point(460, 300), size);
        Assert.IsTrue(left.Nearby && right.Nearby,
            "Pointer movement outside the HWND must still drive nearby perspective.");
        Assert.IsTrue(left.Yaw < 0 && right.Yaw > 0);
        Assert.IsTrue(left.OffsetX > 0 && right.OffsetX < 0);
        Assert.AreEqual(left.Yaw, -right.Yaw);
        Assert.AreEqual(left.OffsetX, -right.OffsetX);
        Assert.AreEqual(0.0, left.Pitch);
        Assert.AreEqual(0.0, right.Pitch);

        PerspectivePose above = PerspectivePose.FromPointer(new Point(200, -60), size);
        PerspectivePose below = PerspectivePose.FromPointer(new Point(200, 660), size);
        Assert.IsTrue(above.Nearby && below.Nearby);
        Assert.IsTrue(above.Pitch < 0 && below.Pitch > 0);
        Assert.IsTrue(above.OffsetY > 0 && below.OffsetY < 0);
        Assert.AreEqual(above.Pitch, -below.Pitch);
        Assert.AreEqual(above.OffsetY, -below.OffsetY);
        Assert.AreEqual(0.0, above.Yaw);
        Assert.AreEqual(0.0, below.Yaw);
    }

    [STATestMethod]
    public void ApproachedEdgeRecedesAndShortensOnAllFourSides()
    {
        // Test the actual host projection, not just angle signs: from a camera
        // in front of the panel, the edge pushed away must appear shorter.
        var surface = new PerspectiveSurface { Child = new Border(), IsPerspectiveEnabled = true };
        foreach (Size size in new[] { new Size(400, 600), new Size(700, 260), new Size(300, 1000) })
        {
            surface.Measure(size);
            surface.Arrange(new Rect(size));
            surface.UpdateLayout();
            foreach (double distance in new[] { 0.0, 60.0 })
            foreach (var (side, pointer, first, second, oppositeFirst, oppositeSecond) in new[]
            {
                ("left", new Point(-distance, size.Height / 2), new Point(0, 0), new Point(0, size.Height),
                    new Point(size.Width, 0), new Point(size.Width, size.Height)),
                ("right", new Point(size.Width + distance, size.Height / 2), new Point(size.Width, 0), new Point(size.Width, size.Height),
                    new Point(0, 0), new Point(0, size.Height)),
                ("top", new Point(size.Width / 2, -distance), new Point(0, 0), new Point(size.Width, 0),
                    new Point(0, size.Height), new Point(size.Width, size.Height)),
                ("bottom", new Point(size.Width / 2, size.Height + distance), new Point(0, size.Height), new Point(size.Width, size.Height),
                    new Point(0, 0), new Point(size.Width, 0)),
            })
            {
                Apply(default);
                double neutralEdge = EdgeLength(first, second);
                PerspectivePose pose = PerspectivePose.FromPointer(pointer, size);
                Apply(pose);
                double approachedEdge = EdgeLength(first, second);
                double oppositeEdge = EdgeLength(oppositeFirst, oppositeSecond);
                Assert.IsTrue(approachedEdge < neutralEdge - 0.1,
                    $"Approaching {side} at {distance} DIPs outside {size} must push that edge away, not raise it.");
                Assert.IsTrue(oppositeEdge > neutralEdge + 0.1,
                    $"The opposite edge must approach the viewer when {side} is pressed down.");
                Assert.IsTrue(approachedEdge < oppositeEdge,
                    "The resulting trapezoid must retain the near/far perspective difference.");
            }
        }

        double EdgeLength(Point first, Point second)
            => (surface.ProjectPoint(second) - surface.ProjectPoint(first)).Length;

        void Apply(PerspectivePose pose)
        {
            surface.Yaw = pose.Yaw;
            surface.Pitch = pose.Pitch;
            surface.OffsetX = pose.OffsetX;
            surface.OffsetY = pose.OffsetY;
        }
    }

    [TestMethod]
    public void FixedPoseRetainsDepthWithoutRequestingPointerFollowing()
    {
        PerspectivePose fixedPose = PerspectivePose.Fixed;
        Assert.IsFalse(fixedPose.Nearby,
            "The fixed panel pose must not itself request nearby pointer following.");
        Assert.IsTrue(Math.Abs(fixedPose.Yaw) > 0 && Math.Abs(fixedPose.Yaw) <= PerspectiveSurface.MaximumYaw);
        Assert.IsTrue(Math.Abs(fixedPose.Pitch) > 0 && Math.Abs(fixedPose.Pitch) <= PerspectiveSurface.MaximumPitch);
        Assert.AreEqual(0.0, fixedPose.OffsetX);
        Assert.AreEqual(0.0, fixedPose.OffsetY);
    }

    [TestMethod]
    public void CenterIsNeutralButRemainsWithinTheProximityRegion()
    {
        foreach (Size size in Sizes())
        {
            PerspectivePose pose = PerspectivePose.FromPointer(new Point(size.Width / 2, size.Height / 2), size);
            Assert.IsTrue(pose.Nearby);
            Assert.AreEqual(0.0, pose.Yaw);
            Assert.AreEqual(0.0, pose.Pitch);
            Assert.AreEqual(0.0, pose.OffsetX);
            Assert.AreEqual(0.0, pose.OffsetY);
        }
    }

    [TestMethod]
    public void OutsideMotionFadesWithoutAnEdgeOrReachJump()
    {
        var size = new Size(400, 600);
        PerspectivePose previous = PerspectivePose.FromPointer(new Point(0, 300), size);
        Assert.IsTrue(previous.Yaw < 0);
        for (double distance = 0.5; distance <= PerspectivePose.Reach; distance += 0.5)
        {
            PerspectivePose pose = PerspectivePose.FromPointer(new Point(-distance, 300), size);
            Assert.IsTrue(pose.Yaw >= previous.Yaw && pose.Yaw <= 0,
                "Moving away from the panel must fade the existing tilt monotonically.");
            Assert.IsTrue(Math.Abs(pose.OffsetX) <= Math.Abs(previous.OffsetX));
            // The pose deliberately rounds to 0.1 to ignore pointer noise.
            // Half-DIP movement may cross one rounding boundary, but cannot
            // introduce a visible discontinuity at the panel or reach edge.
            Assert.IsTrue(Math.Abs(pose.Yaw - previous.Yaw) <= 0.100001);
            Assert.IsTrue(Math.Abs(pose.OffsetX - previous.OffsetX) <= 0.100001);
            previous = pose;
        }
        Assert.AreEqual(default(PerspectivePose), previous);
        PerspectivePose justInside = PerspectivePose.FromPointer(new Point(-PerspectivePose.Reach + 0.001, 300), size);
        Assert.IsTrue(justInside.Nearby);
        Assert.AreEqual(0.0, justInside.Yaw);
        Assert.AreEqual(0.0, justInside.OffsetX);
    }

    [TestMethod]
    public void BeyondReachAndDiagonalDistanceReturnInactiveNeutralPose()
    {
        var size = new Size(400, 600);
        foreach (Point point in new Point[]
        {
            new(-180, 300), new(580, 300), new(200, -180), new(200, 780),
            new(-130, -130), new(530, 730), new(-10000, 10000),
        })
            Assert.AreEqual(default(PerspectivePose), PerspectivePose.FromPointer(point, size),
                $"{point} must not keep nearby pointer polling active.");

        Assert.IsTrue(PerspectivePose.FromPointer(new Point(-100, -100), size).Nearby,
            "A diagonal point inside the radial reach must still respond.");
    }

    [TestMethod]
    public void AcrossPanelSizesAndPointerPositionsAllMotionRemainsBounded()
    {
        foreach (Size size in Sizes())
        for (int x = 0; x <= 32; x++)
        for (int y = 0; y <= 32; y++)
        {
            var point = new Point(
                -240 + (size.Width + 480) * x / 32,
                -240 + (size.Height + 480) * y / 32);
            PerspectivePose pose = PerspectivePose.FromPointer(point, size);
            Assert.IsTrue(double.IsFinite(pose.Yaw) && Math.Abs(pose.Yaw) <= 10);
            Assert.IsTrue(double.IsFinite(pose.Pitch) && Math.Abs(pose.Pitch) <= 7);
            Assert.IsTrue(double.IsFinite(pose.OffsetX) && Math.Abs(pose.OffsetX) <= 2);
            Assert.IsTrue(double.IsFinite(pose.OffsetY) && Math.Abs(pose.OffsetY) <= 2);
            if (!pose.Nearby) Assert.AreEqual(default(PerspectivePose), pose);
        }
    }

    [TestMethod]
    public void InvalidPointersAndEmptyLayoutReturnInactiveNeutralPose()
    {
        foreach (Point point in new Point[]
        {
            new(double.NaN, 0), new(0, double.NaN),
            new(double.PositiveInfinity, 0), new(double.NegativeInfinity, 0),
            new(0, double.PositiveInfinity), new(0, double.NegativeInfinity),
        })
            Assert.AreEqual(default(PerspectivePose), PerspectivePose.FromPointer(point, new Size(400, 600)));

        foreach (Size size in new[] { Size.Empty, new Size(0, 600), new Size(400, 0), new Size(0, 0),
            new Size(double.PositiveInfinity, 600), new Size(400, double.PositiveInfinity), new Size(double.NaN, 600) })
            Assert.AreEqual(default(PerspectivePose), PerspectivePose.FromPointer(new Point(50, 50), size));
    }

    private static Size[] Sizes() =>
    [
        new(1, 1), new(280, 300), new(400, 44), new(400, 600),
        new(1200, 300), new(300, 1400), new(3840, 2160),
    ];
}
