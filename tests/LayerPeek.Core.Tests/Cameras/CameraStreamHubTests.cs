using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Tests.Monitoring;

namespace LayerPeek.Core.Tests.Cameras;

/// <summary>The shared camera connection of T-143 (fake feeds, fake time).</summary>
public sealed class CameraStreamHubTests : IAsyncDisposable
{
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");
    private static readonly Uri OtherUri = new("rtsp://192.168.0.228/live");

    // Longer than any time a test advances: only the hub's linger time matters here, not the stream's timeouts.
    private static readonly CameraFeedOptions Options = new()
    {
        ConnectTimeout = CameraFeedOptions.MaxValue,
        FrameTimeout = CameraFeedOptions.MaxValue,
        ReconnectDelay = TimeSpan.FromSeconds(5),
    };

    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(1);

    private readonly FakeTimeProvider _time = new();
    private readonly FakeItemFeedFactory _factory = new();
    private readonly ListLogger<CameraStreamHub> _logger = new();
    private readonly CameraStreamHub _hub;

    // Bounds every read of a subscription: a bug fails the test instead of hanging the whole run.
    private readonly CancellationTokenSource _timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

    public CameraStreamHubTests()
    {
        _timeout.CancelAfter(TestTimeouts.Wait * 3);
        _hub = new CameraStreamHub(new ReconnectingCameraFeed(_factory, _time, new ListLogger<ReconnectingCameraFeed>(), Options), _time, _logger);
    }

    public async ValueTask DisposeAsync()
    {
        await _hub.DisposeAsync().AsTask().WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        _timeout.Dispose();
    }

    private CancellationToken Bounded => _timeout.Token;

    private static Task<T> Within<T>(Task<T> task) => task.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

    private static Task Within(ValueTask task) => task.AsTask().WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

    private static PrinterDefinition Printer(string id = "mk4", Uri? camera = null, bool enabled = true) => new()
    {
        Id = id,
        Name = id.ToUpperInvariant(),
        ConnectionType = ConnectionType.PrusaLink,
        Address = "192.168.0.10",
        Camera = camera is null && !enabled ? CameraSettings.Disabled : new CameraSettings(enabled, camera ?? StreamUri),
    };

    private static async Task<byte> NextFrameAsync(CameraFrameSubscription subscription)
    {
        CameraFrameBuffer? frame = null;
        await Eventually.TrueAsync(() => (frame = subscription.TakeLatest()) is not null, "a frame");
        return frame!.Pixels[0];
    }

    // ---- Sharing and lifetime ----

    [Fact]
    public async Task SubscribersOfOnePrinterShareOneConnection()
    {
        FrameProbe a = new();
        FrameProbe b = new();
        using CameraFrameSubscription first = _hub.SubscribeFrames("mk4", StreamUri, a.Options());
        using CameraFrameSubscription second = _hub.SubscribeFrames("MK4", StreamUri, b.Options());
        await using CameraPacketSubscription packets = _hub.SubscribePackets("mk4", StreamUri);
        FakeItemFeed feed = await _factory.FeedAsync(1);

        feed.PushFrame(fill: 7);

        Assert.Equal(7, await NextFrameAsync(first));
        Assert.Equal(7, await NextFrameAsync(second));
        Assert.Equal(1, _factory.CreateCount);
        Assert.Equal(1, _hub.OpenConnections);
        Assert.Equal(StreamUri, feed.Uri);
    }

    [Fact]
    public async Task NothingConnectsBeforeTheFirstSubscriber()
    {
        _hub.Configure([Printer()]);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(0, _factory.CreateCount);
        Assert.Equal(0, _hub.OpenConnections);
    }

    [Fact]
    public async Task ConnectionClosesTheLingerTimeAfterTheLastSubscriber()
    {
        CameraFrameSubscription first = _hub.SubscribeFrames("mk4", StreamUri);
        CameraFrameSubscription second = _hub.SubscribeFrames("mk4", StreamUri);
        FakeItemFeed feed = await _factory.FeedAsync(1);

        first.Dispose();
        second.Dispose();
        _time.Advance(CameraStreamHub.DefaultLinger - Tick);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(feed.IsDisposed);
        Assert.Equal(1, _hub.OpenConnections);

        _time.Advance(Tick);
        await feed.Disposed.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        await Eventually.TrueAsync(() => _hub.OpenConnections == 0, "connection closed");
        Assert.Equal(TimeSpan.FromSeconds(10), CameraStreamHub.DefaultLinger);
    }

