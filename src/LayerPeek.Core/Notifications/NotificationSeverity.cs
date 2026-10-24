namespace LayerPeek.Core.Notifications;

/// <summary>
/// How important a notification is. Implementations use it e.g. for the sound: by the user's decision (T-010) only
/// <see cref="ActionRequired"/> plays a sound by default.
/// </summary>
public enum NotificationSeverity
{
    /// <summary>Progress of a print (started, finished, paused...); the printer is back online.</summary>
    Information = 0,

    /// <summary>Something may be wrong, but the user does not have to act (the printer went offline).</summary>
    Warning,

    /// <summary>The printer needs the user (error, waiting for the user).</summary>
    ActionRequired,
}
