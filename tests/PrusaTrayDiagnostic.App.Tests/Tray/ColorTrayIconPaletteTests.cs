using Avalonia.Media;

using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.App.Tray.Icons;

namespace PrusaTrayDiagnostic.App.Tests.Tray;

public sealed class ColorTrayIconPaletteTests
{
    [Theory]
    [InlineData(TrayStatus.Idle)]
    [InlineData(TrayStatus.Busy)]
    [InlineData(TrayStatus.Finished)]
    [InlineData(TrayStatus.Error)]
    [InlineData(TrayStatus.Attention)]
    public void NonPrintingStatusesAreFilledDots(TrayStatus status)
    {
        TrayIconAppearance appearance = ColorTrayIconPalette.GetAppearance(TrayIconKey.For(status, null));

        Assert.Equal(TrayIconShape.Dot, appearance.Shape);
        Assert.Equal(0, appearance.ProgressSweepDegrees);
    }

    [Fact]
    public void OfflineIsCrossedOutRing()
    {
        TrayIconAppearance appearance = ColorTrayIconPalette.GetAppearance(TrayIconKey.For(TrayStatus.Offline, null));

        Assert.Equal(TrayIconShape.CrossedRing, appearance.Shape);
        Assert.Equal(ColorTrayIconPalette.GetAppearance(TrayIconKey.For(TrayStatus.Idle, null)).Fill, appearance.Fill);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(25, 90)]
    [InlineData(50, 180)]
    [InlineData(95, 342)]
    [InlineData(100, 360)]
    public void PrintingAndPausedDrawProgressArc(double progress, double expectedSweep)
    {
        foreach (TrayStatus status in (TrayStatus[])[TrayStatus.Printing, TrayStatus.Paused])
        {
            TrayIconAppearance appearance = ColorTrayIconPalette.GetAppearance(TrayIconKey.For(status, progress));

            Assert.Equal(TrayIconShape.ProgressRing, appearance.Shape);
            Assert.Equal(expectedSweep, appearance.ProgressSweepDegrees, precision: 6);
        }
    }

    [Fact]
    public void AgreedColourFamilies()
    {
        static Color Fill(TrayStatus status) => ColorTrayIconPalette.GetAppearance(TrayIconKey.For(status, 50)).Fill;

        Assert.True(IsGrey(Fill(TrayStatus.Idle)));
        Assert.True(Dominant(Fill(TrayStatus.Printing)) == 'b');
        Assert.True(Dominant(Fill(TrayStatus.Finished)) == 'g');
        Assert.True(Dominant(Fill(TrayStatus.Error)) == 'r');
        Color paused = Fill(TrayStatus.Paused);
        Assert.True(paused.R > 200 && paused.G > 180 && paused.B < 100, $"Paused should be yellow, was {paused}.");
        Color attention = Fill(TrayStatus.Attention);
        Assert.True(attention.R > 200 && attention.G is > 80 and < 180 && attention.B < 80, $"Attention should be orange, was {attention}.");
    }

    [Fact]
    public void ActiveStatusesHaveDistinctColours()
    {
        TrayStatus[] statuses = [TrayStatus.Idle, TrayStatus.Printing, TrayStatus.Paused, TrayStatus.Finished, TrayStatus.Error, TrayStatus.Attention];

        Color[] fills = [.. statuses.Select(status => ColorTrayIconPalette.GetAppearance(TrayIconKey.For(status, null)).Fill)];

        Assert.Equal(fills.Length, fills.Distinct().Count());
    }

    [Fact]
    public void EveryKeyHasOpaqueColours()
    {
        foreach (TrayStatus status in Enum.GetValues<TrayStatus>())
        {
            TrayIconAppearance appearance = ColorTrayIconPalette.GetAppearance(TrayIconKey.For(status, 50));
            Assert.Equal(255, appearance.Fill.A);
            Assert.Equal(255, appearance.Outline.A);
        }
    }

    private static bool IsGrey(Color color) =>
        Math.Abs(color.R - color.G) < 16 && Math.Abs(color.G - color.B) < 16;

    private static char Dominant(Color color) =>
        color.R > color.G && color.R > color.B ? 'r' : color.G > color.B ? 'g' : 'b';
}
