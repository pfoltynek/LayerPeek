using Microsoft.Extensions.Logging;

using LayerPeek.App.Tray;
using LayerPeek.Core.Cameras.Timelapse;
using LayerPeek.Core.Notifications;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Notifications;

/// <summary>
/// Turns <see cref="CameraTimelapseJpegExporter.Completed"/> (T-165) and <see cref="CameraTimelapseFromRecordingMaker.Completed"/>
/// (T-164) into a short notification (user decision 2026-11-04: at
/// the end, done and failed; a cancel is the user's own act and is not notified), with the printer name of the current run
/// as the title. Follows "Show notifications"; it is information, so it sounds only with the "all notifications" sound
/// setting. Results come on any thread; the notification service queues them. Never throws into the exporter or the maker.
/// </summary>
internal sealed partial class TimelapseExportNotificationObserver : IMonitoringObserver, IDisposable
{
    private readonly CameraTimelapseJpegExporter _exporter;
    private readonly CameraTimelapseFromRecordingMaker? _maker;
    private readonly INotificationService _notifications;
    private readonly ILogger<TimelapseExportNotificationObserver> _logger;
    private readonly Lock _gate = new();

    // Guarded by _gate: what the current run knows.
    private Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);
    private NotificationSettings _settings = new();
    private bool _disposed;

    /// <param name="maker">The timelapse from a recording (T-164); <see langword="null"/>: only the JPEG export is notified.</param>
    public TimelapseExportNotificationObserver(
        CameraTimelapseJpegExporter exporter,
        INotificationService notifications,
        ILogger<TimelapseExportNotificationObserver> logger,
        CameraTimelapseFromRecordingMaker? maker = null)
    {
        ArgumentNullException.ThrowIfNull(exporter);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(logger);
        _exporter = exporter;
        _notifications = notifications;
        _logger = logger;
        _maker = maker;
        _exporter.Completed += OnCompleted;
        if (_maker is not null)
        {
            _maker.Completed += OnMade;
        }
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
        if (_maker is not null)
        {
            _maker.Completed -= OnMade;
        }

        lock (_gate)
        {
            _disposed = true;
        }
    }

    /// <summary>Any thread: an export ended.</summary>
    internal void OnCompleted(object? sender, CameraTimelapseExportResult result)
    {
        if (result is not null)
        {
            Notify(TimelapseExportKind.Jpeg, result.PrinterId, result.Frames, result.Problem);
        }
    }

    /// <summary>Any thread: a timelapse from a recording ended (T-164).</summary>
    internal void OnMade(object? sender, CameraTimelapseFromRecordingResult result)
    {
        if (result is not null)
        {
            Notify(TimelapseExportKind.FromRecording, result.PrinterId, result.Frames, result.Problem);
        }
    }

    private void Notify(TimelapseExportKind kind, string? printerId, int frames, CameraTimelapseExportProblem problem)
    {
        if (problem == CameraTimelapseExportProblem.Cancelled)
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
                LogNotNotified(kind, problem);
                return;
            }

            string? name = printerId is { } id && _names.TryGetValue(id, out string? known) ? known : null;
            notification = new TimelapseExportNotification(printerId, name, frames, problem, _settings.ShouldPlaySound(NotificationSeverity.Information), kind);
        }

        try
        {
            _notifications.ShowTimelapseExportFinished(notification);
        }
        catch (Exception ex)
        {
            LogShowFailed(ex, kind, problem);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "The end of a timelapse job ({Kind}, {Problem}) is not notified: notifications are off.")]
    private partial void LogNotNotified(TimelapseExportKind kind, CameraTimelapseExportProblem problem);

    [LoggerMessage(Level = LogLevel.Error, Message = "Showing the end of a timelapse job ({Kind}, {Problem}) failed.")]
    private partial void LogShowFailed(Exception exception, TimelapseExportKind kind, CameraTimelapseExportProblem problem);
}
