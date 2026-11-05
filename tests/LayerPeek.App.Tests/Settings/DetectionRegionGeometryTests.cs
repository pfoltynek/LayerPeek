using LayerPeek.App.Settings;
using LayerPeek.Core.Detection;

namespace LayerPeek.App.Tests.Settings;

/// <summary>
/// The geometry of the bed region editor (T-154): the frame drawn uniformly with letterbox / pillarbox bands, pointer
/// positions converted to normalized frame coordinates without the bands and clamped into the frame, and every drag and key
/// operation giving a valid region.
/// </summary>
public sealed class DetectionRegionGeometryTests
{
    private const double Tolerance = 1e-9;

    [Fact]
    public void A16By9FrameFillsA16By9Editor()
    {
        Assert.Equal(new ViewRect(0, 0, 288, 162), DetectionRegionGeometry.ImageRect(288, 162, 16.0 / 9));
        // No frame: 16:9.
        Assert.Equal(new ViewRect(0, 0, 288, 162), DetectionRegionGeometry.ImageRect(288, 162, double.NaN));
        Assert.Equal(default, DetectionRegionGeometry.ImageRect(0, 162, 16.0 / 9));
    }

    [Fact]
    public void A4By3FrameHasBandsLeftAndRightAndAWideOneAboveAndBelow()
    {
        // 4:3 in 288 × 162: 216 wide, 36 on each side.
        ViewRect pillar = DetectionRegionGeometry.ImageRect(288, 162, 4.0 / 3);
        Assert.Equal(new ViewRect(36, 0, 216, 162), pillar);

        // 21:9 in 288 × 162: 288 wide, 123.43 high, bands above and below.
        ViewRect letter = DetectionRegionGeometry.ImageRect(288, 162, 21.0 / 9);
        Assert.Equal(0, letter.X, Tolerance);
        Assert.Equal(288, letter.Width, Tolerance);
        Assert.Equal(288 / (21.0 / 9), letter.Height, Tolerance);
        Assert.Equal((162 - letter.Height) / 2, letter.Y, Tolerance);
    }

    [Fact]
    public void PointsOfTheBandsAreClampedToTheEdgeOfTheFrame()
    {
        ViewRect image = DetectionRegionGeometry.ImageRect(288, 162, 4.0 / 3);

        Assert.Equal((0.0, 0.5), DetectionRegionGeometry.ToFrame(10, 81, image));
        Assert.Equal((1.0, 0.0), DetectionRegionGeometry.ToFrame(280, -20, image));
        (double x, double y) = DetectionRegionGeometry.ToFrame(36 + 54, 40.5, image);
        Assert.Equal(0.25, x, Tolerance);
        Assert.Equal(0.25, y, Tolerance);
    }

    [Fact]
    public void TheRegionIsDrawnOverTheFrameOnly()
    {
        ViewRect image = DetectionRegionGeometry.ImageRect(288, 162, 4.0 / 3);

        ViewRect view = DetectionRegionGeometry.ToView(DetectionRegion.CoreOneBed, image);

        Assert.Equal(36 + (0.15 * 216), view.X, Tolerance);
        Assert.Equal(0.55 * 162, view.Y, Tolerance);
        Assert.Equal(0.70 * 216, view.Width, Tolerance);
        Assert.Equal(0.45 * 162, view.Height, Tolerance);
        Assert.Equal(image, DetectionRegionGeometry.ToView(default, image));
    }

    [Fact]
    public void APressGrabsACornerTheRegionOrDraws()
    {
        ViewRect image = new(0, 0, 200, 100);
        DetectionRegion region = new(0.25, 0.25, 0.5, 0.5);

        Assert.Equal(RegionDragKind.ResizeTopLeft, DetectionRegionGeometry.HitTest(region, image, 52, 23));
        Assert.Equal(RegionDragKind.ResizeTopRight, DetectionRegionGeometry.HitTest(region, image, 150, 25));
        Assert.Equal(RegionDragKind.ResizeBottomLeft, DetectionRegionGeometry.HitTest(region, image, 50, 75));
        Assert.Equal(RegionDragKind.ResizeBottomRight, DetectionRegionGeometry.HitTest(region, image, 155, 79));
        Assert.Equal(RegionDragKind.Move, DetectionRegionGeometry.HitTest(region, image, 100, 50));
        Assert.Equal(RegionDragKind.Draw, DetectionRegionGeometry.HitTest(region, image, 10, 10));
    }

