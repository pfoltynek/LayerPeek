namespace PrusaTrayDiagnostic.Simulator.Printers;

/// <summary>What <c>/api/v1/status</c> reports at one moment (firmware state names, °C, mm, %, RPM).</summary>
public sealed record SimulatedStatus(
    string State,
    SimulatedJob? Job,
    double TempNozzle,
    double TargetNozzle,
    double TempBed,
    double TargetBed,
    double AxisZ,
    int Flow,
    int Speed,
    int FanHotend,
    int FanPrint)
{
    /// <summary>X and Y are reported only while the printer is not printing (as the firmware does); null otherwise.</summary>
    public (double X, double Y)? AxisXY { get; init; }
}

/// <param name="Progress">Percent, 0–100.</param>
/// <param name="TimeRemaining">Seconds; null while the firmware has no estimate yet (heating).</param>
/// <param name="TimePrinting">Seconds since the print started.</param>
public sealed record SimulatedJob(long Id, double Progress, int? TimeRemaining, int TimePrinting, SimulatedFile File);

/// <param name="Name">Short (8.3) name.</param>
/// <param name="DisplayName">Long name shown to the user.</param>
/// <param name="Size">Bytes.</param>
public sealed record SimulatedFile(string Name, string DisplayName, long Size);

/// <summary>Durations of the simulated print cycle and of the <see cref="PrinterScenario.Flaky"/> outages.</summary>
public sealed record SimulationTiming
{
    public static SimulationTiming Default { get; } = new();

    /// <summary>Heating before the first layer (state PRINTING, progress 0, no estimate). Simulated time.</summary>
    public TimeSpan HeatUp { get; init; } = TimeSpan.FromSeconds(90);

    /// <summary>The print itself, 0 → 100 %. Simulated time.</summary>
    public TimeSpan Print
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            field = value;
        }
    } = TimeSpan.FromMinutes(30);

    /// <summary>FINISHED after the print. Simulated time.</summary>
    public TimeSpan FinishedHold { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>IDLE before the next job. Simulated time.</summary>
    public TimeSpan IdleHold { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>Flaky printer: responding part of each outage cycle. Real time (not sped up).</summary>
    public TimeSpan FlakyOnline { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Flaky printer: not responding part of each outage cycle. Real time (not sped up).</summary>
    public TimeSpan FlakyOffline { get; init; } = TimeSpan.FromSeconds(20);
}
