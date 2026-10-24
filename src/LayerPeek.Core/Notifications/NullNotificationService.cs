namespace LayerPeek.Core.Notifications;

/// <summary>
/// Discards notifications. Used on platforms without an implementation (user decision: notifications are then
/// silently not sent).
/// </summary>
public sealed class NullNotificationService : INotificationService
{
    public static NullNotificationService Instance { get; } = new();

    private NullNotificationService()
    {
    }

    public void Show(PrintNotification notification)
    {
    }
}
