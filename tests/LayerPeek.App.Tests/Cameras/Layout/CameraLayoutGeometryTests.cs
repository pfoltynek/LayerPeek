using Avalonia;

using LayerPeek.App.Cameras.Layout;
using LayerPeek.Core.Cameras.Overlay;

namespace LayerPeek.App.Tests.Cameras.Layout;

/// <summary>Geometry of the layout mode (T-076 5.2–5.5): zone sizes, the target of a drag and its position, arrow key focus.</summary>
public sealed class CameraLayoutGeometryTests
{
    private static readonly CameraLayoutZoneBounds[] Zones =
    [
        new(OverlayAnchor.TopLeft, new Rect(12, 12, 260, 150)),
        new(OverlayAnchor.TopCenter, new Rect(350, 12, 260, 150)),
        new(OverlayAnchor.TopRight, new Rect(688, 12, 260, 150)),
        new(OverlayAnchor.BottomLeft, new Rect(12, 378, 260, 150)),
        new(OverlayAnchor.BottomCenter, new Rect(350, 378, 260, 150)),
        new(OverlayAnchor.BottomRight, new Rect(688, 378, 260, 150)),
    ];

    [Theory]
    [InlineData(960, 540, 260, 150)]
    [InlineData(640, 360, 197.33333333333334, 134)]
    [InlineData(480, 300, 144, 104)]
    [InlineData(528, 300, 160, 104)]
    [InlineData(1920, 1080, 260, 150)]
    public void ZoneSizeFollowsTheWindow(double width, double height, double zoneWidth, double zoneHeight)
    {
        Size size = CameraLayoutGeometry.ZoneSize(width, height);

        Assert.Equal(zoneWidth, size.Width, 6);
        Assert.Equal(zoneHeight, size.Height, 6);
    }

    [Fact]
    public void ZoneUnderThePointerIsTheTarget()
    {
        foreach (CameraLayoutZoneBounds zone in Zones)
        {
            Assert.Equal(zone.Anchor, CameraLayoutGeometry.TargetZone(zone.Bounds.Center, Zones, excluded: null));
        }
    }

    [Fact]
    public void OutsideTheZonesTheNearestCentreWins()
    {
        Assert.Equal(OverlayAnchor.TopLeft, CameraLayoutGeometry.TargetZone(new Point(300, 200), Zones, excluded: null));
        Assert.Equal(OverlayAnchor.BottomRight, CameraLayoutGeometry.TargetZone(new Point(955, 535), Zones, excluded: null));
        Assert.Equal(OverlayAnchor.TopCenter, CameraLayoutGeometry.TargetZone(new Point(480, 200), Zones, excluded: null));
    }

    [Fact]
    public void OverTheToolbarThereIsNoTarget()
    {
        Rect toolbar = new(200, 240, 560, 60);

        Assert.Null(CameraLayoutGeometry.TargetZone(new Point(480, 270), Zones, toolbar));
        Assert.Equal(OverlayAnchor.TopCenter, CameraLayoutGeometry.TargetZone(new Point(480, 230), Zones, toolbar));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(39, 0)]
    [InlineData(41, 1)]
    [InlineData(120, 2)]
    [InlineData(500, 3)]
    public void InsertIndexCountsTheCardsAbove(double y, int index)
    {
        Rect[] cards = [new(0, 0, 100, 80), new(0, 88, 100, 40), new(0, 136, 100, 100)];

        Assert.Equal(index, CameraLayoutGeometry.InsertIndex(y, cards));
    }

    [Fact]
    public void ArrowKeysFocusTheNearestCardInTheirDirection()
    {
        Rect from = new(12, 12, 200, 120);
        Rect[] cards = [new(400, 50, 200, 60), new(700, 60, 200, 60), new(12, 400, 200, 120), new(400, 400, 200, 120)];

        Assert.Equal(0, CameraLayoutGeometry.Nearest(from, cards, CameraLayoutDirection.Right));
        Assert.Equal(2, CameraLayoutGeometry.Nearest(from, cards, CameraLayoutDirection.Down));
        Assert.Null(CameraLayoutGeometry.Nearest(from, cards, CameraLayoutDirection.Left));
        Assert.Null(CameraLayoutGeometry.Nearest(from, cards, CameraLayoutDirection.Up));
        Assert.Equal(3, CameraLayoutGeometry.Nearest(cards[2], [from, cards[0], cards[1], cards[3]], CameraLayoutDirection.Right) is { } i ? i : -1);
    }
}
