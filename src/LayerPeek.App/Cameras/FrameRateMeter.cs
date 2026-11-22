namespace LayerPeek.App.Cameras;

/// <summary>
/// The frame rate of frames recorded by their instants (T-251: the frames the mini preview really shows), over a sliding
/// window. A fixed ring of timestamps (<see cref="TimeProvider.GetTimestamp"/> units): recording and measuring never
/// allocate. Not thread-safe; the camera window uses it on the UI thread only.
/// </summary>
/// <remarks>
/// With n frames in the window, the oldest at <c>first</c> and the newest at <c>last</c>, the rate is n − 1 frames over
/// their span: exact for a steady stream whatever the phase of the measurement. Once the frames stop, the time since the
/// last one beyond one average interval is added to the span, so the rate falls smoothly instead of staying at its last
/// value, until the window holds no frame (0). A single frame in the window counts as one per window.
/// </remarks>
internal sealed class FrameRateMeter
{
    /// <summary>
    /// Timestamps kept: 2 s at 128 fps. A faster stream keeps only its newest frames, which still measure its rate (over a
    /// shorter span).
    /// </summary>
    private const int Capacity = 256;

    private readonly long[] _stamps = new long[Capacity];
    private readonly long _window;
    private readonly double _frequency;

    // The next slot to write and the number of timestamps kept (at most Capacity).
    private int _next;
    private int _count;

    /// <param name="window">How far back frames count, e.g. 2 s.</param>
    /// <param name="frequency">Timestamp units per second (<see cref="TimeProvider.TimestampFrequency"/>).</param>
    public FrameRateMeter(TimeSpan window, long frequency)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(frequency, 0);
        _frequency = frequency;
        _window = (long)Math.Ceiling(window.TotalSeconds * frequency);
    }

    /// <summary>Forgets every frame (a new run of the picture).</summary>
    public void Reset()
    {
        _next = 0;
        _count = 0;
    }

    /// <summary>A frame was shown at <paramref name="timestamp"/>.</summary>
    public void Record(long timestamp)
    {
        _stamps[_next] = timestamp;
        _next = (_next + 1) % Capacity;
        if (_count < Capacity)
        {
            _count++;
        }
    }

    /// <summary>Frames per second at <paramref name="now"/> over the window (0 when it holds no frame); never negative.</summary>
    public double RateAt(long now)
    {
        if (_count == 0)
        {
            return 0;
        }

        // Newest first; stop at the first one older than the window (timestamps are recorded in order). A clock that went
        // back may leave timestamps after now: they do not count, and the span uses the earliest and latest that do.
        int newest = (_next - 1 + Capacity) % Capacity;
        long first = long.MaxValue;
        long last = long.MinValue;
        int frames = 0;
        for (int i = 0; i < _count; i++)
        {
            long stamp = _stamps[(newest - i + Capacity) % Capacity];
            long age = now - stamp;
            if (age > _window)
            {
                break;
            }

            if (age >= 0)
            {
                first = Math.Min(first, stamp);
                last = Math.Max(last, stamp);
                frames++;
            }
        }

        if (frames == 0)
        {
            return 0;
        }

        if (frames == 1)
        {
            return _frequency / _window;
        }

        // Frames with the same timestamp must not make the span zero.
        double span = Math.Max(last - first, 1);
        double interval = span / (frames - 1);
        // Time since the last frame beyond one average interval: the frames slowed down or stopped.
        span += Math.Max(0, now - last - interval);
        return (frames - 1) * _frequency / span;
    }
}
