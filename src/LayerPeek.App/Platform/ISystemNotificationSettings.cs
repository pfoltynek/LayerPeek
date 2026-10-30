namespace LayerPeek.App.Platform;

/// <summary>What the OS does with the app's notifications (<see cref="ISystemNotificationSettings.ReadStateAsync"/>).</summary>
internal enum SystemNotificationState
{
    /// <summary>The OS did not say (no reply, an error, a state the app does not know).</summary>
    Unknown,

    /// <summary>macOS: the app has not asked yet; the OS asks the user with the first request.</summary>
    NotAsked,

    /// <summary>The OS shows the app's notifications (which still does not guarantee a banner: Focus, Do not disturb).</summary>
    Allowed,

    /// <summary>The user turned the app's notifications off in the OS; only the user can turn them on there.</summary>
    TurnedOff,

    /// <summary>Windows: a policy of the organization turned them off; the user cannot change it.</summary>
    BlockedByPolicy,
}

/// <summary>How sending a test notification ended (<see cref="ISystemNotificationSettings.SendTestAsync"/>, T-101).</summary>
internal enum TestNotificationOutcome
{
    /// <summary>Handed to the OS (whether it shows a banner the app cannot know).</summary>
    Sent,

    /// <summary>Not sent: the user turned the app's notifications off in the OS.</summary>
    TurnedOff,

    /// <summary>Not sent: a policy of the organization turned them off (Windows).</summary>
    BlockedByPolicy,

    /// <summary>Not sent or refused by the OS (details in the log).</summary>
    Failed,
}

/// <summary>
/// The OS side of the app's notifications for the settings window: whether the OS shows them (T-020 macOS, T-101 Windows),
/// asking for permission and the test notification (T-101), and opening the place where the user turns them on again.
/// Where the OS has no notifications for this run (another OS, a development build without a bundle, the test mode without
/// notifications) the window gets none. Every method may be called on the UI thread: the OS calls run elsewhere.
/// </summary>
internal interface ISystemNotificationSettings
{
    /// <summary>
    /// What the OS does with the app's notifications now, without asking the user. Never throws (failures are logged and
    /// read as <see cref="SystemNotificationState.Unknown"/>, as is a cancelled read).
    /// </summary>
    Task<SystemNotificationState> ReadStateAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Asks for permission to show notifications (macOS: the question of the OS, shown only the first time; elsewhere
    /// nothing to ask). Never throws.
    /// </summary>
    /// <returns>Whether notifications are allowed now; <see langword="false"/> also after a failure or a cancellation.</returns>
    Task<bool> RequestPermissionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Shows the test notification (T-101) the way the app's notifications are shown, with the fixed identifier
    /// <see cref="Notifications.TestNotification.Identifier"/> (a new one replaces the previous one) and the launch argument
    /// <see cref="Notifications.ToastLaunchArgument.Settings"/>. Never throws (a cancellation reads as
    /// <see cref="TestNotificationOutcome.Failed"/>).
    /// </summary>
    /// <param name="playSound">With the default notification sound.</param>
    Task<TestNotificationOutcome> SendTestAsync(bool playSound, CancellationToken cancellationToken);

    /// <summary>
    /// Opens the OS settings of the app's notifications (macOS: System Settings &gt; Notifications &gt; the app; Windows:
    /// Settings &gt; System &gt; Notifications). UI thread. Never throws (a failure is only logged); the test mode opens nothing.
    /// </summary>
    void Open();
}

/// <summary>The OS notification settings of this run for the settings window; <see langword="null"/> where there are none.</summary>
internal sealed record SystemNotificationSettingsOption(ISystemNotificationSettings? Settings);
