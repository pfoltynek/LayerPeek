using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Tests.Monitoring;

namespace LayerPeek.Core.Tests.Cameras.Recording;

/// <summary>T-144: when files start and end, timestamps, failures and the end of a recording (fake writer, real files).</summary>
public sealed class CameraRecordingTests : IDisposable
{
    private readonly string _folder = TestDirectories.NewDirectoryPath();
    private readonly ListLogger<CameraRecorder> _logger = new();
    private readonly TestPackets _packets = new();

    public CameraRecordingTests() => Directory.CreateDirectory(_folder);

    private string BasePath => Path.Combine(_folder, "mk4 print.mp4");

    public void Dispose() => TestDirectories.Delete(_folder);

    [Fact]
    public async Task PacketsBeforeTheStreamDescriptionAndBeforeTheFirstKeyFrameAreDropped()
    {
        FakeRecordingWriterFactory writers = new();
        CameraRecording recording = Start(writers);

        // A packet of an old connection right after an address change (T-143 review), then the new connection.
        _packets.Packet(9, keyFrame: true);
        _packets.Started();
        _packets.Packet(1);
        _packets.Packet(2);
        _packets.Packet(3, keyFrame: true, length: 20);
        _packets.Packet(4);
        // Ending discards what the reader has not taken yet (like quitting does), so the test waits for it.
        await Eventually.TrueAsync(() => writers.Writers is [{ Count: 2 }], "both packets written");
        _packets.End(CameraStreamEndReason.HubStopped);
        await recording.Completion.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        FakeRecordingWriter writer = Assert.Single(writers.Writers);
        Assert.Equal([3, 4], writer.Packets.Select(static p => p.Fill));
        Assert.True(writer.Packets[0].IsKeyFrame);
        Assert.Equal((byte)3, writer.FirstKeyFrame[0]);
        Assert.Equal(20, writer.FirstKeyFrame.Length);
        Assert.Same(TestPackets.Info, writer.Info);
        Assert.Equal([0L, 4500L], writer.Packets.Select(static p => p.Dts));
        Assert.True(writer.IsCompleted);
        Assert.True(writer.IsDisposed);
        Assert.Equal([BasePath], recording.Files);
        Assert.Equal(CameraRecordingState.Completed, recording.State);
        Assert.Equal(CameraRecordingFailure.None, recording.Failure);
        Assert.Null(recording.FailureMessage);
        Assert.Equal(36, recording.BytesWritten);
        Assert.Equal(TimeSpan.FromSeconds(0.1), recording.Duration);
    }

    [Fact]
    public async Task AStreamRestartCompletesTheFileAndStartsTheNextWithASuffixAtTheNextKeyFrame()
    {
        FakeRecordingWriterFactory writers = new();
        List<(CameraRecordingState State, int Files)> changes = [];
        CameraRecording recording = Start(writers, r => changes.Add((r.State, r.Files.Count)));

        _packets.Started();
        _packets.Packet(1, keyFrame: true);
        _packets.Packet(2);
        _packets.Started();
        _packets.Packet(3);
        _packets.Packet(4, keyFrame: true);
        _packets.Packet(5);
        _packets.Started();
        _packets.Packet(6, keyFrame: true);
        await Eventually.TrueAsync(() => writers.Writers is [_, _, { Count: 1 }], "third file written");
        await recording.StopAsync();

        FakeRecordingWriter[] files = writers.Writers;
        Assert.Equal(3, files.Length);
        Assert.Equal([1, 2], files[0].Packets.Select(static p => p.Fill));
        Assert.Equal([4, 5], files[1].Packets.Select(static p => p.Fill));
        Assert.Equal([6], files[2].Packets.Select(static p => p.Fill));
        Assert.All(files, static f => Assert.True(f.IsCompleted && f.IsDisposed));
        // Every file starts at 0.
        Assert.All(files, static f => Assert.Equal(0, f.Packets[0].Dts));
        Assert.Equal(
            [BasePath, Path.Combine(_folder, "mk4 print-2.mp4"), Path.Combine(_folder, "mk4 print-3.mp4")],
            recording.Files);
        Assert.All(recording.Files, static f => Assert.True(File.Exists(f)));
        Assert.Equal(
            [(CameraRecordingState.Recording, 1), (CameraRecordingState.Recording, 2), (CameraRecordingState.Recording, 3), (CameraRecordingState.Completed, 3)],
            changes);
        // 0.1 s + 0.1 s + 0.05 s of video.
        Assert.Equal(TimeSpan.FromSeconds(0.25), recording.Duration);
        Assert.Equal(5 * 16, recording.BytesWritten);
    }

    [Fact]
    public async Task AnExistingFileIsNeverOverwritten()
    {
        await File.WriteAllTextAsync(BasePath, "user data", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_folder, "mk4 print-2.mp4"), "user data", TestContext.Current.CancellationToken);
        FakeRecordingWriterFactory writers = new();
        CameraRecording recording = Start(writers);

