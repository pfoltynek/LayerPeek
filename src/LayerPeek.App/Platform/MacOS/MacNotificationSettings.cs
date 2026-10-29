using Microsoft.Extensions.Logging;

using LayerPeek.App.Notifications;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// <see cref="ISystemNotificationSettings"/> on macOS (T-020): the permission of the bundle in the notification center and
/// System Settings &gt; Notifications &gt; the app (<see cref="SettingsAddress"/>). Opening goes through
/// <paramref name="open"/> (in the app <see cref="MacSystemSettings"/>; in the test mode and in tests a stand-in, so
/// System Settings never opens there).
/// </summary>
/// <param name="open">Opens an address of System Settings; may throw (logged).</param>
internal sealed partial class MacNotificationSettings(IUserNotificationCenter center, Action<Uri> open, ILogger<MacNotificationSettings> logger) : ISystemNotificationSettings
{
    private readonly IUserNotificationCenter _center = center ?? throw new ArgumentNullException(nameof(center));
    private readonly Action<Uri> _open = open ?? throw new ArgumentNullException(nameof(open));
    private readonly ILogger<MacNotificationSettings> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// The notification settings of <paramref name="bundleIdentifier"/> in System Settings (macOS 13 and later; the app needs
    /// macOS 14): <c>x-apple.systempreferences:com.apple.Notifications-Settings.extension?id=&lt;bundle id&gt;</c>.
    /// </summary>
    public static Uri SettingsAddress(string bundleIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleIdentifier);
        return new Uri("x-apple.systempreferences:com.apple.Notifications-Settings.extension?id=" + Uri.EscapeDataString(bundleIdentifier));
    }

    public async Task<bool> AreTurnedOffAsync(CancellationToken cancellationToken)
    {
        try
        {
            NotificationPermission permission = await Task.Run(() => _center.GetPermissionAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
            if (permission == NotificationPermission.Denied)
            {
                LogTurnedOff(_center.BundleIdentifier);
                return true;
            }

            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            LogUnreadable(ex);
            return false;
        }
    }

    public void Open()
    {
        try
        {
            _open(SettingsAddress(_center.BundleIdentifier));
        }
        catch (Exception ex)
        {
            LogOpenFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notifications of {BundleIdentifier} are turned off in macOS; the settings window offers System Settings > Notifications.")]
    private partial void LogTurnedOff(string bundleIdentifier);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading whether macOS allows the notifications failed.")]
    private partial void LogUnreadable(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Opening System Settings > Notifications failed.")]
    private partial void LogOpenFailed(Exception exception);
}
