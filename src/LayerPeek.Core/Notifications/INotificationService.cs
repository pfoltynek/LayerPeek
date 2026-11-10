using LayerPeek.Core.Cameras.Recording;

namespace LayerPeek.Core.Notifications;

/// <summary>
/// Shows system notifications (Windows toast – T-010, macOS User Notifications – T-020). Implemented per platform in App.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Shows <paramref name="notification"/>. Called from background threads, possibly concurrently for different printers:
    /// implementations must be thread-safe, return quickly (marshal or queue work themselves) and may throw; callers log
    /// the exception and carry on. Sound follows <see cref="PrintNotification.PlaySound"/>.
    /// A call may still arrive while the app shuts down (a monitor callback that started before the caller was
    /// disposed): implementations must tolerate it, e.g. by ignoring calls after their own shutdown, and never block it.
    /// </summary>
    void Show(PrintNotification notification);

    /// <summary>
    /// Shows a suspected print failure (T-153; T-154: the texts, the snapshot as the image of the notification, a click opens
    /// the camera window, the "False alarm" action records the alarm). The same rules as <see cref="Show"/>: thread-safe,
    /// returns quickly, may throw. Sound follows
    /// <see cref="Detection.PrintFailureNotification.PlaySound"/>. Default: not shown (platforms without notifications).
    /// </summary>
    void ShowPrintFailure(Detection.PrintFailureNotification notification)
    {
    }

    /// <summary>
    /// Shows that a new version is available (T-058), once per version (the caller remembers which). The same rules as
    /// <see cref="Show"/>: thread-safe, returns quickly, may throw. Its click starts the update action of the app (the release
    /// page, or the update of a Velopack installation). Default: not shown (platforms without notifications).
    /// </summary>
    void ShowUpdateAvailable(UpdateNotification notification)
    {
    }

    /// <summary>
    /// Shows that an update was refused because its feed failed the signature check (T-181), once per offered version (the
    /// caller remembers which). The same rules as <see cref="Show"/>: thread-safe, returns quickly, may throw. Its click opens
    /// the settings window on the Startup and updates tab, where the error stays. Default: not shown (platforms without
    /// notifications).
    /// </summary>
    void ShowUpdateRejected(UpdateRejectedNotification notification)
    {
    }

    /// <summary>
    /// Shows that a camera recording could not start or ended with an error (T-146; the only notified recording event,
    /// user decision 2026-11-03 on T-145). The same rules as <see cref="Show"/>: thread-safe, returns quickly, may throw.
    /// A click opens the settings window (where the folder and the limit are). Default: not shown (platforms without
    /// notifications).
    /// </summary>
    void ShowRecordingFailed(RecordingNotification notification)
    {
    }

    /// <summary>
    /// Shows that the JPEG export of a timelapse (T-165) or a timelapse from a recording (T-164,
    /// <see cref="TimelapseExportNotification.Kind"/>) is done or failed (a cancelled one is not notified). The same rules as
    /// <see cref="Show"/>: thread-safe, returns quickly, may throw. A click opens nothing of its own. Default: not shown
    /// (platforms without notifications).
    /// </summary>
    void ShowTimelapseExportFinished(TimelapseExportNotification notification)
    {
    }
}

/// <summary>
/// What the notification about a finished JPEG export of a timelapse (T-165) or a finished timelapse from a recording
/// (T-164) shows; texts are localized by the implementations from <see cref="Kind"/> and <see cref="Problem"/>. Contains no
/// path.
/// </summary>
/// <param name="PrinterId">The printer whose menu started it; <see langword="null"/>: none (the title is the app name).</param>
/// <param name="PrinterName">Its name, the title of the notification (the id when empty).</param>
/// <param name="Frames">Frames written.</param>
/// <param name="Problem">Why it failed; <see cref="Cameras.Timelapse.CameraTimelapseExportProblem.None"/> when it succeeded.</param>
/// <param name="PlaySound">Whether it plays a sound (the "all notifications" sound setting only: it is information).</param>
/// <param name="Kind">Which job ended.</param>
public sealed record TimelapseExportNotification(
    string? PrinterId,
    string? PrinterName,
    int Frames,
    Cameras.Timelapse.CameraTimelapseExportProblem Problem,
    bool PlaySound,
    TimelapseExportKind Kind = TimelapseExportKind.Jpeg)
{
    public bool Succeeded => Problem == Cameras.Timelapse.CameraTimelapseExportProblem.None;
}

/// <summary>What the notification about a new version shows (T-058); texts are localized by the implementations.</summary>
/// <param name="Version">The new version (<c>0.3.2</c>).</param>
/// <param name="PlaySound">Whether it plays a sound (the "all notifications" sound setting only: it is information).</param>
public sealed record UpdateNotification(string Version, bool PlaySound);

/// <summary>What the notification about a refused update shows (T-181); texts are localized by the implementations.</summary>
/// <param name="Version">
/// The version the refused feed claims (<see cref="Updates.RejectedUpdateFeed.ClaimedVersion"/>; untrusted, shown only), or
/// <see langword="null"/> when it names none.
/// </param>
/// <param name="PlaySound">Whether it plays a sound (like <see cref="UpdateNotification.PlaySound"/>).</param>
public sealed record UpdateRejectedNotification(string? Version, bool PlaySound);

/// <summary>
/// What the notification about a failed camera recording shows (T-146); texts are localized by the implementations from
/// <see cref="Problem"/>. Contains no path.
/// </summary>
/// <param name="PrinterId">The printer (id as configured).</param>
/// <param name="PrinterName">Its name, the title of the notification (the id when empty).</param>
/// <param name="Problem">What went wrong.</param>
/// <param name="SizeLimitGigabytes">The size limit of the recordings, for <see cref="CameraRecordingProblem.SizeLimitReached"/>.</param>
/// <param name="MinFreeGigabytes">The free space the recordings need, for <see cref="CameraRecordingProblem.DiskSpaceLow"/>.</param>
/// <param name="PlaySound">Whether it plays a sound (decided by the sound setting like a printer error).</param>
/// <param name="IsDashcam">The dashcam failed to save the last minutes (T-148), not a recording.</param>
/// <param name="IsTimelapse">A timelapse failed (T-161; <see cref="CameraRecordingError.IsTimelapse"/>), not a recording.</param>
public sealed record RecordingNotification(
    string PrinterId,
    string PrinterName,
    CameraRecordingProblem Problem,
    int SizeLimitGigabytes,
    int MinFreeGigabytes,
    bool PlaySound,
    bool IsDashcam = false,
    bool IsTimelapse = false);

/// <summary>Which job a <see cref="TimelapseExportNotification"/> is about.</summary>
public enum TimelapseExportKind
{
    /// <summary>The JPEG export of a timelapse (T-165).</summary>
    Jpeg,

    /// <summary>A timelapse made from a finished recording (T-164).</summary>
    FromRecording,
}
