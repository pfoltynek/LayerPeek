using LayerPeek.Core.Cameras.Timelapse;
using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Tests.Cameras.Timelapse;

/// <summary>T-160: the time trigger asks for a frame when capturing starts and then every interval, with one timer.</summary>
public sealed class CameraTimelapseTimeTriggerTests : IAsyncDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly CapturingTimeProvider _time = new();
    private readonly FakeTimelapse _timelapse = new("mk4", "/tmp/never-written.mp4", null);

    public ValueTask DisposeAsync() => _timelapse.DisposeAsync();

    [Fact]
    public void ANewTriggerAsksForNothingUntilItCaptures()
    {
        using CameraTimelapseTimeTrigger trigger = new(_timelapse, Interval, _time);

        Assert.Equal(1, _time.Created);
        Assert.Equal((Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan), _time.Timer!.Last);
        Assert.Equal(0, _timelapse.Requests);
        Assert.Equal(Interval, trigger.Interval);
    }

    [Fact]
    public void CapturingAsksForAFrameAtOnceAndThenEveryInterval()
    {
        using CameraTimelapseTimeTrigger trigger = new(_timelapse, Interval, _time);

        trigger.SetCapturing(true);
        Assert.Equal(1, _timelapse.Requests);
        Assert.Equal((Interval, Interval), _time.Timer!.Last);

        // The same value again (every snapshot) changes nothing.
        trigger.SetCapturing(true);
        trigger.OnSnapshot(new PrinterSnapshot { PrinterId = "mk4", State = PrinterState.Printing, Timestamp = DateTimeOffset.UnixEpoch });
        Assert.Equal(1, _timelapse.Requests);
        Assert.Equal(1, _time.Timer.Changes);

        _time.Timer.Fire();
        _time.Timer.Fire();
        Assert.Equal(3, _timelapse.Requests);
    }

    [Fact]
    public void APauseStopsTheTimerWithoutDisposingIt()
    {
        using CameraTimelapseTimeTrigger trigger = new(_timelapse, Interval, _time);
        trigger.SetCapturing(true);

        trigger.SetCapturing(false);
        trigger.SetCapturing(false);

        Assert.Equal((Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan), _time.Timer!.Last);
        Assert.Equal(2, _time.Timer.Changes);
        Assert.False(_time.Timer.IsDisposed);
        Assert.Equal(1, _time.Created);

        trigger.SetCapturing(true);
        Assert.Equal(2, _timelapse.Requests);
    }

    [Fact]
    public void APauseWithdrawsTheRequestWaitingForItsKeyFrameAndTheResumeAsksAgainAtOnce()
    {
        using CameraTimelapseTimeTrigger trigger = new(_timelapse, Interval, _time);
        trigger.SetCapturing(true);
        _time.Timer!.Fire();
        Assert.True(_timelapse.IsRequestPending);

        trigger.SetCapturing(false);
        Assert.False(_timelapse.IsRequestPending);
        Assert.Equal(1, _timelapse.CancelCalls);
        // Every snapshot of the pause repeats the value: nothing more to withdraw.
        trigger.SetCapturing(false);
        Assert.Equal(1, _timelapse.CancelCalls);

        trigger.SetCapturing(true);
        Assert.True(_timelapse.IsRequestPending);
        Assert.Equal(3, _timelapse.Requests);
    }

    [Fact]
    public void ATickStillRunningWhenThePauseStartsAsksForNoFrame()
    {
        using CameraTimelapseTimeTrigger trigger = new(_timelapse, Interval, _time);
        trigger.SetCapturing(true);
        trigger.SetCapturing(false);

        // The timer's callback was already on its way when the timer stopped.
        _time.Timer!.Fire();

        Assert.Equal(1, _timelapse.Requests);
        Assert.False(_timelapse.IsRequestPending);
    }

    [Fact]
    public void APauseRightAfterATickAskedForAFrameWithdrawsThatRequest()
    {
        using CameraTimelapseTimeTrigger trigger = new(_timelapse, Interval, _time);
        trigger.SetCapturing(true);
        _timelapse.OnRequest = () => trigger.SetCapturing(false);

        _time.Timer!.Fire();

        Assert.Equal(2, _timelapse.Requests);
        Assert.False(_timelapse.IsRequestPending);
        Assert.Equal((Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan), _time.Timer.Last);
    }

    [Fact]
    public void ADisposedTriggerDisposesItsTimerAndAsksForNothing()
    {
        CameraTimelapseTimeTrigger trigger = new(_timelapse, Interval, _time);

        trigger.Dispose();
        trigger.SetCapturing(true);

        Assert.True(_time.Timer!.IsDisposed);
        Assert.Equal(0, _timelapse.Requests);
    }

    [Fact]
    public void ATickAllocatesNothing()
    {
        using CameraTimelapseTimeTrigger trigger = new(_timelapse, Interval, _time);
        trigger.SetCapturing(true);
        _time.Timer!.Fire();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            _time.Timer.Fire();
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(1002, _timelapse.Requests);
    }

    [Fact]
    public void InvalidArgumentsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraTimelapseTimeTrigger(_timelapse, TimeSpan.Zero, _time));
        Assert.Throws<ArgumentNullException>(() => new CameraTimelapseTimeTrigger(null!, Interval, _time));
        Assert.Throws<ArgumentNullException>(() => new CameraTimelapseTimeTrigger(_timelapse, Interval, null!));
    }

    /// <summary>Hands out one <see cref="ManualTimer"/> whose callback the test fires.</summary>
    private sealed class CapturingTimeProvider : TimeProvider
    {
        public int Created { get; private set; }

        public ManualTimer? Timer { get; private set; }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Created++;
            Timer = new ManualTimer(callback, state, (dueTime, period));
            return Timer;
        }
    }

    private sealed class ManualTimer(TimerCallback callback, object? state, (TimeSpan Due, TimeSpan Period) initial) : ITimer
    {
        public (TimeSpan Due, TimeSpan Period) Last { get; private set; } = initial;

        public int Changes { get; private set; }

        public bool IsDisposed { get; private set; }

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Changes++;
            Last = (dueTime, period);
            return true;
        }

        public void Dispose() => IsDisposed = true;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
