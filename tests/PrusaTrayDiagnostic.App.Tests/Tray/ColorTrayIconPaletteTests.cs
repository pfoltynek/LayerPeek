using Avalonia.Media;

using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.App.Tray.Icons;

namespace PrusaTrayDiagnostic.App.Tests.Tray;

/// <summary>State mapping of design B "Layers" (T-025).</summary>
public sealed class ColorTrayIconPaletteTests
{
    private static TrayIconAppearance Appearance(TrayStatus status, double? progress = null) =>
        ColorTrayIconPalette.GetAppearance(TrayIconKey.For(status, progress));

    [Fact]
    public void BrandColoursAreTheApprovedOnes()
    {
        Assert.Equal(Color.Parse("#FD5000"), ColorTrayIconPalette.Orange);
        Assert.Equal(Color.Parse("#1E1F24"), ColorTrayIconPalette.Dark);
    }

    [Theory]
    [InlineData(TrayStatus.Idle)]
    [InlineData(TrayStatus.Busy)]
    [InlineData(TrayStatus.Paused)]
    [InlineData(TrayStatus.Finished)]
    [InlineData(TrayStatus.Attention)]
    [InlineData(TrayStatus.Error)]
    public void HotendIsOrangeWhileThereIsDataAndNotPrinting(TrayStatus status) =>
        Assert.Equal(ColorTrayIconPalette.Orange, Appearance(status).Hotend);

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    public void HotendIsPrintingBlueWhilePrinting(double progress)
    {
        TrayIconAppearance printing = Appearance(TrayStatus.Printing, progress);

        // Decided 2026-10-19: distinguishes printing at 0-4 % (no filled pixel yet) from idle without a badge.
        Assert.Equal(Color.Parse("#1E88E5"), printing.Hotend);
        Assert.Equal(printing.Fill, printing.Hotend);
        Assert.NotEqual(Appearance(TrayStatus.Idle), printing with { FilledFraction = 0 });
    }

    [Fact]
    public void OfflineIsGreyWithFainterLayersAndMinusBadge()
    {
        TrayIconAppearance offline = Appearance(TrayStatus.Offline);

        Assert.Equal(ColorTrayIconPalette.Grey, offline.Hotend);
        Assert.True(offline.Track.A < Appearance(TrayStatus.Idle).Track.A);
        Assert.Equal(0, offline.FilledFraction);
        Assert.Equal(TrayBadge.Offline, offline.Badge);
    }

    [Theory]
    [InlineData(TrayStatus.Idle, "None")]
    [InlineData(TrayStatus.Printing, "None")]
    [InlineData(TrayStatus.Busy, "Busy")]
    [InlineData(TrayStatus.Paused, "Paused")]
    [InlineData(TrayStatus.Finished, "Finished")]
    [InlineData(TrayStatus.Attention, "Attention")]
    [InlineData(TrayStatus.Error, "Error")]
    [InlineData(TrayStatus.Offline, "Offline")]
    public void EachStatusHasItsBadge(TrayStatus status, string badge) =>
        Assert.Equal(badge, Appearance(status, 50).Badge.ToString());

    [Theory]
    [InlineData(0, 0)]
    [InlineData(25, 0.25)]
    [InlineData(50, 0.5)]
    [InlineData(97, 0.95)]
    [InlineData(100, 1)]
    public void PrintingAndPausedFillTheLayersWithTheProgress(double progress, double expected)
    {
        foreach (TrayStatus status in (TrayStatus[])[TrayStatus.Printing, TrayStatus.Paused])
        {
            Assert.Equal(expected, Appearance(status, progress).FilledFraction, precision: 6);
        }
    }

    [Theory]
    [InlineData(TrayStatus.Finished)]
    [InlineData(TrayStatus.Error)]
    public void FinishedAndErrorFillEveryLayer(TrayStatus status) =>
        Assert.Equal(1, Appearance(status).FilledFraction);

    [Theory]
    [InlineData(TrayStatus.Idle)]
    [InlineData(TrayStatus.Busy)]
    [InlineData(TrayStatus.Attention)]
    public void OtherStatusesKeepGreyLayers(TrayStatus status)
    {
        TrayIconAppearance appearance = Appearance(status, 50);

        Assert.Equal(0, appearance.FilledFraction);
        Assert.Equal(ColorTrayIconPalette.Track, appearance.Track);
    }

    [Fact]
    public void AgreedColourFamilies()
    {
        Assert.Equal('b', Dominant(Appearance(TrayStatus.Printing, 50).Fill));
        Assert.Equal('g', Dominant(Appearance(TrayStatus.Finished).Fill));
        Assert.Equal('r', Dominant(Appearance(TrayStatus.Error).Fill));
        Color paused = Appearance(TrayStatus.Paused, 50).Fill;
        Assert.True(paused.R > 200 && paused.G > 180 && paused.B < 100, $"Paused should be yellow, was {paused}.");
        Assert.Equal('b', Dominant(Appearance(TrayStatus.Busy).BadgeColors.Fill));

        // Attention keeps the amber of T-007, distinct from the Prusa orange of the hotend.
        Color attention = Appearance(TrayStatus.Attention).BadgeColors.Fill;
        Assert.Equal(Color.Parse("#FB8C00"), attention);
        Assert.NotEqual(ColorTrayIconPalette.Orange, attention);
        Assert.True(IsGrey(Appearance(TrayStatus.Offline).BadgeColors.Fill));
        Assert.True(IsGrey(ColorTrayIconPalette.Track));
    }

    [Fact]
    public void BadgesOfDifferentStatusesDiffer()
    {
        TrayStatus[] statuses = [TrayStatus.Busy, TrayStatus.Paused, TrayStatus.Finished, TrayStatus.Attention, TrayStatus.Error, TrayStatus.Offline];

        Color[] fills = [.. statuses.Select(status => Appearance(status, 50).BadgeColors.Fill)];

        Assert.Equal(fills.Length, fills.Distinct().Count());
    }

    [Fact]
    public void ColoursExceptTheTrackAreOpaque()
    {
        foreach (TrayStatus status in Enum.GetValues<TrayStatus>())
        {
            TrayIconAppearance appearance = Appearance(status, 50);
            Assert.Equal(255, appearance.Hotend.A);
            if (appearance.FilledFraction > 0)
            {
                Assert.Equal(255, appearance.Fill.A);
            }

            if (appearance.Badge != TrayBadge.None)
            {
                Assert.Equal(255, appearance.BadgeColors.Fill.A);
                Assert.Equal(255, appearance.BadgeColors.Outline.A);
                Assert.Equal(255, appearance.BadgeColors.Symbol.A);
            }
        }
    }

    private static bool IsGrey(Color color) =>
        Math.Abs(color.R - color.G) < 16 && Math.Abs(color.G - color.B) < 16;

    private static char Dominant(Color color) =>
        color.R > color.G && color.R > color.B ? 'r' : color.G > color.B ? 'g' : 'b';
}
