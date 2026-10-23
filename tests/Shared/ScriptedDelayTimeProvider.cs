namespace PrusaTrayDiagnostic.Tests.Shared;

/// <summary>
/// Clock for retry tests without real waiting: every timer (i.e. every <c>Task.Delay</c> the code under test starts) is
/// recorded and handed to <see cref="OnDelay"/> on the thread pool, which decides whether the timer fires right away (the
/// default) or never (e.g. after it cancelled the wait). The 1-based index of the delay is passed in.
/// </summary>
internal sealed class ScriptedDelayTimeProvider : TimeProvider
{
    private readonly Lock _lock = new();
    private readonly List<TimeSpan> _delays = [];

    /// <summary>Called before a timer fires; return <see langword="false"/> to keep it from firing.</summary>
    public Func<int, bool> OnDelay { get; init; } = static _ => true;

    /// <summary>The due times of all timers created so far, in creation order.</summary>
    public IReadOnlyList<TimeSpan> Delays
    {
        get
        {
            lock (_lock)
            {
                return [.. _delays];
            }
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        int index;
        lock (_lock)
        {
            _delays.Add(dueTime);
            index = _delays.Count;
        }

        // Never on the creating thread: the caller has not stored the timer yet.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            if (OnDelay(index))
            {
                callback(state);
            }
        });
        return new InertTimer();
    }

    private sealed class InertTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
