using FFmpeg.AutoGen.Abstractions;

using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Cameras.Timelapse;
using LayerPeek.Core.Printers;
using LayerPeek.Simulator.Camera;

namespace LayerPeek.Camera.FFmpeg.Tests;

/// <summary>
/// T-158: timelapses from the camera's key frames without re-encoding, from the fixture file and from the simulated camera
/// (loopback), written by the recording writer (fragmented MP4) and read back strictly through FFmpeg (<see cref="Mp4Probe"/>).
/// </summary>
public sealed class FFmpegTimelapseTests : IDisposable
{
    private static readonly TimeSpan FrameDuration = TimeSpan.FromSeconds(1.0 / ICameraTimelapseRecorder.FramesPerSecond);

    private readonly string _folder = TestDirectories.CreateDirectory();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => TestDirectories.Delete(_folder);

    [FFmpegFact]
    public async Task TheFixturesKeyFramesMakeAPlayableTimelapseIdenticalToTheKeyFramesOfTheWholeStream()
    {
        // Every connection plays the fixture once (two key frames) and ends; the hub reconnects with the same stream, so
        // the frames of all connections go into one file.
        await using Rig rig = new(new FixtureFeedFactory());
        string path = Path.Combine(_folder, "fixture_timelapse.mp4");
        ICameraTimelapse timelapse = rig.Timelapses.Start("mk4", path);
        await RequestUntilAsync(timelapse, 8, TimeSpan.FromMilliseconds(1));
        await timelapse.StopAsync();

        Assert.Equal(CameraRecordingState.Completed, timelapse.State);
        Assert.Equal([path], timelapse.Files);
        List<DecodedFrame> frames = [];
        Mp4Contents contents = Mp4Probe.Read(path, strict: true, frames);
        AssertTimelapse(contents, timelapse.FrameCount);
        Assert.Equal(new FileInfo(path).Length, timelapse.BytesWritten);
        // Bit-exact: every frame is one of the fixture's key frames as decoded from the whole stream (like framemd5); which
        // ones depends on when the requests come (KeyFramesOfTheFixtureDecodeBitExactly checks each).
        HashSet<string> keyFrames = [.. FixtureKeyFrameDigests()];
        Assert.All(frames, frame => Assert.Contains(frame.Digest, keyFrames));
        Assert.All(rig.Feeds.Feeds, static feed => Assert.False(feed.DecoderOpened));
        // The container's cost of one fragment per frame (T-149 prototype: ~135 B more than an unfragmented MP4).
        double overhead = (contents.PacketBytes == 0 ? 0 : new FileInfo(path).Length - contents.PacketBytes) / (double)contents.Packets;
        TestContext.Current.TestOutputHelper?.WriteLine($"{contents.Packets} frames, {contents.PacketBytes} B of H.264, {overhead:F0} B of container per frame.");
        Assert.InRange(overhead, 0, 1024);
    }

    [FFmpegFact]
    public async Task KeyFramesOfTheFixtureDecodeBitExactlyAlsoTwiceInARowWithTheSameIdrPicId()
    {
        // Each key frame twice in a row: the same IDR has the same idr_pic_id in every slice header (H.264 7.4.3 asks
        // consecutive IDRs to differ; the Buddy3D cycles 0–15, so a timelapse can take two equal ones in a row).
        List<byte[]> keyFrames = await FixtureKeyFramesAsync();
        Assert.Equal(2, keyFrames.Count);
        string path = Path.Combine(_folder, "same-idr.mp4");
        CameraStreamInfo info = new(CameraVideoCodec.H264, 0, 0, CameraTimeBase.Rtp90kHz, ReadOnlyMemory<byte>.Empty);
        long n = 0;
        using (ICameraRecordingWriter writer = new FFmpegCameraRecordingWriterFactory().Create(path, info, keyFrames[0]))
        {
            foreach (byte[] keyFrame in (byte[][])[keyFrames[0], keyFrames[0], keyFrames[1], keyFrames[1], keyFrames[0]])
            {
                writer.Write(keyFrame, n * 3000, n * 3000, 3000, isKeyFrame: true);
                n++;
            }

            writer.Complete();
        }

        List<DecodedFrame> frames = [];
        AssertTimelapse(Mp4Probe.Read(path, strict: true, frames), n);
        // Like framemd5: identical to the key frames decoded from the whole stream (with every P-frame).
        List<string> expected = FixtureKeyFrameDigests();
        Assert.Equal([expected[0], expected[0], expected[1], expected[1], expected[0]], frames.Select(static f => f.Digest));
    }

