using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Cameras.Timelapse;

/// <summary>
/// The time trigger (T-160, user decision 2026-11-04): a frame when capturing starts or resumes, then one every interval.
/// One timer per timelapse, created once and stopped (not disposed) while not capturing; its callback only sets the
/// timelapse's request flag, so a tick allocates nothing. The frame itself is the camera's next key frame (T-158), so the
/// interval of the video is the timer's: delays of the key frames do not add up.
/// </summary>
/// <remarks>
/// No frame while not capturing (user decision 2026-11-04): stopping also withdraws the pending request
/// (<see cref="ICameraTimelapse.CancelFrameRequest"/>), so a key frame arriving after the pause started (e.g. the parked
/// head) is not written. A tick and <see cref="SetCapturing"/> share a small lock, so a tick that was already running when
/// the timer stopped either requested before the withdrawal or sees that capturing stopped. Guaranteed: after
/// <see cref="SetCapturing"/>(<see langword="false"/>) returns, at most the one key frame the timelapse was already writing
/// is written; none after it until capturing resumes (which asks for a frame at once, as before).
/// </remarks>
internal sealed class CameraTimelapseTimeTrigger : ICameraTimelapseTrigger
{
    private readonly ICameraTimelapse _timelapse;
    private readonly TimeSpan _interval;
    private readonly ITimer _timer;

    // Orders a tick against SetCapturing; held only for a flag check and a request (uncontended, allocation-free).
    private readonly Lock _gate = new();

    // Calls of the service are serialized (its lock); _capturing is also read by a tick, under _gate.
    private bool _capturing;
    private bool _disposed;

    public CameraTimelapseTimeTrigger(ICameraTimelapse timelapse, TimeSpan interval, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(timelapse);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        _timelapse = timelapse;
        _interval = interval;
        _timer = time.CreateTimer(static state => ((CameraTimelapseTimeTrigger)state!).Tick(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public TimeSpan Interval => _interval;

    public void SetCapturing(bool capturing)
    {
        if (_disposed || capturing == _capturing)
        {
            return;
        }

        lock (_gate)
        {
            _capturing = capturing;
        }

        if (capturing)
        {
            // The print (re)starts: its first frame now rather than an interval later.
            _timelapse.RequestFrame();
            _timer.Change(_interval, _interval);
        }
        else
        {
            // After _capturing is false no tick requests anymore; a request set before (a tick or the resume) is withdrawn.
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _timelapse.CancelFrameRequest();
        }
    }

    /// <summary>The time trigger does not look at the print.</summary>
    public void OnSnapshot(PrinterSnapshot snapshot)
    {
    }

    /// <summary>The timer's callback (thread pool): asks for a frame unless capturing stopped meanwhile.</summary>
    private void Tick()
    {
        lock (_gate)
        {
            if (_capturing)
            {
                _timelapse.RequestFrame();
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }
}
