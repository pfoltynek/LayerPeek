using Avalonia;

using LayerPeek.App.Windows;

namespace LayerPeek.App.Tests.Windows;

/// <summary>
/// Default size of the settings and diagnostics windows and how it, or the size the user chose (T-094), is fitted into
/// the work area of the screen (T-033, T-041).
/// </summary>
public sealed class AuxiliaryWindowPlacementTests
{
    /// <summary>Title bar assumed by the design (Windows 11 at 100 %).</summary>
    private const double TitleBar = 32;

    private static readonly Thickness Chrome = new(0, TitleBar, 0, 0);

    private static readonly Size Default = AuxiliaryWindowPlacement.DefaultClientSize;

    private static readonly Size Min = AuxiliaryWindowPlacement.MinClientSize;

    [Fact]
    public void DefaultSizeWithTheTitleBarFitsTheSmallestCommonLaptopWorkArea()
    {
        // User decision 2026-10-25 (T-094): about 1093 × 580 DIP of work area (1366 × 768 at 125 %, 1920 × 1080 at 175 %).
        Assert.Equal(new Size(780, 532), Default);
        Assert.True(Default.Width <= 1093, $"Width {Default.Width}");
        Assert.True(Default.Height + TitleBar <= 580, $"Height {Default.Height} + {TitleBar}");
        Assert.Equal(new Size(640, 400), Min);
        Assert.Equal(TitleBar, AuxiliaryWindowPlacement.DefaultChrome.Top);
    }

    [Theory]
    [InlineData(1366, 708, 1.25, 195, 1)] // 1366 × 768 at 125 % with a 48 DIP (60 px) taskbar: 1092.8 × 566.4 DIP
    [InlineData(1366, 720, 1.25, 195, 7)] // the same with a 48 px taskbar: 1092.8 × 576 DIP
    [InlineData(1920, 996, 1.75, 277, 4)] // 1920 × 1080 at 175 % with a 48 DIP (84 px) taskbar: 1097.1 × 569.1 DIP
    [InlineData(1920, 1032, 1.5, 375, 93)] // 1920 × 1080 at 150 % with a 48 px taskbar: 1280 × 688 DIP
    public void DefaultSizeIsKeptAndCentredOnTheTargetScreens(int width, int height, double scaling, int x, int y)
    {
        AuxiliaryWindowBounds bounds = Fit(new PixelRect(0, 0, width, height), scaling);

        Assert.Equal(Default, bounds.ClientSize);
        Assert.Equal(new PixelPoint(x, y), bounds.Position);
    }

    [Fact]
    public void ScreenOf800By600ShrinksOnlyTheHeight()
    {
        // The former target (T-033): 800 × 560 DIP of work area leaves 528 for the client below the title bar.
        AuxiliaryWindowBounds bounds = Fit(new PixelRect(0, 0, 800, 560), scaling: 1.0);

        Assert.Equal(new Size(780, 528), bounds.ClientSize);
        Assert.Equal(new PixelPoint(10, 0), bounds.Position);
    }

    [Fact]
    public void SmallerWorkAreaShrinksTheWindowToFitIncludingTheTitleBar()
    {
        // A work area of 700 × 448 DIP (875 × 560 px at 125 %) on a second monitor right of the first.
        AuxiliaryWindowBounds bounds = Fit(new PixelRect(1920, 100, 875, 560), scaling: 1.25);

        Assert.Equal(new Size(700, 416), bounds.ClientSize);
        Assert.Equal(new PixelPoint(1920, 100), bounds.Position);
    }

    [Fact]
    public void OnlyTheDimensionThatDoesNotFitShrinks()
    {
        AuxiliaryWindowBounds bounds = Fit(new PixelRect(0, 0, 1600, 500), scaling: 1.0);

        Assert.Equal(new Size(780, 468), bounds.ClientSize);
        Assert.Equal(new PixelPoint(410, 0), bounds.Position);
    }

    [Fact]
    public void WindowNeverGetsSmallerThanItsMinimumAndStartsAtTheTopLeftOfTheWorkArea()
    {
        AuxiliaryWindowBounds bounds = Fit(new PixelRect(-600, 20, 600, 380), scaling: 1.0);

        Assert.Equal(new Size(640, 400), bounds.ClientSize);
        // Larger than the work area: its title bar stays reachable at the top left.
        Assert.Equal(new PixelPoint(-600, 20), bounds.Position);
    }

