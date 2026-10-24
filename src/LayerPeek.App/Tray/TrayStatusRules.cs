using LayerPeek.Core.Printers;

namespace LayerPeek.App.Tray;

/// <summary>
/// Maps printer states to tray statuses and decides which printer the single tray icon represents.
/// </summary>
internal static class TrayStatusRules
{
    /// <summary>Undefined values behave like <see cref="PrinterState.Unknown"/>.</summary>
    public static TrayStatus FromPrinterState(PrinterState state) => state switch
    {
        PrinterState.Idle => TrayStatus.Idle,
        PrinterState.Busy => TrayStatus.Busy,
        PrinterState.Printing => TrayStatus.Printing,
        PrinterState.Paused => TrayStatus.Paused,
        PrinterState.Finished => TrayStatus.Finished,
        // A cancelled job needs no further action; the tooltip still says "Stopped".
        PrinterState.Stopped => TrayStatus.Idle,
        PrinterState.Error => TrayStatus.Error,
        PrinterState.Attention => TrayStatus.Attention,
        PrinterState.Unauthorized => TrayStatus.Attention,
        _ => TrayStatus.Offline,
    };

    /// <summary>
    /// Higher is more severe: error &gt; attention &gt; offline &gt; paused &gt; printing &gt; busy &gt; finished &gt; idle.
    /// </summary>
    public static int Severity(TrayStatus status) => status switch
    {
        TrayStatus.Idle => 0,
        TrayStatus.Finished => 1,
        TrayStatus.Busy => 2,
        TrayStatus.Printing => 3,
        TrayStatus.Paused => 4,
        TrayStatus.Offline => 5,
        TrayStatus.Attention => 6,
        TrayStatus.Error => 7,
        _ => 5,
    };

    /// <summary>
    /// Index of the printer the tray icon represents: the most severe one, the first configured one on a tie;
    /// -1 for an empty list.
    /// </summary>
    public static int SelectPrimary(IReadOnlyList<TrayPrinter> printers)
    {
        ArgumentNullException.ThrowIfNull(printers);
        int primary = -1;
        int primarySeverity = int.MinValue;
        for (int i = 0; i < printers.Count; i++)
        {
            int severity = Severity(printers[i].Status);
            if (severity > primarySeverity)
            {
                primary = i;
                primarySeverity = severity;
            }
        }

        return primary;
    }
}
