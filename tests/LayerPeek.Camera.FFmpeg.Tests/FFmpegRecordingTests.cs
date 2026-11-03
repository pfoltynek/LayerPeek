using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Printers;
using LayerPeek.Simulator.Camera;

namespace LayerPeek.Camera.FFmpeg.Tests;

/// <summary>
/// T-144: recordings as fragmented MP4 without re-encoding, from the fixture file and from the simulated camera
/// (loopback), read back through FFmpeg's demuxer and decoder (<see cref="Mp4Probe"/>).
/// </summary>
public sealed class FFmpegRecordingTests : IDisposable
{
    private readonly string _folder = TestDirectories.CreateDirectory();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => TestDirectories.Delete(_folder);

    [FFmpegFact]
    public async Task TheFixtureIsRemuxedIntoAValidMp4()
    {
        string path = Path.Combine(_folder, "fixture.mp4");
        int keyFrames = 0;

        await WriteFixtureAsync(path, (writer, _) =>
        {
            writer.Complete();
            return Task.CompletedTask;
        }, key => keyFrames += key ? 1 : 0);

        Mp4Contents contents = Mp4Probe.AssertValid(path, TestFixtures.Buddy3DFrameCount);
        Assert.Equal(TestFixtures.Buddy3DFrameCount, contents.Packets);
        Assert.Equal(keyFrames, contents.KeyFrames);
        // 71 frames at the fixture's 25 fps.
        Assert.InRange(contents.DurationSeconds, 2.7, 3.0);
    }

    [FFmpegFact]
    public async Task AfterACrashTheFileIsReadableUpToTheLastFragment()
    {
        string path = Path.Combine(_folder, "crash.mp4");
        string snapshot = Path.Combine(_folder, "crash-snapshot.mp4");
        int keyFrames = 0;

        await WriteFixtureAsync(path, (writer, _) =>
        {
            // What a killed process leaves on disk: the container was never completed (no trailer, no close).
            File.Copy(path, snapshot);
            writer.Dispose();
            return Task.CompletedTask;
        }, key => keyFrames += key ? 1 : 0);

        Assert.True(keyFrames >= 2, "The fixture needs at least two fragments.");
        Mp4Contents contents = Mp4Probe.AssertValid(snapshot, 1);
        // Every fragment but the one still being collected (from the last key frame on) is there.
        Assert.InRange(contents.Packets, 1, TestFixtures.Buddy3DFrameCount - 1);
    }

    [FFmpegFact]
    public async Task EveryNativeObjectIsFreedWithOrWithoutCompleting()
    {
        byte[] keyFrame = await FirstKeyFrameAsync();
        CameraStreamInfo info = new(CameraVideoCodec.H264, 0, 0, CameraTimeBase.Rtp90kHz, ReadOnlyMemory<byte>.Empty);
        FFmpegCameraRecordingWriterFactory writers = new();

        FFmpegRecordingWriter completed = (FFmpegRecordingWriter)writers.Create(Path.Combine(_folder, "completed.mp4"), info, keyFrame);
        completed.Write(keyFrame, 0, 0, 3600, isKeyFrame: true);
        completed.Complete();
        completed.Complete();
        Assert.True(completed.HoldsNativeResources);
        completed.Dispose();
        completed.Dispose();

        // After a failure the recording disposes without completing (what was written stays as a crash leaves it).
        FFmpegRecordingWriter abandoned = (FFmpegRecordingWriter)writers.Create(Path.Combine(_folder, "abandoned.mp4"), info, keyFrame);
        abandoned.Write(keyFrame, 0, 0, 3600, isKeyFrame: true);
        abandoned.Dispose();

        Assert.False(completed.HoldsNativeResources);
        Assert.False(abandoned.HoldsNativeResources);
        Assert.Throws<ObjectDisposedException>(() => completed.Write(keyFrame, 3600, 3600, 3600, isKeyFrame: true));
        Assert.Throws<ObjectDisposedException>(completed.Complete);
        Mp4Probe.AssertValid(Path.Combine(_folder, "completed.mp4"), 1);
    }

    [FFmpegFact]
    public async Task ParameterSetsAreTakenFromTheKeyFrameWhenTheStreamDescriptionHasNone()
    {
        byte[] keyFrame = await FirstKeyFrameAsync();

        byte[] sets = FFmpegRecordingWriter.ExtractParameterSets(keyFrame);

        Assert.Equal([0, 0, 0, 1], sets[..4]);
        Assert.Equal(7, sets[4] & 0x1F);
        Assert.Contains(sets.Select(static (b, i) => (b, i)), static p => p.i > 4 && p.b == 1);
        Assert.Empty(FFmpegRecordingWriter.ExtractParameterSets([0, 0, 1, 0x65, 0x88, 0x84]));
        Assert.Equal([0, 0, 0, 1, 0x67, 0x42, 0, 0, 0, 1, 0x68, 0xCE], FFmpegRecordingWriter.ExtractParameterSets([0, 0, 1, 0x67, 0x42, 0, 0, 0, 0, 1, 0x68, 0xCE, 0, 0, 1, 0x65, 0x11]));
    }

