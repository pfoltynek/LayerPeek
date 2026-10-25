using LayerPeek.App.Cameras.Overlay;
using LayerPeek.Core.Cameras.Overlay;

namespace LayerPeek.App.Tests.Cameras.Overlay;

/// <summary>The small-window rules of the overlay cards (T-076 1.4) and the anchor geometry (3.1, 3.2) as pure functions.</summary>
public sealed class OverlayArrangementTests
{
    [Theory]
    [InlineData(960, 540, 0)]
    [InlineData(720, 405, 0)]
    [InlineData(719, 405, 1)]
    [InlineData(720, 404, 1)]
    [InlineData(480, 270, 1)]
    [InlineData(479, 300, 2)]
    [InlineData(600, 269, 2)]
    [InlineData(320, 180, 2)]
    [InlineData(319, 300, 3)]
    [InlineData(600, 179, 3)]
    [InlineData(160, 90, 3)]
    public void StageFollowsTheClientSize(double width, double height, int stage) =>
        Assert.Equal(stage, OverlayArrangement.Stage(width, height));

    [Theory]
    [InlineData(OverlayWidgetSize.Large, OverlayAnchor.TopLeft, 0, OverlayWidgetSize.Large)]
    [InlineData(OverlayWidgetSize.Large, OverlayAnchor.TopLeft, 1, OverlayWidgetSize.Medium)]
    [InlineData(OverlayWidgetSize.Medium, OverlayAnchor.BottomCenter, 1, OverlayWidgetSize.Small)]
    [InlineData(OverlayWidgetSize.Small, OverlayAnchor.TopRight, 1, OverlayWidgetSize.Small)]
    [InlineData(OverlayWidgetSize.Large, OverlayAnchor.BottomRight, 2, OverlayWidgetSize.Small)]
    public void StageShrinksTheCards(OverlayWidgetSize size, OverlayAnchor anchor, int stage, OverlayWidgetSize expected) =>
        Assert.Equal(expected, OverlayArrangement.SizeInStage(size, anchor, stage));

    [Theory]
    [InlineData(OverlayAnchor.TopCenter, 2)]
    [InlineData(OverlayAnchor.BottomCenter, 2)]
    [InlineData(OverlayAnchor.TopLeft, 3)]
    [InlineData(OverlayAnchor.BottomRight, 3)]
    public void SmallStagesLeaveCardsOut(OverlayAnchor anchor, int stage) =>
        Assert.Null(OverlayArrangement.SizeInStage(OverlayWidgetSize.Medium, anchor, stage));

    [Fact]
    public void TopRightAnchorStartsBelowTheWindowButtons()
    {
        // 6 (margin of the buttons) + 30 (button) + 8.
        Assert.Equal(44, OverlayArrangement.TopStart(OverlayAnchor.TopRight));
        Assert.Equal(12, OverlayArrangement.TopStart(OverlayAnchor.TopLeft));
        Assert.Equal(12, OverlayArrangement.TopStart(OverlayAnchor.TopCenter));
    }

    [Fact]
    public void CenterWidthKeepsClearOfTheCornersAndTheWindowButtons()
    {
        // T-076 3.2: 960 − 2 × 112 − 2 × 20 = 696 when the corners are narrower than the button reserve.
        Assert.Equal(696, OverlayArrangement.CenterMaxWidth(960, leftWidth: 0, rightWidth: 0, top: true));
        Assert.Equal(920, OverlayArrangement.CenterMaxWidth(960, leftWidth: 0, rightWidth: 0, top: false));
        // T-076 2.6 (gallery L): next to a 352 wide status card only 960 − 2 × (352 + 8 + 12) = 216 are left.
        Assert.Equal(216, OverlayArrangement.CenterMaxWidth(960, leftWidth: 352, rightWidth: 100, top: true));
        Assert.Equal(216, OverlayArrangement.CenterMaxWidth(960, leftWidth: 100, rightWidth: 352, top: false));
    }

