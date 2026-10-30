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
    /// Shows that a new version is available (T-058), once per version (the caller remembers which). The same rules as
    /// <see cref="Show"/>: thread-safe, returns quickly, may throw. Its click starts the update action of the app (the release
    /// page, or the update of a Velopack installation). Default: not shown (platforms without notifications).
    /// </summary>
    void ShowUpdateAvailable(UpdateNotification notification)
    {
    }
}

/// <summary>What the notification about a new version shows (T-058); texts are localized by the implementations.</summary>
/// <param name="Version">The new version (<c>0.3.2</c>).</param>
/// <param name="PlaySound">Whether it plays a sound (the "all notifications" sound setting only: it is information).</param>
public sealed record UpdateNotification(string Version, bool PlaySound);
