namespace LayerPeek.Core.Events;

/// <summary>
/// One detected event.
/// </summary>
/// <param name="Kind">What happened.</param>
/// <param name="PrinterId"><see cref="Printers.PrinterSnapshot.PrinterId"/> of the snapshot that revealed the event.</param>
/// <param name="Timestamp"><see cref="Printers.PrinterSnapshot.Timestamp"/> of that snapshot.</param>
/// <param name="FileName">File name of the job the event concerns, <see langword="null"/> when there is none or it is unknown.</param>
/// <param name="Remaining">Estimated remaining time; set only for <see cref="PrintEventKind.RemainingTime"/>.</param>
public readonly record struct PrintEvent(
    PrintEventKind Kind,
    string PrinterId,
    DateTimeOffset Timestamp,
    string? FileName,
    TimeSpan? Remaining);
