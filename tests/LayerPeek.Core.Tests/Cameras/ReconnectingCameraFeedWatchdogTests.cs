using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Tests.Monitoring;

namespace LayerPeek.Core.Tests.Cameras;

/// <summary>
/// Races of the frame watchdog: a timer callback that runs late (it was already running when a frame arrived) or early
/// (it belongs to an earlier arming) must not cancel the stream. The test fires the timer callback itself.
/// </summary>
public sealed class ReconnectingCameraFeedWatchdogTests : IAsyncDisposable
{
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private static readonly CameraFeedOptions Options = new()
    {
        ConnectTimeout = TimeSpan.FromSeconds(20),
        FrameTimeout = TimeSpan.FromSeconds(10),
        ReconnectDelay = TimeSpan.FromSeconds(5),
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ManualTimeProvider _time = new();
    private readonly FakeCameraFeedFactory _factory = new();
    private readonly RecordingProgress _status = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly IAsyncEnumerator<CameraFrame> _frames;
    private Task<bool>? _pending;

    public ReconnectingCameraFeedWatchdogTests()
    {
        ReconnectingCameraFeed camera = new(_factory, _time, NullLogger<ReconnectingCameraFeed>.Instance, Options);
        _frames = camera.ReadFramesAsync(StreamUri, _status, _cts.Token).GetAsyncEnumerator(_cts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        if (_pending is not null)
        {
            // An async iterator cannot be disposed while a MoveNextAsync is pending.
            await ((Task)_pending).WaitAsync(TestTimeouts.Wait, Ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        await _frames.DisposeAsync();
        _cts.Dispose();
    }

    private Task<bool> Next() => _pending = _frames.MoveNextAsync().AsTask();

    [Fact]
    public async Task LateCallbackAfterAFrameDoesNotCancel()
    {
        FakeCameraFeed stream = await FirstFrameAsync();

        // Past the deadline of the read that just completed; its callback "was already running" when the frame came.
        _time.Advance(Options.ConnectTimeout + TimeSpan.FromSeconds(1));
        _time.Watchdog.Fire();

        await AssertStillStreamingAsync(stream);
    }

    [Fact]
    public async Task EarlyCallbackFromAnEarlierArmingDoesNotCancel()
    {
        FakeCameraFeed stream = await FirstFrameAsync();
        Task<bool> move = Next();
        await stream.NextReadAsync();

        // Armed for the next read just now; a callback before its deadline belongs to an earlier arming (or the
        // timer ran early). It must not cancel, but re-arm the timer for the rest of the time.
        Assert.Equal(Options.FrameTimeout, _time.Watchdog.DueTime);
        _time.Advance(Options.FrameTimeout - TimeSpan.FromSeconds(1));
        _time.Watchdog.Fire();

        Assert.Equal(TimeSpan.FromSeconds(1), _time.Watchdog.DueTime);
        Assert.False(move.IsCompleted);
        Assert.Equal([CameraStatus.Connecting, CameraStatus.Streaming], _status.All);

        // The re-armed timer then ends the read when the real deadline passes.
        _time.Advance(TimeSpan.FromSeconds(1));
        _time.Watchdog.Fire();

        Assert.Equal(CameraStatus.Connecting, await _status.NextAsync());
        Assert.Equal(CameraStatus.Streaming, await _status.NextAsync());
        Assert.Equal(CameraConnectionState.NoSignal, (await _status.NextAsync()).State);
        Assert.False(move.IsCompleted);
    }

    [Fact]
    public async Task CallbackAfterTheDeadlineOfThePendingReadReportsNoSignal()
    {
        FakeCameraFeed stream = await FirstFrameAsync();
        Task<bool> move = Next();
        await stream.NextReadAsync();

        _time.Advance(Options.FrameTimeout);
        _time.Watchdog.Fire();

        Assert.Equal(CameraStatus.Connecting, await _status.NextAsync());
        Assert.Equal(CameraStatus.Streaming, await _status.NextAsync());
        CameraStatus noSignal = await _status.NextAsync();
        Assert.Equal(CameraConnectionState.NoSignal, noSignal.State);
        Assert.False(move.IsCompleted);
    }

    private async Task<FakeCameraFeed> FirstFrameAsync()
    {
        Task<bool> move = Next();
        FakeCameraFeed stream = (await _factory.NextStreamAsync())!;
        stream.PushFrame(1);
        Assert.True(await move.WaitAsync(TestTimeouts.Wait, Ct));
        return stream;
    }

    private async Task AssertStillStreamingAsync(FakeCameraFeed stream)
    {
        Task<bool> move = Next();
        await stream.NextReadAsync();
        stream.PushFrame(2);
        Assert.True(await move.WaitAsync(TestTimeouts.Wait, Ct));
        Assert.Equal(2, _frames.Current.Pixels.Span[0]);
        Assert.Equal([CameraStatus.Connecting, CameraStatus.Streaming], _status.All);
        Assert.Equal(1, _factory.CreateCount);
    }

    /// <summary>Time moves only when told; timers never fire by themselves.</summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];
        private long _now;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        /// <summary>The first timer created: the watchdog (created before any reconnect delay).</summary>
        public ManualTimer Watchdog
        {
            get
            {
                lock (_timers)
                {
                    return _timers[0];
                }
            }
        }

        public override long GetTimestamp() => Volatile.Read(ref _now);

        public void Advance(TimeSpan by) => Interlocked.Add(ref _now, by.Ticks);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ManualTimer timer = new(callback, state);
            lock (_timers)
            {
                _timers.Add(timer);
            }

            return timer;
        }
    }

    private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
    {
        private readonly Lock _lock = new();
        private TimeSpan _dueTime = Timeout.InfiniteTimeSpan;

        /// <summary>Due time of the last <see cref="Change"/> (infinite: disarmed).</summary>
        public TimeSpan DueTime
        {
            get
            {
                lock (_lock)
                {
                    return _dueTime;
                }
            }
        }

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (_lock)
            {
                _dueTime = dueTime;
            }

            return true;
        }

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
