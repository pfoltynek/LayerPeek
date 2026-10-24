namespace LayerPeek.Core.Cooldown;

/// <summary>
/// The nozzle of a printer cooled down to its threshold after a print (<see cref="CooldownPhase.Cooling"/> →
/// <see cref="CooldownPhase.CooledDown"/>): at most once per print, and never for a print that ended already at or below
/// the threshold.
/// </summary>
/// <param name="PrinterId">The printer.</param>
/// <param name="Timestamp">Timestamp of the snapshot that confirmed it (or of the last snapshot when a threshold change did).</param>
/// <param name="FileName">File name of the print that ended, <see langword="null"/> when unknown.</param>
public readonly record struct CooldownEvent(string PrinterId, DateTimeOffset Timestamp, string? FileName);
