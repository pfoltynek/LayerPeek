namespace LayerPeek.Core.Detection;

/// <summary>Fixed parameters of <see cref="PrintFailureDetectionService"/> (T-153); tests shorten them.</summary>
public sealed record PrintFailureDetectionOptions
{
    public static PrintFailureDetectionOptions Default { get; } = new();

    /// <summary>
    /// The samples of the last this long confirm a failure (T-152 proposal: 5 of 8 samples at 2 s ≈ 15 s). In time, not in
    /// samples, so that it works at every sampling interval of 2–5 s.
    /// </summary>
    public TimeSpan ConfirmationWindow
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            field = value;
        }
    } = TimeSpan.FromSeconds(16);

    /// <summary>The share of the samples within <see cref="ConfirmationWindow"/> that must reach the threshold (5 of 8).</summary>
    public double ConfirmationRatio
    {
        get;
        init
        {
            if (!(value is > 0 and <= 1))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The ratio is within 0 (excluded) and 1.");
            }

            field = value;
        }
    } = 5.0 / 8.0;

    /// <summary>At least this many samples within the window (at 5 s there are only about three).</summary>
    public int MinConfirmationSamples
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, ConfirmationCapacity);
            field = value;
        }
    } = 3;

    /// <summary>
    /// The pause between two alarms of the same kind and print in <see cref="PrintFailureAlarmRepeat.WhilePersisting"/>
    /// (chosen with the user decision of 2026-11-04 on a repeated alarm: 10 min).
    /// </summary>
    public TimeSpan RepeatInterval
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            field = value;
        }
    } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Mean luma (0–255) of the region below which the chamber counts as dark (LED off) and detection pauses (T-152:
    /// 63–79 with the light on). A first guess; T-155 tunes it.
    /// </summary>
    public byte DarkBrightness { get; init; } = 12;

    /// <summary>How long an alarm waits for the camera's next key frame for its snapshot; then it is raised without one.</summary>
    public TimeSpan SnapshotTimeout
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            field = value;
        }
    } = TimeSpan.FromSeconds(10);

    /// <summary>JPEG quality of the snapshots (1–100).</summary>
    public int SnapshotQuality
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 100);
            field = value;
        }
    } = 85;

    /// <summary>Samples kept per detector for the confirmation (more than any window of 2–5 s holds).</summary>
    internal const int ConfirmationCapacity = 32;
}
