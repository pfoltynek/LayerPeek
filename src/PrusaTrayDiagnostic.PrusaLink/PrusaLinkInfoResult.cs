using PrusaTrayDiagnostic.Core.Printers;

namespace PrusaTrayDiagnostic.PrusaLink;

/// <summary>
/// Outcome of <see cref="PrusaLinkClient.GetInfoAsync"/>: <see cref="PrinterState.Idle"/> with <see cref="Info"/> when the
/// connection works, otherwise <see cref="PrinterState.Offline"/>, <see cref="PrinterState.Unauthorized"/> or
/// <see cref="PrinterState.Unknown"/> with <see cref="Message"/>.
/// </summary>
public sealed record PrusaLinkInfoResult(PrinterState State, string? Message, PrusaLinkPrinterInfo? Info);

/// <param name="Firmware">Firmware version, e.g. "6.9.2+10123"; null before firmware 6.5.</param>
/// <param name="PrinterModel">Model code "type.version.subversion"; null before firmware 6.5.</param>
public sealed record PrusaLinkPrinterInfo(
    string? Hostname,
    string? Firmware,
    string? ApiVersion,
    string? PrinterModel,
    double? NozzleDiameter,
    bool HasMmu);