    [FFmpegFact]
    public async Task TheSimulatedCameraGivesATimelapseWithoutDecodingThatACrashLeavesReadable()
    {
        await using RtspCamera camera = TestFixtures.StartCamera(loop: true, realTime: true);
        await using Rig rig = new(new FFmpegCameraFeedFactory(), camera.StreamUri);
        // A recording runs at the same time on the same connection.
        ICameraRecording recording = rig.Recorder.Start("mk4", Path.Combine(_folder, "recording.mp4"));
        string path = Path.Combine(_folder, "mk4_timelapse.mp4");
        ICameraTimelapse timelapse = rig.Timelapses.Start("mk4", path);
        await RequestUntilAsync(timelapse, 3, TimeSpan.FromMilliseconds(100));
        // What a killed process leaves on disk: the last frame waits in its fragment until the next one starts.
        await Task.Delay(TimeSpan.FromMilliseconds(300), Ct);
        string snapshot = Path.Combine(_folder, "crash-snapshot.mp4");
        long framesAtCrash = timelapse.FrameCount;
        File.Copy(path, snapshot);
        await RequestUntilAsync(timelapse, framesAtCrash + 1, TimeSpan.FromMilliseconds(100));
        await timelapse.StopAsync();
        await recording.StopAsync();

        Assert.Equal(CameraRecordingState.Completed, timelapse.State);
        AssertTimelapse(Mp4Probe.Read(path, strict: true), timelapse.FrameCount);
        Mp4Contents crashed = Mp4Probe.Read(snapshot, strict: true);
        AssertTimelapse(crashed, framesAtCrash - 1);
        Assert.False(Assert.Single(rig.Feeds.Feeds).DecoderOpened);
        Assert.Equal(CameraRecordingState.Completed, recording.State);
        Mp4Probe.AssertValid(Assert.Single(recording.Files), 25);
    }

    [FFmpegFact]
    public async Task ACameraOutageWithTheSameStreamContinuesTheFile()
    {
        await using RtspCamera camera = TestFixtures.StartCamera(loop: true, realTime: true);
        await using Rig rig = new(new FFmpegCameraFeedFactory(), camera.StreamUri);
        string path = Path.Combine(_folder, "outage_timelapse.mp4");
        ICameraTimelapse timelapse = rig.Timelapses.Start("mk4", path);
        await RequestUntilAsync(timelapse, 2, TimeSpan.FromMilliseconds(100));

        camera.SetOffline(true);
        await Task.Delay(TimeSpan.FromSeconds(1), Ct);
        camera.SetOffline(false);
        await RequestUntilAsync(timelapse, 4, TimeSpan.FromMilliseconds(100));
        await timelapse.StopAsync();

        Assert.True(rig.Feeds.Feeds.Count >= 2, "The camera was connected again.");
        Assert.Equal([path], timelapse.Files);
        AssertTimelapse(Mp4Probe.Read(path, strict: true), timelapse.FrameCount);
    }

