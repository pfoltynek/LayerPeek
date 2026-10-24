namespace LayerPeek.Core.Printers;

/// <summary>
/// Printer state independent of the data source (PrusaLink / Prusa Connect).
/// </summary>
public enum PrinterState
{
    /// <summary>State could not be determined (e.g. unexpected response).</summary>
    Unknown = 0,

    /// <summary>Ready, no job running.</summary>
    Idle,

    /// <summary>Doing something other than printing (homing, calibration, filament change...).</summary>
    Busy,

    Printing,

    Paused,

    /// <summary>The last job finished successfully.</summary>
    Finished,

    /// <summary>The last job was stopped (cancelled) before completion.</summary>
    Stopped,

    Error,

    /// <summary>The printer waits for the user (e.g. filament runout, crash detection).</summary>
    Attention,

    /// <summary>The printer is not reachable.</summary>
    Offline,

    /// <summary>The printer rejected the configured credentials.</summary>
    Unauthorized,
}
