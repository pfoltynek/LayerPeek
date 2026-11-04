using System.Collections.Concurrent;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Tests.Monitoring;
using LayerPeek.Tests.Allocation;

namespace LayerPeek.Core.Tests.Cameras.Recording;

/// <summary>
/// T-148: the dashcam over a packet subscription: what is held, what a save writes (before and after the event), its
/// end and failures (fake writer, fake clock, real files).
/// </summary>
public sealed class CameraDashcamTests : IAsyncDisposable
{
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(100);

    private static readonly CameraDashcamOptions Options = new()
    {
        BufferDuration = TimeSpan.FromSeconds(2),
        PostEventDuration = TimeSpan.FromSeconds(1),
    };

    private readonly string _folder = TestDirectories.NewDirectoryPath();
    private readonly TestPackets _packets = new();
    private readonly FakeTimeProvider _time = new();
    private readonly ConcurrentQueue<CameraDashcamSave> _saves = new();
    private readonly ConcurrentQueue<CameraDashcamState> _states = new();
    private CameraDashcam? _dashcam;
    private int _sent;

    public CameraDashcamTests() => Directory.CreateDirectory(_folder);

    private string FilePath => Path.Combine(_folder, "2026-11-04_14-05-09_dashcam-error.mp4");

    public async ValueTask DisposeAsync()
    {
        if (_dashcam is not null)
        {
            await _dashcam.StopAsync().WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        }

        TestDirectories.Delete(_folder);
    }

    private CameraDashcam Start(FakeRecordingWriterFactory writers, CameraDashcamOptions? options = null)
    {
        _dashcam = CameraDashcam.Start(
            "mk4", _packets.Subscription, writers, _time, options ?? Options, d => _states.Enqueue(d.State), _saves.Enqueue, NullLogger.Instance);
        return _dashcam;
    }

    /// <summary>The stream description, read by the dashcam.</summary>
    private async Task StartedAsync()
    {
        _packets.Started();
        _sent++;
        await ReadAsync();
    }

    /// <summary>
    /// <paramref name="count"/> packets, one every 100 ms of the fake clock (each read by the dashcam before the clock moves
    /// on), a key frame every <paramref name="gop"/> packets; the fill counts on from <paramref name="first"/>.
    /// </summary>
    private async Task FeedAsync(int count, int gop = 5, int first = 0)
    {
        for (int i = 0; i < count; i++)
        {
            int number = first + i;
            _packets.Packet((byte)number, keyFrame: number % gop == 0);
            _sent++;
            await ReadAsync();
            _time.Advance(Step);
        }
    }

    private async Task ReadAsync() =>
        await Eventually.TrueAsync(() => _dashcam!.ItemsRead == _sent || _dashcam.State == CameraDashcamState.Ended, $"item {_sent} read");

    private async Task<CameraDashcamSave> SavedAsync(int number = 1)
    {
        await Eventually.TrueAsync(() => _saves.Count >= number, $"save {number} reported");
        return _saves.ElementAt(number - 1);
    }

