using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Cameras.Timelapse;

/// <summary>
/// The time trigger (T-160, user decision 2026-11-04): a frame when capturing starts or resumes, then one every interval.
/// One timer per timelapse, created once and stopped (not disposed) while not capturing; its callback only sets the
/// timelapse's request flag, so a tick allocates nothing and never waits. The frame itself is the camera's next key frame
/// (T-158), so the interval of the video is the timer's: delays of the key frames do not add up.
/// </summary>
internal sealed class CameraTimelapseTimeTrigger : ICameraTimelapseTrigger
{
    private readonly ICameraTimelapse _timelapse;
    private readonly TimeSpan _interval;
    private readonly ITimer _timer;

    // Under the service's lock (calls are serialized).
    private bool _capturing;
    private bool _disposed;

    public CameraTimelapseTimeTrigger(ICameraTimelapse timelapse, TimeSpan interval, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(timelapse);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        _timelapse = timelapse;
        _interval = interval;
        _timer = time.CreateTimer(static state => ((ICameraTimelapse)state!).RequestFrame(), timelapse, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public TimeSpan Interval => _interval;

    public void SetCapturing(bool capturing)
    {
        if (_disposed || capturing == _capturing)
        {
            return;
        }

        _capturing = capturing;
        if (capturing)
        {
            // The print (re)starts: its first frame now rather than an interval later.
            _timelapse.RequestFrame();
            _timer.Change(_interval, _interval);
        }
        else
        {
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>The time trigger does not look at the print.</summary>
    public void OnSnapshot(PrinterSnapshot snapshot)
    {
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }
}
