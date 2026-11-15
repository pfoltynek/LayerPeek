using Microsoft.Extensions.Time.Testing;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Tests.Monitoring;

namespace LayerPeek.Core.Tests.Cameras;

/// <summary>
/// T-188 (user decision 2026-11-11): a camera window lease (<see cref="CameraFrameSubscriptionOptions.ShortensReconnectWait"/>)
/// that joins a connection waiting between reconnect attempts makes it retry at once; print failure detection, recording
/// and the other consumers never shorten the wait. Fake feeds, fake time (nothing advances unless a test says so).
/// </summary>
public sealed class CameraStreamHubReconnectTests : IAsyncDisposable
{
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private static readonly CameraFeedOptions Options = new()
    {
        ConnectTimeout = CameraFeedOptions.MaxValue,
        FrameTimeout = CameraFeedOptions.MaxValue,
        ReconnectDelay = TimeSpan.FromSeconds(5),
    };

    private readonly FakeTimeProvider _time = new();
    private readonly FakeItemFeedFactory _factory = new();
    private readonly CameraStreamHub _hub;

    public CameraStreamHubReconnectTests()
    {
        _hub = new CameraStreamHub(new ReconnectingCameraFeed(_factory, _time, new ListLogger<ReconnectingCameraFeed>(), Options), _time, new ListLogger<CameraStreamHub>());
    }

    public enum Consumer
    {
        Frames,
        Detection,
        Packets,
    }

    public async ValueTask DisposeAsync() =>
        await _hub.DisposeAsync().AsTask().WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

    private static CameraFrameSubscriptionOptions Window(FrameProbe probe) => probe.Options(shortensReconnectWait: true);

    [Fact]
    public async Task WindowJoiningDuringTheWaitReconnectsAtOnceAndShowsConnecting()
    {
        // Print failure detection holds the connection, which fails and waits.
        using CameraFrameSubscription detection = _hub.SubscribeFrames("mk4", StreamUri, new CameraFrameSubscriptionOptions { KeyFramesOnly = true, MinFrameInterval = TimeSpan.FromSeconds(2) });
        CameraStatus waiting = await FailAsync(detection);

        FrameProbe probe = new();
        using CameraFrameSubscription window = _hub.SubscribeFrames("mk4", StreamUri, Window(probe));

        // No time passes: the next attempt starts now, and the window never shows the wait it ended.
        FakeItemFeed second = await _factory.FeedAsync(2);
        Assert.True(_time.GetUtcNow() < waiting.NextAttemptAt);
        await Eventually.TrueAsync(() => detection.Status == CameraStatus.Connecting, "reconnecting for everyone");
        Assert.Equal([CameraStatus.Connecting], probe.Statuses);

        // The backoff starts over.
        second.Fail(new CameraFeedException("Connection refused."));
        await Eventually.TrueAsync(() => window.Status.State == CameraConnectionState.Error, "failed again");
        Assert.Equal(_time.GetUtcNow() + Options.ReconnectDelay, window.Status.NextAttemptAt);
    }

    [Theory]
    [InlineData(Consumer.Frames)]
    [InlineData(Consumer.Detection)]
    [InlineData(Consumer.Packets)]
    public async Task OtherConsumersJoiningDuringTheWaitNeverShortenIt(Consumer consumer)
    {
        await using CameraPacketSubscription recording = _hub.SubscribePackets("mk4", StreamUri);
        CameraStatus waiting = await FailAsync(recording);

        await using CameraStreamSubscription joined = consumer switch
        {
            Consumer.Frames => _hub.SubscribeFrames("mk4", StreamUri),
            Consumer.Detection => _hub.SubscribeFrames("mk4", StreamUri, new CameraFrameSubscriptionOptions { KeyFramesOnly = true, MinFrameInterval = TimeSpan.FromSeconds(2) }),
            _ => _hub.SubscribePackets("mk4", StreamUri),
        };

        // It joins the wait as it is.
        Assert.Equal(waiting, joined.Status);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(1, _factory.CreateCount);

        _time.Advance(Options.ReconnectDelay);
        await _factory.FeedAsync(2);
    }

    [Fact]
    public async Task WindowJoiningAStreamingConnectionChangesNothing()
    {
        using CameraFrameSubscription detection = _hub.SubscribeFrames("mk4", StreamUri);
        FakeItemFeed feed = await _factory.FeedAsync(1);
        feed.PushFrame(fill: 1);
        await Eventually.TrueAsync(() => detection.Status == CameraStatus.Streaming, "streaming");

        FrameProbe probe = new();
        using CameraFrameSubscription window = _hub.SubscribeFrames("mk4", StreamUri, Window(probe));
        feed.PushFrame(fill: 2);
        await Eventually.TrueAsync(() => window.Status == CameraStatus.Streaming, "window streaming");

        Assert.Equal(1, _factory.CreateCount);
        Assert.False(feed.IsDisposed);

        // A failure afterwards waits its full delay: the window's lease was not remembered as a retry.
        feed.Fail(new CameraFeedException("Connection refused."));
        await Eventually.TrueAsync(() => window.Status.State == CameraConnectionState.Error, "failed");
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(1, _factory.CreateCount);
    }

    [Fact]
    public async Task WindowOpeningWithoutAConnectionStartsOneWithoutWaiting()
    {
        FrameProbe probe = new();
        using CameraFrameSubscription window = _hub.SubscribeFrames("mk4", StreamUri, Window(probe));

        await _factory.FeedAsync(1);
        Assert.Equal([CameraStatus.Connecting], probe.Statuses);
    }

    [Fact]
    public async Task HubStoppingRightAfterAWindowRetriedClosesEverything()
    {
        await using CameraPacketSubscription recording = _hub.SubscribePackets("mk4", StreamUri);
        await FailAsync(recording);

        using CameraFrameSubscription window = _hub.SubscribeFrames("mk4", StreamUri, Window(new FrameProbe()));
        await _hub.DisposeAsync().AsTask().WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        Assert.Equal(0, _hub.OpenConnections);
        Assert.Equal(CameraStreamEndReason.HubStopped, await window.Ended.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken));
    }

    /// <summary>The first connection fails; returns the status <paramref name="holder"/> waits with.</summary>
    private async Task<CameraStatus> FailAsync(CameraStreamSubscription holder)
    {
        FakeItemFeed first = await _factory.FeedAsync(1);
        first.Fail(new CameraFeedException("Connection refused."));
        await Eventually.TrueAsync(() => holder.Status.NextAttemptAt is not null, "waiting for the next attempt");
        return holder.Status;
    }
}
