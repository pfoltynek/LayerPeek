using Microsoft.Extensions.Logging;

using LayerPeek.App.Resources;
using LayerPeek.Core.Cameras;

namespace LayerPeek.App.Cameras;

/// <summary>
/// T-188 (user decision 2026-11-11): while the connection waits between reconnect attempts, the status says roughly when
/// the next one starts (<see cref="CameraStatus.NextAttemptAt"/>).
/// <para>
/// T-194 (user decision 2026-11-11): the text never goes stale. It is computed from <see cref="CameraStatus.NextAttemptAt"/>
/// and the clock whenever the line may become visible or change (a new status, the window shown or restored, leaving the
/// mini preview, a language switch), and a one-shot timer is due at the next instant the rounded text changes (once a
/// second below a minute, not before that above it). The timer exists only while the line is visible: the window is
/// active (shown, not minimized), not in the mini preview, and the connection waits. Hiding, minimizing, closing, the
/// mini preview and every other status cancel it. Nothing of this touches the frame path.
/// </para>
/// </summary>
public sealed partial class CameraViewModel
{
    private readonly Action _refreshNextAttempt;

    // UI thread only: the text shown (null: hidden), and the timer due when it changes next (null: none).
    private string? _nextAttemptText;
    private ITimer? _nextAttemptTimer;

    /// <summary>
    /// "Next attempt in about 20 s" below the status; <see langword="null"/> while connecting or streaming, while the window
    /// is minimized or closed, and in the mini preview (no room down to 240 × 135).
    /// </summary>
    public string? NextAttemptText => _nextAttemptText;

    public bool HasNextAttempt => _nextAttemptText is not null;

    /// <summary>Whether the timer that keeps <see cref="NextAttemptText"/> current exists (tests: none while hidden).</summary>
    internal bool HasNextAttemptTimer => _nextAttemptTimer is not null;

    /// <summary>Seconds below a minute (rounded up), whole minutes from a minute on (the backoff's cap is 1 min).</summary>
    internal static string FormatNextAttempt(TimeSpan remaining)
    {
        // Up: the status arrives just after the delay was planned, so a 5 s delay reads 5 s, not 4 s.
        double seconds = Math.Ceiling(remaining.TotalSeconds);
        return seconds < 60
            ? Format(Strings.CameraNextAttemptSeconds, (int)seconds)
            : Format(Strings.CameraNextAttemptMinutes, MinutesOf(remaining));
    }

    /// <summary>
    /// How long <see cref="FormatNextAttempt"/> of <paramref name="remaining"/> (positive) stays the same: after exactly
    /// this time the text is a different one (or, at zero, gone).
    /// </summary>
    internal static TimeSpan UntilNextAttemptTextChanges(TimeSpan remaining)
    {
        double seconds = Math.Ceiling(remaining.TotalSeconds);
        if (seconds < 60)
        {
            // "N s" while N - 1 s < remaining <= N s.
            return remaining - TimeSpan.FromSeconds(seconds - 1);
        }

        int minutes = MinutesOf(remaining);
        return minutes == 1
            // "1 min" down to 59 s exclusive, then "59 s".
            ? remaining - TimeSpan.FromSeconds(59)
            // "k min" while remaining >= k - 0.5 min (rounded away from zero): one tick below that it is "k - 1 min".
            : remaining - TimeSpan.FromSeconds((minutes - 0.5) * 60) + TimeSpan.FromTicks(1);
    }

    private static int MinutesOf(TimeSpan remaining) =>
        (int)Math.Max(1, Math.Round(remaining.TotalMinutes, MidpointRounding.AwayFromZero));

    /// <summary>
    /// UI thread, after anything the line depends on changed (status, activity, mode, language) and when its timer is due:
    /// the text as of now, and the timer for its next change while it is visible. Idempotent, so a timer callback posted
    /// just before the timer was cancelled or moved does no harm.
    /// </summary>
    private void RefreshNextAttempt()
    {
        TimeSpan remaining = !_disposed && _active && !IsMini
            && _status is { State: CameraConnectionState.NoSignal or CameraConnectionState.Error, NextAttemptAt: { } at }
            ? at - _timeProvider.GetUtcNow()
            : TimeSpan.Zero;
        string? text = null;
        if (remaining > TimeSpan.Zero)
        {
            text = FormatNextAttempt(remaining);
            // Whole milliseconds, rounded up: timers round down, and a timer due a moment early would only come back.
            TimeSpan due = TimeSpan.FromMilliseconds(Math.Ceiling(UntilNextAttemptTextChanges(remaining).TotalMilliseconds));
            if (_nextAttemptTimer is { } timer)
            {
                timer.Change(due, Timeout.InfiniteTimeSpan);
            }
            else
            {
                _nextAttemptTimer = _timeProvider.CreateTimer(
                    static state => ((CameraViewModel)state!).OnNextAttemptTextDue(), this, due, Timeout.InfiniteTimeSpan);
            }
        }
        else
        {
            // Hidden, or due already (the attempt is starting, its Connecting follows).
            CancelNextAttemptRefresh();
        }

        if (!string.Equals(_nextAttemptText, text, StringComparison.Ordinal))
        {
            bool had = _nextAttemptText is not null;
            _nextAttemptText = text;
            OnPropertyChanged(nameof(NextAttemptText));
            if (had != text is not null)
            {
                OnPropertyChanged(nameof(HasNextAttempt));
            }
        }
    }

    private void CancelNextAttemptRefresh()
    {
        _nextAttemptTimer?.Dispose();
        _nextAttemptTimer = null;
    }

    // Timer thread.
    private void OnNextAttemptTextDue()
    {
        try
        {
            _dispatcher.Post(_refreshNextAttempt);
        }
        catch (Exception ex)
        {
            // The UI is gone (quitting): nothing to show any more.
            LogNextAttemptNotPosted(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "The time to the next camera attempt was not refreshed because the UI is shutting down.")]
    private partial void LogNextAttemptNotPosted(Exception exception);
}
