using System.Globalization;

namespace LayerPeek.Core.Updates;

/// <summary>
/// Whether updates offer pre-release versions (user decision 2026-10-24, T-017): the user's explicit choice
/// (<see cref="Settings.AppSettings.OfferPrereleaseUpdates"/>) always wins; while there is none, the running version decides:
/// <c>0.x</c> offers them (every <c>0.x</c> release is a GitHub pre-release, T-055), <c>1.0</c> and later do not.
/// </summary>
public static class PrereleaseUpdates
{
    /// <param name="choice">The user's choice; <see langword="null"/> while the user never set it.</param>
    /// <param name="runningVersion">The version of the running app (<c>X.Y.Z</c>).</param>
    public static bool Offered(bool? choice, string runningVersion)
    {
        ArgumentNullException.ThrowIfNull(runningVersion);
        return choice ?? IsDefaultFor(runningVersion);
    }

    /// <summary>
    /// The default without a choice: on for <c>0.x</c>; also on for a version that cannot be read (the app reads its own
    /// version from its assembly, so that is never a released 1.x).
    /// </summary>
    public static bool IsDefaultFor(string runningVersion)
    {
        ArgumentNullException.ThrowIfNull(runningVersion);
        int dot = runningVersion.IndexOf('.', StringComparison.Ordinal);
        ReadOnlySpan<char> major = dot < 0 ? runningVersion : runningVersion.AsSpan(0, dot);
        return !int.TryParse(major, NumberStyles.None, CultureInfo.InvariantCulture, out int number) || number == 0;
    }
}
