namespace LayerPeek.Core.Monitoring;

/// <summary>
/// The exponential backoff of the printer polling (<see cref="PrinterMonitor"/>) and of the camera reconnect
/// (<see cref="Cameras.ReconnectingCameraFeed"/>, T-188).
/// </summary>
internal static class ExponentialBackoff
{
    /// <summary>
    /// <paramref name="initial"/> for the first consecutive failure (and for none), doubled for every further one, capped
    /// at <paramref name="max"/>; a <paramref name="max"/> below <paramref name="initial"/> means no backoff.
    /// </summary>
    public static TimeSpan Delay(TimeSpan initial, TimeSpan max, int consecutiveFailures)
    {
        if (max <= initial)
        {
            return initial;
        }

        long ticks = initial.Ticks;
        for (int failure = 1; failure < consecutiveFailures && ticks < max.Ticks; failure++)
        {
            // Capped before doubling, so a cap near TimeSpan.MaxValue cannot overflow into a negative delay.
            ticks = ticks > max.Ticks / 2 ? max.Ticks : ticks * 2;
        }

        return ticks < max.Ticks ? TimeSpan.FromTicks(ticks) : max;
    }
}