    [Fact]
    public async Task TheBufferHoldsAtMostTheLengthFromAKeyFrameAndASaveWritesItAndTheTimeAfterTheEvent()
    {
        FakeRecordingWriterFactory writers = new();
        CameraDashcam dashcam = Start(writers);
        await StartedAsync();
        // Starts in the middle of a GOP: those packets are never held.
        await FeedAsync(50, first: 3);
        Assert.InRange(dashcam.BufferedDuration, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(2));
        Assert.True(dashcam.BufferedBytes > 0);
        Assert.Empty(writers.Writers);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_folder));

        Assert.True(dashcam.Save(FilePath));
        Assert.Equal(CameraDashcamState.Saving, dashcam.State);
        // The event was at packet 53; 1 s after it (10 packets) the save ends.
        await FeedAsync(15, first: 53);
        CameraDashcamSave save = await SavedAsync();

        FakeRecordingWriter writer = Assert.Single(writers.Writers);
        byte[] fills = [.. writer.Packets.Select(static p => p.Fill)];
        Assert.True(writer.Packets[0].IsKeyFrame);
        Assert.Equal(0, fills[0] % 5);
        // Before the event: from a key frame at most 2 s back; after it: the post-event second.
        int before = Array.IndexOf(fills, (byte)53);
        Assert.InRange(before, 16, 21);
        Assert.InRange(fills.Length - before, 10, 12);
        Assert.Equal(Enumerable.Range(fills[0], fills.Length).Select(static n => (byte)n), fills);
        Assert.Equal(0, writer.Packets[0].Dts);
        Assert.True(writer.IsCompleted && writer.IsDisposed);

        Assert.Equal([FilePath], save.Files);
        Assert.Equal(CameraRecordingFailure.None, save.Failure);
        Assert.Equal(writer.BytesWritten, save.BytesWritten);
        Assert.Equal(CameraDashcamState.Buffering, dashcam.State);
        Assert.Equal([CameraDashcamState.Saving, CameraDashcamState.Buffering], _states);
        Assert.Empty(dashcam.SaveFiles);
        Assert.Equal(0, dashcam.SaveBytesWritten);
        // It goes on buffering after the save.
        Assert.True(dashcam.BufferedDuration > TimeSpan.FromSeconds(1.5));
    }

    [Fact]
    public async Task AnotherEventDuringTheSaveExtendsIt()
    {
        FakeRecordingWriterFactory writers = new();
        CameraDashcam dashcam = Start(writers);
        await StartedAsync();
        await FeedAsync(20);

        Assert.True(dashcam.Save(FilePath));
        await FeedAsync(5, first: 20);
        Assert.True(dashcam.Save(Path.Combine(_folder, "never-used.mp4")));
        await FeedAsync(8, first: 25);
        Assert.Empty(_saves);
        await FeedAsync(5, first: 33);

        CameraDashcamSave save = await SavedAsync();
        Assert.Equal([FilePath], save.Files);
        FakeRecordingWriter writer = Assert.Single(writers.Writers);
        // 1 s after the second event (at packet 25).
        Assert.InRange(writer.Packets[^1].Fill, 34, 36);
        Assert.False(File.Exists(Path.Combine(_folder, "never-used.mp4")));
    }

    [Fact]
    public async Task AStalledCameraStillEndsTheSaveOnTime()
    {
        FakeRecordingWriterFactory writers = new();
        CameraDashcam dashcam = Start(writers);
        await StartedAsync();
        await FeedAsync(10);

        Assert.True(dashcam.Save(FilePath));
        await Eventually.TrueAsync(() => writers.Writers is [{ Count: > 0 }], "buffer written");
        _time.Advance(TimeSpan.FromSeconds(1));

        CameraDashcamSave save = await SavedAsync();
        Assert.Equal([FilePath], save.Files);
        Assert.True(writers.Writers[0].IsCompleted);
        await Eventually.TrueAsync(() => dashcam.State == CameraDashcamState.Buffering, "buffering again");
    }

    [Fact]
    public async Task EndingAfterTheSaveReleasesTheBufferAtOnceAndEndsWhenTheSaveIsDone()
    {
        FakeRecordingWriterFactory writers = new();
        CameraDashcam dashcam = Start(writers);
        await StartedAsync();
        await FeedAsync(20);
        Assert.True(dashcam.Save(FilePath));

        dashcam.EndAfterSave();
        await FeedAsync(3, first: 20);
        Assert.Equal(0, dashcam.BufferedBytes);
        Assert.Equal(CameraDashcamState.Saving, dashcam.State);

        await FeedAsync(10, first: 23);
        await dashcam.Completion.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        Assert.Equal(CameraDashcamState.Ended, dashcam.State);
        Assert.Equal(CameraRecordingFailure.None, dashcam.Failure);
        Assert.Equal([FilePath], (await SavedAsync()).Files);
        Assert.True(_packets.Subscription.IsEnded);
        Assert.False(dashcam.Save(FilePath));
    }

    [Fact]
    public async Task EndingWithoutASaveEndsAtOnce()
    {
        CameraDashcam dashcam = Start(new FakeRecordingWriterFactory());
        await StartedAsync();
        await FeedAsync(5);

        dashcam.EndAfterSave();
        dashcam.EndAfterSave();

        await dashcam.Completion.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        Assert.Equal(CameraDashcamState.Ended, dashcam.State);
        Assert.Equal(0, dashcam.BufferedBytes);
        Assert.False(dashcam.Save(FilePath));
        Assert.Empty(_saves);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_folder));
    }

    [Fact]
    public async Task StoppingCompletesARunningSave()
    {
        FakeRecordingWriterFactory writers = new();
        CameraDashcam dashcam = Start(writers);
        await StartedAsync();
        await FeedAsync(10);
        Assert.True(dashcam.Save(FilePath));
        await FeedAsync(2, first: 10);

        await dashcam.StopAsync().WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        await dashcam.StopAsync().WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        Assert.Equal(CameraDashcamState.Ended, dashcam.State);
        Assert.True(Assert.Single(writers.Writers).IsCompleted);
        Assert.Equal([FilePath], Assert.Single(_saves).Files);
        Assert.Equal(0, dashcam.BufferedBytes);
        Assert.True(_packets.Subscription.IsEnded);
    }

    [Fact]
    public async Task ANewConnectionClearsTheBufferAndASaveGoesOnInTheNextFile()
    {
        FakeRecordingWriterFactory writers = new();
        CameraDashcam dashcam = Start(writers);
        await StartedAsync();
        await FeedAsync(10);
        await StartedAsync();
        Assert.Equal(TimeSpan.Zero, dashcam.BufferedDuration);
        await FeedAsync(4, first: 11);
        Assert.Equal(TimeSpan.Zero, dashcam.BufferedDuration);
        await FeedAsync(3, first: 15);

        Assert.True(dashcam.Save(FilePath));
        await FeedAsync(2, first: 18);
        await StartedAsync();
        await FeedAsync(10, first: 20);

        CameraDashcamSave save = await SavedAsync();
        Assert.Equal([FilePath, Path.Combine(_folder, "2026-11-04_14-05-09_dashcam-error-2.mp4")], save.Files);
        Assert.Equal([15, 16, 17, 18, 19], writers.Writers[0].Packets.Select(static p => (int)p.Fill));
        Assert.Equal(20, writers.Writers[1].Packets[0].Fill);
        Assert.All(writers.Writers, static w => Assert.True(w.IsCompleted));
    }

    [Fact]
    public async Task ASaveThatCannotBeWrittenIsReportedAndBufferingGoesOn()
    {
        FakeRecordingWriterFactory writers = new() { WriteFailure = (3, new CameraRecordingException(CameraRecordingFailure.DiskFull, "The disk is full.")) };
        CameraDashcam dashcam = Start(writers);
        await StartedAsync();
        await FeedAsync(10);

        Assert.True(dashcam.Save(FilePath));
        CameraDashcamSave save = await SavedAsync();

        Assert.Equal(CameraRecordingFailure.DiskFull, save.Failure);
        Assert.Equal("The disk is full.", save.FailureMessage);
        Assert.Equal([FilePath], save.Files);
        await Eventually.TrueAsync(() => dashcam.State == CameraDashcamState.Buffering, "buffering again");
        await FeedAsync(5, first: 10);
        Assert.True(dashcam.BufferedDuration > TimeSpan.FromSeconds(1));
        Assert.True(dashcam.Save(Path.Combine(_folder, "next.mp4")));
    }

    [Theory]
    [InlineData(CameraStreamEndReason.Overflow, CameraRecordingFailure.TooSlow)]
    [InlineData(CameraStreamEndReason.CameraUnavailable, CameraRecordingFailure.CameraUnavailable)]
    [InlineData(CameraStreamEndReason.Failed, CameraRecordingFailure.StreamFailed)]
    [InlineData(CameraStreamEndReason.HubStopped, CameraRecordingFailure.None)]
    public async Task TheEndOfTheSubscriptionEndsTheDashcam(CameraStreamEndReason reason, CameraRecordingFailure failure)
    {
        CameraDashcam dashcam = Start(new FakeRecordingWriterFactory());
        await StartedAsync();
        await FeedAsync(5);

        _packets.End(reason);

        await dashcam.Completion.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        Assert.Equal(CameraDashcamState.Ended, dashcam.State);
        Assert.Equal(failure, dashcam.Failure);
        Assert.Equal(failure == CameraRecordingFailure.None, dashcam.FailureMessage is null);
        Assert.Equal(0, dashcam.BufferedBytes);
    }

    [Fact]
    public void WithoutTheMuxerTheDashcamEndsAtOnce()
    {
        CameraDashcam dashcam = CameraDashcam.FailedAtOnce("mk4", Options, CameraRecordingFailure.Unavailable, "No MP4 muxer.", NullLogger.Instance);

        Assert.Equal(CameraDashcamState.Ended, dashcam.State);
        Assert.Equal(CameraRecordingFailure.Unavailable, dashcam.Failure);
        Assert.True(dashcam.Completion.IsCompleted);
        Assert.False(dashcam.Save(FilePath));
        Assert.Throws<ArgumentException>(() => dashcam.Save("relative.mp4"));
    }

    [Fact]
    public void TheBoundsFollowTheLength()
    {
        CameraDashcamOptions options = new() { BufferDuration = TimeSpan.FromMinutes(2) };

        Assert.Equal(120L * CameraDashcamOptions.DefaultMaxBytesPerSecond, options.MaxBufferBytes);
        Assert.Equal(120 * CameraDashcamOptions.DefaultMaxPacketsPerSecond, options.MaxBufferPackets);
        Assert.Equal(TimeSpan.FromSeconds(30), CameraDashcamOptions.Default.PostEventDuration);
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraDashcamOptions { BufferDuration = TimeSpan.Zero });
    }
}

