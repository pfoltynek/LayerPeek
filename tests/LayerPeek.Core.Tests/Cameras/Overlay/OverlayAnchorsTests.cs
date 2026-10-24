using LayerPeek.Core.Cameras.Overlay;

namespace LayerPeek.Core.Tests.Cameras.Overlay;

/// <summary>Anchor geometry and mirroring (T-077): left ↔ right, top ↔ bottom, centers stay when mirrored horizontally.</summary>
public class OverlayAnchorsTests
{
    [Fact]
    public void AllHasTheSixAnchorsInDeclarationOrder()
    {
        Assert.Equal(
            [OverlayAnchor.TopLeft, OverlayAnchor.TopCenter, OverlayAnchor.TopRight, OverlayAnchor.BottomLeft, OverlayAnchor.BottomCenter, OverlayAnchor.BottomRight],
            OverlayAnchors.All);
    }

    [Theory]
    [InlineData(OverlayAnchor.TopLeft, true, true, false, false)]
    [InlineData(OverlayAnchor.TopCenter, true, false, false, true)]
    [InlineData(OverlayAnchor.TopRight, true, false, true, false)]
    [InlineData(OverlayAnchor.BottomLeft, false, true, false, false)]
    [InlineData(OverlayAnchor.BottomCenter, false, false, false, true)]
    [InlineData(OverlayAnchor.BottomRight, false, false, true, false)]
    public void GeometryOfEveryAnchor(OverlayAnchor anchor, bool top, bool left, bool right, bool center)
    {
        Assert.Equal(top, OverlayAnchors.IsTop(anchor));
        Assert.Equal(left, OverlayAnchors.IsLeft(anchor));
        Assert.Equal(right, OverlayAnchors.IsRight(anchor));
        Assert.Equal(center, OverlayAnchors.IsCenter(anchor));
        Assert.Equal(!center, OverlayAnchors.IsCorner(anchor));
    }

    [Theory]
    [InlineData(OverlayAnchor.TopLeft, OverlayAnchor.TopRight, OverlayAnchor.BottomLeft, OverlayAnchor.BottomRight)]
    [InlineData(OverlayAnchor.TopCenter, OverlayAnchor.TopCenter, OverlayAnchor.BottomCenter, OverlayAnchor.BottomCenter)]
    [InlineData(OverlayAnchor.TopRight, OverlayAnchor.TopLeft, OverlayAnchor.BottomRight, OverlayAnchor.BottomLeft)]
    [InlineData(OverlayAnchor.BottomLeft, OverlayAnchor.BottomRight, OverlayAnchor.TopLeft, OverlayAnchor.TopRight)]
    [InlineData(OverlayAnchor.BottomCenter, OverlayAnchor.BottomCenter, OverlayAnchor.TopCenter, OverlayAnchor.TopCenter)]
    [InlineData(OverlayAnchor.BottomRight, OverlayAnchor.BottomLeft, OverlayAnchor.TopRight, OverlayAnchor.TopLeft)]
    public void MirrorSwapsSidesAndHeights(OverlayAnchor anchor, OverlayAnchor horizontally, OverlayAnchor vertically, OverlayAnchor both)
    {
        Assert.Equal(anchor, OverlayAnchors.Mirror(anchor, horizontally: false, vertically: false));
        Assert.Equal(horizontally, OverlayAnchors.Mirror(anchor, horizontally: true, vertically: false));
        Assert.Equal(vertically, OverlayAnchors.Mirror(anchor, horizontally: false, vertically: true));
        Assert.Equal(both, OverlayAnchors.Mirror(anchor, horizontally: true, vertically: true));
    }

    /// <summary>
    /// Every anchor × every old T-036 corner, moving the main anchor (<see cref="OverlayAnchor.BottomLeft"/>) into the
    /// corner. Rows: the anchor; columns: target corner TopLeft, TopRight, BottomLeft, BottomRight.
    /// </summary>
    [Theory]
    [InlineData(OverlayAnchor.TopLeft, OverlayAnchor.BottomLeft, OverlayAnchor.BottomRight, OverlayAnchor.TopLeft, OverlayAnchor.TopRight)]
    [InlineData(OverlayAnchor.TopCenter, OverlayAnchor.BottomCenter, OverlayAnchor.BottomCenter, OverlayAnchor.TopCenter, OverlayAnchor.TopCenter)]
    [InlineData(OverlayAnchor.TopRight, OverlayAnchor.BottomRight, OverlayAnchor.BottomLeft, OverlayAnchor.TopRight, OverlayAnchor.TopLeft)]
    [InlineData(OverlayAnchor.BottomLeft, OverlayAnchor.TopLeft, OverlayAnchor.TopRight, OverlayAnchor.BottomLeft, OverlayAnchor.BottomRight)]
    [InlineData(OverlayAnchor.BottomCenter, OverlayAnchor.TopCenter, OverlayAnchor.TopCenter, OverlayAnchor.BottomCenter, OverlayAnchor.BottomCenter)]
    [InlineData(OverlayAnchor.BottomRight, OverlayAnchor.TopRight, OverlayAnchor.TopLeft, OverlayAnchor.BottomRight, OverlayAnchor.BottomLeft)]
    public void MirrorBetweenCornersForEveryAnchorAndEveryOldCorner(
        OverlayAnchor anchor, OverlayAnchor toTopLeft, OverlayAnchor toTopRight, OverlayAnchor toBottomLeft, OverlayAnchor toBottomRight)
    {
        Assert.Equal(toTopLeft, OverlayAnchors.MirrorBetweenCorners(anchor, OverlayAnchor.BottomLeft, OverlayAnchor.TopLeft));
        Assert.Equal(toTopRight, OverlayAnchors.MirrorBetweenCorners(anchor, OverlayAnchor.BottomLeft, OverlayAnchor.TopRight));
        Assert.Equal(toBottomLeft, OverlayAnchors.MirrorBetweenCorners(anchor, OverlayAnchor.BottomLeft, OverlayAnchor.BottomLeft));
        Assert.Equal(toBottomRight, OverlayAnchors.MirrorBetweenCorners(anchor, OverlayAnchor.BottomLeft, OverlayAnchor.BottomRight));
    }

    [Fact]
    public void MirrorBetweenCornersMovesTheSourceCornerIntoTheTarget()
    {
        foreach (OverlayAnchor from in OverlayAnchors.All.Where(OverlayAnchors.IsCorner))
        {
            foreach (OverlayAnchor to in OverlayAnchors.All.Where(OverlayAnchors.IsCorner))
            {
                Assert.Equal(to, OverlayAnchors.MirrorBetweenCorners(from, from, to));
            }
        }
    }

    [Theory]
    [InlineData(OverlayAnchor.TopCenter, OverlayAnchor.TopLeft)]
    [InlineData(OverlayAnchor.TopLeft, OverlayAnchor.BottomCenter)]
    [InlineData((OverlayAnchor)6, OverlayAnchor.TopLeft)]
    public void MirrorBetweenCornersRejectsCentersAndUnknownValues(OverlayAnchor from, OverlayAnchor to) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => OverlayAnchors.MirrorBetweenCorners(OverlayAnchor.TopLeft, from, to));

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public void UnknownAnchorIsRejected(int value)
    {
        OverlayAnchor anchor = (OverlayAnchor)value;

        Assert.False(OverlayAnchors.IsDefined(anchor));
        Assert.Throws<ArgumentOutOfRangeException>(() => OverlayAnchors.Mirror(anchor, horizontally: true, vertically: false));
        Assert.Throws<ArgumentOutOfRangeException>(() => OverlayAnchors.IsTop(anchor));
    }
}
