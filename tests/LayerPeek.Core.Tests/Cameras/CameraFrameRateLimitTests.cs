using Microsoft.Extensions.Time.Testing;

using LayerPeek.Core.Cameras;

namespace LayerPeek.Core.Tests.Cameras;

/// <summary>
/// T-185: the pace of a subscription with <see cref="CameraFrameSubscriptionOptions.MaxFrameRate"/> (the mini preview),
/// driven directly with the timestamps of a fake clock: at most the rate on average whatever the camera's own rate, no
/// burst, and a new rate (or none) applies to the next frame.
/// </summary>
public sealed class CameraFrameRateLimitTests
{
    private static readonly CameraFrame Frame = new(4, 2, 16, new byte[32]);

    private readonly FakeTimeProvider _time = new();

    private CameraFrameSubscription Subscribe(int? rate, List<long> taken) =>
        new(hub: null, "mk4", new CameraFrameSubscriptionOptions
        {
            MaxFrameRate = rate,
            FrameReceived = (in CameraFrame _) => taken.Add(_time.GetTimestamp()),
        }, _time);

    /// <summary>Publishes <paramref name="count"/> frames of a camera with <paramref name="cameraFps"/>, advancing the clock.</summary>
    private int Run(CameraFrameSubscription subscription, double cameraFps, int count)
    {
        TimeSpan period = TimeSpan.FromSeconds(1 / cameraFps);
        int taken = 0;
        for (int i = 0; i < count; i++)
        {
            taken += subscription.Publish(Frame, _time.GetTimestamp()) ? 1 : 0;
            _time.Advance(period);
        }

        return taken;
    }

    [Theory]
    // The profiles (5 and 10 fps) and custom limits, below, close to and above the camera's own rate (22 and 25 fps).
    [InlineData(25.0, 10)]
    [InlineData(25.0, 5)]
    [InlineData(22.0, 10)]
    [InlineData(22.0, 5)]
    [InlineData(25.0, 1)]
    [InlineData(25.0, 12)]
    [InlineData(22.0, 20)]
    [InlineData(30.0, 30)]
    public void AtMostTheRateIsTakenOnAverageWhateverTheCamerasRate(double cameraFps, int rate)
    {
        List<long> taken = [];
        using CameraFrameSubscription subscription = Subscribe(rate, taken);

        // 10 s of the camera.
        Run(subscription, cameraFps, (int)Math.Round(cameraFps * 10));

        // The rate on average; the first frame of the grid may make one more.
        Assert.InRange(taken.Count, (rate * 10) - 1, (rate * 10) + 1);
        // No burst: any second has at most one frame more than the rate (a frame may be a quarter interval early).
        for (int i = 0; i < taken.Count; i++)
        {
            int inSecond = taken.Count(at => at >= taken[i] && at - taken[i] < _time.TimestampFrequency);
            Assert.True(inSecond <= rate + 1, $"{inSecond} frames in the second from frame {i}.");
        }
    }

    [Fact]
    public void ARateAboveTheCamerasTakesEveryFrame()
    {
        List<long> taken = [];
        using CameraFrameSubscription subscription = Subscribe(30, taken);

        Assert.Equal(220, Run(subscription, 22, 220));
    }

    [Fact]
    public void ACameraAtTheRateWithJitterKeepsItsRate()
    {
        // 10 fps from a 10 fps camera whose frames come a few milliseconds early or late: never halved.
        List<long> taken = [];
        using CameraFrameSubscription subscription = Subscribe(10, taken);
        int[] jitter = [0, -8, 6, -12, 3, 9, -5, 0, -15, 11];
        long start = _time.GetTimestamp();
        for (int i = 0; i < 100; i++)
        {
            long at = start + ((i * 100) + jitter[i % jitter.Length]) * _time.TimestampFrequency / 1000;
            subscription.Publish(Frame, at);
        }

        Assert.Equal(100, taken.Count);
    }

    [Fact]
    public void WithoutALimitEveryFrameIsTaken()
    {
        List<long> taken = [];
        using CameraFrameSubscription subscription = Subscribe(null, taken);

        Assert.Equal(50, Run(subscription, 25, 50));
        Assert.Null(subscription.MaxFrameRate);
    }

    [Fact]
    public void ANewLimitAppliesToTheNextFrame()
    {
        List<long> taken = [];
        using CameraFrameSubscription subscription = Subscribe(null, taken);
        Assert.Equal(25, Run(subscription, 25, 25));

        // To the mini preview: the next frame right away, then the pace.
        subscription.SetMaxFrameRate(5);
        Assert.Equal(5, subscription.MaxFrameRate);
        Assert.True(subscription.Publish(Frame, _time.GetTimestamp()));
        _time.Advance(TimeSpan.FromMilliseconds(40));
        Assert.Equal(5, Run(subscription, 25, 25));

        // Back to the normal window: every frame at once.
        subscription.SetMaxFrameRate(null);
        Assert.Equal(25, Run(subscription, 25, 25));

        // Another limit while limited (the setting changed): the next frame right away.
        subscription.SetMaxFrameRate(2);
        Assert.True(subscription.Publish(Frame, _time.GetTimestamp()));
        subscription.SetMaxFrameRate(10);
        Assert.True(subscription.Publish(Frame, _time.GetTimestamp()));
        Assert.False(subscription.Publish(Frame, _time.GetTimestamp()));
    }

    [Fact]
    public void AFrameAfterAStallStartsANewPace()
    {
        List<long> taken = [];
        using CameraFrameSubscription subscription = Subscribe(10, taken);
        Run(subscription, 25, 25);
        int before = taken.Count;

        // Nothing for 3 s (a reconnect), then the camera again: no catching up with frames in a burst.
        _time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(10, Run(subscription, 25, 25));
        Assert.Equal(10, before);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ARateBelowOneIsRejected(int rate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraFrameSubscriptionOptions { MaxFrameRate = rate });
        using CameraFrameSubscription subscription = Subscribe(null, []);
        Assert.Throws<ArgumentOutOfRangeException>(() => subscription.SetMaxFrameRate(rate));
    }

    [Fact]
    public void TheIntervalOfDetectionStaysAsItWasBesideALimit()
    {
        // A subscriber with both (not used by the app, but the two must not loosen each other): the stricter one wins.
        List<long> taken = [];
        using CameraFrameSubscription subscription = new(hub: null, "mk4", new CameraFrameSubscriptionOptions
        {
            MaxFrameRate = 10,
            MinFrameInterval = TimeSpan.FromSeconds(1),
            FrameReceived = (in CameraFrame _) => taken.Add(_time.GetTimestamp()),
        }, _time);

        Run(subscription, 25, 250);

        Assert.InRange(taken.Count, 9, 10);
    }
}
