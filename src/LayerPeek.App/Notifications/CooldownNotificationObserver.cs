using LayerPeek.App.Tray;
using LayerPeek.Core.Notifications;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Notifications;

/// <summary>
/// Hands the printers and notification settings of every monitoring run to the <see cref="CooldownNotifier"/> (T-080).
/// Registered before the <see cref="Cooldown.CooldownObserver"/>, so that a threshold raised by the same settings, which
/// may cool a printer down right away, is notified with the new settings.
/// </summary>
internal sealed class CooldownNotificationObserver : IMonitoringObserver
{
    private readonly CooldownNotifier _notifier;

    public CooldownNotificationObserver(CooldownNotifier notifier)
    {
        ArgumentNullException.ThrowIfNull(notifier);
        _notifier = notifier;
    }

    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _notifier.Configure(settings.Printers, settings.Notifications);
    }

    /// <summary>Same printers, new notification settings (e.g. only the switch changed).</summary>
    public void UpdateSettings(AppSettings settings) => ApplySettings(settings);

    /// <summary>The tray icon and menu already show the failure; no notification is sent for it.</summary>
    public void ReportMonitoringFailed()
    {
    }
}
