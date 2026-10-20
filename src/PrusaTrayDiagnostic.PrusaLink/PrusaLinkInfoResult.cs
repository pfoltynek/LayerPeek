using PrusaTrayDiagnostic.Core.Printers;

namespace PrusaTrayDiagnostic.PrusaLink;

/// <summary>
/// Outcome of <see cref="PrusaLinkClient.GetInfoAsync"/>: <see cref="PrinterState.Idle"/> with <see cref="Info"/> when the
/// connection works, otherwise <see cref="PrinterState.Offline"/>, <see cref="PrinterState.Unauthorized"/> or
/// <see cref="PrinterState.Unknown"/> with <see cref="Message"/> and <see cref="Failure"/>.
/// </summary>
public sealed record PrusaLinkInfoResult(PrinterState State, string? Message, PrusaLinkPrinterInfo? Info)
{
    /// <summary>Why the request failed; <see cref="PrusaLinkFailure.None"/> on success.</summary>
    public PrusaLinkFailure Failure { get; init; }
}

/// <param name="Firmware">Firmware version, e.g. "6.9.2+10123"; null before firmware 6.5.</param>
/// <param name="PrinterModel">Model code "type.version.subversion"; null before firmware 6.5.</param>
public sealed record PrusaLinkPrinterInfo(
    string? Hostname,
    string? Firmware,
    string? ApiVersion,
    string? PrinterModel,
    double? NozzleDiameter,
    bool HasMmu)
{
    /// <summary>
    /// <see cref="PrinterModel"/> parsed (T-039): <see langword="null"/> when missing (firmware before 6.5) or malformed.
    /// Map it with <see cref="PrinterModels.FromCode(Core.Printers.PrinterModelCode)"/>; store it with
    /// <see cref="Core.Settings.AppSettings.WithDetectedModel"/>.
    /// </summary>
    public PrinterModelCode? ModelCode => PrinterModelCode.TryParse(PrinterModel, out PrinterModelCode code) ? code : null;
}
