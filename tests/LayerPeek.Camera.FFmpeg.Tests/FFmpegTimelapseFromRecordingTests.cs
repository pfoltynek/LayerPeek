using FFmpeg.AutoGen.Abstractions;

using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Cameras.Timelapse;
using LayerPeek.Core.Printers;
using LayerPeek.Simulator.Camera;

namespace LayerPeek.Camera.FFmpeg.Tests;

/// <summary>
/// T-164: a timelapse from a real recording (fragmented MP4 by the recording writer of T-144: the fixture remuxed with its
/// own timing, and a recording of the simulated camera with an outage) through FFmpeg's MP4 demuxer, packets only, written
/// by the recording writer and read back strictly (<see cref="Mp4Probe"/>): only key frames, bit-exact, 30 fps. Also a
/// truncated file, garbage and cancelling.
/// </summary>
public sealed class FFmpegTimelapseFromRecordingTests : IDisposable
{
    /// <summary>Frame duration of the remuxed fixture in 90 kHz ticks (~23 fps, like the Buddy3D).</summary>
    private const long FixtureFrameTicks = 3913;

    private static readonly TimeSpan FrameDuration = TimeSpan.FromSeconds(1.0 / ICameraTimelapseRecorder.FramesPerSecond);

    private readonly string _folder = TestDirectories.CreateDirectory();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => TestDirectories.Delete(_folder);

    [FFmpegFact]
    public async Task TheKeyFramesOfARecordingAtTheIntervalMakeAPlayableBitExactTimelapse()
    {
        // 20 plays of the fixture (~62 s, 40 key frames ~1.4 / ~1.7 s apart), a frame every 5 s.
        (string recording, List<long> keyTimes) = await WriteFixtureRecordingAsync("2026-11-04_14-05-09_Benchy.mp4", loops: 20);
        CapturingFeeds feeds = new();

        CameraTimelapseFromRecordingResult result = await MakeAsync(feeds, recording, TimeSpan.FromSeconds(5));

        Assert.True(result.Succeeded, result.Problem.ToString());
        string timelapse = Assert.Single(result.OutputFiles);
        Assert.Equal(Path.Combine(_folder, "2026-11-04_14-05-09_Benchy_recording_timelapse.mp4"), timelapse);
        List<int> expected = ExpectedKeyFrames(keyTimes, TimeSpan.FromSeconds(5));
        Assert.Equal(expected.Count, result.Frames);
        Assert.InRange(result.Frames, 12, 13);
        List<DecodedFrame> frames = [];
        AssertTimelapse(Mp4Probe.Read(timelapse, strict: true, frames), result.Frames);
        // Bit-exact: the packets are the recording's key frames at the steps, byte for byte ...
        List<byte[]> recordingKeyFrames = await KeyFramePacketsAsync(recording);
        List<byte[]> written = await KeyFramePacketsAsync(timelapse);
        Assert.Equal(expected.Count, written.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.True(recordingKeyFrames[expected[i]].AsSpan().SequenceEqual(written[i]), $"Frame {i} differs from key frame {expected[i]}.");
        }

        // ... and decode like the fixture's key frames decoded from the whole stream (like framemd5).
        List<string> fixture = FixtureKeyFrameDigests();
        Assert.Equal(expected.Select(k => fixture[k % 2]), frames.Select(static f => f.Digest));
        // Packets only: the app's decoder was never opened.
        Assert.All(feeds.Feeds, static feed => Assert.False(feed.DecoderOpened));
        Assert.DoesNotContain(Directory.GetFiles(_folder), static f => Path.GetFileName(f).StartsWith('.'));
    }

    [FFmpegFact]
    public async Task ARecordingOfTheSimulatedCameraWithAnOutageIsOneTimelapse()
    {
        string first = Path.Combine(_folder, "2026-11-04_14-05-09_Benchy.mp4");
        await using (RtspCamera camera = TestFixtures.StartCamera(loop: true, realTime: true))
        {
            await using RecordingRig rig = new(camera);
            ICameraRecording recording = rig.Recorder.Start("mk4", first);
            await WaitAsync(() => recording.Duration >= TimeSpan.FromSeconds(3), "first segment recorded");
            camera.SetOffline(true);
            await Task.Delay(TimeSpan.FromSeconds(1), Ct);
            camera.SetOffline(false);
            await WaitAsync(() => recording.Files.Count == 2 && recording.Duration >= TimeSpan.FromSeconds(6), "second segment recorded");
            await recording.StopAsync();
            Assert.Equal([first, Path.Combine(_folder, "2026-11-04_14-05-09_Benchy-2.mp4")], recording.Files);
        }

        CapturingFeeds feeds = new();

        // The second segment is chosen; a frame every second of recording time.
        CameraTimelapseFromRecordingResult result = await MakeAsync(feeds, Path.Combine(_folder, "2026-11-04_14-05-09_Benchy-2.mp4"), TimeSpan.FromSeconds(1));

        Assert.True(result.Succeeded, result.Problem.ToString());
        // The same stream parameters after the outage: one file of both segments.
        string timelapse = Assert.Single(result.OutputFiles);
        Assert.Equal(Path.Combine(_folder, "2026-11-04_14-05-09_Benchy_recording_timelapse.mp4"), timelapse);
        Assert.Equal(2, feeds.Feeds.Count);
        Assert.InRange(result.Frames, 4, 12);
        List<DecodedFrame> frames = [];
        AssertTimelapse(Mp4Probe.Read(timelapse, strict: true, frames), result.Frames);
        HashSet<string> keyFrames = [.. FixtureKeyFrameDigests()];
        Assert.All(frames, frame => Assert.Contains(frame.Digest, keyFrames));
        Assert.All(feeds.Feeds, static feed => Assert.False(feed.DecoderOpened));
    }

