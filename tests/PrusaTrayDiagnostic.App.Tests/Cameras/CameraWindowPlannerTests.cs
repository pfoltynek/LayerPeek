using Avalonia;

using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.Core.Cameras;

using static PrusaTrayDiagnostic.App.Tests.Cameras.FakeScreenCatalog;

namespace PrusaTrayDiagnostic.App.Tests.Cameras;

/// <summary>Where the camera window opens and goes on a mode switch (T-035), over fake monitors.</summary>
public sealed class CameraWindowPlannerTests
{
    /// <summary>The Windows 11 chrome of a normal window (measured at 100 %), given explicitly so that the tests do not depend on the OS.</summary>
    private static readonly WindowChrome WindowsChrome = new(new Size(16, 39), new Thickness(7, 0, 7, 7));

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
        CameraWindowPlacement? placement = CameraWindowPlanner.ForOpening(CameraWindowDefaults.Default, CameraWindowMode.Normal, null, Screens, WindowsChrome);

        // Work area 2560 × 1392; the visible window 962 × 572 centred at (799, 410), moved out by the 7 px invisible left border.
        Assert.Equal(new CameraWindowPlacement(792, 410, 960, 540), placement);
    }

    [Fact]
    public void MiniPreviewGoesToItsCornerOfTheChosenMonitorWithItsScaling()
    {
        CameraWindowDefaults defaults = CameraWindowDefaults.Default with { Monitor = Lg };

        CameraWindowPlacement? placement = CameraWindowPlanner.ForOpening(defaults, CameraWindowMode.Mini, null, Screens, CameraWindowPlanner.AssumedChrome(CameraWindowMode.Mini));

        // 150 %: 480 × 270 px with a 24 px margin in the bottom right corner of the work area (-1920, 200, 1920, 1032); no frame.
        Assert.Equal(new CameraWindowPlacement(-504, 938, 320, 180), placement);
    }

    [Fact]
    public void FullScreenPlacesTheNormalWindowOnTheChosenMonitor()
    {
        CameraWindowDefaults defaults = CameraWindowDefaults.Default with { Monitor = Lg };

        CameraWindowPlacement placement = CameraWindowPlanner.ForOpening(defaults, CameraWindowMode.FullScreen, null, Screens, WindowsChrome)!.Value;

        Assert.Equal("LG HDR 4K", CameraWindowPlanner.ScreenOf(new PixelBounds(placement.X, placement.Y, 1, 1), Screens)!.DisplayName);
    }

    [Fact]
    public void ReachableRememberedPlacementWins()
    {
        CameraWindowPlacement remembered = new(-1800, 300, 700, 400);

        Assert.Equal(remembered, CameraWindowPlanner.ForOpening(CameraWindowDefaults.Default, CameraWindowMode.Normal, remembered, Screens, WindowsChrome));
    }

    [Fact]
    public void UnreachableRememberedPlacementFallsBackToTheDefaultsInsteadOfTheCentreOfThePrimaryMonitor()
    {
        CameraWindowDefaults defaults = CameraWindowDefaults.Default with
        {
            Normal = new CameraWindowLayout(new CameraWindowSize(640, 360), CameraWindowCorner.TopLeft),
        };

        CameraWindowPlacement? placement = CameraWindowPlanner.ForOpening(defaults, CameraWindowMode.Normal, new CameraWindowPlacement(9000, 9000, 700, 400), Screens, WindowsChrome);

        Assert.Equal(new CameraWindowPlacement(9, 16, 640, 360), placement);
    }

    [Fact]
    public void InvalidRememberedPlacementIsIgnored()
    {
        CameraWindowPlacement? placement = CameraWindowPlanner.ForOpening(CameraWindowDefaults.Default, CameraWindowMode.Normal, new CameraWindowPlacement(10, 10, double.NaN, 400), Screens, WindowsChrome);

        Assert.Equal(new CameraWindowPlacement(792, 410, 960, 540), placement);
    }

    [Fact]
    public void WithoutScreensOnlyARememberedPlacementIsReturned()
    {
        CameraWindowPlacement remembered = new(9000, 9000, 700, 400);

        Assert.Equal(remembered, CameraWindowPlanner.ForOpening(CameraWindowDefaults.Default, CameraWindowMode.Normal, remembered, [], WindowsChrome));
        Assert.Null(CameraWindowPlanner.ForOpening(CameraWindowDefaults.Default, CameraWindowMode.Normal, null, [], WindowsChrome));
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
        Assert.Equal(WindowChrome.None, CameraWindowPlanner.AssumedChrome(CameraWindowMode.Mini));
        Assert.Equal(CameraWindowPlanner.AssumedNormalChrome, CameraWindowPlanner.AssumedChrome(CameraWindowMode.Normal));
    }

    // ---- Visible margin (T-035 Q9): the invisible resize borders of Windows do not count ----

    public static TheoryData<CameraWindowCorner, int, int, double> VisibleMargins => new()
    {
        // 100 % primary, work area (0, 0, 2560, 1392), 960 × 540 client, visible frame 2 × 32, invisible 7 / 0 / 7 / 7.
        { CameraWindowCorner.TopLeft, 0, 16, 1.0 },
        { CameraWindowCorner.TopRight, 0, 16, 1.0 },
        { CameraWindowCorner.BottomLeft, 0, 16, 1.0 },
        { CameraWindowCorner.BottomRight, 0, 16, 1.0 },
        // 150 % LG monitor, work area (-1920, 200, 1920, 1032): margin 24 px, invisible 10.5 → 11 px.
        { CameraWindowCorner.TopLeft, -1920, 24, 1.5 },
        { CameraWindowCorner.BottomRight, -1920, 24, 1.5 },
    };

    [Theory]
    [MemberData(nameof(VisibleMargins))]
    public void VisibleWindowKeepsExactlyTheMarginFromTheWorkArea(CameraWindowCorner corner, int screenX, int marginPixels, double scaling)
    {
        CameraScreen screen = Screens.Single(candidate => candidate.Bounds.X == screenX);
        CameraWindowLayout layout = new(new CameraWindowSize(960, 540), corner);

        CameraWindowPlacement placement = CameraWindowPlanner.Place(layout, screen, WindowsChrome);

        // The visible window: the position moved in by the invisible left / top border, the client plus the visible frame.
        int left = placement.X + (int)Math.Round(7 * scaling, MidpointRounding.AwayFromZero);
        int top = placement.Y;
        int width = (int)Math.Round((960 + 2) * scaling, MidpointRounding.AwayFromZero);
        int height = (int)Math.Round((540 + 32) * scaling, MidpointRounding.AwayFromZero);
        PixelBounds area = screen.UsableArea;
        bool leftSide = corner is CameraWindowCorner.TopLeft or CameraWindowCorner.BottomLeft;
        bool topSide = corner is CameraWindowCorner.TopLeft or CameraWindowCorner.TopRight;
        Assert.Equal(marginPixels, leftSide ? left - area.X : area.Right - (left + width));
        Assert.Equal(marginPixels, topSide ? top - area.Y : area.Bottom - (top + height));
        Assert.Equal((960, 540), (placement.Width, placement.Height));
    }

    [Fact]
    public void WithoutInvisibleBordersTheWholeWindowKeepsTheMargin()
    {
        CameraWindowPlacement placement = CameraWindowPlanner.Place(
            new CameraWindowLayout(new CameraWindowSize(640, 360), CameraWindowCorner.TopLeft),
            Screens[1],
            new WindowChrome(new Size(0, 32), default));

        Assert.Equal(new CameraWindowPlacement(16, 16, 640, 360), placement);
    }

    [Fact]
    public void InvisibleBordersAreTheDifferenceOfTheWindowAndItsVisiblePart()
    {
        // Measured on Windows 11 at 100 %: GetWindowRect (100, 100, 516, 439), DWM extended frame bounds (107, 100, 509, 432).
        Assert.Equal(new Thickness(7, 0, 7, 7), CameraWindowPlanner.InvisibleBordersOf(new PixelRect(100, 100, 416, 339), new PixelRect(107, 100, 402, 332), 1.0));
        // 150 %: pixels become DIP.
        Assert.Equal(new Thickness(8, 0, 8, 8), CameraWindowPlanner.InvisibleBordersOf(new PixelRect(0, 0, 624, 510), new PixelRect(12, 0, 600, 498), 1.5));
        // A visible part outside the window (never expected) counts as no border; an invalid scaling as 100 %.
        Assert.Equal(default, CameraWindowPlanner.InvisibleBordersOf(new PixelRect(10, 10, 100, 100), new PixelRect(0, 0, 200, 200), double.NaN));
        // A borderless window: nothing invisible.
        Assert.Equal(default, CameraWindowPlanner.InvisibleBordersOf(new PixelRect(700, 100, 320, 180), new PixelRect(700, 100, 320, 180), 1.0));
    }

    [Fact]
    public void ChromeKnowsItsVisibleFrameAndWhetherItIsCloseToAnother()
    {
        Assert.Equal(new Size(2, 32), WindowsChrome.VisibleFrame);
        Assert.Equal(new Size(0, 0), new WindowChrome(new Size(4, 4), new Thickness(5)).VisibleFrame);
        Assert.True(WindowsChrome.IsCloseTo(new WindowChrome(new Size(16.3, 39.2), new Thickness(7.4, 0, 7, 6.6))));
        Assert.False(WindowsChrome.IsCloseTo(new WindowChrome(new Size(16, 39), new Thickness(0, 0, 7, 7))));
        Assert.False(WindowsChrome.IsCloseTo(new WindowChrome(new Size(16, 31), new Thickness(7, 0, 7, 7))));
    }
}
