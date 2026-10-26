using LayerPeek.App.Tray;
using LayerPeek.Core.Cooldown;

namespace LayerPeek.App.Cameras.Overlay.Widgets;

/// <summary>
/// The shared rules of the shown cool-down estimate (T-088), used by the Cool-down widget and the mini preview strip
/// (T-089) so that both always show the same minutes: an estimate is shown only when the monitor has one, it is still
/// running (counted down by the monitor from its reading, never "0 min") and the state is not older than
/// <see cref="StaleAfter"/> (monitoring stopped).
/// </summary>
public static class CooldownEstimate
{
    /// <summary>
    /// The age of a state beyond which its estimate is not shown: the longest gap between snapshots the monitor still
    /// counts as observed (3 × the longest polling interval of 5 min, <see cref="CooldownTracker.GapLimit"/>). An older
    /// state means monitoring stopped; the monitor ends the cool-down at the next snapshot anyway.
    /// </summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

    /// <summary>How often a shown estimate is computed again without snapshots (the minutes lag at most this much).</summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The whole minutes (rounded up, at least 1) of a running, fresh estimate at <paramref name="now"/>;
    /// <see langword="null"/> for none, an expired or a stale one. Allocates nothing.
    /// </summary>
    public static long? Minutes(in CooldownState state, DateTimeOffset now)
    {
        if (state.EstimatedRemaining is not { } remaining
            || remaining <= TimeSpan.Zero
            || state.UpdatedAt is not { } updatedAt
            || now - updatedAt > StaleAfter)
        {
            return null;
        }

        return ToolTipContent.CeilingMinutes(remaining);
    }
}

/// <summary>
/// The clock that counts a shown cool-down estimate down while no snapshots arrive (T-088): every
/// <see cref="CooldownEstimate.TickInterval"/> on the window clock it calls back, only while switched on. The timer is
/// created the first time it is switched on and only re-armed after that (no allocation per switch). Thread-safe.
/// </summary>
internal sealed class CooldownCountdown : IDisposable
{
    private readonly TimeProvider _time;
    private readonly Action _tick;
    private readonly Lock _gate = new();

    // Guarded by _gate.
    private ITimer? _timer;
    private bool _ticking;
    private bool _disposed;

    /// <param name="time">The window clock.</param>
    /// <param name="tick">Called on a timer thread on every tick; must be thread-safe and cheap.</param>
    public CooldownCountdown(TimeProvider time, Action tick)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(tick);
        _time = time;
        _tick = tick;
    }

    /// <summary>The clock runs.</summary>
    public bool IsTicking
    {
        get
        {
            lock (_gate)
            {
                return _ticking;
            }
        }
    }

    /// <summary>Starts or stops the clock; nothing after <see cref="Dispose"/>.</summary>
    public void Set(bool ticking)
    {
        lock (_gate)
        {
            if (ticking == _ticking || _disposed)
            {
                return;
            }

            _ticking = ticking;
            if (!ticking)
            {
                _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
            else if (_timer is null)
            {
                _timer = _time.CreateTimer(static state => ((CooldownCountdown)state!)._tick(), this, CooldownEstimate.TickInterval, CooldownEstimate.TickInterval);
            }
            else
            {
                _timer.Change(CooldownEstimate.TickInterval, CooldownEstimate.TickInterval);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _ticking = false;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