    [Fact]
    public void MeasuredWindowFrameIsPartOfTheFit()
    {
        // Borders of 8 DIP left, right and bottom (invisible resize borders on Windows 10) and a 31 DIP title bar.
        AuxiliaryWindowBounds bounds = AuxiliaryWindowPlacement.Fit(Default, Min, new PixelRect(0, 0, 790, 600), scaling: 1.0, new Thickness(8, 31, 8, 8));

        Assert.Equal(new Size(774, 532), bounds.ClientSize);
        Assert.Equal(new PixelPoint(0, 14), bounds.Position);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(double.NaN)]
    [InlineData(-1.0)]
    public void InvalidScalingCountsAsOne(double scaling)
    {
        AuxiliaryWindowBounds bounds = Fit(new PixelRect(0, 0, 800, 600), scaling);

        Assert.Equal(Default, bounds.ClientSize);
        Assert.Equal(new PixelPoint(10, 18), bounds.Position);
    }

    [Fact]
    public void FrameMeasuredFromTheWindowSplitsTheBordersEvenly()
    {
        Thickness chrome = AuxiliaryWindowPlacement.ChromeOf(new Size(796, 571), new Size(780, 532));

        Assert.Equal(new Thickness(8, 31, 8, 8), chrome);
    }

    [Fact]
    public void UnknownFrameFallsBackToTheAssumedTitleBar()
    {
        Assert.Equal(AuxiliaryWindowPlacement.DefaultChrome, AuxiliaryWindowPlacement.ChromeOf(null, Default));
        // A frame not larger than the client area (headless, not shown yet) tells nothing either.
        Assert.Equal(AuxiliaryWindowPlacement.DefaultChrome, AuxiliaryWindowPlacement.ChromeOf(Default, Default));
    }

    // ---- Before the window is shown (no measured frame yet), and the correction after it opened ----

    [Fact]
    public void BeforeShowingTheWindowIsFittedWithTheAssumedTitleBar()
    {
        // 700 × 448 DIP of work area: the window must already be small enough when it appears (no visible jump).
        Size client = AuxiliaryWindowPlacement.SizeBeforeShowing(Default, Min, new PixelRect(0, 0, 875, 560), scaling: 1.25);

        Assert.Equal(new Size(700, 416), client);
    }

    [Fact]
    public void BeforeShowingOnALargeScreenTheDesignedSizeIsKept()
    {
        Size client = AuxiliaryWindowPlacement.SizeBeforeShowing(Default, Min, new PixelRect(0, 0, 1920, 1032), scaling: 1.5);

        Assert.Equal(Default, client);
    }

    [Fact]
    public void BeforeShowingWithoutAScreenTheDesignedSizeIsKept()
    {
        Size client = AuxiliaryWindowPlacement.SizeBeforeShowing(Default, Min, workArea: null, scaling: 1.0);

        Assert.Equal(Default, client);
    }

    [Fact]
    public void AfterOpeningOnlyALargerMeasuredFrameCorrectsTheWindow()
    {
        Size shown = Default;
        PixelRect workArea = new(0, 0, 800, 600);

        // Windows 11: the frame matches the assumption, nothing moves.
        Assert.Null(AuxiliaryWindowPlacement.CorrectionAfterOpening(shown, Default, Min, workArea, 1.0, new Thickness(0, 32, 0, 0)));
        // A slightly thinner frame would allow a larger window, but the window does not grow (that would be a jump too).
        Assert.Null(AuxiliaryWindowPlacement.CorrectionAfterOpening(shown, Default, Min, workArea, 1.0, new Thickness(0, 30, 0, 0)));

        // A thicker frame than assumed (borders of 12): the window would not fit, so it shrinks and is centred again.
        AuxiliaryWindowBounds? corrected = AuxiliaryWindowPlacement.CorrectionAfterOpening(shown, Default, Min, workArea, 1.0, new Thickness(12, 31, 12, 12));
        Assert.Equal(new AuxiliaryWindowBounds(new Size(776, 532), new PixelPoint(0, 12)), corrected);
    }

    // ---- The remembered size (T-094) ----

    [Fact]
    public void WithoutARememberedSizeTheDesignedSizeIsPreferred()
    {
        Assert.Equal(Default, AuxiliaryWindowPlacement.PreferredClientSize(null, Default, Min));
    }

    [Fact]
    public void RememberedSizeIsPreferredAndNeverBelowTheMinimum()
    {
        Assert.Equal(new Size(900, 640), AuxiliaryWindowPlacement.PreferredClientSize(new Size(900, 640), Default, Min));
        Assert.Equal(new Size(1600, 1000), AuxiliaryWindowPlacement.PreferredClientSize(new Size(1600, 1000), Default, Min));
        Assert.Equal(Min, AuxiliaryWindowPlacement.PreferredClientSize(new Size(320, 200), Default, Min));
        Assert.Equal(new Size(640, 700), AuxiliaryWindowPlacement.PreferredClientSize(new Size(500, 700), Default, Min));
    }

