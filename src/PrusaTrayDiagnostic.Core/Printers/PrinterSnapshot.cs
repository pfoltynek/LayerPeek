namespace PrusaTrayDiagnostic.Core.Printers;

/// <summary>
/// Immutable state of one printer at one point in time. Equality is value-based but includes
/// <see cref="Timestamp"/> and fluctuating <see cref="Telemetry"/>, so it is not suitable for detecting
/// state changes between polls; compare <see cref="State"/> / <see cref="Job"/> for that.
/// </summary>
public sealed record PrinterSnapshot
{
    /// <summary><see cref="PrinterDefinition.Id"/> of the printer.</summary>
    public required string PrinterId
    {
        get;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            field = value;
        }
    }

    public required PrinterState State { get; init; }

    /// <summary>When the snapshot was taken.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Current or last job, <see langword="null"/> when there is none or it is unknown.</summary>
    public PrintJob? Job { get; init; }

    /// <summary><see langword="null"/> when telemetry is unavailable (e.g. printer offline).</summary>
    public Telemetry? Telemetry { get; init; }

    /// <summary>Optional human-readable detail, e.g. why the printer is offline. Never contains secrets.</summary>
    public string? StatusMessage { get; init; }

    /// <summary>Why no data is available, for a translated text; <see cref="PrinterProblem.None"/> when unknown or not applicable.</summary>
    public PrinterProblem Problem { get; init; }

    /// <summary>
    /// Expected end of the job (<see cref="Timestamp"/> + <see cref="PrintJob.EstimatedRemaining"/>) while it is running,
    /// paused or waiting for the user mid-print (<see cref="PrinterState.Attention"/> with a job);
    /// <see langword="null"/> after it ended or when unknown.
    /// </summary>
    public DateTimeOffset? EstimatedCompletion =>
        (State.HasActiveJob() || State == PrinterState.Attention)
        && Job?.EstimatedRemaining is { } remaining
        && remaining.Ticks <= MaxAddableTicks(Timestamp)
            ? Timestamp + remaining
            : null;

    /// <summary>Creates a snapshot for a printer that could not provide data (offline, unauthorized...).</summary>
    public static PrinterSnapshot Unavailable(string printerId, PrinterState state, DateTimeOffset timestamp, string? statusMessage = null, PrinterProblem problem = PrinterProblem.None) =>
        new() { PrinterId = printerId, State = state, Timestamp = timestamp, StatusMessage = statusMessage, Problem = problem };

    // DateTimeOffset addition overflows when either the UTC or the local clock time exceeds DateTime.MaxValue.
    private static long MaxAddableTicks(DateTimeOffset timestamp) =>
        DateTime.MaxValue.Ticks - Math.Max(timestamp.UtcTicks, timestamp.Ticks);
}
