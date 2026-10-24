namespace LayerPeek.App.Tray;

/// <summary>
/// What the tray icon shows. Several <see cref="Core.Printers.PrinterState"/> values share one status
/// (see <see cref="TrayStatusRules.FromPrinterState"/>); icon sets draw one look per status.
/// </summary>
public enum TrayStatus
{
    Idle,
    Finished,

    /// <summary>The printer works on something other than a print (homing, calibration...).</summary>
    Busy,
    Printing,
    Paused,

    /// <summary>No usable data: unreachable, unknown state, not yet polled or nothing configured.</summary>
    Offline,

    /// <summary>The user has to act (printer waits, rejected credentials).</summary>
    Attention,
    Error,
}
