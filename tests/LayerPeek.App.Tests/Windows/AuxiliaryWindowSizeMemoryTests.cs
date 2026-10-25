using Avalonia;
using Avalonia.Controls;

using LayerPeek.App.Windows;

namespace LayerPeek.App.Tests.Windows;

/// <summary>
/// Which size and state of the settings and diagnostics windows is remembered when they close (T-094): only the user's
/// normal size, never a size the app fitted to a small work area, never the maximized size.
/// </summary>
public sealed class AuxiliaryWindowSizeMemoryTests
{
    private static readonly Size Default = AuxiliaryWindowPlacement.DefaultClientSize;

    [Fact]
    public void WindowOnlyOpenedAndClosedRemembersNothingNew()
    {
        AuxiliaryWindowSizeMemory memory = new(AuxiliaryWindowState.Default);
        memory.OnAutomaticSize(Default);
        // Windows reports the size of the shown window without a reason.
        memory.OnResized(Default, byUser: true, normal: true);

        Assert.Equal(AuxiliaryWindowState.Default, memory.Current);
    }

    [Fact]
    public void SizeShrunkOnlyToFitASmallWorkAreaIsNotRemembered()
    {
        AuxiliaryWindowSizeMemory memory = new(new AuxiliaryWindowState(new Size(1400, 900), Maximized: false));
        // Fitted to 1366 × 768 at 125 % before showing, then once more after measuring the real frame.
        memory.OnAutomaticSize(new Size(1092.8, 534.4));
        memory.OnResized(new Size(1092.8, 534.4), byUser: true, normal: true);
        memory.OnAutomaticSize(new Size(1076.8, 526.4));
        memory.OnResized(new Size(1076.8, 526.4), byUser: false, normal: true);
        // The platform rounds the size to whole pixels.
        memory.OnResized(new Size(1076.8, 526.6), byUser: true, normal: true);

        Assert.Equal(new AuxiliaryWindowState(new Size(1400, 900), false), memory.Current);
    }

    [Fact]
    public void SizeChangedByTheUserIsRemembered()
    {
        AuxiliaryWindowSizeMemory memory = new(AuxiliaryWindowState.Default);
        memory.OnAutomaticSize(Default);
        memory.OnResized(new Size(850, 560), byUser: true, normal: true);
        memory.OnResized(new Size(900.04, 610.96), byUser: true, normal: true);

        Assert.Equal(new AuxiliaryWindowState(new Size(900, 611), false), memory.Current);
    }

    [Fact]
    public void SizeChangedByTheUserAfterTheFitIsRememberedEvenWhenSmallerThanBefore()
    {
        AuxiliaryWindowSizeMemory memory = new(new AuxiliaryWindowState(new Size(1400, 900), false));
        memory.OnAutomaticSize(new Size(1092.8, 534.4));
        memory.OnResized(new Size(1000, 500), byUser: true, normal: true);

        Assert.Equal(new AuxiliaryWindowState(new Size(1000, 500), false), memory.Current);
    }

    [Fact]
    public void ResizesTheUserDidNotMakeAreIgnored()
    {
        AuxiliaryWindowSizeMemory memory = new(AuxiliaryWindowState.Default);
        memory.OnAutomaticSize(Default);
        // Requested by the app (layout, application) or a DPI change after moving to another monitor.
        memory.OnResized(new Size(700, 450), byUser: false, normal: true);

        Assert.Equal(AuxiliaryWindowState.Default, memory.Current);
    }

    [Fact]
    public void MaximizingRemembersTheFlagAndKeepsTheLastNormalSize()
    {
        AuxiliaryWindowSizeMemory memory = new(AuxiliaryWindowState.Default);
        memory.OnAutomaticSize(Default);
        memory.OnResized(new Size(900, 600), byUser: true, normal: true);
        // Windows reports the maximized size first, while the window is still normal, and only then the new state.
        memory.OnResized(new Size(1920, 1009), byUser: true, normal: true);
        memory.OnStateChanged(WindowState.Maximized);

        Assert.Equal(new AuxiliaryWindowState(new Size(900, 600), Maximized: true), memory.Current);
    }

