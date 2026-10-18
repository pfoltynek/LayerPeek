using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.Core.Notifications;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.App.Notifications;

/// <summary>Hands the printers and notification settings of every monitoring run to the <see cref="PrintEventNotifier"/>.</summary>
internal sealed class PrintNotificationObserver : IMonitoringObserver
{
    private readonly PrintEventNotifier _notifier;

    public PrintNotificationObserver(PrintEventNotifier notifier)
    {
        ArgumentNullException.ThrowIfNull(notifier);
        _notifier = notifier;
    }

    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _notifier.Configure(settings.Printers, settings.Notifications);
    }

    /// <summary>Same printers, new notification settings; the notifier keeps what it knows about the printers.</summary>
    public void UpdateSettings(AppSettings settings) => ApplySettings(settings);

    /// <summary>The tray icon and menu already show the failure; no notification is sent for it.</summary>
    public void ReportMonitoringFailed()
    {
    }
}
