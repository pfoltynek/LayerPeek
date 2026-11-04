using Microsoft.Extensions.Logging;

using LayerPeek.App.Tray;
using LayerPeek.Core.Cameras.Timelapse;
using LayerPeek.Core.Notifications;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Notifications;

/// <summary>
/// Turns <see cref="CameraTimelapseJpegExporter.Completed"/> into a short notification (T-165, user decision 2026-11-04: at
/// the end, done and failed; a cancel is the user's own act and is not notified), with the printer name of the current run
/// as the title. Follows "Show notifications"; it is information, so it sounds only with the "all notifications" sound
/// setting. Results come on any thread; the notification service queues them. Never throws into the exporter.
/// </summary>
internal sealed partial class TimelapseExportNotificationObserver : IMonitoringObserver, IDisposable
{
    private readonly CameraTimelapseJpegExporter _exporter;
    private readonly INotificationService _notifications;
    private readonly ILogger<TimelapseExportNotificationObserver> _logger;
    private readonly Lock _gate = new();

    // Guarded by _gate: what the current run knows.
    private Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);
    private NotificationSettings _settings = new();
    private bool _disposed;

    public TimelapseExportNotificationObserver(CameraTimelapseJpegExporter exporter, INotificationService notifications, ILogger<TimelapseExportNotificationObserver> logger)
    {
        ArgumentNullException.ThrowIfNull(exporter);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(logger);
        _exporter = exporter;
        _notifications = notifications;
        _logger = logger;
        _exporter.Completed += OnCompleted;
    }

    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Dictionary<string, string> names = new(settings.Printers.Count, StringComparer.OrdinalIgnoreCase);
        foreach (PrinterDefinition printer in settings.Printers)
        {
            names.TryAdd(printer.Id, printer.Name);
        }

        lock (_gate)
        {
            _names = names;
            _settings = settings.Notifications;
        }
    }

    public void UpdateSettings(AppSettings settings) => ApplySettings(settings);

    /// <summary>Exports do not depend on monitoring; nothing is notified for its failure.</summary>
    public void ReportMonitoringFailed()
    {
    }

    public void Dispose()
    {
        _exporter.Completed -= OnCompleted;
        lock (_gate)
        {
            _disposed = true;
        }
    }

    /// <summary>Any thread: an export ended.</summary>
    internal void OnCompleted(object? sender, CameraTimelapseExportResult result)
    {
        if (result is null || result.Problem == CameraTimelapseExportProblem.Cancelled)
        {
            return;
        }

        TimelapseExportNotification notification;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (!_settings.Enabled)
            {
                LogNotNotified(result.Problem);
                return;
            }

            string? name = result.PrinterId is { } id && _names.TryGetValue(id, out string? known) ? known : null;
            notification = new TimelapseExportNotification(result.PrinterId, name, result.Frames, result.Problem, _settings.ShouldPlaySound(NotificationSeverity.Information));
        }

        try
        {
            _notifications.ShowTimelapseExportFinished(notification);
        }
        catch (Exception ex)
        {
            LogShowFailed(ex, result.Problem);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "The end of a JPEG export ({Problem}) is not notified: notifications are off.")]
    private partial void LogNotNotified(CameraTimelapseExportProblem problem);

    [LoggerMessage(Level = LogLevel.Error, Message = "Showing the end of a JPEG export ({Problem}) failed.")]
    private partial void LogShowFailed(Exception exception, CameraTimelapseExportProblem problem);
}
