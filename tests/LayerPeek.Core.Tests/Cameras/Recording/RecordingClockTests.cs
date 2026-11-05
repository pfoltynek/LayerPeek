using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;

namespace LayerPeek.Core.Tests.Cameras.Recording;

/// <summary>T-144: RTSP timestamps can be missing, step back or jump; a file's timestamps start at 0 and always increase.</summary>
public sealed class RecordingClockTests
{
    private static readonly CameraTimeBase Rtp = CameraTimeBase.Rtp90kHz;

    [Fact]
    public void StartsAtZeroAndKeepsTheStreamsSpacing()
    {
        RecordingClock clock = new(Rtp);

        Assert.Equal([0L, 4000L, 8100L, 12_000L], Dts(ref clock, 900_000, 904_000, 908_100, 912_000));
        // Up to the end of the last packet: one more step of the last spacing (3900 ticks).
        Assert.Equal(TimeSpan.FromTicks(1_766_667), clock.Elapsed);
    }

    [Fact]
    public void AMissingTimestampContinuesOneFrameLater()
    {
        RecordingClock clock = new(Rtp);

        Assert.Equal([0L, 4000L, 8000L, 12_000L], Dts(ref clock, 100, 4100, null, 12_100));
    }

    [Fact]
    public void ABackwardStepContinuesOneFrameLaterAndReanchors()
    {
        RecordingClock clock = new(Rtp);

        // The camera's clock steps back by 50 000 ticks: the packet follows one frame later, the rest keeps its spacing.
        Assert.Equal([0L, 4000L, 8000L, 12_000L], Dts(ref clock, 100_000, 104_000, 58_000, 62_000));
    }

    [Fact]
    public void ARepeatedTimestampStillIncreases()
    {
        RecordingClock clock = new(Rtp);

        long[] dts = Dts(ref clock, 0, 3600, 3600, 7200);

        Assert.Equal([0L, 3600L, 7200L, 10_800L], dts);
    }

    [Fact]
    public void AJumpIsADiscontinuityNotAGap()
    {
        RecordingClock clock = new(Rtp);

        // An hour forward (a reset camera clock): one frame later, not an hour of nothing.
        Assert.Equal([0L, 4000L, 8000L, 12_000L], Dts(ref clock, 0, 4000, 4000 + (3600L * 90_000), 4000 + (3600L * 90_000) + 4000));
    }

    [Fact]
    public void AStepUpToTheLimitIsARealGap()
    {
        RecordingClock clock = new(Rtp);

        Assert.Equal([0L, 5L * 90_000], Dts(ref clock, 0, 5L * 90_000));
    }

    [Fact]
    public void ARealGapDoesNotBecomeTheFrameDuration()
    {
        RecordingClock clock = new(Rtp);

        // A 5 s gap (a stalled camera) is kept, but a missing timestamp after it continues one frame (4000), not 5 s.
        Assert.Equal([0L, 4000L, 454_000L, 458_000L], Dts(ref clock, 0, 4000, 454_000, null));
        Assert.Equal(TimeSpan.FromTicks(Rescale(462_000)), clock.Elapsed);

        static long Rescale(long ticks) => RecordingClock.Rescale(ticks, Rtp, new CameraTimeBase(1, (int)TimeSpan.TicksPerSecond));
    }

    [Fact]
    public void ALongPacketDurationIsNotTakenAsTheFrameDuration()
    {
        RecordingClock clock = new(Rtp);
        clock.Next(Packet(0, 0, duration: 2L * 90_000), out _, out _, out _);

        clock.Next(Packet(null, null), out _, out long dts, out _);

        // The assumed 25 fps (3600 ticks), not the 2 s the packet claimed.
        Assert.Equal(3600L, dts);
    }

    [Fact]
    public void ThePacketDurationIsUsedForAMissingTimestamp()
    {
        RecordingClock clock = new(Rtp);
        clock.Next(Packet(0, 0, duration: 3000), out _, out _, out _);

        clock.Next(Packet(null, null, duration: 3000), out long pts, out long dts, out long duration);

        Assert.Equal((3000L, 3000L, 3000L), (pts, dts, duration));
    }

    [Fact]
    public void PresentationOffsetsAreKeptAndNeverBelowTheDecodingTime()
    {
        RecordingClock clock = new(Rtp);
        clock.Next(Packet(dts: 1000, pts: 1000), out _, out _, out _);

        clock.Next(Packet(dts: 4600, pts: 11_800), out long withOffset, out long dts, out _);
        clock.Next(Packet(dts: 8200, pts: 100), out long belowDts, out long dts2, out _);

        Assert.Equal((10_800L, 3600L), (withOffset, dts));
        Assert.Equal(dts2, belowDts);
    }