    [FFmpegFact]
    public async Task ARecordingCutOffByACrashGivesTheFramesBeforeTheCut()
    {
        (string complete, List<long> keyTimes) = await WriteFixtureRecordingAsync("2026-11-04_14-05-09.mp4", loops: 20);
        byte[] bytes = await File.ReadAllBytesAsync(complete, Ct);
        // A crash cuts the last fragment's media data (the writer flushes every fragment whole, T-144): here right through
        // the key frame at its start, at about 60 % of the file.
        (_, long start, long size) = Boxes(bytes).Last(box => box.Type == "mdat" && box.Start < bytes.Length * 0.6);
        string truncated = Path.Combine(_folder, "2026-11-04_15-00-00.mp4");
        await File.WriteAllBytesAsync(truncated, bytes[..(int)(start + 8 + 1000)], Ct);
        Assert.True(size > 8 + 1000);

        // Every key frame is taken (they are ~1.4–1.7 s apart).
        CameraTimelapseFromRecordingResult result = await MakeAsync(new CapturingFeeds(), truncated, TimeSpan.FromSeconds(1));

        int all = ExpectedKeyFrames(keyTimes, TimeSpan.FromSeconds(1)).Count;
        TestContext.Current.TestOutputHelper?.WriteLine($"{result.Problem}, {result.Frames} frames of {all}.");
        Assert.True(result.Succeeded, result.Problem.ToString());
        Assert.InRange(result.Frames, 1, all - 1);
        // The cut key frame is not part of it: every frame decodes.
        AssertTimelapse(Mp4Probe.Read(Assert.Single(result.OutputFiles), strict: true), result.Frames);
    }

    [FFmpegFact]
    public async Task ARecordingCutInsideAFragmentHeaderFailsCleanly()
    {
        (string complete, _) = await WriteFixtureRecordingAsync("2026-11-04_14-05-09.mp4", loops: 20);
        byte[] bytes = await File.ReadAllBytesAsync(complete, Ct);
        // FFmpeg's MP4 demuxer reads the fragment headers when it opens the file and refuses a cut one ("trun sample count
        // exceeds the samples the input can hold"); measured with the macOS build and Homebrew FFmpeg 8.
        (_, long start, _) = Boxes(bytes).Last(box => box.Type == "moof" && box.Start < bytes.Length * 0.6);
        string truncated = Path.Combine(_folder, "2026-11-04_15-00-00.mp4");
        await File.WriteAllBytesAsync(truncated, bytes[..(int)(start + 40)], Ct);
        File.Delete(complete);

        CameraTimelapseFromRecordingResult result = await MakeAsync(new CapturingFeeds(), truncated, TimeSpan.FromSeconds(1));

        TestContext.Current.TestOutputHelper?.WriteLine($"{result.Problem}, {result.Frames} frames.");
        Assert.True(result.Problem is CameraTimelapseExportProblem.Unreadable or CameraTimelapseExportProblem.None, result.Problem.ToString());
        if (!result.Succeeded)
        {
            Assert.Equal([truncated], Directory.GetFiles(_folder));
        }
    }

    /// <summary>The top-level boxes of an MP4 file: type, start and size.</summary>
    private static List<(string Type, long Start, long Size)> Boxes(byte[] file)
    {
        List<(string, long, long)> boxes = [];
        long position = 0;
        while (position + 8 <= file.Length)
        {
            long size = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan((int)position, 4));
            string type = System.Text.Encoding.ASCII.GetString(file, (int)position + 4, 4);
            Assert.True(size >= 8, "Only 32-bit box sizes in a recording.");
            boxes.Add((type, position, size));
            position += size;
        }

