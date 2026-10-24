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
}
