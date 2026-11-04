using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Cameras.Timelapse;
using LayerPeek.Core.Tests.Cameras.Recording;
using LayerPeek.Core.Tests.Monitoring;

namespace LayerPeek.Core.Tests.Cameras.Timelapse;

/// <summary>T-158: which key frames a timelapse takes, its timestamps, files, reconnects and failures (fake writer, real files).</summary>
public sealed class CameraTimelapseTests : IDisposable
{
    private static readonly byte[] SpsA = [0, 0, 0, 1, 0x67, 0x42, 0xC0, 0x1F];
    private static readonly byte[] SpsB = [0, 0, 0, 1, 0x67, 0x42, 0xC0, 0x28];
    private static readonly byte[] Pps = [0, 0, 0, 1, 0x68, 0xCE, 0x3C, 0x80];

    private readonly string _folder = TestDirectories.NewDirectoryPath();
    private readonly ListLogger<CameraTimelapseRecorder> _logger = new();
    private readonly TestPackets _packets = new();
    private CameraTimelapse? _timelapse;

    public CameraTimelapseTests() => Directory.CreateDirectory(_folder);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string BasePath => Path.Combine(_folder, "mk4_timelapse.mp4");

    public void Dispose() => TestDirectories.Delete(_folder);

    [Fact]
    public async Task OnlyTheFirstKeyFrameAfterARequestIsWrittenAtThirtyFramesPerSecond()
    {
        FakeRecordingWriterFactory writers = new();
        CameraTimelapse timelapse = Start(writers);
        _packets.Started();
        // Not asked for: nothing is written and no file exists.
        _packets.Packet(1, keyFrame: true);
        _packets.Packet(2);
        await ReadAsync(3);
        Assert.Empty(writers.Writers);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_folder));
        Assert.Equal(CameraRecordingState.Starting, timelapse.State);

        // A request waits for the next key frame; the packets in between are dropped.
        Assert.True(timelapse.RequestFrame());
        _packets.Packet(3);
        _packets.Packet(4, keyFrame: true, length: 20);
        _packets.Packet(5, keyFrame: true);
        await ReadAsync(6);
        // Several requests before the next key frame make one frame.
        timelapse.RequestFrame();
        timelapse.RequestFrame();
        timelapse.RequestFrame();
        _packets.Packet(6);
        _packets.Packet(7, keyFrame: true);
        _packets.Packet(8, keyFrame: true);
        await ReadAsync(9);
        timelapse.RequestFrame();
        _packets.Packet(9, keyFrame: true);
        await Eventually.TrueAsync(() => timelapse.FrameCount == 3, "three frames");
        await timelapse.StopAsync().WaitAsync(TestTimeouts.Wait, Ct);

        FakeRecordingWriter writer = Assert.Single(writers.Writers);
        Assert.Equal([4, 7, 9], writer.Packets.Select(static p => p.Fill));
        Assert.Equal(20, writer.FirstKeyFrame.Length);
        Assert.Same(TestPackets.Info, writer.Info);
        // Frame n at n / 30 s in the stream's 90 kHz clock, one frame long, whatever the camera's timestamps.
        Assert.Equal([new WrittenPacket(4, 20, 0, 0, 3000, true), new WrittenPacket(7, 16, 3000, 3000, 3000, true), new WrittenPacket(9, 16, 6000, 6000, 3000, true)], writer.Packets);
        Assert.True(writer.IsCompleted && writer.IsDisposed);
        Assert.Equal([BasePath], timelapse.Files);
        Assert.Equal(3, timelapse.FrameCount);
        Assert.Equal(TimeSpan.FromSeconds(0.1), timelapse.Duration);
        Assert.Equal(52, timelapse.BytesWritten);
        Assert.Equal(CameraRecordingState.Completed, timelapse.State);
        Assert.Equal(CameraRecordingFailure.None, timelapse.Failure);
        Assert.Null(timelapse.FailureMessage);
    }

    [Fact]
    public async Task ARequestBeforeTheCameraIsConnectedTakesItsFirstKeyFrame()
    {
        FakeRecordingWriterFactory writers = new();
        List<(CameraRecordingState State, int Files, long Frames)> changes = [];
        CameraTimelapse timelapse = Start(writers, t => changes.Add((t.State, t.Files.Count, t.FrameCount)));

        timelapse.RequestFrame();
        // A packet of an old connection right after an address change (T-143 review) is not taken.
        _packets.Packet(9, keyFrame: true);
        _packets.Started();
        _packets.Packet(1);
        _packets.Packet(2, keyFrame: true);
        await Eventually.TrueAsync(() => timelapse.FrameCount == 1, "first frame");
        _packets.End(CameraStreamEndReason.HubStopped);
        await timelapse.Completion.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal([2], Assert.Single(writers.Writers).Packets.Select(static p => p.Fill));
        Assert.Equal([(CameraRecordingState.Recording, 1, 1L), (CameraRecordingState.Completed, 1, 1L)], changes);
        Assert.False(timelapse.RequestFrame());
    }

    [Fact]
    public async Task AReconnectWithTheSameParameterSetsContinuesTheFile()
    {
        FakeRecordingWriterFactory writers = new();
        CameraTimelapse timelapse = Start(writers);

        _packets.Started();
        await TakeAsync(timelapse, KeyFrame(SpsA, 30), 1);
        // The same camera reconnects: a new (equal) stream description, in-band SPS/PPS as before.
        _packets.Started(new CameraStreamInfo(CameraVideoCodec.H264, 1280, 720, CameraTimeBase.Rtp90kHz, TestPackets.Info.Extradata.ToArray()));
        await TakeAsync(timelapse, KeyFrame(SpsA, 31), 2);
        await timelapse.StopAsync().WaitAsync(TestTimeouts.Wait, Ct);

        FakeRecordingWriter writer = Assert.Single(writers.Writers);
        Assert.Equal([0L, 3000L], writer.Packets.Select(static p => p.Dts));
        Assert.Equal([BasePath], timelapse.Files);
        // 2 / 30 s, rounded to the nearest tick.
        Assert.Equal(TimeSpan.FromTicks(666_667), timelapse.Duration);
    }

    [Fact]
    public async Task WithoutAStreamDescriptionTheInBandParameterSetsDecide()
    {
        CameraStreamInfo inBandOnly = new(CameraVideoCodec.H264, 0, 0, CameraTimeBase.Rtp90kHz, ReadOnlyMemory<byte>.Empty);
        FakeRecordingWriterFactory writers = new();
        CameraTimelapse timelapse = Start(writers);

        _packets.Started(inBandOnly);
        await TakeAsync(timelapse, KeyFrame(SpsA, 30), 1);
        _packets.Started(inBandOnly);
        await TakeAsync(timelapse, KeyFrame(SpsA, 31), 2);
        _packets.Started(inBandOnly);
        await TakeAsync(timelapse, KeyFrame(SpsB, 32), 3);
        await timelapse.StopAsync().WaitAsync(TestTimeouts.Wait, Ct);

        FakeRecordingWriter[] files = writers.Writers;
        Assert.Equal(2, files.Length);
        Assert.Equal([30, 31], files[0].Packets.Select(static p => p.Length));
        Assert.Equal([0L, 3000L], files[0].Packets.Select(static p => p.Dts));
        Assert.Equal([32], files[1].Packets.Select(static p => p.Length));
        Assert.Equal(0, files[1].Packets[0].Dts);
        Assert.Equal([BasePath, Path.Combine(_folder, "mk4_timelapse-2.mp4")], timelapse.Files);
        Assert.Equal(3, timelapse.FrameCount);
    }

    [Theory]
    [InlineData("extradata")]
    [InlineData("size")]
    public async Task AReconnectWithOtherStreamParametersStartsTheNextFile(string change)
    {
        CameraStreamInfo other = change == "size"
            ? new(CameraVideoCodec.H264, 1920, 1080, CameraTimeBase.Rtp90kHz, TestPackets.Info.Extradata)
            : new(CameraVideoCodec.H264, 1280, 720, CameraTimeBase.Rtp90kHz, new byte[] { 0, 0, 0, 1, 0x67, 0x64 });
        FakeRecordingWriterFactory writers = new();
        List<int> files = [];
        CameraTimelapse timelapse = Start(writers, t => files.Add(t.Files.Count));

        _packets.Started();
        await TakeAsync(timelapse, KeyFrame(SpsA, 30), 1);
        // Reconnected, but no frame asked for yet: the file stays as it is.
        _packets.Started(other);
        _packets.Packet(KeyFrame(SpsA, 40), keyFrame: true);
        await ReadAsync(4);
        Assert.Single(writers.Writers);
        Assert.False(writers.Writers[0].IsCompleted);
        await TakeAsync(timelapse, KeyFrame(SpsA, 31), 2);
        await timelapse.StopAsync().WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal(2, writers.Writers.Length);
        Assert.True(writers.Writers[0].IsCompleted);
        Assert.Same(other, writers.Writers[1].Info);
        Assert.Equal([31], writers.Writers[1].Packets.Select(static p => p.Length));
        Assert.Equal([BasePath, Path.Combine(_folder, "mk4_timelapse-2.mp4")], timelapse.Files);
        Assert.Equal([1, 2, 2], files);
        Assert.Contains(_logger.Entries, static e => e.Message.Contains("other stream parameters", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(CameraStreamEndReason.HubStopped, CameraRecordingState.Completed, CameraRecordingFailure.None)]
    [InlineData(CameraStreamEndReason.CameraUnavailable, CameraRecordingState.Failed, CameraRecordingFailure.CameraUnavailable)]
    [InlineData(CameraStreamEndReason.Overflow, CameraRecordingState.Failed, CameraRecordingFailure.TooSlow)]
    [InlineData(CameraStreamEndReason.Failed, CameraRecordingState.Failed, CameraRecordingFailure.StreamFailed)]
    public async Task TheEndOfTheSubscriptionEndsTheTimelapseAndCompletesTheFile(CameraStreamEndReason reason, CameraRecordingState state, CameraRecordingFailure failure)
    {
        FakeRecordingWriterFactory writers = new();
        CameraTimelapse timelapse = Start(writers);
        _packets.Started();
        await TakeAsync(timelapse, KeyFrame(SpsA, 30), 1);

        _packets.End(reason);
        await timelapse.Completion.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal(state, timelapse.State);
        Assert.Equal(failure, timelapse.Failure);
        Assert.Equal(failure != CameraRecordingFailure.None, timelapse.FailureMessage is not null);
        Assert.True(Assert.Single(writers.Writers).IsCompleted);
        Assert.False(timelapse.RequestFrame());
    }

    [Fact]
    public async Task AWriteFailureFailsTheTimelapseAndClosesTheFile()
    {
        FakeRecordingWriterFactory writers = new() { WriteFailure = (2, new CameraRecordingException(CameraRecordingFailure.DiskFull, "The disk is full.")) };
        CameraTimelapse timelapse = Start(writers);
        _packets.Started();
        await TakeAsync(timelapse, KeyFrame(SpsA, 30), 1);

        timelapse.RequestFrame();
        _packets.Packet(KeyFrame(SpsA, 31), keyFrame: true);
        await timelapse.Completion.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal(CameraRecordingState.Failed, timelapse.State);
        Assert.Equal(CameraRecordingFailure.DiskFull, timelapse.Failure);
        Assert.Equal("The disk is full.", timelapse.FailureMessage);
        Assert.Equal(1, timelapse.FrameCount);
        FakeRecordingWriter writer = Assert.Single(writers.Writers);
        Assert.True(writer.IsCompleted && writer.IsDisposed);
        Assert.True(_packets.Subscription.IsEnded);
    }

    [Fact]
    public async Task StopIsIdempotentAndARequestAfterItIsRefused()
    {
        CameraTimelapse timelapse = Start(new FakeRecordingWriterFactory());
        Assert.Equal(CameraRecordingState.Starting, timelapse.State);

        await timelapse.StopAsync().WaitAsync(TestTimeouts.Wait, Ct);
        await timelapse.StopAsync().WaitAsync(TestTimeouts.Wait, Ct);
        await timelapse.DisposeAsync();

        Assert.False(timelapse.RequestFrame());
        Assert.True(_packets.Subscription.IsEnded);
        Assert.Equal(CameraRecordingState.Completed, timelapse.State);
        Assert.Empty(timelapse.Files);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_folder));
        Assert.Equal(TimeSpan.Zero, timelapse.Duration);
    }

    [Fact]
    public async Task AFailingObserverDoesNotStopTheTimelapse()
    {
        FakeRecordingWriterFactory writers = new();
        CameraTimelapse timelapse = Start(writers, _ => throw new InvalidOperationException("observer"));
        _packets.Started();

        await TakeAsync(timelapse, KeyFrame(SpsA, 30), 1);
        await TakeAsync(timelapse, KeyFrame(SpsA, 31), 2);
        await timelapse.StopAsync().WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal(CameraRecordingState.Completed, timelapse.State);
        Assert.Contains(_logger.Entries, static e => e.Exception is InvalidOperationException);
    }

    [Fact]
    public void TheParameterSetsOfAnAccessUnitAreItsSpsAndPpsWithoutStartCodes()
    {
        Assert.Equal([0x67, 0x42, 0xC0, 0x1F, 0x68, 0xCE, 0x3C, 0x80], H264ParameterSets.Extract(KeyFrame(SpsA, 30)));
        // 3-byte start codes and trailing zeros.
        Assert.Equal([0x67, 0x42, 0x68, 0xCE], H264ParameterSets.Extract([0, 0, 1, 0x67, 0x42, 0, 0, 0, 0, 1, 0x68, 0xCE, 0, 0, 1, 0x65, 0x11]));
        Assert.Empty(H264ParameterSets.Extract([0, 0, 1, 0x65, 0x88, 0x84]));
        Assert.Empty(H264ParameterSets.Extract([1, 2, 3]));
        Assert.Empty(H264ParameterSets.Extract([]));
        Assert.Empty(H264ParameterSets.Extract([0, 0, 1]));
        // Start codes back to back, a parameter set's header as the very last byte, only zeros.
        Assert.Equal([0x67, 0x68], H264ParameterSets.Extract([0, 0, 1, 0, 0, 1, 0x67, 0, 0, 1, 0x68]));
        Assert.Empty(H264ParameterSets.Extract(new byte[64]));
    }

    [Fact]
    public void TheParameterSetsOfAnyBytesAreFoundWithoutThrowingOrReadingPastThem()
    {
        // The camera's bitstream is untrusted: random bytes dense with start codes and parameter set headers.
        Random random = new(158);
        byte[] alphabet = [0, 0, 0, 1, 0x67, 0x68, 0x65, 0x41, 0xFF];
        for (int round = 0; round < 2000; round++)
        {
            byte[] data = new byte[random.Next(0, 48)];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = alphabet[random.Next(alphabet.Length)];
            }

            byte[] sets = H264ParameterSets.Extract(data);

            Assert.True(sets.Length <= data.Length);
            Assert.Equal(sets, H264ParameterSets.Extract(data));
        }
    }

    /// <summary>An IDR access unit with in-band SPS/PPS of <paramref name="length"/> bytes (the length tells frames apart).</summary>
    private static byte[] KeyFrame(byte[] sps, int length)
    {
        byte[] data = new byte[length];
        sps.CopyTo(data, 0);
        Pps.CopyTo(data, sps.Length);
        int idr = sps.Length + Pps.Length;
        data[idr + 2] = 1;
        data[idr + 3] = 0x65;
        data.AsSpan(idr + 4).Fill(0x88);
        return data;
    }

    private CameraTimelapse Start(FakeRecordingWriterFactory writers, Action<ICameraTimelapse>? changed = null) =>
        _timelapse = CameraTimelapse.Start("mk4", BasePath, _packets.Subscription, writers, changed, _logger);

    /// <summary>Requests a frame and sends a key frame; waits until it is the <paramref name="frames"/>-th written.</summary>
    private async Task TakeAsync(CameraTimelapse timelapse, byte[] keyFrame, long frames)
    {
        Assert.True(timelapse.RequestFrame());
        _packets.Packet(keyFrame, keyFrame: true);
        await Eventually.TrueAsync(() => timelapse.FrameCount == frames, $"frame {frames} written");
    }

    /// <summary>Waits until the reader took <paramref name="items"/> stream items.</summary>
    private Task ReadAsync(long items) => Eventually.TrueAsync(() => _timelapse!.ItemsRead >= items, $"{items} items read");
}
