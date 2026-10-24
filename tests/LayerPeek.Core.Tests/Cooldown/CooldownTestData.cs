using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Tests.Cooldown;

/// <summary>
/// Snapshots and the nozzle cooling curve of the developer simulator (<c>tools/PrusaSimulator</c>, <c>PrinterSimulation</c>):
/// first order towards 23 °C ambient with τ = 60 s for the nozzle and τ = 300 s for the bed, plus its ±0.2 °C wobble.
/// </summary>
internal static class CooldownTestData
{
    public const double Ambient = 23;

    public const double NozzlePrintTemperature = 215;

    public const double BedPrintTemperature = 60;

    public const double NozzleTauSeconds = 60;

    public const double BedTauSeconds = 300;

    public const double PollSeconds = 5;

    public static readonly DateTimeOffset Start = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Nozzle temperature <paramref name="seconds"/> after the heater was switched off.</summary>
    public static double SimulatedNozzle(double seconds) =>
        Ambient + ((NozzlePrintTemperature - Ambient) * Math.Exp(-seconds / NozzleTauSeconds));

    public static double SimulatedBed(double seconds) =>
        Ambient + ((BedPrintTemperature - Ambient) * Math.Exp(-seconds / BedTauSeconds));

    /// <summary>The simulator's wobble, <c>0.2 * sin(0.9 t)</c>.</summary>
    public static double Wobble(double value, double seconds) => value + (0.2 * Math.Sin(seconds * 0.9));

    /// <summary>When the simulated nozzle reaches <paramref name="threshold"/>, in seconds after the heater was switched off.</summary>
    public static double SecondsToReach(double threshold) =>
        NozzleTauSeconds * Math.Log((NozzlePrintTemperature - Ambient) / (threshold - Ambient));

    public static PrintJob Job(long? id = 7, string? fileName = "benchy.bgcode") => new() { Id = id, FileName = fileName, ProgressPercent = 50 };

    /// <summary>A snapshot <paramref name="seconds"/> after <see cref="Start"/>; no telemetry when neither temperature is given.</summary>
    public static PrinterSnapshot Snap(
        PrinterState state,
        double seconds,
        double? nozzle = null,
        double? nozzleTarget = 0,
        double? bed = null,
        PrintJob? job = null,
        string printerId = "mk4") => new()
        {
            PrinterId = printerId,
            State = state,
            Timestamp = Start.AddSeconds(seconds),
            Job = job,
            Telemetry = nozzle is null && bed is null
                ? null
                : new Telemetry
                {
                    Nozzle = nozzle is { } n ? new Temperature(n, nozzleTarget) : null,
                    Bed = bed is { } b ? new Temperature(b, 0) : null,
                },
        };

    /// <summary>A printing snapshot with the heaters on.</summary>
    public static PrinterSnapshot Printing(double seconds, string printerId = "mk4", PrintJob? job = null) =>
        Snap(PrinterState.Printing, seconds, NozzlePrintTemperature, NozzlePrintTemperature, BedPrintTemperature, job ?? Job(), printerId);

    /// <summary>
    /// A snapshot <paramref name="seconds"/> after the end of a print that ended at <paramref name="endSeconds"/>, with the
    /// simulated (heater off) temperatures.
    /// </summary>
    public static PrinterSnapshot Cooling(PrinterState state, double endSeconds, double seconds, string printerId = "mk4", PrintJob? job = null) =>
        Snap(state, seconds, SimulatedNozzle(seconds - endSeconds), 0, SimulatedBed(seconds - endSeconds), job ?? Job(), printerId);
}
