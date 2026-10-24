namespace LayerPeek.Core.Cooldown;

/// <summary>
/// How far a printer is in cooling down after a print. Only the nozzle decides; the bed is shown only. While
/// <see cref="Phase"/> is <see cref="CooldownPhase.None"/> every other value is its default (<see cref="None"/>).
/// </summary>
/// <param name="Phase">Cooling or cooled down.</param>
/// <param name="NozzleCelsius">Last valid nozzle reading of the cool-down.</param>
/// <param name="BedCelsius">Last valid bed reading of the cool-down; <see langword="null"/> when the printer reports none.</param>
/// <param name="ThresholdCelsius">The printer's threshold (<see cref="CooldownThreshold"/>).</param>
/// <param name="Progress">
/// 0–1, from the nozzle temperature at the start of the cool-down (the highest reading since) to the threshold; 1 once
/// cooled down.
/// </param>
/// <param name="EstimatedRemaining">
/// Estimated time until the nozzle reaches the threshold, measured from <paramref name="UpdatedAt"/>; never negative.
/// <see langword="null"/> while there is no reliable estimate (too few readings, temperature not falling, noise) and once
/// cooled down.
/// </param>
/// <param name="StartedAt">When the cool-down started: the print ended and the nozzle heater was off.</param>
/// <param name="UpdatedAt">Timestamp of the snapshot this state was derived from.</param>
public readonly record struct CooldownState(
    CooldownPhase Phase,
    double? NozzleCelsius,
    double? BedCelsius,
    int ThresholdCelsius,
    double Progress,
    TimeSpan? EstimatedRemaining,
    DateTimeOffset? StartedAt,
    DateTimeOffset? UpdatedAt)
{
    /// <summary>No cool-down.</summary>
    public static CooldownState None => default;
}
