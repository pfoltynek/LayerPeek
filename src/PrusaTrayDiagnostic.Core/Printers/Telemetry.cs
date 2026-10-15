namespace PrusaTrayDiagnostic.Core.Printers;

/// <summary>
/// Measured temperature in °C with the optional target (0 usually means the heater is off).
/// </summary>
public readonly record struct Temperature(double Actual, double? Target);

/// <summary>
/// Telemetry values; any value the data source does not provide is <see langword="null"/>.
/// </summary>
public sealed record Telemetry
{
    public Temperature? Nozzle { get; init; }

    public Temperature? Bed { get; init; }

    public int? HotendFanRpm { get; init; }

    public int? PrintFanRpm { get; init; }

    /// <summary>Z axis position in millimetres.</summary>
    public double? AxisZ { get; init; }

    /// <summary>Print speed multiplier in percent (100 = nominal).</summary>
    public int? SpeedPercent { get; init; }

    /// <summary>Flow multiplier in percent (100 = nominal).</summary>
    public int? FlowPercent { get; init; }
}
