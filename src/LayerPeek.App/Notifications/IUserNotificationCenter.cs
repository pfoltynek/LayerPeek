namespace LayerPeek.App.Notifications;

/// <summary>What the user allowed for the app's notifications in the settings of the OS (macOS, T-020).</summary>
internal enum NotificationPermission
{
    /// <summary>The OS did not say (no reply, an error).</summary>
    Unknown,

    /// <summary>The app has not asked yet; the OS asks the user with the first notification.</summary>
    NotDetermined,

    /// <summary>The user turned the app's notifications off.</summary>
    Denied,

    /// <summary>Notifications are shown (also provisionally, quietly in the notification center only).</summary>
    Allowed,
}

/// <summary>One notification for <see cref="IUserNotificationCenter.Post"/>.</summary>
/// <param name="Identifier">Unique per notification.</param>
/// <param name="Title">The printer name.</param>
/// <param name="Body">The event, and on a second line the job file name or the temperatures.</param>
/// <param name="PlaySound">With the default notification sound.</param>
/// <param name="LaunchArgument">
/// Handed back on click (<see cref="ToastLaunchArgument"/>); <see langword="null"/>: a click opens nothing.
/// </param>
internal sealed record UserNotificationRequest(string Identifier, string Title, string Body, bool PlaySound, string? LaunchArgument);

/// <summary>
/// The notification center of the OS for one app identity (macOS: <c>UNUserNotificationCenter</c> of the bundle identifier,
/// T-020), replaceable by a fake in tests. Thread-safe; the calls may block shortly on the OS, so not on the UI thread.
/// </summary>
internal interface IUserNotificationCenter
{
    /// <summary>The bundle identifier the notifications belong to.</summary>
    string BundleIdentifier { get; }

    /// <summary>
    /// Asks for permission to show alerts with sound; the OS asks the user only the first time, later it answers with the
    /// user's choice at once.
    /// </summary>
    /// <returns>Whether notifications are allowed.</returns>
    /// <exception cref="Exception">The OS did not answer or refused the request.</exception>
    Task<bool> RequestAuthorizationAsync(CancellationToken cancellationToken);

    /// <summary>The current permission, without asking the user.</summary>
    /// <exception cref="Exception">The OS did not answer.</exception>
    Task<NotificationPermission> GetPermissionAsync(CancellationToken cancellationToken);

    /// <summary>Hands the notification to the OS (delivery is asynchronous; its failures are logged by the implementation).</summary>
    void Post(UserNotificationRequest request);

    /// <summary>Removes every delivered and pending notification of the app (the test cleanup only).</summary>
    void RemoveAll();

    /// <summary>How many notifications of the app the notification center shows.</summary>
    /// <exception cref="Exception">The OS did not answer.</exception>
    Task<int> CountDeliveredAsync(CancellationToken cancellationToken);
}
