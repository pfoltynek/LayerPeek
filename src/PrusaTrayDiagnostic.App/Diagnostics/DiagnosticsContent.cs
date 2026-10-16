using PrusaTrayDiagnostic.Core.Printers;

namespace PrusaTrayDiagnostic.App.Diagnostics;

/// <summary>
/// What the diagnostics window shows of one snapshot, rounded exactly like the display. Cheap to build and to compare on a
/// monitor thread: equal content means no visible change, so nothing is sent to the UI thread.
/// </summary>
/// <param name="Timestamp">Time of the snapshot, truncated to whole seconds (the window shows seconds).</param>
internal readonly record struct DiagnosticsContent(
    PrinterState State,
    string? Message,
    double? NozzleActual,
    double? NozzleTarget,
    double? BedActual,
    double? BedTarget,
    int? HotendFanRpm,
    int? PrintFanRpm,
    int? SpeedPercent,
    int? FlowPercent,
    double? AxisZ,
    DateTimeOffset Timestamp)
{
    public static DiagnosticsContent From(PrinterSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Telemetry? telemetry = snapshot.Telemetry;
        long ticks = snapshot.Timestamp.UtcTicks;
        return new DiagnosticsContent(
            snapshot.State,
            string.IsNullOrWhiteSpace(snapshot.StatusMessage) ? null : snapshot.StatusMessage,
            DiagnosticsFormatter.Round(telemetry?.Nozzle?.Actual, DiagnosticsFormatter.TemperatureDecimals),
            DiagnosticsFormatter.Round(telemetry?.Nozzle?.Target, DiagnosticsFormatter.TargetDecimals),
            DiagnosticsFormatter.Round(telemetry?.Bed?.Actual, DiagnosticsFormatter.TemperatureDecimals),
            DiagnosticsFormatter.Round(telemetry?.Bed?.Target, DiagnosticsFormatter.TargetDecimals),
            telemetry?.HotendFanRpm,
            telemetry?.PrintFanRpm,
            telemetry?.SpeedPercent,
            telemetry?.FlowPercent,
            DiagnosticsFormatter.Round(telemetry?.AxisZ, DiagnosticsFormatter.AxisZDecimals),
            new DateTimeOffset(ticks - (ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero));
    }
}