    [Fact]
    public void DrawingSpansThePressAndThePointerInAnyDirection()
    {
        DetectionRegion drawn = DetectionRegionGeometry.Drag(RegionDragKind.Draw, DetectionRegion.Full, (0.8, 0.9), (0.2, 0.3));

        Assert.Equal(0.2, drawn.X, Tolerance);
        Assert.Equal(0.3, drawn.Y, Tolerance);
        Assert.Equal(0.6, drawn.Width, Tolerance);
        Assert.Equal(0.6, drawn.Height, Tolerance);

        // A click without a drag: at least the smallest size, inside the frame.
        DetectionRegion tiny = DetectionRegionGeometry.Drag(RegionDragKind.Draw, DetectionRegion.Full, (1, 1), (1, 1));
        Assert.Equal(DetectionRegion.MinSize, tiny.Width, Tolerance);
        Assert.Equal(1 - DetectionRegion.MinSize, tiny.X, Tolerance);
        Assert.True(tiny.X + tiny.Width <= 1 + Tolerance);
    }

    [Fact]
    public void MovingKeepsTheSizeAndStopsAtTheEdges()
    {
        DetectionRegion region = new(0.25, 0.25, 0.5, 0.5);

        DetectionRegion moved = DetectionRegionGeometry.Drag(RegionDragKind.Move, region, (0.5, 0.5), (0.6, 0.4));
        AssertRegion(moved, 0.35, 0.15, 0.5, 0.5);

        DetectionRegion stopped = DetectionRegionGeometry.Drag(RegionDragKind.Move, region, (0.5, 0.5), (1, 1));
        Assert.Equal(0.5, stopped.X, Tolerance);
        Assert.Equal(0.5, stopped.Y, Tolerance);
        Assert.Equal(0.5, stopped.Width, Tolerance);
        Assert.Equal(DetectionRegion.Full, DetectionRegionGeometry.Move(DetectionRegion.Full, -0.1, 0.1));
    }

    [Fact]
    public void ACornerResizesAgainstTheOppositeOneAlsoAcrossIt()
    {
        DetectionRegion region = new(0.25, 0.25, 0.5, 0.5);

        DetectionRegion bigger = DetectionRegionGeometry.Drag(RegionDragKind.ResizeBottomRight, region, (0.75, 0.75), (0.9, 0.8));
        Assert.Equal(0.25, bigger.X, Tolerance);
        Assert.Equal(0.65, bigger.Width, Tolerance);
        Assert.Equal(0.55, bigger.Height, Tolerance);

        // Dragged past the opposite corner: the rectangle flips instead of getting a negative size.
        DetectionRegion flipped = DetectionRegionGeometry.Drag(RegionDragKind.ResizeTopLeft, region, (0.25, 0.25), (0.9, 0.95));
        Assert.Equal(0.75, flipped.X, Tolerance);
        Assert.Equal(0.75, flipped.Y, Tolerance);
        Assert.Equal(0.15, flipped.Width, Tolerance);
        Assert.Equal(0.2, flipped.Height, Tolerance);

        DetectionRegion topRight = DetectionRegionGeometry.Drag(RegionDragKind.ResizeTopRight, region, (0.75, 0.25), (1.2, -0.3));
        AssertRegion(topRight, 0.25, 0, 0.75, 0.75);
        DetectionRegion bottomLeft = DetectionRegionGeometry.Drag(RegionDragKind.ResizeBottomLeft, region, (0.25, 0.75), (0, 1));
        AssertRegion(bottomLeft, 0, 0.25, 0.75, 0.75);
    }

    [Fact]
    public void KeysResizeTheRightAndBottomEdgeWithinTheFrame()
    {
        DetectionRegion region = new(0.25, 0.25, 0.5, 0.5);

        DetectionRegion wider = DetectionRegionGeometry.Resize(region, 0.1, -0.1);
        Assert.Equal(0.6, wider.Width, Tolerance);
        Assert.Equal(0.4, wider.Height, Tolerance);
        Assert.Equal((0.25, 0.25), (wider.X, wider.Y));

        Assert.Equal(0.75, DetectionRegionGeometry.Resize(region, 1, 0).Width, Tolerance);
        Assert.Equal(DetectionRegion.MinSize, DetectionRegionGeometry.Resize(region, -1, 0).Width, Tolerance);
        Assert.Equal(region, DetectionRegionGeometry.Resize(region, double.NaN, double.PositiveInfinity));
    }

    private static void AssertRegion(DetectionRegion actual, double x, double y, double width, double height)
    {
        Assert.Equal(x, actual.X, Tolerance);
        Assert.Equal(y, actual.Y, Tolerance);
        Assert.Equal(width, actual.Width, Tolerance);
        Assert.Equal(height, actual.Height, Tolerance);
    }
}