    [Fact]
    public async Task SubscriberComingBackWithinTheLingerTimeKeepsTheConnection()
    {
        CameraFrameSubscription first = _hub.SubscribeFrames("mk4", StreamUri);
        FakeItemFeed feed = await _factory.FeedAsync(1);
        first.Dispose();
        _time.Advance(CameraStreamHub.DefaultLinger - Tick);

        using CameraFrameSubscription again = _hub.SubscribeFrames("mk4", StreamUri);
        _time.Advance(CameraStreamHub.DefaultLinger * 2);
        feed.PushFrame(fill: 3);

        Assert.Equal(3, await NextFrameAsync(again));
        Assert.False(feed.IsDisposed);
        Assert.Equal(1, _factory.CreateCount);
    }

    [Fact]
    public async Task WithoutLingerTheConnectionClosesAtOnceAndLeavesNothingRunning()
    {
        await using CameraStreamHub hub = new(new ReconnectingCameraFeed(_factory, _time, new ListLogger<ReconnectingCameraFeed>(), Options), _time, _logger, TimeSpan.Zero);
        CameraFrameSubscription lease = hub.SubscribeFrames("mk4", StreamUri);
        FakeItemFeed feed = await _factory.FeedAsync(1);

        lease.Dispose();

        await feed.Disposed.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        await Eventually.TrueAsync(() => hub.OpenConnections == 0, "connection closed");
        Assert.Equal(CameraStreamEndReason.Disposed, await lease.Ended.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisposingTheHubEndsEveryLeaseAndClosesEveryConnection()
    {
        FrameProbe probe = new();
        CameraFrameSubscription frames = _hub.SubscribeFrames("mk4", StreamUri, probe.Options());
        CameraPacketSubscription packets = _hub.SubscribePackets("xl", OtherUri);
        FakeItemFeed first = await _factory.FeedAsync(1);
        FakeItemFeed second = await _factory.FeedAsync(2);

        await _hub.DisposeAsync().AsTask().WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        Assert.True(first.IsDisposed);
        Assert.True(second.IsDisposed);
        Assert.Equal(CameraStreamEndReason.HubStopped, await Within(frames.Ended));
        Assert.Equal(CameraStreamEndReason.HubStopped, await Within(packets.Ended));
        Assert.Same(frames, Assert.Single(probe.Ended));
        Assert.Equal(CameraStreamEndReason.HubStopped, await Within(_hub.SubscribeFrames("mk4", StreamUri).Ended));
        Assert.Equal(0, _hub.OpenConnections);
    }

    // ---- Decoding on demand ----

    [Fact]
    public async Task PacketSubscriberAloneDoesNotDecode()
    {
        await using CameraPacketSubscription packets = _hub.SubscribePackets("mk4", StreamUri);
        FakeItemFeed feed = await _factory.FeedAsync(1);
        CameraStreamDemand demand = await Within(feed.Started);

        feed.PushPacket(fill: 1, keyFrame: true);
        feed.PushFrame(fill: 1);
        feed.PushPacket(fill: 2);
        await Eventually.TrueAsync(() => feed.Consumed >= 4, "items read");

        Assert.False(demand.Frames);
        Assert.True(demand.Packets);
        Assert.Equal(0, feed.DecodedFrames);
    }

    [Fact]
    public async Task FrameSubscriberSwitchesDecodingOnAndItStaysOnForTheLingerTimeAfterItLeft()
    {
        await using CameraPacketSubscription packets = _hub.SubscribePackets("mk4", StreamUri);
        FakeItemFeed feed = await _factory.FeedAsync(1);
        CameraStreamDemand demand = await Within(feed.Started);
        Assert.False(demand.Frames);

        CameraFrameSubscription window = _hub.SubscribeFrames("mk4", StreamUri);
        Assert.True(demand.Frames);
        feed.PushFrame(fill: 4);
        Assert.Equal(4, await NextFrameAsync(window));

        // Minimized: decoding goes on for the linger time (a quick restore shows a frame at once), then stops.
        window.Dispose();
        Assert.True(demand.Frames);
        _time.Advance(CameraStreamHub.DefaultLinger);
        Assert.False(demand.Frames);
        Assert.True(demand.Packets);
        Assert.Equal(1, _hub.OpenConnections);
        Assert.False(feed.IsDisposed);
    }

    [Fact]
    public async Task LastPacketSubscriberLeavingStopsThePacketsAtOnce()
    {
        using CameraFrameSubscription window = _hub.SubscribeFrames("mk4", StreamUri);
        CameraPacketSubscription recording = _hub.SubscribePackets("mk4", StreamUri);
        FakeItemFeed feed = await _factory.FeedAsync(1);
        CameraStreamDemand demand = await Within(feed.Started);
        Assert.True(demand.Packets);

        await Within(recording.DisposeAsync());

        Assert.False(demand.Packets);
        Assert.True(demand.Frames);
    }

    [Fact]
    public async Task LingeringPacketOnlyConnectionKeepsReadingAndIsNotTakenForStalled()
    {
        // A real stall timeout shorter than the linger time: with no demand at all the feed would yield nothing and the
        // watchdog of the reconnecting feed would drop the lingering connection.
        CameraFeedOptions options = new()
        {
            ConnectTimeout = CameraFeedOptions.MaxValue,
            FrameTimeout = TimeSpan.FromSeconds(2),
            ReconnectDelay = TimeSpan.FromSeconds(1),
        };
        await using CameraStreamHub hub = new(new ReconnectingCameraFeed(_factory, _time, new ListLogger<ReconnectingCameraFeed>(), options), _time, _logger);
        CameraPacketSubscription recording = hub.SubscribePackets("mk4", StreamUri);
        FakeItemFeed feed = await _factory.FeedAsync(1);
        CameraStreamDemand demand = await Within(feed.Started);

        await Within(recording.DisposeAsync());
        Assert.True(demand.Packets || demand.Frames, "a lingering connection must still read something");

        // The camera keeps sending; the linger time passes in steps shorter than the stall timeout.
        for (int second = 1; second < CameraStreamHub.DefaultLinger.TotalSeconds; second++)
        {
            int consumed = feed.Consumed;
            feed.PushPacket(fill: (byte)second);
            await Eventually.TrueAsync(() => feed.Consumed > consumed, "packet read");
            _time.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.False(feed.IsDisposed);
        Assert.Equal(1, _factory.CreateCount);
        _time.Advance(TimeSpan.FromSeconds(1));
        await feed.Disposed.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        await Eventually.TrueAsync(() => hub.OpenConnections == 0, "connection closed after the linger time");
    }

    [Fact]
    public async Task CancelledPacketReaderStopsWhileItemsAreStillQueued()
    {
        await using CameraPacketSubscription recording = _hub.SubscribePackets("mk4", StreamUri);
        FakeItemFeed feed = await _factory.FeedAsync(1);
        feed.PushPacket(fill: 1, keyFrame: true);
        feed.PushPacket(fill: 2);
        await Eventually.TrueAsync(() => feed.Consumed >= 3, "packets queued");
        using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(Bounded);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (CameraStreamItem item in recording.ReadAsync(cancel.Token))
            {
                // The description: the reader gives up, two packets are still queued.
                await cancel.CancelAsync();
            }
        });
    }

    // ---- Packets ----

    [Fact]
    public async Task PacketsArriveInOrderAfterTheStreamDescriptionAsOwnedCopies()
    {
        await using CameraPacketSubscription packets = _hub.SubscribePackets("mk4", StreamUri);
        FakeItemFeed feed = await _factory.FeedAsync(1);
        feed.PushPacket(fill: 1, keyFrame: true, length: 32);
        feed.PushPacket(fill: 2, length: 8);
        await Eventually.TrueAsync(() => feed.Consumed >= 3, "packets queued");
        // The decoder reuses its buffers: the queued copies must not change.
        feed.OverwriteBuffers(0xEE);

        List<(CameraStreamItemKind Kind, byte First, int Length, bool Key, long? Pts)> items = [];
        await foreach (CameraStreamItem item in packets.ReadAsync(Bounded))
        {
            items.Add(item.Kind == CameraStreamItemKind.Packet
                ? (item.Kind, item.Packet.Data.Span[0], item.Packet.Data.Length, item.Packet.IsKeyFrame, item.Packet.Pts)
                : (item.Kind, 0, 0, false, null));
            if (item.Kind == CameraStreamItemKind.StreamStarted)
            {
                Assert.Same(FakeItemFeed.Info, item.Info);
            }

            if (items.Count == 3)
            {
                break;
            }
        }

        Assert.Equal(
            [
                (CameraStreamItemKind.StreamStarted, (byte)0, 0, false, (long?)null),
                (CameraStreamItemKind.Packet, (byte)1, 32, true, 4500L),
                (CameraStreamItemKind.Packet, (byte)2, 8, false, 9000L),
            ],
            items);
    }

    [Fact]
    public async Task PacketSubscriberJoiningARunningConnectionGetsItsDescriptionFirst()
    {
        using CameraFrameSubscription window = _hub.SubscribeFrames("mk4", StreamUri);
        FakeItemFeed feed = await _factory.FeedAsync(1);
        feed.PushFrame(fill: 1);
        await NextFrameAsync(window);

        await using CameraPacketSubscription recording = _hub.SubscribePackets("mk4", StreamUri);
        feed.PushPacket(fill: 5, keyFrame: true);

        await using IAsyncEnumerator<CameraStreamItem> items = recording.ReadAsync(Bounded).GetAsyncEnumerator(Bounded);
        Assert.True(await items.MoveNextAsync());
        Assert.Equal(CameraStreamItemKind.StreamStarted, items.Current.Kind);
        Assert.True(await items.MoveNextAsync());
        Assert.Equal((CameraStreamItemKind.Packet, (byte)5), (items.Current.Kind, items.Current.Packet.Data.Span[0]));
    }

    [Fact]
    public async Task ReconnectStartsANewStreamDescription()
    {
        await using CameraPacketSubscription recording = _hub.SubscribePackets("mk4", StreamUri);
        FakeItemFeed first = await _factory.FeedAsync(1);
        first.PushPacket(fill: 1, keyFrame: true);
        first.End();
        await first.Disposed.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        await Eventually.TrueAsync(() => recording.Status.State == CameraConnectionState.NoSignal, "no signal");
        _time.Advance(Options.ReconnectDelay);
        FakeItemFeed second = await _factory.FeedAsync(2);
        second.PushPacket(fill: 2, keyFrame: true);

        List<CameraStreamItemKind> kinds = [];
        await foreach (CameraStreamItem item in recording.ReadAsync(Bounded))
        {
            kinds.Add(item.Kind);
            if (kinds.Count == 4)
            {
                break;
            }
        }

        Assert.Equal([CameraStreamItemKind.StreamStarted, CameraStreamItemKind.Packet, CameraStreamItemKind.StreamStarted, CameraStreamItemKind.Packet], kinds);
    }

    // ---- Slow and failing subscribers ----

    [Fact]
    public async Task SlowFrameSubscriberDoesNotSlowTheOthers()
    {
        // Its consumer is blocked and never takes a frame; the other one gets every frame.
        FrameProbe slow = new();
        using CameraFrameSubscription blocked = _hub.SubscribeFrames("mk4", StreamUri, slow.Options());
        using CameraFrameSubscription live = _hub.SubscribeFrames("mk4", StreamUri);
        FakeItemFeed feed = await _factory.FeedAsync(1);

        for (byte fill = 1; fill <= 20; fill++)
        {
            feed.PushFrame(fill);
            Assert.Equal(fill, await NextFrameAsync(live));
        }

        Assert.Equal(1, slow.Notified);
        Assert.Equal(19, blocked.DroppedFrames);
        // The blocked one gets the newest frame once it takes one.
        Assert.Equal(20, blocked.TakeLatest()!.Pixels[0]);
    }

    [Fact]
    public async Task PacketSubscriberThatFallsBehindEndsWithOverflowAndTheOthersGoOn()
    {
        // The reader is blocked (never reads): its queue fills up.
        FrameProbe probe = new();
        CameraPacketSubscription stuck = _hub.SubscribePackets("mk4", StreamUri, new CameraPacketSubscriptionOptions { QueueCapacity = 4, Ended = probe.Ended.Enqueue });
        await using CameraPacketSubscription reading = _hub.SubscribePackets("mk4", StreamUri);
        using CameraFrameSubscription window = _hub.SubscribeFrames("mk4", StreamUri);
        FakeItemFeed feed = await _factory.FeedAsync(1);
        Task<int> read = CountPacketsAsync(reading, 10, Bounded);

        for (byte fill = 1; fill <= 10; fill++)
        {
            feed.PushPacket(fill, keyFrame: fill == 1);
        }

        feed.PushFrame(fill: 9);

        Assert.Equal(CameraStreamEndReason.Overflow, await stuck.Ended.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken));
        Assert.Same(stuck, Assert.Single(probe.Ended));
        Assert.Equal(10, await read.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken));
        Assert.Equal(9, await NextFrameAsync(window));
        Assert.Contains(_logger.Entries, static e => e.Level == LogLevel.Warning && e.Message.Contains("fell behind", StringComparison.Ordinal));
        // Nobody was reading it: its queued buffers went back to the pool, and reading it now ends at once.
        int queued = 0;
        await foreach (CameraStreamItem item in stuck.ReadAsync(Bounded))
        {
            queued++;
        }