    [FFmpegFact]
    public async Task AStreamWithoutParameterSetsCannotBeRecorded()
    {
        CameraStreamInfo info = new(CameraVideoCodec.H264, 0, 0, CameraTimeBase.Rtp90kHz, ReadOnlyMemory<byte>.Empty);
        FFmpegCameraRecordingWriterFactory writers = new();

        CameraRecordingException error = Assert.Throws<CameraRecordingException>(() => writers.Create(Path.Combine(_folder, "x.mp4"), info, [0, 0, 1, 0x65, 0x88]));

        Assert.Equal(CameraRecordingFailure.StreamFailed, error.Failure);
        await Task.CompletedTask;
    }

    [FFmpegFact]
    public void AMissingFolderFailsWithoutThePathInTheMessage()
    {
        string missing = Path.Combine(_folder, "missing", "x.mp4");
        CameraStreamInfo info = new(CameraVideoCodec.H264, 1280, 720, CameraTimeBase.Rtp90kHz, new byte[] { 0, 0, 0, 1, 0x67, 0x42, 0, 0x1F });

        CameraRecordingException error = Assert.Throws<CameraRecordingException>(() => new FFmpegCameraRecordingWriterFactory().Create(missing, info, [0, 0, 1, 0x65]));

        Assert.Equal(CameraRecordingFailure.WriteFailed, error.Failure);
        Assert.StartsWith("Creating the recording file failed: ", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_folder, error.Message, StringComparison.Ordinal);
    }

    [FFmpegFact]
    public void WithoutTheMuxerRecordingIsUnavailableWithAReason()
    {
        FFmpegCameraRecordingWriterFactory writers = new("layerpeek-no-such-muxer");

        Assert.False(writers.Availability.IsAvailable);
        Assert.Equal("The video library (FFmpeg) has no MP4 muxer.", writers.Availability.Reason);
        CameraRecordingException error = Assert.Throws<CameraRecordingException>(() => writers.Create(Path.Combine(_folder, "x.mp4"), new(CameraVideoCodec.H264, 0, 0, CameraTimeBase.Rtp90kHz, default), []));
        Assert.Equal(CameraRecordingFailure.Unavailable, error.Failure);
        Assert.True(new FFmpegCameraRecordingWriterFactory().Availability.IsAvailable);
    }

    [FFmpegFact]
    public async Task TheSimulatedCameraIsRecordedFromAKeyFrameWithoutDecoding()
    {
        await using RtspCamera camera = TestFixtures.StartCamera(loop: true, realTime: true);
        await using Rig rig = new(camera);
        // The camera window already runs, so the recording joins in the middle of a group of pictures.
        await using CameraPacketSubscription window = rig.Hub.SubscribePackets("mk4");
        await Rig.SkipPacketsAsync(window, 30);

        ICameraRecording recording = rig.Recorder.Start("mk4", Path.Combine(_folder, "mk4.mp4"));
        await WaitAsync(() => recording.Duration >= TimeSpan.FromSeconds(3), "3 s recorded");
        await recording.StopAsync();

        Assert.Equal(CameraRecordingState.Completed, recording.State);
        string file = Assert.Single(recording.Files);
        Mp4Contents contents = Mp4Probe.AssertValid(file, 60);
        Assert.InRange(contents.DurationSeconds, 2.5, 6);
        Assert.Equal(new FileInfo(file).Length, recording.BytesWritten);
        Assert.False(Assert.Single(rig.Feeds.Feeds).DecoderOpened);
    }

    [FFmpegFact]
    public async Task ACameraOutageStartsTheNextFile()
    {
        await using RtspCamera camera = TestFixtures.StartCamera(loop: true, realTime: true);
        await using Rig rig = new(camera);
        ICameraRecording recording = rig.Recorder.Start("mk4", Path.Combine(_folder, "outage.mp4"));
        await WaitAsync(() => recording.Duration >= TimeSpan.FromSeconds(1.5), "first file recorded");

        camera.SetOffline(true);
        await Task.Delay(TimeSpan.FromSeconds(1), Ct);
        camera.SetOffline(false);
        await WaitAsync(() => recording.Files.Count == 2 && recording.Duration >= TimeSpan.FromSeconds(3.5), "second file recorded");
        await recording.StopAsync();

        Assert.Equal(CameraRecordingState.Completed, recording.State);
        Assert.Equal([Path.Combine(_folder, "outage.mp4"), Path.Combine(_folder, "outage-2.mp4")], recording.Files);
        Mp4Probe.AssertValid(recording.Files[0], 25);
        Mp4Probe.AssertValid(recording.Files[1], 25);
    }