/// <summary>T-148: a buffering dashcam allocates nothing per packet; isolated, see <see cref="AllocationSensitive"/>.</summary>
[Collection(nameof(AllocationSensitive))]
public sealed class CameraDashcamAllocationTests
{
    private const int WarmUpPackets = 300;
    private const int MeasuredPackets = 200;

    [Fact]
    public async Task SteadyStateBufferingDoesNotAllocate()
    {
        long minimum = long.MaxValue;
        for (int pass = 0; pass < 3 && minimum > 0; pass++)
        {
            // The dashcam reads on the thread pool: all threads are counted; a stray allocation elsewhere does not repeat
            // on every pass, a per-packet one would.
            minimum = Math.Min(minimum, await MeasurePassAsync());
        }

        Assert.Equal(0, minimum);
    }

    private static async Task<long> MeasurePassAsync()
    {
        TestPackets packets = new(capacity: 512);
        SteppedClock time = new();
        CameraDashcam dashcam = CameraDashcam.Start(
            "mk4", packets.Subscription, new FakeRecordingWriterFactory(), time, new CameraDashcamOptions { BufferDuration = TimeSpan.FromSeconds(5) }, null, null, NullLogger.Instance);
        packets.Started();
        long sent = 1;

        // Warm-up: 15 s at 20 packets per second fill the buffer (5 s), which drops GOPs off the front from then on.
        for (int i = 0; i < WarmUpPackets; i++)
        {
            sent = Send(packets, time, dashcam, sent, i);
        }

        long before = GC.GetTotalAllocatedBytes(precise: true);
        for (int i = 0; i < MeasuredPackets; i++)
        {
            sent = Send(packets, time, dashcam, sent, i);
        }

        long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        Assert.InRange(dashcam.BufferedDuration, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5));
        await dashcam.StopAsync().WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        return allocated;
    }

    /// <summary>One packet, paced like a camera without awaiting (an await could allocate): spins until it is read.</summary>
    private static long Send(TestPackets packets, SteppedClock time, CameraDashcam dashcam, long sent, int number)
    {
        packets.Packet((byte)number, keyFrame: number % 20 == 0);
        sent++;
        DateTime deadline = DateTime.UtcNow + TestTimeouts.Wait;
        SpinWait spin = default;
        while (dashcam.ItemsRead < sent)
        {
            Assert.True(DateTime.UtcNow < deadline, "Packets not read.");
            spin.SpinOnce();
        }

        time.Ticks += 50;
        return sent;
    }

    /// <summary>Milliseconds that the test moves on by hand; no timers (a buffering dashcam creates none).</summary>
    private sealed class SteppedClock : TimeProvider
    {
        private long _ticks;

        public long Ticks
        {
            get => Volatile.Read(ref _ticks);
            set => Volatile.Write(ref _ticks, value);
        }

        public override long TimestampFrequency => 1000;

        public override long GetTimestamp() => Ticks;
    }
}