        _packets.Started();
        _packets.Packet(1, keyFrame: true);
        await Eventually.TrueAsync(() => recording.Files.Count == 1, "file started");
        await recording.StopAsync();

        Assert.Equal([Path.Combine(_folder, "mk4 print-3.mp4")], recording.Files);
        Assert.Equal("user data", await File.ReadAllTextAsync(BasePath, TestContext.Current.CancellationToken));
        Assert.Equal("user data", await File.ReadAllTextAsync(Path.Combine(_folder, "mk4 print-2.mp4"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WithoutAKeyFrameNoFileIsCreated()
    {
        FakeRecordingWriterFactory writers = new();
        CameraRecording recording = Start(writers);

        _packets.Started();
        _packets.Packet(1);
        _packets.Packet(2);
        await recording.StopAsync();

        Assert.Empty(writers.Writers);
        Assert.Empty(recording.Files);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_folder));
        Assert.Equal(CameraRecordingState.Completed, recording.State);
        Assert.Equal(TimeSpan.Zero, recording.Duration);
    }

    [Fact]
    public async Task StopIsIdempotentAndEndsTheSubscription()
    {
        CameraRecording recording = Start(new FakeRecordingWriterFactory());
        Assert.Equal(CameraRecordingState.Starting, recording.State);

        await recording.StopAsync();
        await recording.StopAsync();
        await recording.DisposeAsync();

        Assert.True(_packets.Subscription.IsEnded);
        Assert.Equal(CameraStreamEndReason.Disposed, _packets.Subscription.EndReason);
        Assert.Equal(CameraRecordingState.Completed, recording.State);
        Assert.True(recording.Completion.IsCompletedSuccessfully);
    }

    [Theory]
    [InlineData(CameraStreamEndReason.HubStopped, CameraRecordingState.Completed, CameraRecordingFailure.None)]
    [InlineData(CameraStreamEndReason.CameraUnavailable, CameraRecordingState.Failed, CameraRecordingFailure.CameraUnavailable)]
    [InlineData(CameraStreamEndReason.Overflow, CameraRecordingState.Failed, CameraRecordingFailure.TooSlow)]
    [InlineData(CameraStreamEndReason.Failed, CameraRecordingState.Failed, CameraRecordingFailure.StreamFailed)]
    public async Task TheEndOfTheSubscriptionCompletesTheFileAndDecidesTheOutcome(CameraStreamEndReason reason, CameraRecordingState state, CameraRecordingFailure failure)
    {
        FakeRecordingWriterFactory writers = new();
        CameraRecording recording = Start(writers);

        _packets.Started();
        _packets.Packet(1, keyFrame: true);
        await Eventually.TrueAsync(() => recording.BytesWritten > 0, "packet written");
        _packets.End(reason);
        await recording.Completion.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(writers.Writers).IsCompleted);
        Assert.Equal(state, recording.State);
        Assert.Equal(failure, recording.Failure);
        Assert.Equal(failure != CameraRecordingFailure.None, recording.FailureMessage is not null);
    }

