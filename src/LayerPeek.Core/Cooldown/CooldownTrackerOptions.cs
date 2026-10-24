using LayerPeek.Core.Events;

namespace LayerPeek.Core.Cooldown;

public sealed record CooldownTrackerOptions
{
    public static CooldownTrackerOptions Default { get; } = new();

    /// <summary>
    /// How long a printer must stay unreachable before its cool-down ends ("the printer went offline"). Same as the delay
    /// of the offline notification (<see cref="PrintEventDetectorOptions.OfflineDelay"/>), so that a single missed poll
    /// (weak Wi-Fi) does not lose the cool-down and its event. Measured between snapshot timestamps;
    /// <see cref="TimeSpan.Zero"/> ends it with the first offline snapshot.
    /// </summary>
    public TimeSpan OfflineGrace
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            field = value;
        }
    } = PrintEventDetectorOptions.Default.OfflineDelay;
}
