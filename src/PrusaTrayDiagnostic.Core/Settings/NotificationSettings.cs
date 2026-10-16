using PrusaTrayDiagnostic.Core.Events;
using PrusaTrayDiagnostic.Core.Notifications;

namespace PrusaTrayDiagnostic.Core.Settings;

/// <summary>
/// Which print events produce a system notification. <see cref="Enabled"/> is the master switch.
/// </summary>
public sealed record NotificationSettings
{
    public const int DefaultRemainingTimeMinutes = 10;

    /// <summary>Upper bound of <see cref="RemainingTimeMinutes"/> (one day).</summary>
    public const int MaxRemainingTimeMinutes = 24 * 60;

    public bool Enabled { get; init; } = true;

    public bool PrintStarted { get; init; } = true;

    public bool PrintFinished { get; init; } = true;

    public bool PrintPaused { get; init; } = true;

    public bool PrintResumed { get; init; } = true;

    /// <summary>A job was stopped (cancelled) before completion.</summary>
    public bool PrintStopped { get; init; } = true;

    public bool Error { get; init; } = true;

    /// <summary>The printer waits for the user (e.g. filament runout) or rejected the configured credentials.</summary>
    public bool Attention { get; init; } = true;

    /// <summary>The printer went offline or came back online.</summary>
    public bool Offline { get; init; } = true;

    /// <summary>
    /// Notify once per job when its estimated remaining time drops to this many minutes; 0 disables the notification.
    /// Within 0–<see cref="MaxRemainingTimeMinutes"/>.
    /// </summary>
    public int RemainingTimeMinutes
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxRemainingTimeMinutes);
            field = value;
        }
    } = DefaultRemainingTimeMinutes;

    /// <summary>Which notifications play a sound; by the user's decision only important ones by default.</summary>
    public NotificationSound Sound
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown notification sound setting.");
            }

            field = value;
        }
    } = NotificationSound.ImportantOnly;

    /// <summary>Whether a notification of <paramref name="severity"/> plays a sound according to <see cref="Sound"/>.</summary>
    public bool ShouldPlaySound(NotificationSeverity severity) => Sound switch
    {
        NotificationSound.All => true,
        NotificationSound.ImportantOnly => severity == NotificationSeverity.ActionRequired,
        _ => false,
    };

    /// <summary>
    /// Whether <paramref name="kind"/> is notified: the master switch and the event's own switch are on
    /// (<see cref="Offline"/> covers going offline and back online, <see cref="Attention"/> also rejected credentials, <see cref="RemainingTimeMinutes"/> above 0 enables
    /// <see cref="PrintEventKind.RemainingTime"/>). Undefined kinds are never notified.
    /// </summary>
    public bool IsEnabledFor(PrintEventKind kind) => Enabled && kind switch
    {
        PrintEventKind.PrintStarted => PrintStarted,
        PrintEventKind.PrintFinished => PrintFinished,
        PrintEventKind.PrintPaused => PrintPaused,
        PrintEventKind.PrintResumed => PrintResumed,
        PrintEventKind.PrintStopped => PrintStopped,
        PrintEventKind.Error => Error,
        // Rejected credentials also need the user to act, so they share the "printer needs you" switch.
        PrintEventKind.AttentionRequired or PrintEventKind.CredentialsRejected => Attention,
        PrintEventKind.PrinterOffline or PrintEventKind.PrinterOnline => Offline,
        PrintEventKind.RemainingTime => RemainingTimeMinutes > 0,
        _ => false,
    };
}