    [FFmpegFact]
    public async Task QuittingTheApplicationCompletesTheFile()
    {
        await using RtspCamera camera = TestFixtures.StartCamera(loop: true, realTime: true);
        Rig rig = new(camera);
        ICameraRecording recording = rig.Recorder.Start("mk4", Path.Combine(_folder, "quit.mp4"));
        await WaitAsync(() => recording.Duration >= TimeSpan.FromSeconds(1), "1 s recorded");

        await rig.DisposeAsync();
        await recording.Completion.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(CameraRecordingState.Completed, recording.State);
        Mp4Probe.AssertValid(Assert.Single(recording.Files), 20);
    }

    /// <summary>Writes the fixture's packets (timestamps from 0) and hands the open writer to <paramref name="finish"/>.</summary>
    private static async Task WriteFixtureAsync(string path, Func<ICameraRecordingWriter, CameraStreamInfo, Task> finish, Action<bool> keyFrame)
    {
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);
        FFmpegCameraRecordingWriterFactory writers = new();
        ICameraRecordingWriter? writer = null;
        CameraStreamInfo? info = null;
        long frame = 0;
        long index = 0;
        try
        {
            await foreach (CameraStreamItem item in feed.ReadAsync(new CameraStreamDemand(frames: false, packets: true), Ct))
            {
                if (item.Kind == CameraStreamItemKind.StreamStarted)
                {
                    info = item.Info;
                    continue;
                }

                CameraPacket packet = item.Packet;
                if (writer is null)
                {
                    Assert.True(packet.IsKeyFrame);
                    writer = writers.Create(path, info!, packet.Data.Span);
                    // Raw H.264 has no timing of its own (the Core recording fixes RTSP timestamps): 25 fps.
                    frame = (long)info!.TimeBase.Denominator / info.TimeBase.Numerator / 25;
                }

                long dts = index++ * frame;
                writer.Write(packet.Data.Span, dts, dts, frame, packet.IsKeyFrame);
                keyFrame(packet.IsKeyFrame);
            }

            Assert.NotNull(writer);
            await finish(writer, info!);
        }
        finally
        {
            writer?.Dispose();
        }
    }

    private static async Task<byte[]> FirstKeyFrameAsync()
    {
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);
        await foreach (CameraStreamItem item in feed.ReadAsync(new CameraStreamDemand(frames: false, packets: true), Ct))
        {
            if (item.Kind == CameraStreamItemKind.Packet && item.Packet.IsKeyFrame)
            {
                return item.Packet.Data.ToArray();
            }
        }

        throw new InvalidOperationException("The fixture has no key frame.");
    }

    private static async Task WaitAsync(Func<bool> condition, string description)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for: {description}");
            await Task.Delay(20, Ct);
        }
    }

    /// <summary>The app's recording stack against one simulated camera: FFmpeg feed, reconnecting feed, hub, recorder.</summary>
    private sealed class Rig : IAsyncDisposable
    {
        public Rig(RtspCamera camera)
        {
            ReconnectingCameraFeed feed = new(
                Feeds,
                TimeProvider.System,
                NullLogger<ReconnectingCameraFeed>.Instance,
                new CameraFeedOptions { ReconnectDelay = TimeSpan.FromMilliseconds(200), FrameTimeout = TimeSpan.FromSeconds(2) });
            Hub = new CameraStreamHub(feed, TimeProvider.System, NullLogger<CameraStreamHub>.Instance, TimeSpan.Zero);
            Hub.Configure([new PrinterDefinition
            {
                Id = "mk4",
                Name = "MK4",
                ConnectionType = ConnectionType.PrusaLink,
                Address = "127.0.0.1",
                Camera = new CameraSettings(true, camera.StreamUri),
            }]);
            Recorder = new CameraRecorder(Hub, new FFmpegCameraRecordingWriterFactory(), NullLogger<CameraRecorder>.Instance);
        }

        public CapturingFeedFactory Feeds { get; } = new(new FFmpegCameraFeedFactory());

        public CameraStreamHub Hub { get; }

        public CameraRecorder Recorder { get; }

        public static async Task SkipPacketsAsync(CameraPacketSubscription subscription, int count)
        {
            int packets = 0;
            await foreach (CameraStreamItem item in subscription.ReadAsync(Ct))
            {
                if (item.Kind == CameraStreamItemKind.Packet && ++packets == count)
                {
                    return;
                }
            }
        }

        public ValueTask DisposeAsync() => Hub.DisposeAsync();
    }

    /// <summary>Keeps the FFmpeg feeds it creates, so that a test can inspect them.</summary>
    private sealed class CapturingFeedFactory(FFmpegCameraFeedFactory inner) : ICameraFeedFactory
    {
        private readonly List<FFmpegCameraFeed> _feeds = [];

        public IReadOnlyList<FFmpegCameraFeed> Feeds
        {
            get
            {
                lock (_feeds)
                {
                    return [.. _feeds];
                }
            }
        }

        public ICameraFeed Create(Uri streamUri)
        {
            FFmpegCameraFeed feed = (FFmpegCameraFeed)inner.Create(streamUri);
            lock (_feeds)
            {
                _feeds.Add(feed);
            }

            return feed;
        }
    }
}
