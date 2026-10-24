using LayerPeek.App.Tray;

namespace LayerPeek.App.Tests.Tray;

public sealed class TrayIconKeyTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(4.99, 0)]
    [InlineData(5, 5)]
    [InlineData(42.7, 40)]
    [InlineData(99.9, 95)]
    [InlineData(100, 100)]
    [InlineData(-3, 0)]
    [InlineData(250, 100)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 100)]
    [InlineData(double.NegativeInfinity, 0)]
    public void PrintingAndPausedProgressIsRoundedDownToStep(double progress, int expected)
    {
        foreach (TrayStatus status in (TrayStatus[])[TrayStatus.Printing, TrayStatus.Paused])
        {
            TrayIconKey key = TrayIconKey.For(status, progress);

            Assert.Equal(status, key.Status);
            Assert.Equal(expected, key.Progress);
        }
    }

    [Theory]
    [InlineData(TrayStatus.Printing)]
    [InlineData(TrayStatus.Paused)]
    public void UnknownProgressShowsEmptyProgress(TrayStatus status)
    {
        Assert.Equal(0, TrayIconKey.For(status, null).Progress);
    }

    [Fact]
    public void PausedAndPrintingWithSameProgressAreDifferentIcons()
    {
        Assert.NotEqual(TrayIconKey.For(TrayStatus.Printing, 50), TrayIconKey.For(TrayStatus.Paused, 50));
    }

    [Fact]
    public void ProgressWithinOneStepGivesEqualKeys()
    {
        // The rendered icon is cached per key, so polls inside one step must not produce a new icon.
        Assert.Equal(TrayIconKey.For(TrayStatus.Printing, 41), TrayIconKey.For(TrayStatus.Printing, 44.9));
        Assert.NotEqual(TrayIconKey.For(TrayStatus.Printing, 44.9), TrayIconKey.For(TrayStatus.Printing, 45));
    }

    [Theory]
    [InlineData(TrayStatus.Idle)]
    [InlineData(TrayStatus.Busy)]
    [InlineData(TrayStatus.Finished)]
    [InlineData(TrayStatus.Error)]
    [InlineData(TrayStatus.Attention)]
    [InlineData(TrayStatus.Offline)]
    public void OtherStatusesIgnoreProgress(TrayStatus status)
    {
        Assert.Equal(TrayIconKey.For(status, null), TrayIconKey.For(status, 63));
        Assert.Equal(0, TrayIconKey.For(status, 63).Progress);
    }

    [Fact]
    public void NumberOfDistinctKeysIsBounded()
    {
        HashSet<TrayIconKey> keys = [];
        foreach (TrayStatus status in Enum.GetValues<TrayStatus>())
        {
            for (double progress = 0; progress <= 100; progress += 0.25)
            {
                keys.Add(TrayIconKey.For(status, progress));
            }
        }

        // One icon per status without progress, one per step for printing and paused.
        int expected = (Enum.GetValues<TrayStatus>().Length - 2) + (2 * ((100 / TrayIconKey.ProgressStep) + 1));
        Assert.Equal(expected, keys.Count);
    }
}
