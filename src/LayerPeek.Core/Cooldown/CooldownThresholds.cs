namespace LayerPeek.Core.Cooldown;

/// <summary>
/// The nozzle and bed thresholds of one printer (T-109): it counts as cooled down only when the nozzle is at or below
/// <see cref="NozzleCelsius"/> <b>and</b> the bed at or below <see cref="BedCelsius"/>. Each within
/// <see cref="CooldownThreshold.MinCelsius"/>–<see cref="CooldownThreshold.MaxCelsius"/>; <c>default</c> (0 / 0) is not
/// valid, use <see cref="Default"/>.
/// </summary>
/// <param name="NozzleCelsius">Nozzle threshold in whole °C.</param>
/// <param name="BedCelsius">Bed threshold in whole °C.</param>
public readonly record struct CooldownThresholds(int NozzleCelsius, int BedCelsius)
{
    /// <summary><see cref="CooldownThreshold.DefaultNozzleCelsius"/> / <see cref="CooldownThreshold.DefaultBedCelsius"/>.</summary>
    public static CooldownThresholds Default => new(CooldownThreshold.DefaultNozzleCelsius, CooldownThreshold.DefaultBedCelsius);

    /// <summary>Both thresholds are within the range.</summary>
    public bool IsValid => CooldownThreshold.IsValid(NozzleCelsius) && CooldownThreshold.IsValid(BedCelsius);
}