    [Fact]
    public async Task AWriteFailureFailsTheRecordingAndStillCompletesTheFile()
    {
        FakeRecordingWriterFactory writers = new()
        {
            WriteFailure = (3, new CameraRecordingException(CameraRecordingFailure.DiskFull, "Writing the recording failed: No space left on device.")),
        };
        CameraRecording recording = Start(writers);

        _packets.Started();
        _packets.Packet(1, keyFrame: true);
        _packets.Packet(2);
        _packets.Packet(3);
        _packets.Packet(4);
        await recording.Completion.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        FakeRecordingWriter writer = Assert.Single(writers.Writers);
        Assert.Equal(2, writer.Count);
        Assert.True(writer.IsCompleted, "What was written stays readable: the end of the container is still written.");
        Assert.True(writer.IsDisposed);
        Assert.Equal(CameraRecordingState.Failed, recording.State);
        Assert.Equal(CameraRecordingFailure.DiskFull, recording.Failure);
        Assert.Equal("Writing the recording failed: No space left on device.", recording.FailureMessage);
        Assert.True(_packets.Subscription.IsEnded);
        Assert.Contains(_logger.Entries, static e => e.Message.Contains("DiskFull", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFailureWhileCompletingTheFileFailsTheRecording()
    {
        FakeRecordingWriterFactory writers = new() { CompleteFailure = new CameraRecordingException("Completing the recording failed: I/O error.") };
        CameraRecording recording = Start(writers);

        _packets.Started();
        _packets.Packet(1, keyFrame: true);
        await Eventually.TrueAsync(() => recording.BytesWritten > 0, "first packet written");
        await recording.StopAsync();

        Assert.True(Assert.Single(writers.Writers).IsDisposed);
        Assert.Equal(CameraRecordingFailure.WriteFailed, recording.Failure);
    }

    [Fact]
    public async Task AFileTheWriterCannotOpenIsRemovedAgain()
    {
        FakeRecordingWriterFactory writers = new() { CreateFailure = new CameraRecordingException(CameraRecordingFailure.StreamFailed, "The camera stream has no H.264 parameter sets (SPS/PPS) to record.") };
        CameraRecording recording = Start(writers);

        _packets.Started();
        _packets.Packet(1, keyFrame: true);
        await recording.Completion.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        Assert.Equal(CameraRecordingFailure.StreamFailed, recording.Failure);
        Assert.Empty(recording.Files);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_folder));
    }

    [Fact]
    public async Task AMissingFolderFailsWithAReasonWithoutThePath()
    {
        string missing = Path.Combine(_folder, "missing", "print.mp4");
        CameraRecording recording = CameraRecording.Start("mk4", missing, _packets.Subscription, new FakeRecordingWriterFactory(), null, _logger);

        _packets.Started();
        _packets.Packet(1, keyFrame: true);
        await recording.Completion.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        Assert.Equal(CameraRecordingState.Failed, recording.State);
        Assert.Equal(CameraRecordingFailure.WriteFailed, recording.Failure);
        Assert.Equal("The recording folder does not exist.", recording.FailureMessage);
        Assert.DoesNotContain(_logger.Entries, e => e.Message.Contains(_folder, StringComparison.Ordinal));
    }

    [Fact]
    public async Task LogsNameFilesWithoutTheirFolder()
    {
        CameraRecording recording = Start(new FakeRecordingWriterFactory());
        _packets.Started();
        _packets.Packet(1, keyFrame: true);
        _packets.Started();
        _packets.Packet(2, keyFrame: true);
        await Eventually.TrueAsync(() => recording.Files.Count == 2, "second file");
        await recording.StopAsync();

        Assert.Contains(_logger.Entries, static e => e.Message.Contains("'mk4 print-2.mp4'", StringComparison.Ordinal));
        Assert.DoesNotContain(_logger.Entries, e => e.Message.Contains(_folder, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFailingObserverDoesNotStopTheRecording()
    {
        FakeRecordingWriterFactory writers = new();
        CameraRecording recording = Start(writers, static _ => throw new InvalidOperationException("observer"));

        _packets.Started();
        _packets.Packet(1, keyFrame: true);
        _packets.Packet(2);
        await Eventually.TrueAsync(() => recording.BytesWritten == 32, "both packets written");
        await recording.StopAsync();

        Assert.Equal(CameraRecordingState.Completed, recording.State);
        Assert.Equal(2, Assert.Single(writers.Writers).Count);
    }

    [Fact]
    public async Task SizeAndDurationGrowWhileRecording()
    {
        CameraRecording recording = Start(new FakeRecordingWriterFactory());
        _packets.Started();
        _packets.Packet(1, keyFrame: true, length: 10);
        for (int i = 0; i < 39; i++)
        {
            _packets.Packet(2, length: 10);
        }

        await Eventually.TrueAsync(() => recording.BytesWritten == 400, "all packets written");

        // 40 packets of 1/20 s.
        Assert.Equal(TimeSpan.FromSeconds(2), recording.Duration);
        Assert.Equal(CameraRecordingState.Recording, recording.State);
        await recording.StopAsync();
    }

    [Fact]
    public async Task AnObserverOfAnAtOnceEndedRecordingSeesItsRealCompletion()
    {
        // The lease ended before the recording runs (camera switched off): the first callback comes at once.
        _packets.End(CameraStreamEndReason.CameraUnavailable);
        TaskCompletionSource<bool> completedInCallback = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CameraRecording recording = Start(new FakeRecordingWriterFactory(), r => completedInCallback.TrySetResult(r.Completion.IsCompleted));

        await recording.Completion.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        Assert.False(await completedInCallback.Task, "Completion completes after the last callback, never before it is set.");
        Assert.Equal(CameraRecordingFailure.CameraUnavailable, recording.Failure);
    }

    [Fact]
    public void DiskFullIsRecognizedByThePlatformsErrorCode()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.True(CameraRecording.IsDiskFull(new IOException("full", unchecked((int)0x80070070))));
            Assert.True(CameraRecording.IsDiskFull(new IOException("full", unchecked((int)0x80070027))));
            Assert.False(CameraRecording.IsDiskFull(new IOException("paper", unchecked((int)0x8007001C))));
        }
        else
        {
            // ENOSPC as .NET reports it on macOS and Linux (verified on a full RAM disk, T-144 review).
            Assert.True(CameraRecording.IsDiskFull(new IOException("full", 28)));
            Assert.False(CameraRecording.IsDiskFull(new IOException("not empty", 39)));
        }

        Assert.False(CameraRecording.IsDiskFull(new UnauthorizedAccessException()));
    }

    private CameraRecording Start(FakeRecordingWriterFactory writers, Action<ICameraRecording>? changed = null) =>
        CameraRecording.Start("mk4", BasePath, _packets.Subscription, writers, changed, _logger);
}
