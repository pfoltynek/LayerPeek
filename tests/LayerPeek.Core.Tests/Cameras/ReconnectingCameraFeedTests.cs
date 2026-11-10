using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Tests.Monitoring;

namespace LayerPeek.Core.Tests.Cameras;

public sealed class ReconnectingCameraFeedTests : IAsyncDisposable
{
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private static readonly CameraFeedOptions Options = new()
    {
        ConnectTimeout = TimeSpan.FromSeconds(20),
        FrameTimeout = TimeSpan.FromSeconds(10),
        ReconnectDelay = TimeSpan.FromSeconds(5),
    };

    private static readonly TimeSpan Tick = TimeSpan.FromTicks(1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeTimeProvider _time = new();
    private readonly FakeCameraFeedFactory _factory = new();
    private readonly ListLogger<ReconnectingCameraFeed> _logger = new();
    private readonly RecordingProgress _status = new();
    private readonly CancellationTokenSource _cts = new();
    private ReconnectingCameraFeed _camera;
    private IAsyncEnumerator<CameraFrame>? _frames;
    private Task<bool>? _pending;

    public ReconnectingCameraFeedTests()
    {
        _camera = new ReconnectingCameraFeed(_factory, _time, _logger, Options);
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        if (_pending is not null)
        {
            await ((Task)_pending).WaitAsync(TestTimeouts.Wait, Ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        if (_frames is not null)
        {
            await _frames.DisposeAsync();
        }

        _cts.Dispose();
    }

    [Fact]
    public async Task FirstFrameReportsStreaming()
    {
        Task<bool> move = Next();
        FakeCameraFeed stream = (await _factory.NextStreamAsync())!;
        Assert.Equal(CameraStatus.Connecting, await _status.NextAsync());
        Assert.False(move.IsCompleted);

        stream.PushFrame(fill: 5, width: 3, height: 2);

        Assert.True(await move.WaitAsync(TestTimeouts.Wait, Ct));
        Assert.Equal(CameraStatus.Streaming, await _status.NextAsync());
        Assert.Equal(3, _frames!.Current.Width);
        Assert.Equal(2, _frames.Current.Height);
        Assert.Equal(5, _frames.Current.Pixels.Span[0]);
        Assert.Equal(StreamUri, _factory.LastUri);
    }

    [Fact]
    public async Task FramesArePassedThroughInOrderAndStreamingIsReportedOnce()
    {
        Task<bool> move = Next();
        FakeCameraFeed stream = (await _factory.NextStreamAsync())!;
        stream.PushFrame(1);
        stream.PushFrame(2);
        stream.PushFrame(3);

        for (byte expected = 1; expected <= 3; expected++)
        {
            Assert.True(await move.WaitAsync(TestTimeouts.Wait, Ct));
            Assert.Equal(expected, _frames!.Current.Pixels.Span[0]);
            if (expected < 3)
            {
                move = Next();
            }
        }

        Assert.Equal([CameraStatus.Connecting, CameraStatus.Streaming], _status.All);
        Assert.Equal(1, _factory.CreateCount);
    }

    [Fact]
    public async Task StreamEndReportsNoSignalAndReconnectsAfterDelay()
    {
        Task<bool> move = Next();
        FakeCameraFeed first = (await _factory.NextStreamAsync())!;
        first.PushFrame(1);
        Assert.True(await move.WaitAsync(TestTimeouts.Wait, Ct));
        move = Next();
        first.End();

        Assert.Equal(CameraStatus.Connecting, await _status.NextAsync());
        Assert.Equal(CameraStatus.Streaming, await _status.NextAsync());
        CameraStatus noSignal = await _status.NextAsync();
        Assert.Equal(CameraConnectionState.NoSignal, noSignal.State);
        Assert.False(string.IsNullOrWhiteSpace(noSignal.Reason));
        Assert.True(first.IsDisposed);

        _time.Advance(Options.ReconnectDelay - Tick);
        Assert.False(_factory.HasPendingCreate);

        _time.Advance(Tick);
        FakeCameraFeed second = (await _factory.NextStreamAsync())!;
        Assert.Equal(CameraStatus.Connecting, await _status.NextAsync());
        Assert.False(move.IsCompleted);

        second.PushFrame(2);
        Assert.True(await move.WaitAsync(TestTimeouts.Wait, Ct));
        Assert.Equal(2, _frames!.Current.Pixels.Span[0]);
        Assert.Equal(CameraStatus.Streaming, await _status.NextAsync());
    }

    [Fact]
    public async Task RepeatedFailureIsLoggedInFullOnlyTheFirstTime()
    {
        // An unreachable camera fails every few seconds; one stack trace is enough, then a short line per attempt.
        Task<bool> move = Next();
        foreach (string reason in new[] { "Connection refused.", "Connection refused.", "Connection timed out." })
        {
            (await _factory.NextStreamAsync())!.Fail(new CameraFeedException(reason));
            Assert.Equal(CameraStatus.Connecting, await _status.NextAsync());
            CameraStatus error = await _status.NextAsync();
            Assert.Equal(CameraConnectionState.Error, error.State);
            _time.Advance(error.NextAttemptAt!.Value - _time.GetUtcNow());
        }

        Assert.NotNull(await _factory.NextStreamAsync());

        var failures = _logger.Entries.Where(static e => e.Level == LogLevel.Warning).ToList();
        Assert.Equal(3, failures.Count);
        Assert.IsType<CameraFeedException>(failures[0].Exception);
        Assert.Null(failures[1].Exception);
        Assert.Contains("Connection refused.", failures[1].Message, StringComparison.Ordinal);
        Assert.Contains("again", failures[1].Message, StringComparison.Ordinal);
        // A different failure is logged in full again.
        Assert.IsType<CameraFeedException>(failures[2].Exception);
        Assert.False(move.IsCompleted);
    }

    [Fact]
    public async Task CameraFeedExceptionReportsErrorWithItsReason()
    {
        Task<bool> move = Next();
        FakeCameraFeed first = (await _factory.NextStreamAsync())!;
        Assert.Equal(CameraStatus.Connecting, await _status.NextAsync());

        first.Fail(new CameraFeedException("Connection refused."));

        Assert.Equal(
            new CameraStatus(CameraConnectionState.Error, "Connection refused.") { NextAttemptAt = _time.GetUtcNow() + Options.ReconnectDelay },
            await _status.NextAsync());
        Assert.True(first.IsDisposed);
        Assert.Contains(_logger.Entries, static e => e.Level == LogLevel.Warning && e.Exception is CameraFeedException);

        _time.Advance(Options.ReconnectDelay);
        Assert.NotNull(await _factory.NextStreamAsync());
        Assert.Equal(CameraStatus.Connecting, await _status.NextAsync());
        Assert.False(move.IsCompleted);
    }

    [Fact]
    public async Task HostUnreachableIsKeptInTheStatus()
    {
        _ = Next();
        FakeCameraFeed stream = (await _factory.NextStreamAsync())!;
        await _status.NextAsync();

        // T-097: "no route to host" reaches the UI, which may explain it per platform.
        stream.Fail(new CameraFeedException("Opening the stream: No route to host.") { IsHostUnreachable = true });

        CameraStatus error = await _status.NextAsync();
        Assert.Equal(CameraConnectionState.Error, error.State);
        Assert.True(error.IsHostUnreachable);
        Assert.Equal("Opening the stream: No route to host.", error.Reason);
        Assert.NotEqual(new CameraStatus(CameraConnectionState.Error, error.Reason) { NextAttemptAt = error.NextAttemptAt }, error);
    }

    [Fact]
    public async Task OtherFailuresAreNotHostUnreachable()
    {
        _ = Next();
        FakeCameraFeed stream = (await _factory.NextStreamAsync())!;
        await _status.NextAsync();

        stream.Fail(new CameraFeedException("Opening the stream: Connection refused."));

        Assert.False((await _status.NextAsync()).IsHostUnreachable);
    }

    [Fact]
    public async Task OtherExceptionReasonContainsOnlyItsType()
    {
        _ = Next();
        FakeCameraFeed stream = (await _factory.NextStreamAsync())!;
        await _status.NextAsync();

        stream.Fail(new InvalidOperationException("failed rtsp://admin:secret@192.168.0.227/live"));

        CameraStatus error = await _status.NextAsync();
        Assert.Equal(CameraConnectionState.Error, error.State);
        Assert.Contains(nameof(InvalidOperationException), error.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", error.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LogMessagesNeverContainUserInfo()
    {
        Uri withCredentials = new("rtsp://admin:secret@192.168.0.227/live");
        _frames = _camera.ReadFramesAsync(withCredentials, _status, _cts.Token).GetAsyncEnumerator(_cts.Token);
        _pending = _frames.MoveNextAsync().AsTask();
        FakeCameraFeed stream = (await _factory.NextStreamAsync())!;
        await _status.NextAsync();

        stream.Fail(new CameraFeedException("Connection refused."));
        await _status.NextAsync();

        Assert.Equal(withCredentials, _factory.LastUri);
        Assert.NotEmpty(_logger.Entries);
        Assert.All(_logger.Entries, static e => Assert.DoesNotContain("secret", e.Message, StringComparison.Ordinal));
        Assert.Contains(_logger.Entries, static e => e.Message.Contains("rtsp://***@192.168.0.227/live", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FactoryFailureIsReportedAsErrorAndRetried()
    {
        int calls = 0;
        _factory.Behavior = _ => ++calls == 1 ? throw new CameraFeedException("Decoder not available.") : null;

        Task<bool> move = Next();
        Assert.Null(await _factory.NextStreamAsync());
        Assert.Equal(CameraStatus.Connecting, await _status.NextAsync());
        Assert.Equal(
            new CameraStatus(CameraConnectionState.Error, "Decoder not available.") { NextAttemptAt = _time.GetUtcNow() + Options.ReconnectDelay },
            await _status.NextAsync());

        _time.Advance(Options.ReconnectDelay);
        FakeCameraFeed stream = (await _factory.NextStreamAsync())!;
        stream.PushFrame(4);

        Assert.True(await move.WaitAsync(TestTimeouts.Wait, Ct));
        Assert.Equal(CameraStatus.Connecting, await _status.NextAsync());
        Assert.Equal(CameraStatus.Streaming, await _status.NextAsync());
    }

    [Fact]
    public async Task StreamThatFailsToStartIsReportedAsErrorAndClosed()
    {
        ThrowingCameraFeed broken = new(throwOnRead: true, throwOnDispose: true);
        int calls = 0;
        _factory.Behavior = _ => ++calls == 1 ? broken : null;

        _ = Next();
        await _factory.NextStreamAsync();
        Assert.Equal(CameraStatus.Connecting, await _status.NextAsync());
        Assert.Equal(CameraConnectionState.Error, (await _status.NextAsync()).State);
        Assert.True(broken.DisposeCalled);
        // The failing dispose is logged, the stream keeps reconnecting.
        Assert.Contains(_logger.Entries, static e => e.Exception is ThrowingCameraFeed.DisposeFailedException);

        _time.Advance(Options.ReconnectDelay);
        Assert.NotNull(await _factory.NextStreamAsync());
    }

    [Fact]
    public async Task NoFrameWithinConnectTimeoutReportsNoSignalAndReconnects()
    {
        Task<bool> move = Next();
        FakeCameraFeed first = (await _factory.NextStreamAsync())!;
        await first.NextReadAsync();
        Assert.Equal(CameraStatus.Connecting, await _status.NextAsync());

        _time.Advance(Options.ConnectTimeout - Tick);
        Assert.False(_status.HasPending);

        _time.Advance(Tick);
        CameraStatus noSignal = await _status.NextAsync();
        Assert.Equal(CameraConnectionState.NoSignal, noSignal.State);
        // The reason reaches the camera window (status detail): number and unit are joined by a non-breaking space.
        Assert.Equal("The camera sent no image within 20 s.", noSignal.Reason);
        Assert.True(first.IsDisposed);

        _time.Advance(Options.ReconnectDelay);
        Assert.NotNull(await _factory.NextStreamAsync());
        Assert.False(move.IsCompleted);
    }

    [Fact]
    public async Task StalledStreamReportsNoSignalAfterFrameTimeout()
    {
        Task<bool> move = Next();
        FakeCameraFeed stream = (await _factory.NextStreamAsync())!;
        stream.PushFrame(1);
        Assert.True(await move.WaitAsync(TestTimeouts.Wait, Ct));
        await _status.NextAsync();
        await _status.NextAsync();

        move = Next();
        await stream.NextReadAsync();
        await stream.NextReadAsync();
        _time.Advance(Options.FrameTimeout - Tick);
        Assert.False(_status.HasPending);

        _time.Advance(Tick);
        CameraStatus noSignal = await _status.NextAsync();
        Assert.Equal(CameraConnectionState.NoSignal, noSignal.State);
        Assert.Equal("The camera stopped sending images (none within 10 s).", noSignal.Reason);
        Assert.True(stream.IsDisposed);
        Assert.False(move.IsCompleted);
    }

    [Fact]
    public async Task TimeSpentByConsumerIsNotCountedAsFrameTimeout()
    {
        Task<bool> move = Next();
        FakeCameraFeed stream = (await _factory.NextStreamAsync())!;
        stream.PushFrame(1);
        Assert.True(await move.WaitAsync(TestTimeouts.Wait, Ct));

        // The consumer holds the frame (e.g. a slow UI) for longer than both timeouts.
        _time.Advance(Options.ConnectTimeout + Options.FrameTimeout);
        move = Next();
        stream.PushFrame(2);

        Assert.True(await move.WaitAsync(TestTimeouts.Wait, Ct));
        Assert.Equal(2, _frames!.Current.Pixels.Span[0]);
        Assert.Equal([CameraStatus.Connecting, CameraStatus.Streaming], _status.All);
        Assert.Equal(1, _factory.CreateCount);
    }

    [Fact]
    public async Task CancellationWhileWaitingForFrameEndsEnumerationAndClosesStream()
    {
        Task<bool> move = Next();
        FakeCameraFeed stream = (await _factory.NextStreamAsync())!;
        await stream.NextReadAsync();

        await _cts.CancelAsync();

        Assert.False(await move.WaitAsync(TestTimeouts.Wait, Ct));
        Assert.True(stream.IsDisposed);
        Assert.Equal([CameraStatus.Connecting], _status.All);
        Assert.Equal(1, _factory.CreateCount);
    }

    [Fact]
    public async Task CancellationDuringReconnectDelayEndsWithoutNewAttempt()
    {
        Task<bool> move = Next();
        FakeCameraFeed stream = (await _factory.NextStreamAsync())!;
        stream.End();
        await _status.NextAsync();
        Assert.Equal(CameraConnectionState.NoSignal, (await _status.NextAsync()).State);

        await _cts.CancelAsync();

        Assert.False(await move.WaitAsync(TestTimeouts.Wait, Ct));
        _time.Advance(Options.ReconnectDelay);
        Assert.Equal(1, _factory.CreateCount);
    }

    [Fact]
    public async Task DelayDoublesForEveryFailedAttemptUpToTheCap()
    {
        // T-188: 5 s, 10 s, 20 s, 40 s, then the 1 min cap (the app's values).
        _ = Next();
        foreach (int seconds in (int[])[5, 10, 20, 40, 60, 60])
        {
            TimeSpan delay = TimeSpan.FromSeconds(seconds);
            (await _factory.NextStreamAsync())!.Fail(new CameraFeedException("Connection refused."));
            Assert.Equal(CameraStatus.Connecting, await _status.NextAsync());
            await AssertRetryAfterAsync(delay);
        }
    }

    [Fact]
    public async Task EveryFailureKindBacksOff()
    {
        // Open failure, connect timeout, stream ended before any frame: all count as failed attempts.
        int calls = 0;
        _factory.Behavior = _ => ++calls == 1 ? throw new CameraFeedException("Decoder not available.") : null;
        _ = Next();
        Assert.Null(await _factory.NextStreamAsync());
        await _status.NextAsync();
        await AssertRetryAfterAsync(Options.ReconnectDelay);

        FakeCameraFeed silent = (await _factory.NextStreamAsync())!;
        await silent.NextReadAsync();
        await _status.NextAsync();
        _time.Advance(Options.ConnectTimeout);
        await AssertRetryAfterAsync(Options.ReconnectDelay * 2);

        (await _factory.NextStreamAsync())!.End();
        await _status.NextAsync();
        await AssertRetryAfterAsync(Options.ReconnectDelay * 4);
    }

    [Fact]
    public async Task StreamingResetsTheDelay()
    {
        Task<bool> move = Next();
        for (int i = 0; i < 3; i++)
        {
            (await _factory.NextStreamAsync())!.Fail(new CameraFeedException("Connection refused."));
            await _status.NextAsync();
            await AssertRetryAfterAsync(Options.ReconnectDelay * (1 << i));
        }

        // The attempt that streams counts as successful: when it ends, the next attempt follows after the initial delay.
        FakeCameraFeed streaming = (await _factory.NextStreamAsync())!;
        streaming.PushFrame(1);
        Assert.True(await move.WaitAsync(TestTimeouts.Wait, Ct));
        Assert.Equal(CameraStatus.Connecting, await _status.NextAsync());
        Assert.Equal(CameraStatus.Streaming, await _status.NextAsync());
        move = Next();
        streaming.End();
        await AssertRetryAfterAsync(Options.ReconnectDelay);

        // And the backoff starts over.
        (await _factory.NextStreamAsync())!.Fail(new CameraFeedException("Connection refused."));
        await _status.NextAsync();
        await AssertRetryAfterAsync(Options.ReconnectDelay * 2);
        Assert.False(move.IsCompleted);
    }

    [Fact]
    public async Task ReconnectDelayLongerThanTheCapSwitchesTheBackoffOff()
    {
        _camera = new ReconnectingCameraFeed(
            _factory,
            _time,
            _logger,
            Options with { ReconnectDelay = TimeSpan.FromSeconds(30), MaxReconnectDelay = TimeSpan.FromSeconds(10) });
        _ = Next();
        for (int i = 0; i < 3; i++)
        {
            (await _factory.NextStreamAsync())!.Fail(new CameraFeedException("Connection refused."));
            await _status.NextAsync();
            await AssertRetryAfterAsync(TimeSpan.FromSeconds(30));
        }
    }

    [Fact]
    public async Task CancellationDuringALongBackoffEndsAtOnceAndClosesEverything()
    {
        Task<bool> move = Next();
        List<FakeCameraFeed> streams = [];
        for (int i = 0; i < 5; i++)
        {
            FakeCameraFeed stream = (await _factory.NextStreamAsync())!;
            streams.Add(stream);
            stream.Fail(new CameraFeedException("Connection refused."));
            await _status.NextAsync();
            CameraStatus error = await _status.NextAsync();
            if (i < 4)
            {
                _time.Advance(error.NextAttemptAt!.Value - _time.GetUtcNow());
            }
        }

        // Waiting the full minute now; stopping must not wait for it (no time passes here).
        await _cts.CancelAsync();

        Assert.False(await move.WaitAsync(TestTimeouts.Wait, Ct));
        Assert.All(streams, static stream => Assert.True(stream.IsDisposed));
        _time.Advance(Options.MaxReconnectDelay);
        Assert.Equal(5, _factory.CreateCount);
        Assert.False(_status.HasPending);
    }

    /// <summary>The failure reported next plans the next attempt in <paramref name="delay"/>, which starts exactly then.</summary>
    private async Task AssertRetryAfterAsync(TimeSpan delay)
    {
        CameraStatus failure = await _status.NextAsync();
        Assert.NotEqual(CameraConnectionState.Connecting, failure.State);
        Assert.NotEqual(CameraConnectionState.Streaming, failure.State);
        Assert.Equal(_time.GetUtcNow() + delay, failure.NextAttemptAt);
        int created = _factory.CreateCount;

        _time.Advance(delay - Tick);
        Assert.False(_factory.HasPendingCreate);
        Assert.Equal(created, _factory.CreateCount);

        // The caller takes the next attempt's Connecting.
        _time.Advance(Tick);
    }

    [Fact]
    public async Task CancellationWhileConsumerHoldsFrameEndsAtNextRead()
    {
        Task<bool> move = Next();
        FakeCameraFeed stream = (await _factory.NextStreamAsync())!;
        stream.PushFrame(1);
        stream.PushFrame(2);
        Assert.True(await move.WaitAsync(TestTimeouts.Wait, Ct));

        await _cts.CancelAsync();

        Assert.False(await Next().WaitAsync(TestTimeouts.Wait, Ct));
        Assert.True(stream.IsDisposed);
    }

    [Fact]
    public async Task AlreadyCancelledTokenYieldsNothing()
    {
        await _cts.CancelAsync();

        Assert.False(await Next().WaitAsync(TestTimeouts.Wait, Ct));
        Assert.Equal(0, _factory.CreateCount);
        Assert.Empty(_status.All);
    }

    [Fact]
    public async Task LeavingTheLoopClosesStream()
    {
        FakeCameraFeed stream = new();
        stream.PushFrame(1);
        _factory.Behavior = _ => stream;

        await foreach (CameraFrame frame in _camera.ReadFramesAsync(StreamUri, status: null, TestContext.Current.CancellationToken))
        {
            Assert.Equal(1, frame.Pixels.Span[0]);
            break;
        }

        Assert.True(stream.IsDisposed);
    }

    [Fact]
    public async Task FailingStatusObserverDoesNotStopStreaming()
    {
        _status.OnReport = static _ => throw new InvalidOperationException("observer failed");

        Task<bool> move = Next();
        FakeCameraFeed stream = (await _factory.NextStreamAsync())!;
        stream.PushFrame(1);

        Assert.True(await move.WaitAsync(TestTimeouts.Wait, Ct));
        Assert.Contains(_logger.Entries, static e => e.Level == LogLevel.Error && e.Exception is InvalidOperationException);
    }

    [Fact]
    public void InvalidArgumentsAreRejectedImmediately()
    {
        Assert.Throws<ArgumentNullException>(() => _camera.ReadFramesAsync(null!, _status, _cts.Token));
        Assert.Throws<ArgumentException>(() => _camera.ReadFramesAsync(new Uri("live", UriKind.Relative), _status, _cts.Token));
        Assert.Throws<ArgumentNullException>(() => new ReconnectingCameraFeed(null!, _time, _logger));
        Assert.Throws<ArgumentNullException>(() => new ReconnectingCameraFeed(_factory, null!, _logger));
        Assert.Throws<ArgumentNullException>(() => new ReconnectingCameraFeed(_factory, _time, null!));
    }

    [Fact]
    public void OptionsHaveSaneDefaultsAndRejectInvalidValues()
    {
        CameraFeedOptions defaults = CameraFeedOptions.Default;
        Assert.True(defaults.ConnectTimeout > TimeSpan.Zero);
        Assert.True(defaults.FrameTimeout > TimeSpan.Zero);
        Assert.True(defaults.ReconnectDelay > TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromMinutes(1), defaults.MaxReconnectDelay);

        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraFeedOptions { ConnectTimeout = TimeSpan.Zero });
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraFeedOptions { FrameTimeout = TimeSpan.FromHours(1) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraFeedOptions { ReconnectDelay = TimeSpan.FromMilliseconds(-1) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraFeedOptions { MaxReconnectDelay = TimeSpan.Zero });
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraFeedOptions { MaxReconnectDelay = TimeSpan.FromMinutes(6) });
    }

    private Task<bool> Next()
    {
        _frames ??= _camera.ReadFramesAsync(StreamUri, _status, _cts.Token).GetAsyncEnumerator(_cts.Token);
        return _pending = _frames.MoveNextAsync().AsTask();
    }
}
