namespace LayerPeek.Core.Cooldown;

/// <summary>Where a printer is in cooling down after a print (T-079).</summary>
public enum CooldownPhase
{
    /// <summary>No cool-down is tracked (no print ended, the next one started, the printer is offline or heats again).</summary>
    None = 0,

    /// <summary>The print ended and the nozzle or the bed is still above its threshold.</summary>
    Cooling,

    /// <summary>The nozzle and the bed are at or below their thresholds; lasts until the next print, offline or heating.</summary>
    CooledDown,
}
