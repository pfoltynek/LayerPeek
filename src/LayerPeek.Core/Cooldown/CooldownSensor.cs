namespace LayerPeek.Core.Cooldown;

/// <summary>Which temperature of a printer a cool-down value refers to (T-109).</summary>
public enum CooldownSensor
{
    /// <summary>The nozzle (hotend).</summary>
    Nozzle = 0,

    /// <summary>The heated bed.</summary>
    Bed,
}
