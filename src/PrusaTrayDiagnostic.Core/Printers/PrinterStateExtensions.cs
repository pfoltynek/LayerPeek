namespace PrusaTrayDiagnostic.Core.Printers;

/// <summary>
/// Classification of <see cref="PrinterState"/> values. Undefined values behave like <see cref="PrinterState.Unknown"/>.
/// </summary>
public static class PrinterStateExtensions
{
    /// <summary>The printer answered and the reported state is meaningful.</summary>
    public static bool IsReachable(this PrinterState state) =>
        state is PrinterState.Idle or PrinterState.Busy or PrinterState.Printing or PrinterState.Paused
            or PrinterState.Finished or PrinterState.Stopped or PrinterState.Error or PrinterState.Attention;

    /// <summary>A job is in progress (running or paused).</summary>
    public static bool HasActiveJob(this PrinterState state) =>
        state is PrinterState.Printing or PrinterState.Paused;

    /// <summary>The user has to act (printer error, printer waiting, rejected credentials).</summary>
    public static bool RequiresAttention(this PrinterState state) =>
        state is PrinterState.Error or PrinterState.Attention or PrinterState.Unauthorized;
}