    [Fact]
    public void MaximizingWhenTheStateArrivesFirstKeepsTheLastNormalSize()
    {
        AuxiliaryWindowSizeMemory memory = new(new AuxiliaryWindowState(new Size(900, 600), false));
        memory.OnStateChanged(WindowState.Maximized);
        memory.OnResized(new Size(1920, 1009), byUser: true, normal: false);

        Assert.Equal(new AuxiliaryWindowState(new Size(900, 600), Maximized: true), memory.Current);
    }

    [Fact]
    public void MaximizingAWindowNeverResizedKeepsNoSize()
    {
        AuxiliaryWindowSizeMemory memory = new(AuxiliaryWindowState.Default);
        memory.OnAutomaticSize(Default);
        memory.OnResized(new Size(1920, 1009), byUser: true, normal: true);
        memory.OnStateChanged(WindowState.Maximized);

        Assert.Equal(new AuxiliaryWindowState(null, Maximized: true), memory.Current);
    }

    [Fact]
    public void RestoringClearsTheFlagAndKeepsTheNormalSize()
    {
        AuxiliaryWindowSizeMemory memory = new(new AuxiliaryWindowState(new Size(900, 600), Maximized: true));
        memory.OnAutomaticSize(new Size(900, 600));
        // Opened maximized; restoring reports the normal size while the window still counts as maximized.
        memory.OnResized(new Size(1920, 1009), byUser: true, normal: false);
        memory.OnResized(new Size(900, 600), byUser: true, normal: false);
        memory.OnStateChanged(WindowState.Normal);

        Assert.Equal(new AuxiliaryWindowState(new Size(900, 600), Maximized: false), memory.Current);
    }

    [Fact]
    public void ReopeningAMaximizedWindowAndClosingItKeepsEverything()
    {
        AuxiliaryWindowState remembered = new(new Size(1400, 900), Maximized: true);
        AuxiliaryWindowSizeMemory memory = new(remembered);
        memory.OnAutomaticSize(new Size(1092.8, 534.4));
        memory.OnStateChanged(WindowState.Maximized);
        memory.OnResized(new Size(1366, 696), byUser: true, normal: false);

        Assert.Equal(remembered, memory.Current);
        Assert.Same(remembered, memory.Remembered);
    }

    [Fact]
    public void FullScreenAndMinimizedDoNotChangeTheFlag()
    {
        AuxiliaryWindowSizeMemory memory = new(new AuxiliaryWindowState(null, Maximized: true));
        memory.OnStateChanged(WindowState.FullScreen);
        memory.OnStateChanged(WindowState.Minimized);

        Assert.True(memory.Current.Maximized);
    }

    [Fact]
    public void ResizeIntoFullScreenIsNotANormalSize()
    {
        AuxiliaryWindowSizeMemory memory = new(new AuxiliaryWindowState(new Size(900, 600), false));
        memory.OnResized(new Size(1920, 1080), byUser: true, normal: true);
        memory.OnStateChanged(WindowState.FullScreen);

        Assert.Equal(new Size(900, 600), memory.Current.ClientSize);
    }

    [Fact]
    public void UnusableRememberedSizeCountsAsNone()
    {
        AuxiliaryWindowSizeMemory memory = new(new AuxiliaryWindowState(new Size(double.NaN, 600), false));

        Assert.Null(memory.Current.ClientSize);
    }

    [Fact]
    public void UnusableReportedSizeIsIgnored()
    {
        AuxiliaryWindowSizeMemory memory = new(AuxiliaryWindowState.Default);
        memory.OnResized(new Size(0, 0), byUser: true, normal: true);
        memory.OnResized(new Size(double.PositiveInfinity, 600), byUser: true, normal: true);

        Assert.Equal(AuxiliaryWindowState.Default, memory.Current);
    }
}
