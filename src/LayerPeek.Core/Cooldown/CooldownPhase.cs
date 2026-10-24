namespace LayerPeek.Core.Cooldown;

/// <summary>Where a printer is in cooling down after a print (T-079).</summary>
public enum CooldownPhase
{
    /// <summary>No cool-down is tracked (no print ended, the next one started, the printer is offline or heats again).</summary>
    None = 0,

    /// <summary>The print ended and the nozzle is still above the threshold.</summary>
    Cooling,

    /// <summary>The nozzle is at or below the threshold; lasts until the next print, offline or heating.</summary>
    CooledDown,
}
