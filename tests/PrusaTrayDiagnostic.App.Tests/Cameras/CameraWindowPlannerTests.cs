using Avalonia;

using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.Core.Cameras;

using static PrusaTrayDiagnostic.App.Tests.Cameras.FakeScreenCatalog;

namespace PrusaTrayDiagnostic.App.Tests.Cameras;

/// <summary>Where the camera window opens and goes on a mode switch (T-035), over fake monitors.</summary>
public sealed class CameraWindowPlannerTests
{
    /// <summary>The Windows 11 frame of a normal window (measured), given explicitly so that the tests do not depend on the OS.</summary>
    private static readonly Size WindowsFrame = new(16, 39);

    private static readonly IReadOnlyList<CameraScreen> Screens = ThreeMonitors().GetScreens();

    private static CameraMonitorId Lg => new("LG HDR 4K", 1920, 1080);

    [Fact]
    public void ScreensAreOrderedLeftToRight()
    {
        Assert.Equal([-1920, 0, 2560], Screens.Select(static screen => screen.Bounds.X));
    }

    [Fact]
    public void WithoutARememberedPlacementTheNormalWindowUsesTheDefaultsOnThePrimaryMonitor()
    {
        CameraWindowPlacement? placement = CameraWindowPlanner.ForOpening(CameraWindowDefaults.Default, CameraWindowMode.Normal, null, Screens, WindowsFrame);

        // Work area 2560 × 1392; outer window 976 × 579 centred.
        Assert.Equal(new CameraWindowPlacement(792, 406, 960, 540), placement);
    }

    [Fact]
    public void MiniPreviewGoesToItsCornerOfTheChosenMonitorWithItsScaling()
    {
        CameraWindowDefaults defaults = CameraWindowDefaults.Default with { Monitor = Lg };

        CameraWindowPlacement? placement = CameraWindowPlanner.ForOpening(defaults, CameraWindowMode.Mini, null, Screens, CameraWindowPlanner.AssumedFrame(CameraWindowMode.Mini));

        // 150 %: 480 × 270 px with a 24 px margin in the bottom right corner of the work area (-1920, 200, 1920, 1032); no frame.
        Assert.Equal(new CameraWindowPlacement(-504, 938, 320, 180), placement);
    }

    [Fact]
    public void FullScreenPlacesTheNormalWindowOnTheChosenMonitor()
    {
        CameraWindowDefaults defaults = CameraWindowDefaults.Default with { Monitor = Lg };

        CameraWindowPlacement placement = CameraWindowPlanner.ForOpening(defaults, CameraWindowMode.FullScreen, null, Screens, WindowsFrame)!.Value;

        Assert.Equal("LG HDR 4K", CameraWindowPlanner.ScreenOf(new PixelBounds(placement.X, placement.Y, 1, 1), Screens)!.DisplayName);
    }

    [Fact]
    public void ReachableRememberedPlacementWins()
    {
        CameraWindowPlacement remembered = new(-1800, 300, 700, 400);

        Assert.Equal(remembered, CameraWindowPlanner.ForOpening(CameraWindowDefaults.Default, CameraWindowMode.Normal, remembered, Screens, WindowsFrame));
    }

    [Fact]
    public void UnreachableRememberedPlacementFallsBackToTheDefaultsInsteadOfTheCentreOfThePrimaryMonitor()
    {
        CameraWindowDefaults defaults = CameraWindowDefaults.Default with
        {
            Normal = new CameraWindowLayout(new CameraWindowSize(640, 360), CameraWindowCorner.TopLeft),
        };

        CameraWindowPlacement? placement = CameraWindowPlanner.ForOpening(defaults, CameraWindowMode.Normal, new CameraWindowPlacement(9000, 9000, 700, 400), Screens, WindowsFrame);

        Assert.Equal(new CameraWindowPlacement(16, 16, 640, 360), placement);
    }

