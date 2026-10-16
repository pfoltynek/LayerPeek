using PrusaTrayDiagnostic.Core.Events;
using PrusaTrayDiagnostic.Core.Printers;

namespace PrusaTrayDiagnostic.Core.Notifications;

/// <summary>
/// What a notification shows. Contains no secrets. Texts are not localized here: implementations build them from
/// <see cref="Kind"/>, <see cref="Printer"/> name, <see cref="FileName"/> and <see cref="Remaining"/>.
/// </summary>
public sealed record PrintNotification
{
    /// <summary>The printer, so that a click can open its web interface.</summary>
    public required PrinterDefinition Printer { get; init; }

    public required PrintEventKind Kind { get; init; }

    /// <summary>When the event was detected (timestamp of the snapshot).</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>File name of the job, <see langword="null"/> when there is none or it is unknown.</summary>
    public string? FileName { get; init; }

    /// <summary>Estimated remaining time; set for <see cref="PrintEventKind.RemainingTime"/>.</summary>
    public TimeSpan? Remaining { get; init; }

    /// <summary>
    /// Whether the notification plays a sound, decided by <see cref="Settings.NotificationSettings.ShouldPlaySound"/>
    /// when it is created; implementations only follow it.
    /// </summary>
    public bool PlaySound { get; init; }

    /// <summary>Derived from <see cref="Kind"/>, see <see cref="SeverityOf"/>.</summary>
    public NotificationSeverity Severity => SeverityOf(Kind);

    /// <summary>Severity of an event kind; undefined kinds are <see cref="NotificationSeverity.Information"/>.</summary>
    public static NotificationSeverity SeverityOf(PrintEventKind kind) => kind switch
    {
        PrintEventKind.Error or PrintEventKind.AttentionRequired or PrintEventKind.CredentialsRejected => NotificationSeverity.ActionRequired,
        PrintEventKind.PrinterOffline => NotificationSeverity.Warning,
        _ => NotificationSeverity.Information,
    };
}
