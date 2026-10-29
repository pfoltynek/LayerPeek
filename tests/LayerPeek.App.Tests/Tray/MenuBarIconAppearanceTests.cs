using LayerPeek.App.Tray;
using LayerPeek.App.Tray.Icons;

namespace LayerPeek.App.Tests.Tray;

/// <summary>State mapping and progress of the macOS menu bar template icons (T-018, docs/design/macos-tray-icons.md).</summary>
public sealed class MenuBarIconAppearanceTests
{
    private static MenuBarIconAppearance Appearance(TrayStatus status, double? progress = null) =>
        MenuBarIconAppearance.For(TrayIconKey.For(status, progress));

    [Theory]
    [InlineData(TrayStatus.Idle, nameof(MenuBarSymbol.None))]
    [InlineData(TrayStatus.Busy, nameof(MenuBarSymbol.Busy))]
    [InlineData(TrayStatus.Printing, nameof(MenuBarSymbol.Printing))]
    [InlineData(TrayStatus.Paused, nameof(MenuBarSymbol.Paused))]
    [InlineData(TrayStatus.Finished, nameof(MenuBarSymbol.Finished))]
    [InlineData(TrayStatus.Attention, nameof(MenuBarSymbol.Attention))]
    [InlineData(TrayStatus.Error, nameof(MenuBarSymbol.Error))]
    [InlineData(TrayStatus.Offline, nameof(MenuBarSymbol.Slash))]
    public void EveryStatusHasItsSymbol(TrayStatus status, string symbol) =>
        Assert.Equal(symbol, Appearance(status, 50).Symbol.ToString());

    [Fact]
    public void EveryStatusExceptIdleHasAnOwnSymbol()
    {
        // Decisions 2026-10-29: ▶ for printing, × for error and ! for attention, a slash for offline.
        MenuBarSymbol[] symbols = [.. Enum.GetValues<TrayStatus>().Select(status => Appearance(status).Symbol)];

        Assert.Equal(symbols.Length, symbols.Distinct().Count());
        Assert.Equal(MenuBarSymbol.None, Appearance(TrayStatus.Idle).Symbol);
    }

    [Theory]
    [InlineData(TrayStatus.Idle, 0)]
    [InlineData(TrayStatus.Busy, 0)]
    [InlineData(TrayStatus.Attention, 0)]
    [InlineData(TrayStatus.Offline, 0)]
    [InlineData(TrayStatus.Finished, 1)]
    [InlineData(TrayStatus.Error, 1)]
    public void LayersAreEmptyOrFullOutsideAPrint(TrayStatus status, double filled) =>
        Assert.Equal(filled, Appearance(status, 50).FilledFraction);

    [Theory]
    [InlineData(TrayStatus.Printing, 37, 0.35)]
    [InlineData(TrayStatus.Paused, 50, 0.5)]
    [InlineData(TrayStatus.Printing, 100, 1)]
    public void PrintingAndPausedShowTheQuantizedProgress(TrayStatus status, double progress, double filled) =>
        Assert.Equal(filled, Appearance(status, progress).FilledFraction, 9);

    [Fact]
    public void OnlyBlackAndTheDesignAlphasAreUsed()
    {
        foreach (TrayStatus status in Enum.GetValues<TrayStatus>())
        {
            MenuBarIconAppearance appearance = Appearance(status, 50);
            bool offline = status == TrayStatus.Offline;

            Assert.Equal(offline ? 89 : 255, appearance.Ink);
            Assert.Equal(offline ? 51 : 89, appearance.Track);
        }

        // The alphas of the design: 0.35 and 0.2.
        Assert.Equal(MenuBarIconAppearance.Dimmed, (byte)Math.Round(0.35 * 255));
        Assert.Equal(MenuBarIconAppearance.Faint, (byte)Math.Round(0.2 * 255));
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(5, 2, 0, 0)]
    [InlineData(10, 4, 0, 0)]
    [InlineData(35, 16, 0, 0)]
    [InlineData(50, 16, 8, 0)]
    [InlineData(70, 16, 16, 1)]
    [InlineData(95, 16, 16, 13)]
    [InlineData(100, 16, 16, 16)]
    public void ProgressFillsWholePointsBottomLayerFirst(int progress, int bottom, int middle, int top) =>
        // floor(48 pt × progress), as the designer's SVGs (menubar-printing-10/35/70/95, menubar-paused-50).
        Assert.Equal([bottom, middle, top], MenuBarIconRenderer.FilledPoints(progress / 100.0));

    [Fact]
    public void EveryProgressStepIsVisible()
    {
        for (int progress = TrayIconKey.ProgressStep; progress <= 100; progress += TrayIconKey.ProgressStep)
        {
            int before = MenuBarIconRenderer.FilledPoints((progress - TrayIconKey.ProgressStep) / 100.0).Sum();
            int after = MenuBarIconRenderer.FilledPoints(progress / 100.0).Sum();
            Assert.True(after - before >= 2, $"{progress} %: only {after - before} pt more than the previous step.");
        }
    }
}