    [Theory]
    [InlineData(double.NaN, 600)]
    [InlineData(900, double.NaN)]
    [InlineData(double.PositiveInfinity, 600)]
    [InlineData(900, double.NegativeInfinity)]
    [InlineData(-900, 600)]
    [InlineData(900, 0)]
    [InlineData(20000, 600)]
    [InlineData(900, 16385)]
    public void UnusableRememberedSizeGivesTheDesignedSize(double width, double height)
    {
        Assert.False(AuxiliaryWindowPlacement.IsUsable(new Size(width, height)));
        Assert.Equal(Default, AuxiliaryWindowPlacement.PreferredClientSize(new Size(width, height), Default, Min));
    }

    [Fact]
    public void RememberedSizeSmallerThanTheWorkAreaIsKept()
    {
        Size client = Restore(new Size(900, 640), new PixelRect(0, 0, 1920, 1032), scaling: 1.0);

        Assert.Equal(new Size(900, 640), client);
    }

    [Theory]
    [InlineData(1.0, 1366, 728, 1366, 696)] // 1366 × 768 at 100 %, 40 px taskbar
    [InlineData(1.25, 1366, 708, 1092.8, 534.4)] // 1366 × 768 at 125 %, 48 DIP taskbar
    [InlineData(1.75, 1920, 996, 1097.142857, 537.142857)] // 1920 × 1080 at 175 %, 48 DIP taskbar
    public void RememberedSizeLargerThanTheWorkAreaShrinksToItAtEveryScaling(double scaling, int width, int height, double clientWidth, double clientHeight)
    {
        // Remembered on a 2560 × 1440 monitor at 100 %, opened on a smaller one (or the large one was disconnected).
        PixelRect workArea = new(0, 0, width, height);
        AuxiliaryWindowBounds bounds = AuxiliaryWindowPlacement.Fit(
            AuxiliaryWindowPlacement.PreferredClientSize(new Size(2400, 1300), Default, Min), Min, workArea, scaling, Chrome);

        Assert.Equal(clientWidth, bounds.ClientSize.Width, 0.001);
        Assert.Equal(clientHeight, bounds.ClientSize.Height, 0.001);
        AssertInside(bounds, workArea, scaling);
    }

    [Fact]
    public void RememberedSizeOnAMonitorLeftOfThePrimaryStaysOnThatMonitor()
    {
        // The window opens on a monitor with negative coordinates: it is centred there, never on the primary.
        PixelRect workArea = new(-1366, 0, 1366, 708);
        AuxiliaryWindowBounds bounds = AuxiliaryWindowPlacement.Fit(new Size(1000, 600), Min, workArea, 1.25, Chrome);

        Assert.Equal(new Size(1000, 534.4), bounds.ClientSize);
        AssertInside(bounds, workArea, 1.25);
    }

    [Fact]
    public void WorkAreaSmallerThanTheMinimumKeepsTheMinimumWithTheTitleBarReachable()
    {
        // A tiny remembered size and a tiny monitor: never below the minimum, the top left corner stays on the work area.
        PixelRect workArea = new(100, 50, 600, 380);
        AuxiliaryWindowBounds bounds = AuxiliaryWindowPlacement.Fit(
            AuxiliaryWindowPlacement.PreferredClientSize(new Size(200, 100), Default, Min), Min, workArea, 1.0, Chrome);

        Assert.Equal(Min, bounds.ClientSize);
        Assert.Equal(new PixelPoint(100, 50), bounds.Position);
    }

    private static Size Restore(Size remembered, PixelRect workArea, double scaling) =>
        AuxiliaryWindowPlacement.SizeBeforeShowing(AuxiliaryWindowPlacement.PreferredClientSize(remembered, Default, Min), Min, workArea, scaling);

    private static void AssertInside(AuxiliaryWindowBounds bounds, PixelRect workArea, double scaling)
    {
        double outerWidth = (bounds.ClientSize.Width + Chrome.Left + Chrome.Right) * scaling;
        double outerHeight = (bounds.ClientSize.Height + Chrome.Top + Chrome.Bottom) * scaling;
        Assert.True(bounds.Position.X >= workArea.X && bounds.Position.Y >= workArea.Y, $"Position {bounds.Position} is outside {workArea}.");
        Assert.True(bounds.Position.X + outerWidth <= workArea.Right + 0.5, $"Right edge {bounds.Position.X + outerWidth} beyond {workArea.Right}.");
        Assert.True(bounds.Position.Y + outerHeight <= workArea.Bottom + 0.5, $"Bottom edge {bounds.Position.Y + outerHeight} beyond {workArea.Bottom}.");
    }

    private static AuxiliaryWindowBounds Fit(PixelRect workArea, double scaling) =>
        AuxiliaryWindowPlacement.Fit(Default, Min, workArea, scaling, Chrome);
}
