using Microsoft.Extensions.Time.Testing;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Tests.Monitoring;

namespace LayerPeek.Core.Tests.Cameras.Recording;

/// <summary>T-144: recordings over the shared camera connection of the hub (fake feeds, fake writer, real files).</summary>
public sealed class CameraRecorderTests : IAsyncDisposable
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
    private readonly ListLogger<CameraRecorder> _logger = new();
    private readonly CameraStreamHub _hub;

    public CameraRecorderTests()
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

    private string FilePath => Path.Combine(_folder, "print.mp4");

    public async ValueTask DisposeAsync()
    {
        await _hub.DisposeAsync().AsTask().WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        TestDirectories.Delete(_folder);
    }

    [Fact]
    public async Task RecordsThePacketsOfTheSharedConnectionWithoutDecoding()
    {
        FakeRecordingWriterFactory writers = new();
        CameraRecorder recorder = new(_hub, writers, _logger);

        ICameraRecording recording = recorder.Start("mk4", FilePath);
        FakeItemFeed feed = await _factory.FeedAsync(1);
        feed.PushPacket(1);
        feed.PushPacket(2, keyFrame: true);
        feed.PushPacket(3);
        await Eventually.TrueAsync(() => writers.Writers is [{ Count: 2 }], "two packets written");
        await recording.StopAsync();

        Assert.Equal([2, 3], writers.Writers[0].Packets.Select(static p => p.Fill));
        Assert.Equal(FakeItemFeed.Info, writers.Writers[0].Info);
        Assert.Equal([FilePath], recording.Files);
        Assert.Equal("mk4", recording.PrinterId);
        Assert.Equal(CameraRecordingState.Completed, recording.State);
        Assert.False((await feed.Started).Frames);
        Assert.Equal(0, feed.DecodedFrames);
    }

    [Fact]
    public async Task QuittingTheApplicationCompletesTheRecording()
    {
        FakeRecordingWriterFactory writers = new();
        ICameraRecording recording = new CameraRecorder(_hub, writers, _logger).Start("mk4", FilePath);
        FakeItemFeed feed = await _factory.FeedAsync(1);
        feed.PushPacket(1, keyFrame: true);
        await Eventually.TrueAsync(() => recording.BytesWritten > 0, "first packet written");

        await _hub.DisposeAsync();
        await recording.Completion.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        Assert.Equal(CameraRecordingState.Completed, recording.State);
        Assert.True(writers.Writers[0].IsCompleted);
    }

    [Fact]
    public async Task APrinterWithoutCameraFailsWithoutAnException()
    {
        ICameraRecording recording = new CameraRecorder(_hub, new FakeRecordingWriterFactory(), _logger).Start("xl", FilePath);

        await recording.Completion.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        Assert.Equal(CameraRecordingState.Failed, recording.State);
        Assert.Equal(CameraRecordingFailure.CameraUnavailable, recording.Failure);
        Assert.Empty(recording.Files);
    }

    [Fact]
    public async Task WithoutAMuxerTheRecorderIsUnavailableAndARecordingFailsAtOnce()
    {
        FakeRecordingWriterFactory writers = new() { Availability = CameraRecorderAvailability.Unavailable("The video library (FFmpeg) has no MP4 muxer.") };
        CameraRecorder recorder = new(_hub, writers, _logger);

        ICameraRecording recording = recorder.Start("mk4", FilePath);

        Assert.False(recorder.Availability.IsAvailable);
        Assert.Equal("The video library (FFmpeg) has no MP4 muxer.", recorder.Availability.Reason);
        Assert.True(recording.Completion.IsCompletedSuccessfully);
        Assert.Equal(CameraRecordingState.Failed, recording.State);
        Assert.Equal(CameraRecordingFailure.Unavailable, recording.Failure);
        Assert.Equal("The video library (FFmpeg) has no MP4 muxer.", recording.FailureMessage);
        await recording.StopAsync();
        // No connection was opened for it.
        Assert.Equal(0, _hub.OpenConnections);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_folder));
    }

    [Theory]
    [InlineData("")]
    [InlineData("print.mp4")]
    [InlineData("recordings/print.mp4")]
    public void InvalidPathsAreRejected(string path)
    {
        CameraRecorder recorder = new(_hub, new FakeRecordingWriterFactory(), _logger);

        Assert.ThrowsAny<ArgumentException>(() => recorder.Start("mk4", path));
    }

    [Fact]
    public void AFolderWithoutAFileNameIsRejected()
    {
        CameraRecorder recorder = new(_hub, new FakeRecordingWriterFactory(), _logger);

        Assert.ThrowsAny<ArgumentException>(() => recorder.Start("mk4", _folder + Path.DirectorySeparatorChar));
        Assert.ThrowsAny<ArgumentException>(() => recorder.Start(" ", FilePath));
    }

    [Fact]
    public void AvailabilityReasonIsRequired()
    {
        Assert.ThrowsAny<ArgumentException>(static () => CameraRecorderAvailability.Unavailable(" "));
        Assert.True(CameraRecorderAvailability.Available.IsAvailable);
        Assert.Null(CameraRecorderAvailability.Available.Reason);
    }
}
