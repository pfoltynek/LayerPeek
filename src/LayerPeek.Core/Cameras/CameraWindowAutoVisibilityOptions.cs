using LayerPeek.Core.Monitoring;

namespace LayerPeek.Core.Cameras;

/// <summary>
/// Delays of <see cref="CameraWindowAutoVisibility"/> (T-150). The production values are fixed (user decision 2026-11-03:
/// 30 s / 10 s, not a setting); only tests and the Debug-only E2E switch of T-151 shorten them.
/// </summary>
public sealed record CameraWindowAutoVisibilityOptions
{
    /// <summary>Longest accepted delay; keeps the deadline arithmetic far from overflowing.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromDays(1);

    public static CameraWindowAutoVisibilityOptions Default { get; } = new();

    /// <summary>
    /// How long a printer must be <see cref="Printers.PrinterState.Offline"/> without interruption before its open camera
    /// window closes; 30 s by default. <see cref="TimeSpan.Zero"/> closes with the first offline snapshot.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Negative or above <see cref="MaxDelay"/>.</exception>
    public TimeSpan CloseAfterOffline
    {
        get;
        init => field = Validate(value);
    } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a printer must be online (any state but <see cref="Printers.PrinterState.Offline"/>) without interruption
    /// before its automatically closed camera window opens again; 10 s by default.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Negative or above <see cref="MaxDelay"/>.</exception>
    public TimeSpan OpenAfterOnline
    {
        get;
        init => field = Validate(value);
    } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Options of the printer monitor that delivers the snapshots (its offline back-off and poll timeout set
    /// <see cref="CameraWindowAutoVisibility.StaleAfter"/>); <see cref="PrinterMonitorOptions.Default"/> by default. Pass the
    /// monitor's own options when they differ.
    /// </summary>
    public PrinterMonitorOptions Monitor
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = PrinterMonitorOptions.Default;

    private static TimeSpan Validate(TimeSpan value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxDelay);
        return value;
    }
}
