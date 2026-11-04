using Microsoft.Extensions.Logging;

using LayerPeek.Core.Detection;

namespace LayerPeek.App.Detection;

/// <summary>The "False alarm" button of a print failure notification (T-154), as the notification router sees it.</summary>
internal interface IFalseAlarmReporter
{
    /// <summary>
    /// Keeps a local record of alarm <paramref name="alarmId"/> (an id of the button's argument, already checked by
    /// <see cref="Notifications.ToastLaunchArgument.TryGetFalseAlarmId"/>). Never throws for runtime conditions.
    /// </summary>
    /// <returns><see langword="true"/> when the record was saved.</returns>
    Task<bool> ReportAsync(string alarmId, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IFalseAlarmReporter"/> over the <see cref="PrintFailureDetectionService"/> (T-153): one of its recent alarms by
/// id, recorded locally with its values and snapshot (user decision 2026-11-04: nothing is sent, the sensitivity stays). An
/// alarm this run of the app does not know (an old notification after a restart, more than
/// <see cref="PrintFailureDetectionService.RecentAlarmCount"/> alarms since) is logged and ignored. The service is
/// resolved at the first click: it depends on the notifications, whose clicks come here.
/// </summary>
internal sealed partial class FalseAlarmReporter : IFalseAlarmReporter
{
    private readonly Func<string, PrintFailureAlarm?> _find;
    private readonly Func<PrintFailureAlarm, CancellationToken, Task<string?>> _record;
    private readonly ILogger<FalseAlarmReporter> _logger;

    public FalseAlarmReporter(Func<PrintFailureDetectionService> detection, ILogger<FalseAlarmReporter> logger)
        : this(
            id => detection().TryGetAlarm(id, out PrintFailureAlarm? alarm) ? alarm : null,
            (alarm, token) => detection().ReportFalseAlarmAsync(alarm, token),
            logger)
    {
        ArgumentNullException.ThrowIfNull(detection);
    }

    /// <param name="find">One of the recent alarms by id, or <see langword="null"/> (tests replace the service).</param>
    /// <param name="record">Saves the local record; the path, or <see langword="null"/> when it could not be saved.</param>
    internal FalseAlarmReporter(Func<string, PrintFailureAlarm?> find, Func<PrintFailureAlarm, CancellationToken, Task<string?>> record, ILogger<FalseAlarmReporter> logger)
    {
        ArgumentNullException.ThrowIfNull(find);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(logger);
        _find = find;
        _record = record;
        _logger = logger;
    }

    public async Task<bool> ReportAsync(string alarmId, CancellationToken cancellationToken)
    {
        // TryGetAlarm throws for null (T-153 review): untrusted input is checked here as well.
        if (!Notifications.ToastLaunchArgument.IsValidAlarmId(alarmId))
        {
            LogInvalid();
            return false;
        }

        PrintFailureAlarm? alarm;
        try
        {
            alarm = _find(alarmId);
        }
        catch (ObjectDisposedException)
        {
            // Quitting: the services are gone.
            return false;
        }

        if (alarm is null)
        {
            LogUnknown(alarmId);
            return false;
        }

        string? path = await _record(alarm, cancellationToken).ConfigureAwait(false);
        if (path is null)
        {
            LogNotSaved(alarm.Id, alarm.PrinterId);
            return false;
        }

        LogSaved(alarm.Id, alarm.PrinterId, alarm.DetectorId);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignored a \"False alarm\" click with an invalid alarm id.")]
    private partial void LogInvalid();

    [LoggerMessage(Level = LogLevel.Information, Message = "\"False alarm\" for alarm {AlarmId}, which this run of the app no longer knows; nothing recorded.")]
    private partial void LogUnknown(string alarmId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The false alarm {AlarmId} of printer '{PrinterId}' could not be recorded.")]
    private partial void LogNotSaved(string alarmId, string printerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recorded alarm {AlarmId} ({DetectorId}) of printer '{PrinterId}' as a false alarm.")]
    private partial void LogSaved(string alarmId, string printerId, string detectorId);
}