    [Fact]
    public void OnlyAPresentationTimestampIsEnough()
    {
        RecordingClock clock = new(Rtp);

        Assert.Equal([0L, 3000L], new[] { Next(ref clock, Packet(dts: null, pts: 500)), Next(ref clock, Packet(dts: null, pts: 3500)) });
    }

    [Fact]
    public void PacketsInAnotherTimeBaseAreConverted()
    {
        RecordingClock clock = new(Rtp);
        CameraTimeBase milliseconds = new(1, 1000);

        long first = Next(ref clock, new CameraPacket(default, 1000, 1000, 0, true, milliseconds));
        long second = Next(ref clock, new CameraPacket(default, 1040, 1040, 0, false, milliseconds));

        Assert.Equal((0L, 3600L), (first, second));
    }

    [Theory]
    [InlineData(1, 1, 90_000, 90_000)]
    [InlineData(45_000, 90_000, 1000, 500)]
    [InlineData(1, 90_000, 1_200_000, 13)]
    [InlineData(-3, 2, 1, -2)]
    [InlineData(1, 3, 2, 1)]
    public void RescaleRoundsToNearest(long value, int fromDenominator, int toDenominator, long expected)
    {
        Assert.Equal(expected, RecordingClock.Rescale(value, new CameraTimeBase(1, fromDenominator), new CameraTimeBase(1, toDenominator)));
    }

    [Fact]
    public void AtAFrameRatePacketNIsAtNFramesWhateverItsTimestamps()
    {
        // T-158: a timelapse at 30 fps in the 90 kHz RTP clock; the camera's timestamps are ~2.2 s apart, missing or backwards.
        RecordingClock clock = RecordingClock.AtFrameRate(Rtp, 30);
        Assert.Equal(TimeSpan.Zero, clock.Elapsed);

        (long Pts, long Dts, long Duration)[] frames = Frames(ref clock, 900_000, 1_098_000, null, 50_000);

        Assert.Equal([(0L, 0L, 3000L), (3000L, 3000L, 3000L), (6000L, 6000L, 3000L), (9000L, 9000L, 3000L)], frames);
        Assert.Equal(TimeSpan.FromSeconds(4.0 / 30), clock.Elapsed);
    }

    [Fact]
    public void AtAFrameRateTheFramesAreRoundedWithoutDrift()
    {
        RecordingClock clock = RecordingClock.AtFrameRate(new CameraTimeBase(1, 1000), 30);

        (long Pts, long Dts, long Duration)[] frames = Frames(ref clock, 0, 0, 0, 0);

        Assert.Equal([0L, 33L, 67L, 100L], frames.Select(static f => f.Dts));
        Assert.Equal([33L, 34L, 33L, 33L], frames.Select(static f => f.Duration));
        Assert.Equal(TimeSpan.FromMilliseconds(133), clock.Elapsed);
    }

    [Fact]
    public void AtAFrameRateATooCoarseTimeBaseStillIncreases()
    {
        RecordingClock clock = RecordingClock.AtFrameRate(new CameraTimeBase(1, 10), 30);

        (long Pts, long Dts, long Duration)[] frames = Frames(ref clock, 0, 0, 0, 0);

        Assert.Equal([0L, 1L, 2L, 3L], frames.Select(static f => f.Dts));
        Assert.All(frames, static f => Assert.True(f.Duration >= 1));
    }

    [Fact]
    public void AFrameRateMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RecordingClock.AtFrameRate(Rtp, 0));
    }

    private static (long Pts, long Dts, long Duration)[] Frames(ref RecordingClock clock, params long?[] values)
    {
        (long, long, long)[] result = new (long, long, long)[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            clock.Next(Packet(values[i], values[i], duration: 4500), out long pts, out long dts, out long duration);
            result[i] = (pts, dts, duration);
        }

        return result;
    }

    private static long[] Dts(ref RecordingClock clock, params long?[] values)
    {
        long[] result = new long[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            result[i] = Next(ref clock, Packet(values[i], values[i]));
        }

        return result;
    }

    private static long Next(ref RecordingClock clock, CameraPacket packet)
    {
        clock.Next(packet, out long pts, out long dts, out _);
        Assert.True(pts >= dts);
        return dts;
    }

    private static CameraPacket Packet(long? dts, long? pts, long duration = 0) => new(default, pts, dts, duration, false, Rtp);
}
