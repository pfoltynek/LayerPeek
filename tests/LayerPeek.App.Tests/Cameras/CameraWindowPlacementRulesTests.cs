using Avalonia;

using LayerPeek.App.Cameras;

namespace LayerPeek.App.Tests.Cameras;

public sealed class CameraWindowPlacementRulesTests
{
    private static readonly PixelRect Primary = new(0, 0, 1920, 1040);
    private static readonly PixelRect Right = new(1920, 0, 2560, 1400);

    [Fact]
    public void WindowInsideAWorkingAreaIsReachable() =>
        Assert.True(CameraWindowPlacementRules.IsReachable(new PixelRect(100, 100, 960, 540), [Primary]));

    [Fact]
    public void WindowWithTheTopEdgeAboveTheScreenIsNotReachable() =>
        // Most of it is visible, but the title bar (where it is dragged) is not.
        Assert.False(CameraWindowPlacementRules.IsReachable(new PixelRect(100, -100, 960, 540), [Primary]));

    [Fact]
    public void WindowOnADisconnectedScreenIsNotReachable() =>
        Assert.False(CameraWindowPlacementRules.IsReachable(new PixelRect(3000, 100, 960, 540), [Primary]));

    [Fact]
    public void BarelyVisibleCornerIsNotReachable() =>
        Assert.False(CameraWindowPlacementRules.IsReachable(new PixelRect(1900, 1030, 960, 540), [Primary]));

    [Fact]
    public void WindowAcrossTwoScreensWithTheTopEdgeOnOneIsReachable() =>
        Assert.True(CameraWindowPlacementRules.IsReachable(new PixelRect(1500, 50, 960, 540), [Primary, Right]));

    [Fact]
    public void TitleBarAcrossTwoStackedScreensIsReachable()
    {
        // The top edge straddles the border of two monitors above each other: together they cover it.
        PixelRect upper = new(0, 0, 1920, 1040);
        PixelRect lower = new(0, 1040, 1920, 1040);

        Assert.True(CameraWindowPlacementRules.IsReachable(new PixelRect(100, 1030, 960, 540), [upper, lower]));
    }

    [Fact]
    public void TitleBarOverAGapBetweenScreensIsNotReachable()
    {
        PixelRect upper = new(0, 0, 1920, 1040);
        PixelRect lower = new(0, 1100, 1920, 1040);

        Assert.False(CameraWindowPlacementRules.IsReachable(new PixelRect(100, 1030, 960, 540), [upper, lower]));
    }

    [Fact]
    public void TitleBarCoveredSideBySideByTwoScreensIsReachable()
    {
        // Only 30 px on the left screen and 30 px on the right one, but 60 px together.
        PixelRect left = new(0, 0, 1000, 1040);
        PixelRect right = new(1000, 0, 1000, 1040);

        Assert.True(CameraWindowPlacementRules.IsReachable(new PixelRect(970, 100, 60, 400), [left, right]));
    }

    [Fact]
    public void SmallMiniPreviewInsideIsReachable() =>
        Assert.True(CameraWindowPlacementRules.IsReachable(new PixelRect(1600, 860, 320, 180), [Primary]));

    [Fact]
    public void NoScreensMeansNothingToCheck() =>
        Assert.True(CameraWindowPlacementRules.IsReachable(new PixelRect(-5000, -5000, 320, 180), []));
}
