namespace LayerPeek.Core.Cooldown;

/// <summary>Result of <see cref="CooldownTracker.Process"/> and <see cref="CooldownTracker.SetThreshold"/>.</summary>
/// <param name="State">The state after the update.</param>
/// <param name="Event">Set when this update cooled the printer down (at most once per print).</param>
public readonly record struct CooldownUpdate(CooldownState State, CooldownEvent? Event);
