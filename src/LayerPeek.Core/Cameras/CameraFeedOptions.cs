namespace LayerPeek.Core.Cameras;

/// <summary>Timing of <see cref="ReconnectingCameraFeed"/>. Every value is within 1 ms – 5 min.</summary>
public sealed record CameraFeedOptions
{
    /// <summary>Timer resolution; shorter values would be rounded down to zero.</summary>
    public static readonly TimeSpan MinValue = TimeSpan.FromMilliseconds(1);

    public static readonly TimeSpan MaxValue = TimeSpan.FromMinutes(5);

    public static CameraFeedOptions Default { get; } = new();

    /// <summary>How long an attempt may take until its first frame (connect, RTSP setup, first key frame).</summary>
    public TimeSpan ConnectTimeout
    {
        get;
        init => field = Validate(value);
    } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Longest gap between two frames while streaming. Only the time spent waiting for the stream counts, not the time
    /// the consumer spends processing a frame.
    /// </summary>
    public TimeSpan FrameTimeout
    {
        get;
        init => field = Validate(value);
    } = TimeSpan.FromSeconds(10);

    /// <summary>Pause between a failed or ended attempt and the next one.</summary>
    public TimeSpan ReconnectDelay
    {
        get;
        init => field = Validate(value);
    } = TimeSpan.FromSeconds(5);

    private static TimeSpan Validate(TimeSpan value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, MinValue);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxValue);
        return value;
    }
}
