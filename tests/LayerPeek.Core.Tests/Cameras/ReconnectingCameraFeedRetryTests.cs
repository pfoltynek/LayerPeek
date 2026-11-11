using Microsoft.Extensions.Time.Testing;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Tests.Monitoring;

namespace LayerPeek.Core.Tests.Cameras;

/// <summary>T-188: <see cref="CameraReconnectSignal"/> ends the reconnect wait of a <see cref="ReconnectingCameraFeed"/> (fake time).</summary>
public sealed class ReconnectingCameraFeedRetryTests : IAsyncDisposable
{
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private static readonly CameraFeedOptions Options = new()
    {
        ConnectTimeout = TimeSpan.FromSeconds(20),
        FrameTimeout = TimeSpan.FromSeconds(10),
        ReconnectDelay = TimeSpan.FromSeconds(5),
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeTimeProvider _time = new();
    private readonly FakeCameraFeedFactory _factory = new();
    private readonly RecordingProgress _status = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly CameraReconnectSignal _reconnect = new();
    private readonly IAsyncEnumerator<CameraStreamItem> _items;
    private Task<bool>? _pending;

    public ReconnectingCameraFeedRetryTests()
    {
        ReconnectingCameraFeed camera = new(_factory, _time, new ListLogger<ReconnectingCameraFeed>(), Options);
        _items = camera.ReadAsync(StreamUri, new CameraStreamDemand(frames: true, packets: false), _status, _reconnect, _cts.Token).GetAsyncEnumerator(_cts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        if (_pending is not null)
        {
            await ((Task)_pending).WaitAsync(TestTimeouts.Wait, Ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        await _items.DisposeAsync();
        _cts.Dispose();
    }

    [Fact]
    public async Task RetryDuringTheWaitStartsTheNextAttemptAtOnceAndResetsTheBackoff()
    {
        _ = Next();
        await FailAsync();
        _time.Advance(Options.ReconnectDelay);
        await FailAsync();
        _time.Advance(Options.ReconnectDelay * 2);
        CameraStatus waiting = await FailAsync();
        Assert.Equal(_time.GetUtcNow() + (Options.ReconnectDelay * 4), waiting.NextAttemptAt);

        Assert.True(_reconnect.Retry());

        // No time passes: the attempt starts now.
        FakeCameraFeed retried = (await _factory.NextStreamAsync())!;
        Assert.Equal(CameraStatus.Connecting, await _status.NextAsync());
        Assert.Equal(4, _factory.CreateCount);
        // Once only: the wait it ended is over.
        Assert.False(_reconnect.Retry());

        // The backoff starts over: the next failure waits the initial delay.
        retried.Fail(new CameraFeedException("Connection refused."));
        CameraStatus again = await _status.NextAsync();
        Assert.Equal(_time.GetUtcNow() + Options.ReconnectDelay, again.NextAttemptAt);
    }

    [Fact]
    public async Task RetryWhileConnectingOrStreamingDoesNothingAndIsNotRemembered()
    {
        Task<bool> move = Next();
        FakeCameraFeed connecting = (await _factory.NextStreamAsync())!;
        Assert.Equal(CameraStatus.Connecting, await _status.NextAsync());
        Assert.False(_reconnect.Retry());

        connecting.PushFrame(1);
        Assert.True(await move.WaitAsync(TestTimeouts.Wait, Ct));
        Assert.Equal(CameraStatus.Streaming, await _status.NextAsync());
        Assert.False(_reconnect.Retry());

        // The failure after them still waits its full delay.
        _ = Next();
        connecting.Fail(new CameraFeedException("Connection refused."));
        CameraStatus failure = await _status.NextAsync();
        Assert.Equal(_time.GetUtcNow() + Options.ReconnectDelay, failure.NextAttemptAt);
        _time.Advance(Options.ReconnectDelay - TimeSpan.FromTicks(1));
        await Task.Delay(50, Ct);
        Assert.Equal(1, _factory.CreateCount);
        Assert.False(_factory.HasPendingCreate);
    }

    [Fact]
    public async Task StoppingDuringARetryEndsTheEnumerationWithoutAnotherAttempt()
    {
        Task<bool> move = Next();
        await FailAsync();

        Assert.True(_reconnect.Retry());
        await _cts.CancelAsync();

        Assert.False(await move.WaitAsync(TestTimeouts.Wait, Ct));
        Assert.False(_reconnect.Retry());
        // At most the attempt the retry started, closed again; never one after the stop.
        Assert.InRange(_factory.CreateCount, 1, 2);
    }

    [Fact]
    public async Task RetryAfterTheStopDoesNothing()
    {
        Task<bool> move = Next();
        await FailAsync();

        await _cts.CancelAsync();
        Assert.False(await move.WaitAsync(TestTimeouts.Wait, Ct));

        Assert.False(_reconnect.Retry());
        Assert.Equal(1, _factory.CreateCount);
    }

    /// <summary>The next attempt fails; returns the failure status (after its Connecting).</summary>
    private async Task<CameraStatus> FailAsync()
    {
        FakeCameraFeed stream = (await _factory.NextStreamAsync())!;
        Assert.Equal(CameraStatus.Connecting, await _status.NextAsync());
        stream.Fail(new CameraFeedException("Connection refused."));
        CameraStatus failure = await _status.NextAsync();
        Assert.Equal(CameraConnectionState.Error, failure.State);
        return failure;
    }

    private Task<bool> Next() => _pending = _items.MoveNextAsync().AsTask();
}