        Assert.Equal(0, queued);

        static async Task<int> CountPacketsAsync(CameraPacketSubscription subscription, int wanted, CancellationToken cancellationToken)
        {
            int count = 0;
            await foreach (CameraStreamItem item in subscription.ReadAsync(cancellationToken))
            {
                if (item.Kind == CameraStreamItemKind.Packet && ++count == wanted)
                {
                    break;
                }
            }

            return count;
        }
    }

    [Fact]
    public async Task FailingFrameCallbackEndsOnlyThatSubscription()
    {
        FrameProbe probe = new();
        InvalidOperationException failure = new("The UI is gone.");
        CameraFrameSubscription failing = _hub.SubscribeFrames("mk4", StreamUri, new CameraFrameSubscriptionOptions
        {
            FrameAvailable = () => throw failure,
            Ended = probe.Ended.Enqueue,
        });
        using CameraFrameSubscription live = _hub.SubscribeFrames("mk4", StreamUri);
        FakeItemFeed feed = await _factory.FeedAsync(1);

        feed.PushFrame(fill: 1);

        Assert.Equal(CameraStreamEndReason.Failed, await failing.Ended.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken));
        Assert.Same(failure, failing.Failure);
        Assert.Equal(CameraStreamEndReason.Failed, failing.EndReason);
        Assert.Same(failing, Assert.Single(probe.Ended));
        Assert.Equal(1, await NextFrameAsync(live));
        feed.PushFrame(fill: 2);
        Assert.Equal(2, await NextFrameAsync(live));
        Assert.False(feed.IsDisposed);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Error && ReferenceEquals(e.Exception, failure));
    }

    [Fact]
    public async Task FailingStatusObserverIsLoggedAndIgnored()
    {
        using CameraFrameSubscription lease = _hub.SubscribeFrames("mk4", StreamUri, new CameraFrameSubscriptionOptions
        {
            Status = new Progress(_ => throw new InvalidOperationException("observer")),
        });
        FakeItemFeed feed = await _factory.FeedAsync(1);
        feed.PushFrame(fill: 6);

        Assert.Equal(6, await NextFrameAsync(lease));
        Assert.False(lease.IsEnded);
        Assert.Contains(_logger.Entries, static e => e.Message.Contains("observer of a camera stream", StringComparison.Ordinal));
    }

    // ---- Status ----

    [Fact]
    public async Task EveryFrameSubscriberIsConnectingUntilItsOwnFirstFrame()
    {
        FrameProbe first = new();
        using CameraFrameSubscription early = _hub.SubscribeFrames("mk4", StreamUri, first.Options());
        FakeItemFeed feed = await _factory.FeedAsync(1);
        feed.PushFrame(fill: 1);
        await NextFrameAsync(early);
        await Eventually.TrueAsync(() => early.Status == CameraStatus.Streaming, "streaming");

        FrameProbe second = new();
        using CameraFrameSubscription late = _hub.SubscribeFrames("mk4", StreamUri, second.Options());
        Assert.Equal(CameraStatus.Connecting, late.Status);
        feed.PushFrame(fill: 2);
        await NextFrameAsync(late);

        Assert.Equal([CameraStatus.Connecting, CameraStatus.Streaming], first.Statuses);
        Assert.Equal([CameraStatus.Connecting, CameraStatus.Streaming], second.Statuses);
    }

    [Fact]
    public async Task FailuresReachEverySubscriber()
    {
        FrameProbe window = new();
        using CameraFrameSubscription frames = _hub.SubscribeFrames("mk4", StreamUri, window.Options());
        await using CameraPacketSubscription packets = _hub.SubscribePackets("mk4", StreamUri);
        FakeItemFeed feed = await _factory.FeedAsync(1);

        feed.Fail(new CameraFeedException("Decoding failed."));

        await Eventually.TrueAsync(() => frames.Status.State == CameraConnectionState.Error && packets.Status.State == CameraConnectionState.Error, "error everywhere");
        Assert.Equal("Decoding failed.", frames.Status.Reason);
        Assert.Equal("Decoding failed.", packets.Status.Reason);
        _time.Advance(Options.ReconnectDelay);
        await _factory.FeedAsync(2);
        await Eventually.TrueAsync(() => frames.Status == CameraStatus.Connecting, "reconnecting");
    }

    [Fact]
    public async Task MinFrameIntervalSkipsFramesThatComeTooSoon()
    {
        using CameraFrameSubscription sampled = _hub.SubscribeFrames("mk4", StreamUri, new CameraFrameSubscriptionOptions { MinFrameInterval = TimeSpan.FromSeconds(1) });
        using CameraFrameSubscription live = _hub.SubscribeFrames("mk4", StreamUri);
        FakeItemFeed feed = await _factory.FeedAsync(1);

        feed.PushFrame(fill: 1);
        Assert.Equal(1, await NextFrameAsync(live));
        Assert.Equal(1, await NextFrameAsync(sampled));

        _time.Advance(TimeSpan.FromMilliseconds(500));
        feed.PushFrame(fill: 2);
        Assert.Equal(2, await NextFrameAsync(live));
        Assert.Null(sampled.TakeLatest());

        _time.Advance(TimeSpan.FromMilliseconds(500));
        feed.PushFrame(fill: 3);
        Assert.Equal(3, await NextFrameAsync(live));
        Assert.Equal(3, await NextFrameAsync(sampled));
    }

    // ---- Settings ----

    [Fact]
    public async Task ChangedAddressRestartsTheConnectionForEverySubscriber()
    {
        _hub.Configure([Printer()]);
        FrameProbe window = new();
        using CameraFrameSubscription frames = _hub.SubscribeFrames("mk4", options: window.Options());
        await using CameraPacketSubscription packets = _hub.SubscribePackets("mk4");
        FakeItemFeed first = await _factory.FeedAsync(1);
        first.PushFrame(fill: 1);
        await NextFrameAsync(frames);

        _hub.Configure([Printer(camera: OtherUri)]);

        await first.Disposed.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        FakeItemFeed second = await _factory.FeedAsync(2);
        Assert.Equal(OtherUri, second.Uri);
        second.PushFrame(fill: 2);
        Assert.Equal(2, await NextFrameAsync(frames));
        Assert.False(frames.IsEnded);
        Assert.False(packets.IsEnded);
        Assert.Equal([CameraStatus.Connecting, CameraStatus.Streaming, CameraStatus.Connecting, CameraStatus.Streaming], window.Statuses);
        Assert.True(_hub.IsConfigured("MK4", OtherUri));
        Assert.False(_hub.IsConfigured("mk4", StreamUri));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DisabledCameraOrRemovedPrinterEndsEveryLease(bool removed)
    {
        _hub.Configure([Printer(), Printer("xl", OtherUri)]);
        FrameProbe window = new();
        CameraFrameSubscription frames = _hub.SubscribeFrames("mk4", options: window.Options());
        CameraPacketSubscription packets = _hub.SubscribePackets("mk4");
        using CameraFrameSubscription other = _hub.SubscribeFrames("xl");
        // The two connections open in parallel, in either order.
        FakeItemFeed[] feeds = [await _factory.FeedAsync(1), await _factory.FeedAsync(2)];
        FakeItemFeed feed = feeds.Single(static f => f.Uri == StreamUri);
        FakeItemFeed otherFeed = feeds.Single(static f => f.Uri == OtherUri);

        _hub.Configure(removed ? [Printer("xl", OtherUri)] : [Printer(camera: StreamUri, enabled: false), Printer("xl", OtherUri)]);

        Assert.Equal(CameraStreamEndReason.CameraUnavailable, await frames.Ended.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken));
        Assert.Equal(CameraStreamEndReason.CameraUnavailable, await packets.Ended.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken));
        Assert.Same(frames, Assert.Single(window.Ended));
        // At once, without the linger time.
        await feed.Disposed.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        Assert.False(otherFeed.IsDisposed);
        Assert.False(other.IsEnded);
        Assert.Equal(CameraStreamEndReason.CameraUnavailable, await Within(_hub.SubscribeFrames("mk4").Ended));
    }

    [Fact]
    public async Task UnchangedSettingsChangeNothing()
    {
        _hub.Configure([Printer()]);
        using CameraFrameSubscription frames = _hub.SubscribeFrames("mk4");
        FakeItemFeed feed = await _factory.FeedAsync(1);

        _hub.Configure([Printer() with { Name = "Renamed" }]);
        _hub.Configure([Printer()]);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.False(feed.IsDisposed);
        Assert.Equal(1, _factory.CreateCount);
        Assert.False(frames.IsEnded);
    }

    [Fact]
    public async Task UnknownCameraEndsTheLeaseAtOnce()
    {
        CameraFrameSubscription frames = _hub.SubscribeFrames("mk4");
        CameraPacketSubscription packets = _hub.SubscribePackets("mk4");

        Assert.Equal(CameraStreamEndReason.CameraUnavailable, await Within(frames.Ended));
        Assert.Equal(CameraStreamEndReason.CameraUnavailable, await Within(packets.Ended));
        Assert.Null(frames.TakeLatest());
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(0, _factory.CreateCount);
    }

    [Fact]
    public async Task LeaseWithANewerAddressSwitchesTheConnection()
    {
        // A camera window opened right after Apply, before the monitoring run delivered the saved address (T-040).
        _hub.Configure([Printer()]);
        await using CameraPacketSubscription recording = _hub.SubscribePackets("mk4");
        FakeItemFeed first = await _factory.FeedAsync(1);

        using CameraFrameSubscription window = _hub.SubscribeFrames("mk4", OtherUri);

        await first.Disposed.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        FakeItemFeed second = await _factory.FeedAsync(2);
        Assert.Equal(OtherUri, second.Uri);
        Assert.False(recording.IsEnded);

        // The run then delivers it: nothing more happens.
        _hub.Configure([Printer(camera: OtherUri)]);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(second.IsDisposed);
        Assert.Equal(2, _factory.CreateCount);
    }

    [Fact]
    public async Task FramesOfTheOldAddressNeverReachTheNewLease()
    {
        using CameraFrameSubscription old = _hub.SubscribeFrames("mk4", StreamUri);
        FakeItemFeed first = await _factory.FeedAsync(1);
        first.PushFrame(fill: 1);
        await NextFrameAsync(old);

        using CameraFrameSubscription renewed = _hub.SubscribeFrames("mk4", OtherUri);
        first.PushFrame(fill: 9);
        FakeItemFeed second = await _factory.FeedAsync(2);
        second.PushFrame(fill: 2);

        Assert.Equal(2, await NextFrameAsync(renewed));
    }

    [Fact]
    public void ArgumentsAreChecked()
    {
        Assert.Throws<ArgumentNullException>(() => new CameraStreamHub(null!, _time, _logger));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraStreamHub(new ReconnectingCameraFeed(_factory, _time, new ListLogger<ReconnectingCameraFeed>()), _time, _logger, TimeSpan.FromSeconds(-1)));
        Assert.Throws<ArgumentException>(() => _hub.SubscribeFrames(" "));
        Assert.Throws<ArgumentException>(() => _hub.SubscribePackets("mk4", new Uri("/live", UriKind.Relative)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraFrameSubscriptionOptions { MinFrameInterval = TimeSpan.FromSeconds(-1) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraPacketSubscriptionOptions { QueueCapacity = 0 });
    }

    private sealed class Progress(Action<CameraStatus> report) : IProgress<CameraStatus>
    {
        public void Report(CameraStatus value) => report(value);
    }
}
