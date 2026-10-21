namespace PrusaTrayDiagnostic.Simulator.Printers;

/// <summary>What a simulated printer does (see docs/simulator.md).</summary>
public enum PrinterScenario
{
    /// <summary>IDLE, cold, no job.</summary>
    Idle,

    /// <summary>
    /// Endless print cycle: heating (PRINTING, no estimate) → printing (progress, remaining time) → FINISHED → IDLE →
    /// next job.
    /// </summary>
    Printing,

    /// <summary>PAUSED: the job stops where it was (or at 52 %), the nozzle cools to 170 °C.</summary>
    Paused,

    /// <summary>FINISHED: no job in the status, the printer cools down.</summary>
    Finished,

    /// <summary>ERROR: no job, the printer cools down.</summary>
    Error,

    /// <summary>ATTENTION (e.g. filament runout): the job stops where it was (or at 61 %), temperatures are held.</summary>
    Attention,

    /// <summary>Like <see cref="Printing"/>, but the printer stops responding periodically (by default 20 s of every 80 s).</summary>
    Flaky,
}

/// <summary>Command line names of <see cref="PrinterScenario"/>; "offline" is an idle printer that does not respond.</summary>
public static class PrinterScenarios
{
    public const string OfflineName = "offline";

    /// <summary>All names accepted by <see cref="TryParse"/>.</summary>
    public static IReadOnlyList<string> Names { get; } = ["idle", "printing", "paused", "finished", "error", "attention", OfflineName, "flaky"];

    /// <param name="offline">The name was "offline": the scenario is idle and the printer starts offline.</param>
    public static bool TryParse(string? name, out PrinterScenario scenario, out bool offline)
    {
        offline = false;
        switch (name?.Trim().ToUpperInvariant())
        {
            case "IDLE":
                scenario = PrinterScenario.Idle;
                return true;
            case "PRINTING":
                scenario = PrinterScenario.Printing;
                return true;
            case "PAUSED":
                scenario = PrinterScenario.Paused;
                return true;
            case "FINISHED":
                scenario = PrinterScenario.Finished;
                return true;
            case "ERROR":
                scenario = PrinterScenario.Error;
                return true;
            case "ATTENTION":
                scenario = PrinterScenario.Attention;
                return true;
            case "FLAKY":
                scenario = PrinterScenario.Flaky;
                return true;
            case "OFFLINE":
                scenario = PrinterScenario.Idle;
                offline = true;
                return true;
            default:
                scenario = PrinterScenario.Idle;
                return false;
        }
    }

    public static string Format(PrinterScenario scenario) => scenario switch
    {
        PrinterScenario.Idle => "idle",
        PrinterScenario.Printing => "printing",
        PrinterScenario.Paused => "paused",
        PrinterScenario.Finished => "finished",
        PrinterScenario.Error => "error",
        PrinterScenario.Attention => "attention",
        PrinterScenario.Flaky => "flaky",
        _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
    };
}