    [Fact]
    public void CenterCardShrinksUntilItsMinimumWidthFits()
    {
        static double MinWidth(OverlayWidgetSize size) => size switch
        {
            OverlayWidgetSize.Large => 300,
            OverlayWidgetSize.Medium => 240,
            _ => 120,
        };

        Assert.Equal(OverlayWidgetSize.Large, OverlayArrangement.FitCenter(OverlayWidgetSize.Large, 696, MinWidth));
        Assert.Equal(OverlayWidgetSize.Medium, OverlayArrangement.FitCenter(OverlayWidgetSize.Large, 240, MinWidth));
        Assert.Equal(OverlayWidgetSize.Small, OverlayArrangement.FitCenter(OverlayWidgetSize.Large, 216, MinWidth));
        Assert.Equal(OverlayWidgetSize.Small, OverlayArrangement.FitCenter(OverlayWidgetSize.Small, 216, MinWidth));
        Assert.Null(OverlayArrangement.FitCenter(OverlayWidgetSize.Large, 119, MinWidth));
        Assert.Null(OverlayArrangement.FitCenter(OverlayWidgetSize.Small, 0, static _ => 0));
        Assert.Equal(OverlayWidgetSize.Medium, OverlayArrangement.FitCenter(OverlayWidgetSize.Medium, 1, static _ => 0));
    }

    [Fact]
    public void StacksThatFitKeepEveryCard()
    {
        // Status M 126 + Times M 122 + 8 between them fit in 540 − 12 − 12.
        bool[] hiddenTop = new bool[1];
        bool[] hiddenBottom = new bool[2];

        bool hid = OverlayArrangement.Overflowing(540, 12, [new(126, 50)], [new(122, 40), new(134, 30)], hiddenTop, hiddenBottom);

        Assert.False(hid);
        Assert.DoesNotContain(true, hiddenTop);
        Assert.DoesNotContain(true, hiddenBottom);
    }

    [Fact]
    public void OverlappingStacksLoseTheCardWithTheLowestPriority()
    {
        // 300 high, room 276: Status 126 + 8 + Times 122 = 256 would fit, but with the cool-down card (134) the stacks need
        // 398: the cool-down card (priority 30) goes, Status (50) and Times (40) stay.
        bool[] hiddenTop = new bool[1];
        bool[] hiddenBottom = new bool[2];

        bool hid = OverlayArrangement.Overflowing(300, 12, [new(126, 50)], [new(122, 40), new(134, 30)], hiddenTop, hiddenBottom);

        Assert.True(hid);
        Assert.Equal([false], hiddenTop);
        Assert.Equal([false, true], hiddenBottom);
    }

    [Fact]
    public void WholeCardsGoUntilTheColumnFits()
    {
        bool[] hiddenTop = new bool[2];
        bool[] hiddenBottom = new bool[1];

        // 200 high, room 176: 100 + 8 + 60 (top) and 90 (bottom) need 266 → the lowest (10) goes: 100 + 8 + 90 = 198 > 176
        // → then 20 goes: 100 ≤ 176.
        OverlayArrangement.Overflowing(200, 12, [new(100, 50), new(60, 10)], [new(90, 20)], hiddenTop, hiddenBottom);

        Assert.Equal([false, true], hiddenTop);
        Assert.Equal([true], hiddenBottom);
    }

    [Fact]
    public void TopRightColumnStartsBelowTheButtons()
    {
        bool[] fits = new bool[1];
        bool[] tooTall = new bool[1];

        // 100 high: from 12 a 76 high card fits, from 44 (top right) it does not.
        Assert.False(OverlayArrangement.Overflowing(100, 12, [new(76, 10)], [], fits, []));
        Assert.True(OverlayArrangement.Overflowing(100, 44, [new(76, 10)], [], tooTall, []));
        Assert.Equal([true], tooTall);
    }

    [Fact]
    public void EqualPrioritiesLeaveOutTheCardFarthestFromTheEdge()
    {
        bool[] hiddenTop = new bool[2];
        bool[] hiddenBottom = new bool[2];

        OverlayArrangement.Overflowing(200, 12, [new(60, 10), new(60, 10)], [new(60, 10), new(60, 10)], hiddenTop, hiddenBottom);

        // Room 176: four cards need 4 × 60 + 3 × 8 = 264; the innermost bottom card goes first, then the innermost top one.
        Assert.Equal([false, true], hiddenTop);
        Assert.Equal([false, true], hiddenBottom);
    }

    [Fact]
    public void UsedHeightCountsTheGapsBetweenDrawnCards()
    {
        Assert.Equal(0, OverlayArrangement.Used([], []));
        Assert.Equal(126, OverlayArrangement.Used([new(126, 1)], [false]));
        Assert.Equal(126 + 8 + 122, OverlayArrangement.Used([new(126, 1), new(50, 1), new(122, 1)], [false, true, false]));
        Assert.Throws<ArgumentException>(() => OverlayArrangement.Overflowing(100, 12, [new(10, 1)], [], [], []));
    }
}
