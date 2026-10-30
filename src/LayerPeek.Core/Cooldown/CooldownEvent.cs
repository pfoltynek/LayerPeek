namespace LayerPeek.Core.Cooldown;

/// <summary>
/// A printer cooled down to its thresholds after a print (<see cref="CooldownPhase.Cooling"/> →
/// <see cref="CooldownPhase.CooledDown"/>): the nozzle and (when reported) the bed are at or below their thresholds (T-109);
/// at most once per print, and never for a print that ended already at or below both thresholds.
/// </summary>
/// <param name="PrinterId">The printer.</param>
/// <param name="Timestamp">Timestamp of the snapshot that confirmed it (or of the last snapshot when a threshold change did).</param>
/// <param name="FileName">File name of the print that ended, <see langword="null"/> when unknown.</param>
/// <param name="Outcome">
/// How the print ended (<see cref="CooldownPrintOutcome.Finished"/> or <see cref="CooldownPrintOutcome.Stopped"/>), so that
/// the notification can tell "print finished" from "print stopped" (user decision 12, 2026-10-24).
/// </param>
/// <param name="NozzleCelsius">The last nozzle reading when it was confirmed (at or below <paramref name="NozzleThresholdCelsius"/>).</param>
/// <param name="NozzleThresholdCelsius">The printer's nozzle threshold (<see cref="CooldownThresholds.NozzleCelsius"/>).</param>
/// <param name="BedCelsius">
/// The last bed reading when it was confirmed (at or below <paramref name="BedThresholdCelsius"/>); <see langword="null"/>
/// when the printer reported no bed temperature during the cool-down (then the nozzle alone decided).
/// </param>
/// <param name="BedThresholdCelsius">The printer's bed threshold (<see cref="CooldownThresholds.BedCelsius"/>).</param>
public readonly record struct CooldownEvent(
    string PrinterId,
    DateTimeOffset Timestamp,
    string? FileName,
    CooldownPrintOutcome Outcome,
    double NozzleCelsius,
    int NozzleThresholdCelsius,
    double? BedCelsius,
    int BedThresholdCelsius)
{
    /// <summary>Both thresholds as configured.</summary>
    public CooldownThresholds Thresholds => new(NozzleThresholdCelsius, BedThresholdCelsius);
}