    [Fact]
    public void InvalidRememberedPlacementIsIgnored()
    {
        CameraWindowPlacement? placement = CameraWindowPlanner.ForOpening(CameraWindowDefaults.Default, CameraWindowMode.Normal, new CameraWindowPlacement(10, 10, double.NaN, 400), Screens, WindowsFrame);

        Assert.Equal(new CameraWindowPlacement(792, 406, 960, 540), placement);
    }

    [Fact]
    public void WithoutScreensOnlyARememberedPlacementIsReturned()
    {
        CameraWindowPlacement remembered = new(9000, 9000, 700, 400);

        Assert.Equal(remembered, CameraWindowPlanner.ForOpening(CameraWindowDefaults.Default, CameraWindowMode.Normal, remembered, [], WindowsFrame));
        Assert.Null(CameraWindowPlanner.ForOpening(CameraWindowDefaults.Default, CameraWindowMode.Normal, null, [], WindowsFrame));
        Assert.Null(CameraWindowPlanner.OnCurrentScreen(CameraWindowDefaults.DefaultMini, new PixelBounds(0, 0, 10, 10), [], default));
    }

    [Fact]
    public void ModeSwitchUsesTheMonitorTheWindowIsOnNow()
    {
        // Mostly on the rotated monitor on the right (Q5), although the defaults choose the primary one.
        PixelBounds window = new(2300, 100, 1000, 600);

        CameraWindowPlacement? placement = CameraWindowPlanner.OnCurrentScreen(CameraWindowDefaults.DefaultMini, window, Screens, default);

        // Work area (2560, 0, 1440, 2512): bottom right with 16 px.
        Assert.Equal(new CameraWindowPlacement(3664, 2316, 320, 180), placement);
    }

    [Theory]
    [InlineData(100, 100, "DELL U2719D", 0)]
    [InlineData(-1000, 500, "LG HDR 4K", -1920)]
    [InlineData(3000, 2000, "DELL U2719D", 2560)]
    [InlineData(20000, 20000, "DELL U2719D", 0)]
    public void ScreenOfIsTheMostOverlappedMonitorElseThePrimary(int x, int y, string name, int screenX)
    {
        CameraScreen screen = CameraWindowPlanner.ScreenOf(new PixelBounds(x, y, 300, 200), Screens)!;

        Assert.Equal(name, screen.DisplayName);
        Assert.Equal(screenX, screen.Bounds.X);
    }

    [Fact]
    public void ReachabilityUsesTheScalingOfTheMonitorOfTheWindow()
    {
        // The title bar of a window in the gap above the LG monitor (y < 200) is not on any work area left of x = 0.
        Assert.False(CameraWindowPlanner.IsReachable(new CameraWindowPlacement(-1900, 0, 400, 300), Screens));
        Assert.True(CameraWindowPlanner.IsReachable(new CameraWindowPlacement(-1900, 210, 400, 300), Screens));
        Assert.True(CameraWindowPlanner.IsReachable(new CameraWindowPlacement(9000, 9000, 400, 300), []));
    }

    [Fact]
    public void FrameIsTheMeasuredWindowMinusTheClientArea()
    {
        Assert.Equal(new Size(16, 39), CameraWindowPlanner.FrameOf(new Size(416, 339), new Size(400, 300)));
        Assert.Null(CameraWindowPlanner.FrameOf(null, new Size(400, 300)));
        Assert.Null(CameraWindowPlanner.FrameOf(new Size(400, 300), new Size(400, 300)));
        Assert.Null(CameraWindowPlanner.FrameOf(new Size(300, 300), new Size(400, 300)));
    }

    [Fact]
    public void MiniPreviewHasNoFrame()
    {
        Assert.Equal(default, CameraWindowPlanner.AssumedFrame(CameraWindowMode.Mini));
        Assert.Equal(CameraWindowPlanner.AssumedNormalFrame, CameraWindowPlanner.AssumedFrame(CameraWindowMode.Normal));
    }
}
