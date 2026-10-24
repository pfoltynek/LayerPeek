using LayerPeek.Core.Printers;

namespace LayerPeek.PrusaLink.Api;

/// <summary>
/// Maps <c>printer.state</c> of /api/v1/status. Buddy sends IDLE, BUSY, PRINTING, PAUSED, FINISHED, STOPPED,
/// ATTENTION, ERROR and UNKNOWN; READY exists in the spec (Prusa Connect only) and means idle.
/// Anything else, e.g. a state added by newer firmware, is <see cref="PrinterState.Unknown"/>.
/// </summary>
internal static class PrusaLinkStates
{
    public static PrinterState Parse(string? state) => state?.ToUpperInvariant() switch
    {
        "IDLE" or "READY" => PrinterState.Idle,
        "BUSY" => PrinterState.Busy,
        "PRINTING" => PrinterState.Printing,
        "PAUSED" => PrinterState.Paused,
        "FINISHED" => PrinterState.Finished,
        "STOPPED" => PrinterState.Stopped,
        "ERROR" => PrinterState.Error,
        "ATTENTION" => PrinterState.Attention,
        _ => PrinterState.Unknown,
    };
}