    /// <summary>One H.264 track of the Buddy3D's size, only key frames, all decoded without error, DTS increasing, n / 30 s.</summary>
    private static void AssertTimelapse(Mp4Contents contents, long frames)
    {
        Assert.Contains("mp4", contents.FormatName, StringComparison.Ordinal);
        Assert.Equal(1, contents.Streams);
        Assert.Equal(AVCodecID.AV_CODEC_ID_H264, contents.Codec);
        Assert.Equal((TestFixtures.Buddy3DWidth, TestFixtures.Buddy3DHeight), (contents.Width, contents.Height));
        Assert.Equal(frames, contents.Packets);
        Assert.Equal(contents.Packets, contents.KeyFrames);
        Assert.True(contents.TimestampsIncrease, "Timestamps must increase.");
        Assert.Equal(0, contents.DecodeErrors);
        Assert.Equal(contents.Packets, contents.DecodedFrames);
        Assert.Equal((frames * FrameDuration).TotalSeconds, contents.DurationSeconds, 0.002);
    }

    /// <summary>Requests frames every <paramref name="interval"/> until the timelapse has <paramref name="frames"/>.</summary>
    private static async Task RequestUntilAsync(ICameraTimelapse timelapse, long frames, TimeSpan interval)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (timelapse.FrameCount < frames)
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for {frames} frames ({timelapse.State}, {timelapse.FailureMessage}).");
            Assert.True(timelapse.RequestFrame(), $"The timelapse ended ({timelapse.State}, {timelapse.FailureMessage}).");
            await Task.Delay(interval, Ct);
        }
    }

    /// <summary>The fixture's key frames decoded from the whole stream (with every P-frame), in order.</summary>
    private static List<string> FixtureKeyFrameDigests()
    {
        List<DecodedFrame> frames = [];
        Mp4Contents contents = Mp4Probe.Read(TestFixtures.Buddy3D.LocalPath, strict: true, frames);
        Assert.Equal(TestFixtures.Buddy3DFrameCount, contents.DecodedFrames);
        return [.. frames.Where(static f => f.KeyFrame).Select(static f => f.Digest)];
    }

    /// <summary>The fixture's key frame packets (IDR with in-band SPS/PPS), in order.</summary>
    private static async Task<List<byte[]>> FixtureKeyFramesAsync()
    {
        List<byte[]> keyFrames = [];
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);
        await foreach (CameraStreamItem item in feed.ReadAsync(new CameraStreamDemand(frames: false, packets: true), Ct))
        {
            if (item.Kind == CameraStreamItemKind.Packet && item.Packet.IsKeyFrame)
            {
                keyFrames.Add(item.Packet.Data.ToArray());
            }
        }

        return keyFrames;
    }

    /// <summary>The app's timelapse stack against one camera: FFmpeg feed, reconnecting feed, hub, timelapse recorder.</summary>
    private sealed class Rig : IAsyncDisposable
    {
        public Rig(ICameraFeedFactory feeds, Uri? streamUri = null)
        {
            Feeds = new CapturingFeedFactory(feeds);
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
                Camera = new CameraSettings(true, streamUri ?? new Uri("rtsp://127.0.0.1/fixture")),
            }]);
            FFmpegCameraRecordingWriterFactory writers = new();
            Recorder = new CameraRecorder(Hub, writers, NullLogger<CameraRecorder>.Instance);
            Timelapses = new CameraTimelapseRecorder(Hub, writers, NullLogger<CameraTimelapseRecorder>.Instance);
        }

        public CapturingFeedFactory Feeds { get; }

        public CameraStreamHub Hub { get; }

        public CameraRecorder Recorder { get; }

        public CameraTimelapseRecorder Timelapses { get; }

        public ValueTask DisposeAsync() => Hub.DisposeAsync();
    }

    /// <summary>Every connection reads the fixture file once, whatever the configured address.</summary>
    private sealed class FixtureFeedFactory : ICameraFeedFactory
    {
        public ICameraFeed Create(Uri streamUri) => TestFixtures.CreateFeed(TestFixtures.Buddy3D);
    }

    /// <summary>Keeps the FFmpeg feeds it creates, so that a test can inspect them.</summary>
    private sealed class CapturingFeedFactory(ICameraFeedFactory inner) : ICameraFeedFactory
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
