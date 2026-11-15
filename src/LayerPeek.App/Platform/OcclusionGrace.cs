namespace LayerPeek.App.Platform;

/// <summary>
/// Reports a window occluded only after it stayed occluded for a grace time, and visible again at once (T-187, macOS).
/// The window manager reports a window occluded for a moment while it animates (switching Spaces, entering or leaving full
/// screen, Mission Control); pausing and resuming the stream for that would only cost a reconnect to the next key frame.
/// Starts as visible. Platform independent (tested with a fake scheduler); one thread (the UI thread).
/// </summary>
internal sealed class OcclusionGrace : IDisposable
{
    /// <summary>How long a window must stay occluded before its stream pauses.</summary>
    public static readonly TimeSpan Default = TimeSpan.FromSeconds(2);

    private readonly Action<bool> _changed;
    private readonly Func<Action, TimeSpan, IDisposable> _schedule;
    private readonly TimeSpan _grace;
    private readonly Action _elapsed;
    private IDisposable? _pending;
    private bool _reported;
    private bool _disposed;

    /// <param name="changed">Gets every change of the reported state (false first, at once).</param>
    /// <param name="schedule">Runs an action once after a delay on this thread; disposing the result cancels it.</param>
    /// <param name="grace">How long an occlusion must last to be reported.</param>
    public OcclusionGrace(Action<bool> changed, Func<Action, TimeSpan, IDisposable> schedule, TimeSpan grace)
    {
        ArgumentNullException.ThrowIfNull(changed);
        ArgumentNullException.ThrowIfNull(schedule);
        _changed = changed;
        _schedule = schedule;
        _grace = grace;
        _elapsed = OnElapsed;
        changed(false);
    }

    /// <summary>The window manager reports the window occluded (<see langword="true"/>) or (partly) visible.</summary>
    public void Update(bool occluded)
    {
        if (_disposed)
        {
            return;
        }

        if (occluded)
        {
            if (!_reported && _pending is null)
            {
                _pending = _schedule(_elapsed, _grace);
            }

            return;
        }

        CancelPending();
        if (_reported)
        {
            _reported = false;
            _changed(false);
        }
    }

    /// <summary>Stops reporting; a pending occlusion is dropped.</summary>
    public void Dispose()
    {
        _disposed = true;
        CancelPending();
    }

    private void OnElapsed()
    {
        _pending = null;
        if (!_disposed && !_reported)
        {
            _reported = true;
            _changed(true);
        }
    }

    private void CancelPending()
    {
        _pending?.Dispose();
        _pending = null;
    }
}
