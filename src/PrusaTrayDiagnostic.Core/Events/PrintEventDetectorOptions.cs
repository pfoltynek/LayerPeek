namespace PrusaTrayDiagnostic.Core.Events;

public sealed record PrintEventDetectorOptions
{
    public static PrintEventDetectorOptions Default { get; } = new();

    /// <summary>
    /// <see cref="PrintEventKind.RemainingTime"/> is reported when the estimate drops to this value or below;
    /// <see cref="TimeSpan.Zero"/> disables it.
    /// </summary>
    public TimeSpan RemainingTimeThreshold
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            field = value;
        }
    } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long a printer must stay unreachable before <see cref="PrintEventKind.PrinterOffline"/> is reported, so that a
    /// single missed poll (e.g. weak Wi-Fi) produces no offline/online pair. Measured between snapshot timestamps.
    /// </summary>
    public TimeSpan OfflineDelay
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            field = value;
        }
    } = TimeSpan.FromMinutes(1);
}