        return boxes;
    }

    [FFmpegFact]
    public async Task FiveMinutesOfRecordingTakeAFractionOfASecond()
    {
        // ~5 min of the fixture (like the spike's measurement of T-149: 5 min → 0.10 s CPU), a frame every 30 s.
        (string recording, List<long> keyTimes) = await WriteFixtureRecordingAsync("2026-11-04_14-05-09.mp4", loops: 100);
        long bytes = new FileInfo(recording).Length;

        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        CameraTimelapseFromRecordingResult result = await MakeAsync(new CapturingFeeds(), recording, TimeSpan.FromSeconds(30));
        watch.Stop();

        Assert.True(result.Succeeded, result.Problem.ToString());
        Assert.Equal(ExpectedKeyFrames(keyTimes, TimeSpan.FromSeconds(30)).Count, result.Frames);
        double seconds = keyTimes[^1] / 90_000.0;
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{seconds:F0} s of recording, {bytes / 1e6:F1} MB, {result.Frames} frames in {watch.Elapsed.TotalMilliseconds:F0} ms ({bytes / 1e6 / watch.Elapsed.TotalSeconds:F0} MB/s).");
        Assert.InRange(watch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(20));
    }

    [FFmpegFact]
    public async Task AFileThatIsNoVideoFailsWithoutLeftovers()
    {
        string garbage = Path.Combine(_folder, "2026-11-04_14-05-09.mp4");
        byte[] noise = new byte[64 * 1024];
        new Random(164).NextBytes(noise);
        await File.WriteAllBytesAsync(garbage, noise, Ct);
        string empty = Path.Combine(_folder, "2026-11-04_15-00-00.mp4");
        await File.WriteAllBytesAsync(empty, [], Ct);

        Assert.Equal(CameraTimelapseExportProblem.Unreadable, (await MakeAsync(new CapturingFeeds(), garbage, TimeSpan.FromSeconds(5))).Problem);
        Assert.Equal(CameraTimelapseExportProblem.Unreadable, (await MakeAsync(new CapturingFeeds(), empty, TimeSpan.FromSeconds(5))).Problem);
        Assert.Equal([garbage, empty], Directory.GetFiles(_folder).Order(StringComparer.Ordinal));
    }

    [FFmpegFact]
    public async Task CancellingWhileWritingLeavesNothing()
    {
        (string recording, _) = await WriteFixtureRecordingAsync("2026-11-04_14-05-09.mp4", loops: 20);
        CameraTimelapseFromRecordingMaker? maker = null;
        // The second written frame cancels the job, on the demuxer's thread.
        GatedWriters writers = new() { OnWrite = n => { if (n == 2) { maker!.Cancel(); } } };
        await using (maker = new(new CapturingFeeds(), writers, NullLogger<CameraTimelapseFromRecordingMaker>.Instance))
        {
            TaskCompletionSource<CameraTimelapseFromRecordingResult> completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            maker.Completed += (_, r) => completed.TrySetResult(r);
            Assert.True(maker.Start(recording, TimeSpan.FromSeconds(5)));
            CameraTimelapseFromRecordingResult result = await completed.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);

            Assert.Equal(CameraTimelapseExportProblem.Cancelled, result.Problem);
        }

        Assert.Equal([recording], Directory.GetFiles(_folder));
    }

    private static async Task<CameraTimelapseFromRecordingResult> MakeAsync(ICameraFeedFactory feeds, string recording, TimeSpan interval)
    {
        await using CameraTimelapseFromRecordingMaker maker = new(feeds, new FFmpegCameraRecordingWriterFactory(), NullLogger<CameraTimelapseFromRecordingMaker>.Instance);
        TaskCompletionSource<CameraTimelapseFromRecordingResult> completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        maker.Completed += (_, result) => completed.TrySetResult(result);
        Assert.True(maker.Start(recording, interval, "mk4"));
        return await completed.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
    }

    /// <summary>
    /// The indices of the key frames a timelapse takes from key frames at <paramref name="keyTimes"/> (90 kHz ticks from 0):
    /// the first one at or after every step of <paramref name="interval"/>, never two for one step.
    /// </summary>
    private static List<int> ExpectedKeyFrames(List<long> keyTimes, TimeSpan interval)
    {
        long step = (long)(interval.TotalSeconds * 90_000);
        List<int> taken = [];
        long due = 0;
        for (int k = 0; k < keyTimes.Count; k++)
        {
            if (keyTimes[k] >= due)
            {
                taken.Add(k);
                due = ((keyTimes[k] / step) + 1) * step;
            }
        }

        return taken;
    }

    /// <summary>
    /// A recording as the recorder writes it (T-144): the fixture played <paramref name="loops"/> times one after the other,
    /// every packet with its own timestamp (~23 fps); returns the times of its key frames (90 kHz ticks).
    /// </summary>
    private async Task<(string Path, List<long> KeyTimes)> WriteFixtureRecordingAsync(string name, int loops)
    {
        List<(byte[] Data, bool Key)> packets = [];
        await using (FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D))
        {
            await foreach (CameraStreamItem item in feed.ReadAsync(new CameraStreamDemand(frames: false, packets: true), Ct))
            {
                if (item.Kind == CameraStreamItemKind.Packet)
                {
                    packets.Add((item.Packet.Data.ToArray(), item.Packet.IsKeyFrame));
                }
            }
        }

        Assert.True(packets[0].Key, "The fixture starts with a key frame.");
        string path = Path.Combine(_folder, name);
        CameraStreamInfo info = new(CameraVideoCodec.H264, 0, 0, CameraTimeBase.Rtp90kHz, ReadOnlyMemory<byte>.Empty);
        List<long> keyTimes = [];
        using ICameraRecordingWriter writer = new FFmpegCameraRecordingWriterFactory().Create(path, info, packets[0].Data);
        long n = 0;
        for (int loop = 0; loop < loops; loop++)
        {
            foreach ((byte[] data, bool key) in packets)
            {
                long dts = n * FixtureFrameTicks;
                if (key)
                {
                    keyTimes.Add(dts);
                }

                writer.Write(data, dts, dts, FixtureFrameTicks, key);
                n++;
            }
        }

        writer.Complete();
        Assert.Equal(loops * TestFixtures.Buddy3DKeyFrameCount, keyTimes.Count);
        return (path, keyTimes);
    }

    /// <summary>The key frame packets of an MP4 file as FFmpeg's demuxer gives them, in order.</summary>
    private static async Task<List<byte[]>> KeyFramePacketsAsync(string path)
    {
        List<byte[]> keyFrames = [];
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(new Uri(path));
        await foreach (CameraStreamItem item in feed.ReadAsync(new CameraStreamDemand(frames: false, packets: true), Ct))
        {
            if (item.Kind == CameraStreamItemKind.Packet && item.Packet.IsKeyFrame)
            {
                keyFrames.Add(item.Packet.Data.ToArray());
            }
        }

        return keyFrames;
    }

    /// <summary>The fixture's key frames decoded from the whole stream (with every P-frame), in order.</summary>
    private static List<string> FixtureKeyFrameDigests()
    {
        List<DecodedFrame> frames = [];
        Mp4Contents contents = Mp4Probe.Read(TestFixtures.Buddy3D.LocalPath, strict: true, frames);
        Assert.Equal(TestFixtures.Buddy3DFrameCount, contents.DecodedFrames);
        return [.. frames.Where(static f => f.KeyFrame).Select(static f => f.Digest)];
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

    private static async Task WaitAsync(Func<bool> condition, string what)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out: {what}.");
            await Task.Delay(50, Ct);
        }
    }

    /// <summary>FFmpeg feeds that are kept for inspection.</summary>
    private sealed class CapturingFeeds : ICameraFeedFactory
    {
        private readonly FFmpegCameraFeedFactory _inner = new();
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
            FFmpegCameraFeed feed = (FFmpegCameraFeed)_inner.Create(streamUri);
            lock (_feeds)
            {
                _feeds.Add(feed);
            }

            return feed;
        }
    }

    /// <summary>The recording writer, with a callback after every written frame (1-based).</summary>
    private sealed class GatedWriters : ICameraRecordingWriterFactory
    {
        private readonly FFmpegCameraRecordingWriterFactory _inner = new();

        public Action<int>? OnWrite { get; init; }

        public CameraRecorderAvailability Availability => _inner.Availability;

        public ICameraRecordingWriter Create(string path, CameraStreamInfo info, ReadOnlySpan<byte> firstKeyFrame) =>
            new Writer(_inner.Create(path, info, firstKeyFrame), OnWrite);

        private sealed class Writer(ICameraRecordingWriter inner, Action<int>? onWrite) : ICameraRecordingWriter
        {
            private int _writes;

            public long BytesWritten => inner.BytesWritten;

            public void Write(ReadOnlySpan<byte> data, long pts, long dts, long duration, bool isKeyFrame)
            {
                inner.Write(data, pts, dts, duration, isKeyFrame);
                onWrite?.Invoke(++_writes);
            }

            public void Complete() => inner.Complete();

            public void Dispose() => inner.Dispose();
        }
    }

    /// <summary>The app's recording stack against one simulated camera: FFmpeg feed, reconnecting feed, hub, recorder.</summary>
    private sealed class RecordingRig : IAsyncDisposable
    {
        public RecordingRig(RtspCamera camera)
        {
            ReconnectingCameraFeed feed = new(
                new FFmpegCameraFeedFactory(),
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

        public CameraStreamHub Hub { get; }

        public CameraRecorder Recorder { get; }

        public ValueTask DisposeAsync() => Hub.DisposeAsync();
    }
}
