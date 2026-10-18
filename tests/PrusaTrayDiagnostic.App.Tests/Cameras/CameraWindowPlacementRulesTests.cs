using Avalonia;

using PrusaTrayDiagnostic.App.Cameras;

namespace PrusaTrayDiagnostic.App.Tests.Cameras;

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
    public void SmallMiniPreviewInsideIsReachable() =>
        Assert.True(CameraWindowPlacementRules.IsReachable(new PixelRect(1600, 860, 320, 180), [Primary]));

    [Fact]
    public void NoScreensMeansNothingToCheck() =>
        Assert.True(CameraWindowPlacementRules.IsReachable(new PixelRect(-5000, -5000, 320, 180), []));
}
