using Microsoft.Extensions.Logging;

using LayerPeek.App.Notifications;

namespace LayerPeek.App.Platform;

/// <summary>
/// <see cref="ISystemNotificationSettings"/> on Windows (T-101, user decision 2026-10-29: the warning of T-020 on Windows
/// too): whether Windows shows the app's toasts (<c>ToastNotifier.Setting</c>, read on the thread of the
/// <see cref="ToastNotificationService"/>), the test toast through the same service and Settings &gt; System &gt;
/// Notifications (<see cref="SettingsAddress"/>). Windows never asks the user for permission. Opening goes through
/// <paramref name="open"/> (in the app <see cref="Windows.WindowsSystemSettings"/>; in the test mode and in tests a
/// stand-in, so Settings never opens there).
/// </summary>
/// <param name="open">Opens an address of Settings; may throw (logged).</param>
internal sealed partial class ToastNotificationSettings(ToastNotificationService toasts, Action<Uri> open, ILogger<ToastNotificationSettings> logger) : ISystemNotificationSettings
{
    /// <summary>Settings &gt; System &gt; Notifications, where the user switches the notifications of each app.</summary>
    public static readonly Uri SettingsAddress = new("ms-settings:notifications");

    private readonly ToastNotificationService _toasts = toasts ?? throw new ArgumentNullException(nameof(toasts));
    private readonly Action<Uri> _open = open ?? throw new ArgumentNullException(nameof(open));
    private readonly ILogger<ToastNotificationSettings> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public Task<SystemNotificationState> ReadStateAsync(CancellationToken cancellationToken) => _toasts.ReadStateAsync(cancellationToken);

    /// <summary>Windows has no question to ask: the app's toasts are shown unless the user or a policy turned them off.</summary>
    public Task<bool> RequestPermissionAsync(CancellationToken cancellationToken) => Task.FromResult(!cancellationToken.IsCancellationRequested);

    public Task<TestNotificationOutcome> SendTestAsync(bool playSound, CancellationToken cancellationToken) => _toasts.ShowTestAsync(playSound, cancellationToken);

    /// <summary>Starts Settings in the background (a shell execute can take a moment); never throws.</summary>
    public void Open() => _ = OpenAsync();

    /// <summary>What <see cref="Open"/> starts; completes once Settings was started or the failure was logged (tests).</summary>
    internal Task OpenAsync() => Task.Run(() =>
    {
        try
        {
            _open(SettingsAddress);
        }
        catch (Exception ex)
        {
            LogOpenFailed(ex);
        }
    });

    [LoggerMessage(Level = LogLevel.Warning, Message = "Opening Settings > System > Notifications failed.")]
    private partial void LogOpenFailed(Exception exception);
}
