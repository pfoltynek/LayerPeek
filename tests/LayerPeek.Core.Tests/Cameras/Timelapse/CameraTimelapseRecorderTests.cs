using Microsoft.Extensions.Time.Testing;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Cameras.Timelapse;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Tests.Cameras.Recording;
using LayerPeek.Core.Tests.Monitoring;

namespace LayerPeek.Core.Tests.Cameras.Timelapse;

/// <summary>T-158: timelapses over the shared camera connection of the hub (fake feeds, fake writer, real files).</summary>
public sealed class CameraTimelapseRecorderTests : IAsyncDisposable
{
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private static readonly CameraFeedOptions Options = new()
    {
        ConnectTimeout = CameraFeedOptions.MaxValue,
        FrameTimeout = CameraFeedOptions.MaxValue,
        ReconnectDelay = TimeSpan.FromSeconds(5),
    };

    private readonly string _folder = TestDirectories.NewDirectoryPath();
    private readonly FakeTimeProvider _time = new();
    private readonly FakeItemFeedFactory _factory = new();
    private readonly ListLogger<CameraTimelapseRecorder> _logger = new();
    private readonly CameraStreamHub _hub;

    public CameraTimelapseRecorderTests()
    {
        Directory.CreateDirectory(_folder);
        _hub = new CameraStreamHub(new ReconnectingCameraFeed(_factory, _time, new ListLogger<ReconnectingCameraFeed>(), Options), _time, new ListLogger<CameraStreamHub>());
        _hub.Configure([new PrinterDefinition
        {
            Id = "mk4",
            Name = "MK4",
            ConnectionType = ConnectionType.PrusaLink,
            Address = "192.168.0.10",
            Camera = new CameraSettings(true, StreamUri),
        }]);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string FilePath => Path.Combine(_folder, "timelapse.mp4");

    public async ValueTask DisposeAsync()
    {
        await _hub.DisposeAsync().AsTask().WaitAsync(TestTimeouts.Wait, Ct);
        TestDirectories.Delete(_folder);
    }

    [Fact]
    public async Task TakesKeyFramesOfTheSharedConnectionWithoutDecoding()
    {
        FakeRecordingWriterFactory writers = new();
        CameraTimelapseRecorder recorder = new(_hub, writers, _logger);
        // The camera window and a recording use the same connection.
        await using CameraFrameSubscription window = _hub.SubscribeFrames("mk4");

        ICameraTimelapse timelapse = recorder.Start("mk4", FilePath);
        FakeItemFeed feed = await _factory.FeedAsync(1);
        Assert.True(timelapse.RequestFrame());
        feed.PushPacket(1);
        feed.PushPacket(2, keyFrame: true);
        feed.PushPacket(3);
        await Eventually.TrueAsync(() => timelapse.FrameCount == 1, "one frame written");
        await timelapse.StopAsync().WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal([2], Assert.Single(writers.Writers).Packets.Select(static p => p.Fill));
        Assert.Equal([FilePath], timelapse.Files);
        Assert.Equal("mk4", timelapse.PrinterId);
        Assert.Equal(CameraRecordingState.Completed, timelapse.State);
        Assert.Equal(1, _factory.CreateCount);
    }

    [Fact]
    public async Task ATimelapseAloneDecodesNothing()
    {
        FakeRecordingWriterFactory writers = new();
        ICameraTimelapse timelapse = new CameraTimelapseRecorder(_hub, writers, _logger).Start("mk4", FilePath);
        FakeItemFeed feed = await _factory.FeedAsync(1);

        timelapse.RequestFrame();
        feed.PushFrame(1);
        feed.PushPacket(1, keyFrame: true);
        await Eventually.TrueAsync(() => timelapse.FrameCount == 1, "one frame written");
        await timelapse.StopAsync().WaitAsync(TestTimeouts.Wait, Ct);

        Assert.False((await feed.Started.WaitAsync(TestTimeouts.Wait, Ct)).Frames);
        Assert.Equal(0, feed.DecodedFrames);
    }

    [Fact]
    public async Task AKeyFrameSubscriptionQueuesOnlyKeyFramesAndStreamDescriptions()
    {
        await using CameraPacketSubscription packets = _hub.SubscribePackets("mk4", options: new CameraPacketSubscriptionOptions { KeyFramesOnly = true, QueueCapacity = 2 });
        FakeItemFeed feed = await _factory.FeedAsync(1);
        await Eventually.TrueAsync(() => feed.Consumed >= 1, "stream described");

        // More P-frames than the queue holds: they are skipped, never queued, so the queue does not overflow.
        for (int i = 0; i < 10; i++)
        {
            feed.PushPacket((byte)(10 + i));
        }

        feed.PushPacket(1, keyFrame: true);
        List<(CameraStreamItemKind Kind, byte Fill)> items = [];
        await foreach (CameraStreamItem item in packets.ReadAsync(Ct))
        {
            items.Add((item.Kind, item.Kind == CameraStreamItemKind.Packet ? item.Packet.Data.Span[0] : (byte)0));
            if (item.Kind == CameraStreamItemKind.Packet)
            {
                break;
            }
        }

        Assert.Equal([(CameraStreamItemKind.StreamStarted, (byte)0), (CameraStreamItemKind.Packet, (byte)1)], items);
        Assert.False(packets.IsEnded);
    }

    [Fact]
    public async Task APrinterWithoutCameraFailsWithoutAnException()
    {
        ICameraTimelapse timelapse = new CameraTimelapseRecorder(_hub, new FakeRecordingWriterFactory(), _logger).Start("xl", FilePath);

        await timelapse.Completion.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal(CameraRecordingState.Failed, timelapse.State);
        Assert.Equal(CameraRecordingFailure.CameraUnavailable, timelapse.Failure);
        Assert.Empty(timelapse.Files);
        Assert.False(timelapse.RequestFrame());
    }

    [Fact]
    public async Task WithoutAMuxerATimelapseFailsAtOnce()
    {
        FakeRecordingWriterFactory writers = new() { Availability = CameraRecorderAvailability.Unavailable("The video library (FFmpeg) has no MP4 muxer.") };
        CameraTimelapseRecorder recorder = new(_hub, writers, _logger);

        ICameraTimelapse timelapse = recorder.Start("mk4", FilePath);

        Assert.False(recorder.Availability.IsAvailable);
        Assert.True(timelapse.Completion.IsCompletedSuccessfully);
        Assert.Equal(CameraRecordingState.Failed, timelapse.State);
        Assert.Equal(CameraRecordingFailure.Unavailable, timelapse.Failure);
        Assert.Equal("The video library (FFmpeg) has no MP4 muxer.", timelapse.FailureMessage);
        Assert.False(timelapse.RequestFrame());
        await timelapse.StopAsync().WaitAsync(TestTimeouts.Wait, Ct);
        Assert.Equal(0, _hub.OpenConnections);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_folder));
    }

    [Theory]
    [InlineData("")]
    [InlineData("timelapse.mp4")]
    [InlineData("recordings/timelapse.mp4")]
    public void InvalidPathsAreRejected(string path)
    {
        CameraTimelapseRecorder recorder = new(_hub, new FakeRecordingWriterFactory(), _logger);

        Assert.ThrowsAny<ArgumentException>(() => recorder.Start("mk4", path));
        Assert.ThrowsAny<ArgumentException>(() => recorder.Start(" ", FilePath));
    }
}
