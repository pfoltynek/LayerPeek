namespace LayerPeek.App.Platform;

/// <summary>
/// The OS settings of the app's notifications, for the settings window (macOS, T-020, user decision 2026-10-29): whether the
/// user turned them off, and opening the place where they turn them on again. Where the OS has no such switch the window
/// gets none.
/// </summary>
internal interface ISystemNotificationSettings
{
    /// <summary>
    /// Whether the user turned the app's notifications off in the OS; <see langword="false"/> while the app has not asked
    /// yet or the OS does not say. Never throws (failures are logged); off the UI thread inside.
    /// </summary>
    Task<bool> AreTurnedOffAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Opens the OS settings of the app's notifications (macOS: System Settings &gt; Notifications &gt; the app). UI thread.
    /// Never throws (a failure is only logged); the test mode opens nothing.
    /// </summary>
    void Open();
}

/// <summary>The OS notification settings of this run for the settings window; <see langword="null"/> where there are none.</summary>
internal sealed record SystemNotificationSettingsOption(ISystemNotificationSettings? Settings);
