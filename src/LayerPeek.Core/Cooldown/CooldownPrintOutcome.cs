using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Cooldown;

/// <summary>How the print whose cool-down is tracked ended (T-079).</summary>
public enum CooldownPrintOutcome
{
    /// <summary>No cool-down is tracked (<see cref="CooldownState.None"/>).</summary>
    None = 0,

    /// <summary>The print ended as <see cref="PrinterState.Finished"/>.</summary>
    Finished,

    /// <summary>The print ended as <see cref="PrinterState.Stopped"/>.</summary>
    Stopped,
}
