namespace LayerPeek.Core.Settings;

/// <summary>Which notifications play a sound.</summary>
public enum NotificationSound
{
    /// <summary>All notifications are silent.</summary>
    Off = 0,

    /// <summary>
    /// Only notifications the user has to act on (<see cref="Notifications.NotificationSeverity.ActionRequired"/>:
    /// error, attention required, rejected credentials).
    /// </summary>
    ImportantOnly = 1,

    /// <summary>Every notification.</summary>
    All = 2,
}
