namespace LayerPeek.Core.Monitoring;

public sealed record PrinterMonitorOptions
{
    /// <summary>Timer resolution; shorter timeouts would be rounded down to zero.</summary>
    public static readonly TimeSpan MinPollTimeout = TimeSpan.FromMilliseconds(1);

    public static readonly TimeSpan MaxPollTimeout = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan MaxBackoffLimit = TimeSpan.FromHours(1);

    public static PrinterMonitorOptions Default { get; } = new();

    /// <summary>A poll that takes longer is abandoned and the printer is reported as offline (at most 5 min).</summary>
    public TimeSpan PollTimeout
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, MinPollTimeout);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxPollTimeout);
            field = value;
        }
    } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Upper bound of the exponential backoff for unreachable printers (at most 1 h). The polling interval is used
    /// instead when it is longer.
    /// </summary>
    public TimeSpan MaxBackoff
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxBackoffLimit);
            field = value;
        }
    } = TimeSpan.FromMinutes(1);
}
