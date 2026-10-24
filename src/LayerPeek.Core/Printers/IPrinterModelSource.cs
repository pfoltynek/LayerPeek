namespace LayerPeek.Core.Printers;

/// <summary>
/// Optional capability of an <see cref="IPrinterClient"/> that can ask the printer for its model (PrusaLink: one
/// <c>GET /api/version</c>). <see cref="Monitoring.PrinterMonitor"/> asks once per monitoring run, after the first
/// successful poll, and raises <see cref="Monitoring.IPrinterMonitor.ModelDetected"/>.
/// </summary>
public interface IPrinterModelSource
{
    /// <summary>
    /// Asks the printer for its model code. Expected failures (offline, rejected credentials, unexpected response) are
    /// reported as <see cref="PrinterModelDetectionStatus.Failed"/>, not as exceptions. Never called concurrently with
    /// <see cref="IPrinterClient.GetSnapshotAsync"/> of the same instance by the monitor.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<PrinterModelDetection> DetectModelAsync(CancellationToken cancellationToken);
}

public enum PrinterModelDetectionStatus
{
    /// <summary>The printer reported a well-formed code (<see cref="PrinterModelDetection.Code"/>).</summary>
    Detected,

    /// <summary>The printer answered but reports no (valid) code, e.g. firmware before 6.5. Asking again will not help.</summary>
    NotReported,

    /// <summary>The printer could not be asked (offline, credentials, unexpected response); worth retrying later.</summary>
    Failed,
}

/// <summary>Outcome of <see cref="IPrinterModelSource.DetectModelAsync"/>.</summary>
public readonly record struct PrinterModelDetection
{
    private PrinterModelDetection(PrinterModelDetectionStatus status, PrinterModelCode? code)
    {
        Status = status;
        Code = code;
    }

    public static PrinterModelDetection NotReported => new(PrinterModelDetectionStatus.NotReported, null);

    public static PrinterModelDetection Failed => new(PrinterModelDetectionStatus.Failed, null);

    public PrinterModelDetectionStatus Status { get; }

    /// <summary>The reported code for <see cref="PrinterModelDetectionStatus.Detected"/>; otherwise <see langword="null"/>.</summary>
    public PrinterModelCode? Code { get; }

    public static PrinterModelDetection Detected(PrinterModelCode code) => new(PrinterModelDetectionStatus.Detected, code);
}

/// <summary>A printer reported its model code (<see cref="Monitoring.IPrinterMonitor.ModelDetected"/>).</summary>
/// <param name="PrinterId"><see cref="PrinterDefinition.Id"/> of the printer.</param>
public sealed record PrinterModelDetected(string PrinterId, PrinterModelCode Code)
{
    /// <summary>The model of <see cref="Code"/> (<see cref="PrinterModel.Unknown"/> for codes without an illustration).</summary>
    public PrinterModel Model => PrinterModels.FromCode(Code);
}
