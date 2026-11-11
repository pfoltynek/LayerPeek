using LayerPeek.App.Resources;
using LayerPeek.Core.Cameras;

namespace LayerPeek.App.Cameras;

/// <summary>
/// T-188 (user decision 2026-11-11): while the connection waits between reconnect attempts, the status says roughly when
/// the next one starts (<see cref="CameraStatus.NextAttemptAt"/>). Computed once when the status changes, without a timer:
/// the text does not count down, hence "about".
/// </summary>
public sealed partial class CameraViewModel
{
    // UI thread only: the time to the next attempt when the status arrived; null: none planned, or already due.
    private TimeSpan? _nextAttemptIn;

    /// <summary>
    /// "Next attempt in about 20 s" below the status; <see langword="null"/> while connecting or streaming and in the mini
    /// preview (no room down to 240 × 135).
    /// </summary>
    public string? NextAttemptText => IsMini || _nextAttemptIn is not { } remaining ? null : FormatNextAttempt(remaining);

    public bool HasNextAttempt => NextAttemptText is not null;

    /// <summary>Seconds below a minute (rounded up), whole minutes from a minute on (the backoff's cap is 1 min).</summary>
    internal static string FormatNextAttempt(TimeSpan remaining)
    {
        // Up: the status arrives just after the delay was planned, so a 5 s delay reads 5 s, not 4 s.
        double seconds = Math.Ceiling(remaining.TotalSeconds);
        return seconds < 60
            ? Format(Strings.CameraNextAttemptSeconds, (int)seconds)
            : Format(Strings.CameraNextAttemptMinutes, (int)Math.Max(1, Math.Round(remaining.TotalMinutes, MidpointRounding.AwayFromZero)));
    }

    /// <summary>After <see cref="_status"/> changed: the time to its next attempt, as of now.</summary>
    private void UpdateNextAttempt()
    {
        TimeSpan? remaining = _status is { State: CameraConnectionState.NoSignal or CameraConnectionState.Error, NextAttemptAt: { } at }
            ? at - _timeProvider.GetUtcNow()
            : null;
        if (remaining <= TimeSpan.Zero)
        {
            // Due already (a status replayed late): the attempt is starting, its Connecting follows.
            remaining = null;
        }

        if (_nextAttemptIn != remaining)
        {
            _nextAttemptIn = remaining;
            OnNextAttemptTextChanged();
        }
    }

    private void OnNextAttemptTextChanged()
    {
        OnPropertyChanged(nameof(NextAttemptText));
        OnPropertyChanged(nameof(HasNextAttempt));
    }
}
