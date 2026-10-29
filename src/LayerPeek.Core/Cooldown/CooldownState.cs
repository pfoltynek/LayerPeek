namespace LayerPeek.Core.Cooldown;

/// <summary>
/// How far a printer is in cooling down after a print. Both the nozzle and the bed decide (T-109): the printer is cooled
/// down only when each is at or below its threshold (the bed only when the printer reports it). While
/// <see cref="Phase"/> is <see cref="CooldownPhase.None"/> every other value is its default (<see cref="None"/>).
/// </summary>
/// <param name="Phase">Cooling or cooled down.</param>
/// <param name="NozzleCelsius">Last valid nozzle reading of the cool-down.</param>
/// <param name="BedCelsius">Last valid bed reading of the cool-down; <see langword="null"/> when the printer reports none.</param>
/// <param name="NozzleThresholdCelsius">The printer's nozzle threshold (<see cref="CooldownThresholds.NozzleCelsius"/>).</param>
/// <param name="BedThresholdCelsius">The printer's bed threshold (<see cref="CooldownThresholds.BedCelsius"/>).</param>
/// <param name="Progress">
/// 0–1, the smaller of the nozzle's and the bed's progress (the slower one, T-109). Each goes from the temperature at the
/// start of the cool-down (the highest reading since) to its threshold; a temperature that was never above its threshold
/// counts as 1. 1 once cooled down.
/// </param>
/// <param name="EstimatedRemaining">
/// Estimated time until both temperatures reach their thresholds (the longer of the two estimates, T-109), measured from
/// <paramref name="UpdatedAt"/>; never negative. <see langword="null"/> while there is no reliable estimate for a
/// temperature that is still above its threshold (too few readings, temperature not falling, noise) and once cooled down.
/// </param>
/// <param name="StartedAt">
/// When the cool-down started: the first snapshot after the end of the print with the heaters off (the same as
/// <paramref name="PrintEndedAt"/> unless a heater was switched off later).
/// </param>
/// <param name="UpdatedAt">Timestamp of the snapshot this state was derived from.</param>
/// <param name="Outcome">Whether the print finished or was stopped.</param>
/// <param name="PrintEndedAt">
/// Timestamp of the first snapshot that reported the print as ended, i.e. at most one polling interval after the real
/// end. <see langword="null"/> when the end was not seen because the print ended while the printer could not be read
/// (offline, unauthorized) after it was seen printing. A print that was already over at the first snapshot of the printer
/// (app start, printer added) is not tracked at all (user decision 11, 2026-10-24).
/// </param>
/// <param name="CooledDownAt">
/// When the state became <see cref="CooldownPhase.CooledDown"/>: the snapshot that confirmed it (the same as
/// <see cref="CooldownEvent.Timestamp"/> when there is an event); <see langword="null"/> while cooling.
/// </param>
/// <param name="EndedBelowThreshold">
/// Cooled down without cooling: no reading of the nozzle or the bed since the print ended was above the threshold that
/// confirmed "cooled down" (user decision Q5 of T-079, no event); a later threshold change keeps it. The cool-down widget
/// then shows when the print ended but not "cooled down in" (design T-076 2.5). <see langword="false"/> while cooling.
/// </param>
/// <param name="Limiting">
/// The temperature that holds the cool-down up (T-109): while cooling the one with the smaller progress (on a tie the
/// previous one, the nozzle at the start and without a bed reading), once cooled down the one that was limiting last. Compact texts (the mini preview strip,
/// the small card) show <see cref="LimitingCelsius"/>.
/// </param>
public readonly record struct CooldownState(
    CooldownPhase Phase,
    double? NozzleCelsius,
    double? BedCelsius,
    int NozzleThresholdCelsius,
    int BedThresholdCelsius,
    double Progress,
    TimeSpan? EstimatedRemaining,
    DateTimeOffset? StartedAt,
    DateTimeOffset? UpdatedAt,
    CooldownPrintOutcome Outcome,
    DateTimeOffset? PrintEndedAt,
    DateTimeOffset? CooledDownAt,
    bool EndedBelowThreshold = false,
    CooldownSensor Limiting = CooldownSensor.Nozzle)
{
    /// <summary>No cool-down.</summary>
    public static CooldownState None => default;

    /// <summary>Both thresholds of the state.</summary>
    public CooldownThresholds Thresholds => new(NozzleThresholdCelsius, BedThresholdCelsius);

    /// <summary>The reading of <see cref="Limiting"/>: the bed's when it limits, otherwise the nozzle's.</summary>
    public double? LimitingCelsius => Limiting == CooldownSensor.Bed && BedCelsius is not null ? BedCelsius : NozzleCelsius;
}
