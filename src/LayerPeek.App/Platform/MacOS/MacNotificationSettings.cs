using System.Globalization;

using Microsoft.Extensions.Logging;

using LayerPeek.App.Notifications;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// <see cref="ISystemNotificationSettings"/> on macOS (T-020, T-101): the permission of the bundle in the notification
/// center, the question of macOS, the test notification and System Settings &gt; Notifications &gt; the app
/// (<see cref="SettingsAddress"/>). Opening goes through <paramref name="open"/> (in the app <see cref="MacSystemSettings"/>;
/// in the test mode and in tests a stand-in, so System Settings never opens there). The center is that of this run: the
/// test mode has only the one of the test bundle (<c>…LayerPeek.E2E</c>), whose notifications its cleanup removes.
/// </summary>
/// <param name="open">Opens an address of System Settings; may throw (logged).</param>
/// <param name="textCulture">The language of the test notification (the app passes <c>UiLanguage.CurrentTextCulture</c>).</param>
internal sealed partial class MacNotificationSettings(IUserNotificationCenter center, Action<Uri> open, Func<CultureInfo> textCulture, ILogger<MacNotificationSettings> logger) : ISystemNotificationSettings
{
    private readonly IUserNotificationCenter _center = center ?? throw new ArgumentNullException(nameof(center));
    private readonly Action<Uri> _open = open ?? throw new ArgumentNullException(nameof(open));
    private readonly Func<CultureInfo> _textCulture = textCulture ?? throw new ArgumentNullException(nameof(textCulture));
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

    public async Task<SystemNotificationState> ReadStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            NotificationPermission permission = await Task.Run(() => _center.GetPermissionAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
            if (permission == NotificationPermission.Denied)
            {
                LogTurnedOff(_center.BundleIdentifier);
            }

            return StateOf(permission);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return SystemNotificationState.Unknown;
        }
        catch (Exception ex)
        {
            LogUnreadable(ex);
            return SystemNotificationState.Unknown;
        }
    }

    /// <summary>
    /// <c>requestAuthorization</c> (T-101): macOS shows its question only while the permission is not determined; later it
    /// answers at once with the user's choice (Apple: "Subsequent calls to this method don't prompt the person again"). The
    /// answer may take as long as the user needs (at most the timeout of the center, 10 minutes).
    /// </summary>
    public async Task<bool> RequestPermissionAsync(CancellationToken cancellationToken)
    {
        try
        {
            bool granted = await Task.Run(() => _center.RequestAuthorizationAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
            LogAnswered(_center.BundleIdentifier, granted);
            return granted;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            LogRequestFailed(ex);
            return false;
        }
    }

    /// <summary>
    /// The test notification (T-101) when macOS allows notifications: the fixed identifier replaces the previous one in the
    /// notification center (a request with the identifier of an existing one replaces it). Never asks the user.
    /// </summary>
    public async Task<TestNotificationOutcome> SendTestAsync(bool playSound, CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(
                async () =>
                {
                    NotificationPermission permission = await _center.GetPermissionAsync(cancellationToken).ConfigureAwait(false);
                    switch (permission)
                    {
                        case NotificationPermission.Denied:
                            return TestNotificationOutcome.TurnedOff;
                        case NotificationPermission.Allowed:
                            break;
                        default:
                            // Not asked yet (the window asks first) or macOS did not say.
                            LogTestNotAllowed(permission);
                            return TestNotificationOutcome.Failed;
                    }

                    CultureInfo culture = _textCulture();
                    cancellationToken.ThrowIfCancellationRequested();
                    _center.Post(new UserNotificationRequest(
                        TestNotification.Identifier,
                        TestNotification.Title(culture),
                        TestNotification.Body(culture),
                        playSound,
                        ToastLaunchArgument.Settings));
                    LogTestPosted(_center.BundleIdentifier, playSound);
                    return TestNotificationOutcome.Sent;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return TestNotificationOutcome.Failed;
        }
        catch (Exception ex)
        {
            LogTestFailed(ex);
            return TestNotificationOutcome.Failed;
        }
    }

    /// <summary>The permission of the notification center as the state of the settings window.</summary>
    internal static SystemNotificationState StateOf(NotificationPermission permission) => permission switch
    {
        NotificationPermission.NotDetermined => SystemNotificationState.NotAsked,
        NotificationPermission.Denied => SystemNotificationState.TurnedOff,
        NotificationPermission.Allowed => SystemNotificationState.Allowed,
        _ => SystemNotificationState.Unknown,
    };

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

    [LoggerMessage(Level = LogLevel.Information, Message = "macOS answered the request for notifications of {BundleIdentifier}: allowed {Granted}.")]
    private partial void LogAnswered(string bundleIdentifier, bool granted);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Asking macOS for permission to show notifications failed.")]
    private partial void LogRequestFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "The test notification was handed to macOS for {BundleIdentifier} (sound: {PlaySound}).")]
    private partial void LogTestPosted(string bundleIdentifier, bool playSound);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The test notification was not sent: the permission of macOS is {Permission}.")]
    private partial void LogTestNotAllowed(NotificationPermission permission);

    [LoggerMessage(Level = LogLevel.Error, Message = "Sending the test notification failed.")]
    private partial void LogTestFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Opening System Settings > Notifications failed.")]
    private partial void LogOpenFailed(Exception exception);
}
