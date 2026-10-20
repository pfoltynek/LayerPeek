using Avalonia;

using PrusaTrayDiagnostic.App.Windows;

namespace PrusaTrayDiagnostic.App.Tests.Windows;

/// <summary>Default size of the settings and diagnostics windows and how it is fitted into the work area of the screen (T-033, T-041).</summary>
public sealed class AuxiliaryWindowPlacementTests
{
    /// <summary>Title bar assumed by the design (Windows 11 at 100 %).</summary>
    private const double TitleBar = 32;

    private static readonly Thickness Chrome = new(0, TitleBar, 0, 0);

    [Fact]
    public void DefaultSizeWithTheTitleBarFitsTheSmallestSupportedScreen()
    {
        // 800 × 600 at 100 % leaves about 800 × 560 for windows; the window must take at most 780 × 540 of it.
        Size client = AuxiliaryWindowPlacement.DefaultClientSize;

        Assert.True(client.Width <= 780, $"Width {client.Width}");
        Assert.True(client.Height + TitleBar <= 540, $"Height {client.Height} + {TitleBar}");
        Assert.Equal(new Size(780, 508), client);
        Assert.Equal(new Size(640, 400), AuxiliaryWindowPlacement.MinClientSize);
        Assert.Equal(TitleBar, AuxiliaryWindowPlacement.DefaultChrome.Top);
    }

    [Fact]
    public void DefaultSizeIsKeptAndCentredOnTheSmallestSupportedScreen()
    {
        AuxiliaryWindowBounds bounds = Fit(new PixelRect(0, 0, 800, 560), scaling: 1.0);

        Assert.Equal(new Size(780, 508), bounds.ClientSize);
        Assert.Equal(new PixelPoint(10, 10), bounds.Position);
    }

    [Fact]
    public void LargeScaledScreenKeepsTheDefaultSizeAndCentresInPixels()
    {
        // 1920 × 1080 at 150 % with a 48 px taskbar: 1280 × 688 DIP.
        AuxiliaryWindowBounds bounds = Fit(new PixelRect(0, 0, 1920, 1032), scaling: 1.5);

        Assert.Equal(new Size(780, 508), bounds.ClientSize);
        // Outer window 780 × 540 DIP = 1170 × 810 px.
        Assert.Equal(new PixelPoint(375, 111), bounds.Position);
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
        AuxiliaryWindowBounds bounds = AuxiliaryWindowPlacement.Fit(
            AuxiliaryWindowPlacement.DefaultClientSize,
            AuxiliaryWindowPlacement.MinClientSize,
            new PixelRect(0, 0, 790, 560),
            scaling: 1.0,
            new Thickness(8, 31, 8, 8));

        Assert.Equal(new Size(774, 508), bounds.ClientSize);
        Assert.Equal(new PixelPoint(0, 6), bounds.Position);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(double.NaN)]
    [InlineData(-1.0)]
    public void InvalidScalingCountsAsOne(double scaling)
    {
        AuxiliaryWindowBounds bounds = Fit(new PixelRect(0, 0, 800, 560), scaling);

        Assert.Equal(new Size(780, 508), bounds.ClientSize);
        Assert.Equal(new PixelPoint(10, 10), bounds.Position);
    }

    [Fact]
    public void FrameMeasuredFromTheWindowSplitsTheBordersEvenly()
    {
        Thickness chrome = AuxiliaryWindowPlacement.ChromeOf(new Size(796, 547), new Size(780, 508));

        Assert.Equal(new Thickness(8, 31, 8, 8), chrome);
    }

    [Fact]
    public void UnknownFrameFallsBackToTheAssumedTitleBar()
    {
        Assert.Equal(AuxiliaryWindowPlacement.DefaultChrome, AuxiliaryWindowPlacement.ChromeOf(null, new Size(780, 508)));
        // A frame not larger than the client area (headless, not shown yet) tells nothing either.
        Assert.Equal(AuxiliaryWindowPlacement.DefaultChrome, AuxiliaryWindowPlacement.ChromeOf(new Size(780, 508), new Size(780, 508)));
    }

    // ---- Before the window is shown (no measured frame yet), and the correction after it opened ----

    [Fact]
    public void BeforeShowingTheWindowIsFittedWithTheAssumedTitleBar()
    {
        // 700 × 448 DIP of work area: the window must already be small enough when it appears (no visible jump).
        Size client = AuxiliaryWindowPlacement.SizeBeforeShowing(
            AuxiliaryWindowPlacement.DefaultClientSize,
            AuxiliaryWindowPlacement.MinClientSize,
            new PixelRect(0, 0, 875, 560),
            scaling: 1.25);

        Assert.Equal(new Size(700, 416), client);
    }

    [Fact]
    public void BeforeShowingOnALargeScreenTheDesignedSizeIsKept()
    {
        Size client = AuxiliaryWindowPlacement.SizeBeforeShowing(
            AuxiliaryWindowPlacement.DefaultClientSize,
            AuxiliaryWindowPlacement.MinClientSize,
            new PixelRect(0, 0, 1920, 1032),
            scaling: 1.5);

        Assert.Equal(AuxiliaryWindowPlacement.DefaultClientSize, client);
    }

    [Fact]
    public void BeforeShowingWithoutAScreenTheDesignedSizeIsKept()
    {
        Size client = AuxiliaryWindowPlacement.SizeBeforeShowing(AuxiliaryWindowPlacement.DefaultClientSize, AuxiliaryWindowPlacement.MinClientSize, workArea: null, scaling: 1.0);

        Assert.Equal(AuxiliaryWindowPlacement.DefaultClientSize, client);
    }

    [Fact]
    public void AfterOpeningOnlyALargerMeasuredFrameCorrectsTheWindow()
    {
        Size shown = new(780, 508);
        PixelRect workArea = new(0, 0, 800, 560);

        // Windows 11: the frame matches the assumption, nothing moves.
        Assert.Null(AuxiliaryWindowPlacement.CorrectionAfterOpening(shown, AuxiliaryWindowPlacement.DefaultClientSize, AuxiliaryWindowPlacement.MinClientSize, workArea, 1.0, new Thickness(0, 32, 0, 0)));
        // A slightly thinner frame would allow a larger window, but the window does not grow (that would be a jump too).
        Assert.Null(AuxiliaryWindowPlacement.CorrectionAfterOpening(shown, AuxiliaryWindowPlacement.DefaultClientSize, AuxiliaryWindowPlacement.MinClientSize, workArea, 1.0, new Thickness(0, 30, 0, 0)));

        // A thicker frame than assumed (borders of 8): the window would not fit, so it shrinks and is centred again.
        AuxiliaryWindowBounds? corrected = AuxiliaryWindowPlacement.CorrectionAfterOpening(
            shown, AuxiliaryWindowPlacement.DefaultClientSize, AuxiliaryWindowPlacement.MinClientSize, workArea, 1.0, new Thickness(12, 31, 12, 12));
        Assert.Equal(new AuxiliaryWindowBounds(new Size(776, 508), new PixelPoint(0, 4)), corrected);
    }

    private static AuxiliaryWindowBounds Fit(PixelRect workArea, double scaling) =>
        AuxiliaryWindowPlacement.Fit(AuxiliaryWindowPlacement.DefaultClientSize, AuxiliaryWindowPlacement.MinClientSize, workArea, scaling, Chrome);
}
