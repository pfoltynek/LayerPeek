using LayerPeek.App.Cameras;

namespace LayerPeek.App.Tests.Cameras;

/// <summary>
/// T-251: the frame rate the mini preview really shows, measured over a sliding window of the last 2 s from the instants the
/// frames were shown (timestamps of a controlled clock): exact for a steady stream whatever the phase of the measurement,
/// falling smoothly once the frames stop, zero once the window holds no frame.
/// </summary>
public sealed class FrameRateMeterTests
{
    // Timestamps in ticks of 100 ns, like TimeProvider.System on most platforms.
    private const long Frequency = TimeSpan.TicksPerSecond;

    private static readonly TimeSpan Window = TimeSpan.FromSeconds(2);

    private static long At(double seconds) => (long)Math.Round(seconds * Frequency);

    /// <summary>Frames at <paramref name="fps"/> from 0 s for <paramref name="seconds"/>; returns the instant of the last one.</summary>
    private static long Feed(FrameRateMeter meter, double fps, double seconds, double start = 0)
    {
        long last = 0;
        for (int i = 0; i < (int)Math.Round(fps * seconds); i++)
        {
            last = At(start + (i / fps));
            meter.Record(last);
        }

        return last;
    }

    [Fact]
    public void NothingShownIsZero()
    {
        FrameRateMeter meter = new(Window, Frequency);

        Assert.Equal(0, meter.RateAt(At(5)));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(5.0)]
    [InlineData(10.0)]
    [InlineData(15.0)]
    [InlineData(24.0)]
    [InlineData(25.0)]
    [InlineData(30.0)]
    [InlineData(60.0)]
    public void ASteadyStreamIsMeasuredExactlyWhateverThePhaseOfTheMeasurement(double fps)
    {
        FrameRateMeter meter = new(Window, Frequency);
        long last = Feed(meter, fps, seconds: 5);

        // Right at the last frame, half an interval and almost a whole interval after it.
        foreach (double fraction in new[] { 0.0, 0.5, 0.99 })
        {
            Assert.Equal(fps, meter.RateAt(last + At(fraction / fps)), precision: 3);
        }
    }

    [Fact]
    public void AJitteryStreamIsMeasuredOnAverage()
    {
        FrameRateMeter meter = new(Window, Frequency);
        long last = 0;
        // 15 fps whose frames come up to 20 ms early or late.
        for (int i = 0; i < 75; i++)
        {
            last = At((i / 15.0) + ((i % 3) - 1) * 0.02);
            meter.Record(last);
        }

        Assert.InRange(meter.RateAt(last), 14.5, 15.5);
    }

    [Fact]
    public void OnlyTheLastTwoSecondsCount()
    {
        FrameRateMeter meter = new(Window, Frequency);
        Feed(meter, fps: 30, seconds: 3);
        long last = Feed(meter, fps: 10, seconds: 3, start: 3);

        Assert.Equal(10, meter.RateAt(last), precision: 6);
    }

    [Fact]
    public void StoppedFramesLetTheRateFallUntilZero()
    {
        FrameRateMeter meter = new(Window, Frequency);
        long last = Feed(meter, fps: 10, seconds: 3);

        double previous = meter.RateAt(last);
        foreach (double after in new[] { 0.3, 0.6, 1.0, 1.5 })
        {
            double rate = meter.RateAt(last + At(after));
            Assert.True(rate < previous, $"{after} s after the last frame: {rate} fps, before {previous} fps");
            previous = rate;
        }

        // The window holds no frame any more.
        Assert.Equal(0, meter.RateAt(last + At(2.01)));
    }

    [Fact]
    public void ASingleFrameInTheWindowIsOneFramePerWindow()
    {
        FrameRateMeter meter = new(Window, Frequency);
        meter.Record(At(1));

        Assert.Equal(0.5, meter.RateAt(At(1.5)), precision: 6);
    }

    [Fact]
    public void MoreFramesThanTheBufferHoldsStillGiveTheRate()
    {
        // 2 s at 240 fps are 480 frames, more than the meter keeps: the newest ones measure it.
        FrameRateMeter meter = new(Window, Frequency);
        long last = Feed(meter, fps: 240, seconds: 3);

        Assert.Equal(240, meter.RateAt(last), precision: 6);
    }

    [Fact]
    public void ResetForgetsEveryFrame()
    {
        FrameRateMeter meter = new(Window, Frequency);
        long last = Feed(meter, fps: 15, seconds: 2);

        meter.Reset();

        Assert.Equal(0, meter.RateAt(last));
        meter.Record(last + At(0.1));
        meter.Record(last + At(0.2));
        Assert.Equal(10, meter.RateAt(last + At(0.2)), precision: 6);
    }

    [Fact]
    public void AClockThatGoesBackDoesNotBreakTheMeasurement()
    {
        FrameRateMeter meter = new(Window, Frequency);
        long last = Feed(meter, fps: 10, seconds: 2);

        // A frame stamped before the last one and a measurement before the last frame: never negative, never NaN.
        meter.Record(last - At(1));
        double rate = meter.RateAt(last - At(0.5));
        Assert.False(double.IsNaN(rate));
        Assert.True(rate >= 0);
    }

    [Fact]
    public void RecordingDoesNotAllocate()
    {
        FrameRateMeter meter = new(Window, Frequency);
        Feed(meter, fps: 30, seconds: 1);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            meter.Record(At(1 + (i / 30.0)));
        }

        _ = meter.RateAt(At(40));
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    [Fact]
    public void AnInvalidWindowOrFrequencyIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameRateMeter(TimeSpan.Zero, Frequency));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameRateMeter(Window, 0));
    }
}
