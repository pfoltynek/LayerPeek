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
        Assert.Null(CameraWindowPlanner.OnCurrentScreen(CameraWindowDefaults.DefaultMini, new PixelBounds(0, 0, 10, 10), [], WindowChrome.None));
    }

    [Fact]
    public void ModeSwitchUsesTheMonitorTheWindowIsOnNow()
    {
        // Mostly on the rotated monitor on the right (Q5), although the defaults choose the primary one.
        PixelBounds window = new(2300, 100, 1000, 600);

        CameraWindowPlacement? placement = CameraWindowPlanner.OnCurrentScreen(CameraWindowDefaults.DefaultMini, window, Screens, WindowChrome.None);

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

    /// <summary>
    /// The frame of a normal window per DPI as Windows 11 reports it (<c>AdjustWindowRectExForDpi</c> of
    /// <c>WS_OVERLAPPEDWINDOW</c>, 2026-10-23) and its visible border (measured 1 px at 100 / 125 %, 2 px at 150 %): the
    /// title bar and borders do not grow linearly with the scale.
    /// </summary>
    public static TheoryData<double, int, int, int> PixelFrames => new()
    {
        { 1.0, 8, 31, 1 },
        { 1.25, 9, 38, 1 },
        { 1.5, 11, 45, 2 },
        { 1.75, 12, 52, 2 },
        { 2.0, 13, 58, 2 },
    };

    [Theory]
    [MemberData(nameof(PixelFrames))]
    public void VisibleGapIsExactlyTheMarginWithTheFrameOfTheMonitorsScale(double scaling, int border, int top, int visibleBorder)
    {
        // T-035 Q9 / T-071 Q3: the chrome for the scale of the chosen monitor, known before the window is shown, so
        // nothing has to be corrected after it is shown. Only the DWM border of the frame is visible at the sides.
        PixelFrame frame = new(border, top, border, border, visibleBorder);
        CameraScreen screen = new("DELL U2719D", new PixelBounds(2560, 0, 1440, 2560), new PixelBounds(2560, 0, 1440, 2500), scaling, IsPrimary: false);
        WindowChrome chrome = WindowChrome.FromPixels(frame, scaling);
        int margin = (int)Math.Round(16 * scaling, MidpointRounding.AwayFromZero);
        int clientWidth = (int)(640 * scaling);
        int clientHeight = (int)(360 * scaling);

        foreach (CameraWindowCorner corner in new[] { CameraWindowCorner.TopLeft, CameraWindowCorner.TopRight, CameraWindowCorner.BottomLeft, CameraWindowCorner.BottomRight })
        {
            CameraWindowPlacement placement = CameraWindowPlanner.Place(new CameraWindowLayout(new CameraWindowSize(640, 360), corner), screen, chrome);

            // The window as Windows lays it out: client pixels plus the frame; the visible part lacks the invisible borders.
            int visibleLeft = placement.X + frame.Left - frame.VisibleBorder;
            int visibleTop = placement.Y;
            int visibleRight = placement.X + frame.Left + clientWidth + frame.VisibleBorder;
            int visibleBottom = placement.Y + frame.Top + clientHeight + frame.VisibleBorder;
            bool left = corner is CameraWindowCorner.TopLeft or CameraWindowCorner.BottomLeft;
            bool upper = corner is CameraWindowCorner.TopLeft or CameraWindowCorner.TopRight;
            Assert.Equal(margin, left ? visibleLeft - screen.UsableArea.X : screen.UsableArea.Right - visibleRight);
            Assert.Equal(margin, upper ? visibleTop - screen.UsableArea.Y : screen.UsableArea.Bottom - visibleBottom);
            Assert.Equal(new Size(640, 360), new Size(placement.Width, placement.Height));
        }
    }

    [Fact]
    public void FixedChromeOfOneHundredPercentMissesAtOneHundredTwentyFivePercent()
    {
        // The cause of the 1–2 px move after showing (T-071 Q3): 125 %, normal window 640 × 360 in the centre of the
        // rotated monitor; the 100 % chrome (16 × 39 DIP, 7 DIP invisible) puts it at x 2869, the real frame at x 2871.
        CameraScreen screen = new("DELL U2719D", new PixelBounds(2560, 0, 1440, 2560), new PixelBounds(2560, 0, 1440, 2500), 1.25, IsPrimary: false);
        CameraWindowLayout centre = new(new CameraWindowSize(640, 360), CameraWindowCorner.Center);

        CameraWindowPlacement assumed = CameraWindowPlanner.Place(centre, screen, WindowsChrome);
        CameraWindowPlacement exact = CameraWindowPlanner.Place(centre, screen, WindowChrome.FromPixels(new PixelFrame(9, 38, 9, 9, VisibleBorder: 1), 1.25));

        Assert.Equal((2869, 1005), (assumed.X, assumed.Y));
        Assert.Equal((2871, 1005), (exact.X, exact.Y));

        // 150 % (E2E 2026-10-23: window (2789, 950) 982 × 596, visible 964 × 587 after the old correction).
        CameraScreen at150 = new("DELL U2719D", screen.Bounds, new PixelBounds(2560, 0, 1440, 2488), 1.5, IsPrimary: false);
        CameraWindowPlacement assumed150 = CameraWindowPlanner.Place(centre, at150, WindowsChrome);
        CameraWindowPlacement exact150 = CameraWindowPlanner.Place(centre, at150, WindowChrome.FromPixels(new PixelFrame(11, 45, 11, 11, VisibleBorder: 2), 1.5));
        Assert.Equal((2787, 950), (assumed150.X, assumed150.Y));
        Assert.Equal((2789, 950), (exact150.X, exact150.Y));
    }

    [Fact]
    public void ChromeFromPixelsHasNoInvisibleTopAndNeverNegativeBorders()
    {
        WindowChrome chrome = WindowChrome.FromPixels(new PixelFrame(11, 45, 11, 11, VisibleBorder: 2), 1.5);

        Assert.Equal(22 / 1.5, chrome.Frame.Width, 9);
        Assert.Equal(56 / 1.5, chrome.Frame.Height, 9);
        Assert.Equal(new Thickness(9 / 1.5, 0, 9 / 1.5, 9 / 1.5), chrome.InvisibleBorders);
        // A border thicker than the frame side (never expected) leaves nothing invisible; an invalid scaling counts as 100 %.
        Assert.Equal(default, WindowChrome.FromPixels(new PixelFrame(1, 30, 1, 1, VisibleBorder: 2), double.NaN).InvisibleBorders);
        Assert.Equal(WindowChrome.None, WindowChrome.FromPixels(default, 1.25));
    }

    [Fact]
    public void ChromesAreCloseWhenTheyDifferByLessThanHalfAPixel()
    {
        WindowChrome predicted = WindowChrome.FromPixels(new PixelFrame(9, 38, 9, 9, VisibleBorder: 1), 1.25);
        WindowChrome measured = new(new Size(18.2 / 1.25, 47 / 1.25), new Thickness(8 / 1.25, 0, 8 / 1.25, 8.4 / 1.25));
        WindowChrome onePixelOff = new(new Size(18 / 1.25, 47 / 1.25), new Thickness(7 / 1.25, 0, 8 / 1.25, 8 / 1.25));

        Assert.True(predicted.IsCloseTo(measured, 1.25));
        // 1 px at 125 % is 0.8 DIP; at 250 % 0.4 DIP, still a pixel.
        Assert.False(predicted.IsCloseTo(onePixelOff, 1.25));
        Assert.False(WindowChrome.FromPixels(new PixelFrame(16, 75, 16, 16, 2), 2.5).IsCloseTo(WindowChrome.FromPixels(new PixelFrame(16, 75, 16, 15, 2), 2.5), 2.5));
    }

    [Fact]
    public void ChromeIsChosenForTheMonitorTheWindowGoesTo()
    {
        // The 150 % LG on the left gets its own frame, the 100 % primary another one.
        CameraWindowDefaults defaults = CameraWindowDefaults.Default with { Monitor = Lg, Normal = new CameraWindowLayout(new CameraWindowSize(640, 360), CameraWindowCorner.TopLeft) };
        List<double> asked = [];
        WindowChrome ChromeOn(CameraScreen screen)
        {
            asked.Add(screen.Scaling);
            return screen.Scaling == 1.5
                ? WindowChrome.FromPixels(new PixelFrame(11, 45, 11, 11, VisibleBorder: 2), 1.5)
                : WindowChrome.FromPixels(new PixelFrame(8, 31, 8, 8, VisibleBorder: 1), screen.Scaling);
        }

        CameraWindowPlacement? opening = CameraWindowPlanner.ForOpening(defaults, CameraWindowMode.Normal, null, Screens, ChromeOn);
        CameraWindowPlacement? switching = CameraWindowPlanner.OnCurrentScreen(defaults.Normal, new PixelBounds(10, 10, 100, 100), Screens, ChromeOn);

        // LG work area starts at (-1920, 200): visible gap 24 px, 9 px invisible left border, no invisible top.
        Assert.Equal((-1920 + 24 - 9, 200 + 24), (opening!.Value.X, opening.Value.Y));
        // The primary at 100 %: 16 px, 7 px invisible.
        Assert.Equal((16 - 7, 16), (switching!.Value.X, switching.Value.Y));
        Assert.Equal([1.5, 1.0], asked);
    }
}
