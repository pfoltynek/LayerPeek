namespace LayerPeek.Core.Cameras.Recording;

/// <summary>
/// Turns the timestamps of one connection's packets into those of one recording file: in the file's time base, starting
/// at 0 with the first packet, the decoding timestamp strictly increasing and the presentation timestamp never below it.
/// RTSP timestamps can be missing, step back or jump (camera clock, lost packets); such a packet continues one frame
/// after the previous one and the clock re-anchors there, so that a single bad value does not shift the rest.
/// </summary>
internal struct RecordingClock
{
    /// <summary>A step further than this is a discontinuity, not a real gap (the feed reconnects after a few seconds of silence).</summary>
    internal static readonly TimeSpan MaxStep = TimeSpan.FromSeconds(10);

    /// <summary>Assumed frame duration until the stream shows its own (the Buddy3D sends ~22 fps).</summary>
    private const int AssumedFramesPerSecond = 25;

    private readonly CameraTimeBase _timeBase;
    private readonly long _maxStep;
    private bool _started;
    private long _origin;
    private long _lastDts;
    private long _frameDuration;

    public RecordingClock(CameraTimeBase timeBase)
    {
        _timeBase = timeBase;
        _maxStep = Math.Max(1, ToTicks(MaxStep, timeBase));
        _frameDuration = Math.Max(1, ToTicks(TimeSpan.FromSeconds(1.0 / AssumedFramesPerSecond), timeBase));
    }

    /// <summary>The recorded time so far: up to the end of the last packet.</summary>
    public readonly TimeSpan Elapsed => _started ? ToTimeSpan(_lastDts + _frameDuration, _timeBase) : TimeSpan.Zero;

    /// <summary>The file's timestamps of <paramref name="packet"/> (its timestamps are converted to the file's time base first).</summary>
    public void Next(in CameraPacket packet, out long pts, out long dts, out long duration)
    {
        long? rawDts = Convert(packet.Dts, packet.TimeBase);
        long? rawPts = Convert(packet.Pts, packet.TimeBase);
        duration = packet.Duration > 0 ? Math.Max(0, Convert(packet.Duration, packet.TimeBase) ?? 0) : 0;
        long? raw = rawDts ?? rawPts;

        if (!_started)
        {
            _started = true;
            _origin = raw ?? 0;
            dts = 0;
            if (duration > 0 && duration <= _maxStep)
            {
                // The stream states its frame duration: better than the assumption until a second packet shows it.
                _frameDuration = duration;
            }
        }
        else
        {
            long step = raw is { } value ? value - _origin - _lastDts : 0;
            if (raw is null || step <= 0 || step > _maxStep)
            {
                // Missing, backwards or a jump: one frame after the previous packet, and later packets follow from here.
                step = duration > 0 && duration <= _maxStep ? duration : _frameDuration;
                if (raw is { } anchor)
                {
                    _origin = anchor - (_lastDts + step);
                }
            }
            else
            {
                _frameDuration = step;
            }

            dts = _lastDts + step;
        }

        _lastDts = dts;
        // B-frames: the presentation offset is kept when it is sane (the Buddy3D's Baseline profile has none).
        long offset = rawPts is { } p && rawDts is { } d && p >= d && p - d <= _maxStep ? p - d : 0;
        pts = dts + offset;
    }

    private readonly long? Convert(long? value, CameraTimeBase from) =>
        value is not { } ticks ? null : from == _timeBase ? ticks : Rescale(ticks, from, _timeBase);

    /// <summary><paramref name="value"/> ticks of <paramref name="from"/> in ticks of <paramref name="to"/>, rounded to nearest.</summary>
    internal static long Rescale(long value, CameraTimeBase from, CameraTimeBase to)
    {
        Int128 numerator = (Int128)value * from.Numerator * to.Denominator;
        Int128 denominator = (Int128)from.Denominator * to.Numerator;
        Int128 half = denominator / 2;
        Int128 result = numerator >= 0 ? (numerator + half) / denominator : (numerator - half) / denominator;
        return result > long.MaxValue ? long.MaxValue : result < long.MinValue ? long.MinValue : (long)result;
    }

    private static long ToTicks(TimeSpan time, CameraTimeBase timeBase) =>
        Rescale(time.Ticks, new CameraTimeBase(1, (int)TimeSpan.TicksPerSecond), timeBase);

    private static TimeSpan ToTimeSpan(long ticks, CameraTimeBase timeBase) =>
        TimeSpan.FromTicks(Rescale(ticks, timeBase, new CameraTimeBase(1, (int)TimeSpan.TicksPerSecond)));
}
