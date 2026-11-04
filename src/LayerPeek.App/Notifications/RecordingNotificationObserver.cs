using Microsoft.Extensions.Logging;

using LayerPeek.App.Tray;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Notifications;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Notifications;

/// <summary>
/// Turns <see cref="CameraRecordingService.Failed"/> into a notification (T-146; the only notified recording event, user
/// decision 2026-11-03 on T-145): the printer name of the current run as the title, the localized reason built by the
/// notification service from <see cref="CameraRecordingProblem"/> (the English message of the service only goes to the
/// log). Follows "Show notifications" and the sound setting (a failure counts like a printer error). Failures come on any
/// thread; the notification service queues them. Never throws into the service. T-161: a failed timelapse
/// (<see cref="CameraRecordingError.IsTimelapse"/>) has texts of its own.
/// </summary>
internal sealed partial class RecordingNotificationObserver : IMonitoringObserver, IDisposable
{
    private readonly CameraRecordingService _recordings;
    private readonly INotificationService _notifications;
    private readonly int _minFreeGigabytes;
    private readonly ILogger<RecordingNotificationObserver> _logger;
    private readonly Lock _gate = new();

    // Guarded by _gate: what the current run knows.
    private Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);
    private NotificationSettings _settings = new();
    private int _sizeLimitGigabytes = CameraRecordingSettings.DefaultSizeLimitGigabytes;
    private bool _disposed;

    /// <param name="minFreeBytes">The free space the service needs (<see cref="CameraRecordingServiceOptions.MinFreeBytes"/>), told by "disk space low".</param>
    public RecordingNotificationObserver(CameraRecordingService recordings, INotificationService notifications, long minFreeBytes, ILogger<RecordingNotificationObserver> logger)
    {
        ArgumentNullException.ThrowIfNull(recordings);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(logger);
        _recordings = recordings;
        _notifications = notifications;
        _logger = logger;
        // Whole gigabytes (10^9 bytes, like the size limit), rounded up so that the text never promises less than is needed.
        _minFreeGigabytes = (int)Math.Clamp((Math.Max(minFreeBytes, 0) + 999_999_999) / 1_000_000_000, 0, int.MaxValue);
        _recordings.Failed += OnFailed;
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
            _sizeLimitGigabytes = settings.Recording.SizeLimitGigabytes;
        }
    }

    /// <summary>Same printers, new notification settings (or a new name of the same printer).</summary>
    public void UpdateSettings(AppSettings settings) => ApplySettings(settings);

    /// <summary>Recordings do not depend on monitoring; nothing is notified for its failure.</summary>
    public void ReportMonitoringFailed()
    {
    }

    public void Dispose()
    {
        _recordings.Failed -= OnFailed;
        lock (_gate)
        {
            _disposed = true;
        }
    }

    /// <summary>Any thread: a recording could not start or ended with an error.</summary>
    internal void OnFailed(object? sender, CameraRecordingError error)
    {
        if (error is null)
        {
            return;
        }

        RecordingNotification notification;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (!_settings.Enabled)
            {
                LogNotNotified(error.PrinterId, error.Problem);
                return;
            }

            string name = _names.TryGetValue(error.PrinterId, out string? known) && !string.IsNullOrWhiteSpace(known) ? known : error.PrinterId;
            notification = new RecordingNotification(
                error.PrinterId,
                name,
                error.Problem,
                _sizeLimitGigabytes,
                _minFreeGigabytes,
                _settings.ShouldPlaySound(NotificationSeverity.ActionRequired),
                IsDashcam: error.Mode == CameraRecordingMode.Dashcam && !error.IsTimelapse,
                IsTimelapse: error.IsTimelapse);
        }

        try
        {
            _notifications.ShowRecordingFailed(notification);
        }
        catch (Exception ex)
        {
            LogShowFailed(ex, error.PrinterId, error.Problem);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "The recording failure ({Problem}) of printer '{PrinterId}' is not notified: notifications are off.")]
    private partial void LogNotNotified(string printerId, CameraRecordingProblem problem);

    [LoggerMessage(Level = LogLevel.Error, Message = "Showing the recording failure ({Problem}) of printer '{PrinterId}' failed.")]
    private partial void LogShowFailed(Exception exception, string printerId, CameraRecordingProblem problem);
}
