using System.Globalization;
using System.Threading.Channels;

using Microsoft.Extensions.Logging;

using LayerPeek.Core.Events;
using LayerPeek.Core.Notifications;

namespace LayerPeek.App.Notifications;

/// <summary>
/// <see cref="INotificationService"/> on top of an <see cref="IUserNotificationCenter"/> (macOS User Notifications, T-020),
/// with the behaviour of the Windows toasts (T-010): the same texts (<see cref="ToastContentBuilder.Lines"/>) in the language
/// active when the notification is built, the sound of <see cref="PrintNotification.PlaySound"/> (setting
/// <c>notifications.sound</c>), the launch argument of <see cref="ToastLaunchArgument"/> for the click.
/// <para>
/// <see cref="Show"/> only queues (bounded, never blocks); one background task hands the notifications to the OS one after
/// another. Permission is asked with the first notification, not at startup, so a run that never notifies (no printers, the
/// smoke test of a release) never asks; the OS shows its question only the first time and answers later requests at once,
/// so the permission is checked again before every notification and a change in System Settings applies to the next one.
/// Denied: the notification is dropped and the denial logged (once per change).
/// </para>
/// <para>
/// Failures never escape. After <see cref="Dispose"/> calls are ignored; <see cref="Completion"/> completes once nothing is
/// handed to the OS any more (the test cleanup waits for it).
/// </para>
/// </summary>
internal sealed partial class UserNotificationService : INotificationService, IDisposable
{
    /// <summary>Notifications waiting for the task; more are dropped (a burst after a long outage, the user not answering).</summary>
    public const int MaxPending = 32;

    private readonly IUserNotificationCenter _center;
    private readonly Func<CultureInfo> _textCulture;
    private readonly Func<string> _newIdentifier;
    private readonly ILogger<UserNotificationService> _logger;
    private readonly Channel<object> _pending = Channel.CreateBounded<object>(
        new BoundedChannelOptions(MaxPending) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Guarded by _gate.
    private bool _started;
    private bool _disposed;

    // Only the task touches it.
    private bool? _lastAllowed;

    /// <param name="textCulture">
    /// Language of the texts, called for every notification right before it is built (the app passes
    /// <see cref="Localization.UiLanguage.CurrentTextCulture"/>); may throw, the notification is then skipped and logged.
    /// </param>
    /// <param name="newIdentifier">Identifier of a notification; default: a new GUID.</param>
    public UserNotificationService(IUserNotificationCenter center, Func<CultureInfo> textCulture, ILogger<UserNotificationService> logger, Func<string>? newIdentifier = null)
    {
        ArgumentNullException.ThrowIfNull(center);
        ArgumentNullException.ThrowIfNull(textCulture);
        ArgumentNullException.ThrowIfNull(logger);
        _center = center;
        _textCulture = textCulture;
        _logger = logger;
        _newIdentifier = newIdentifier ?? (static () => Guid.NewGuid().ToString("N"));
    }

    /// <summary>The notification center (composition tests, the test cleanup).</summary>
    internal IUserNotificationCenter Center => _center;

    /// <summary>
    /// The image files of print failure notifications (T-154; always a copy, macOS moves an attached file);
    /// <see langword="null"/>: those notifications have no image. Used only by the task.
    /// </summary>
    internal AlarmNotificationImages? AlarmImages { get; init; }

    /// <summary>
    /// Completes once the task has ended for good (after <see cref="Dispose"/>; right then when it never started), so nothing
    /// is handed to the OS any more. Never faults.
    /// </summary>
    internal Task Completion => _completion.Task;

    public void Show(PrintNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_pending.Writer.TryWrite(notification))
            {
                if (!_started)
                {
                    _started = true;
                    _ = Task.Run(RunAsync, CancellationToken.None);
                }

                return;
            }
        }

        LogDropped(notification.Kind, notification.Printer.Id);
    }

    /// <summary>
    /// The notification about a new version (T-058), queued like a printer notification, with the fixed identifier
    /// <see cref="UpdateNotificationText.Identifier"/> (a newer one replaces it) and the launch argument
    /// <see cref="ToastLaunchArgument.Update"/>.
    /// </summary>
    public void ShowUpdateAvailable(UpdateNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        lock (_gate)
        {
            if (_disposed || !_pending.Writer.TryWrite(notification))
            {
                return;
            }

            if (!_started)
            {
                _started = true;
                _ = Task.Run(RunAsync, CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// The notification about a failed camera recording (T-146), queued like a printer notification, with the launch argument
    /// <see cref="ToastLaunchArgument.RecordingSettings"/> (a click opens the settings window on the Recording tab).
    /// </summary>
    public void ShowRecordingFailed(RecordingNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        lock (_gate)
        {
            if (_disposed || !_pending.Writer.TryWrite(notification))
            {
                return;
            }

            if (!_started)
            {
                _started = true;
                _ = Task.Run(RunAsync, CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// The notification of a suspected print failure (T-154), queued like a printer notification: the snapshot as an
    /// attachment, the button "False alarm" and the launch argument of the camera window.
    /// </summary>
    public void ShowPrintFailure(Core.Detection.PrintFailureNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        lock (_gate)
        {
            if (_disposed || !_pending.Writer.TryWrite(notification))
            {
                return;
            }

            if (!_started)
            {
                _started = true;
                _ = Task.Run(RunAsync, CancellationToken.None);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pending.Writer.TryComplete();
            if (!_started)
            {
                _completion.TrySetResult();
            }
        }

        // Ends a wait for the user's answer or for the OS. A task that only starts now finds the source disposed and ends.
        _stop.Cancel();
        _stop.Dispose();
    }

    private async Task RunAsync()
    {
        try
        {
            CancellationToken stop = _stop.Token;
            while (await _pending.Reader.WaitToReadAsync(stop).ConfigureAwait(false))
            {
                while (!stop.IsCancellationRequested && _pending.Reader.TryRead(out object? notification))
                {
                    if (notification is UpdateNotification update)
                    {
                        await ShowUpdateNowAsync(update, stop).ConfigureAwait(false);
                    }
                    else if (notification is RecordingNotification recording)
                    {
                        await ShowRecordingNowAsync(recording, stop).ConfigureAwait(false);
                    }
                    else if (notification is Core.Detection.PrintFailureNotification failure)
                    {
                        await ShowPrintFailureNowAsync(failure, stop).ConfigureAwait(false);
                    }
                    else
                    {
                        await ShowNowAsync((PrintNotification)notification, stop).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Disposed.
        }
        catch (Exception)
        {
            // Only a logger that itself fails (e.g. disposed during quit) gets here; losing notifications is the lesser evil.
        }
        finally
        {
            _completion.TrySetResult();
        }
    }

    private async Task ShowNowAsync(PrintNotification notification, CancellationToken stop)
    {
        if (!await IsAllowedAsync(stop).ConfigureAwait(false))
        {
            LogNotAllowed(notification.Kind, notification.Printer.Id);
            return;
        }

        try
        {
            string? launch = null;
            if (ToastLaunchArgument.TryForPrinter(notification.Printer.Id, out string? argument))
            {
                launch = argument;
            }
            else
            {
                LogNoClickTarget(notification.Kind, notification.Printer.Id.Length);
            }

            // The language is resolved once per notification, right now (as for the toasts).
            NotificationLines lines = ToastContentBuilder.Lines(notification, _textCulture());
            string body = lines.Detail is { } detail ? lines.Body + "\n" + detail : lines.Body;
            stop.ThrowIfCancellationRequested();
            _center.Post(new UserNotificationRequest(_newIdentifier(), lines.Title, body, notification.PlaySound, launch));
            LogShown(notification.Kind, notification.Printer.Id);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogShowFailed(ex, notification.Kind, notification.Printer.Id);
        }
    }

    private async Task ShowUpdateNowAsync(UpdateNotification notification, CancellationToken stop)
    {
        if (!await IsAllowedAsync(stop).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            CultureInfo culture = _textCulture();
            stop.ThrowIfCancellationRequested();
            _center.Post(new UserNotificationRequest(
                UpdateNotificationText.Identifier,
                UpdateNotificationText.Title(culture),
                UpdateNotificationText.Body(notification.Version, culture),
                notification.PlaySound,
                ToastLaunchArgument.Update));
            LogUpdateShown(notification.Version);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogUpdateShowFailed(ex, notification.Version);
        }
    }

    private async Task ShowRecordingNowAsync(RecordingNotification notification, CancellationToken stop)
    {
        if (!await IsAllowedAsync(stop).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            NotificationLines lines = ToastContentBuilder.RecordingLines(notification, _textCulture());
            string body = lines.Detail is { } detail ? lines.Body + "\n" + detail : lines.Body;
            stop.ThrowIfCancellationRequested();
            _center.Post(new UserNotificationRequest(_newIdentifier(), lines.Title, body, notification.PlaySound, ToastLaunchArgument.RecordingSettings));
            LogRecordingShown(notification.Problem, notification.PrinterId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogRecordingShowFailed(ex, notification.Problem, notification.PrinterId);
        }
    }

    private async Task ShowPrintFailureNowAsync(Core.Detection.PrintFailureNotification notification, CancellationToken stop)
    {
        Core.Detection.PrintFailureAlarm alarm = notification.Alarm;
        if (!await IsAllowedAsync(stop).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            CultureInfo culture = _textCulture();
            NotificationLines lines = ToastContentBuilder.PrintFailureLines(notification, culture);
            string body = lines.Detail is { } detail ? lines.Body + "\n" + detail : lines.Body;
            string? launch = ToastLaunchArgument.TryForCamera(alarm.PrinterId, out string? argument) ? argument : null;
            UserNotificationAction? action = ToastLaunchArgument.IsValidAlarmId(alarm.Id)
                ? new UserNotificationAction(PrintFailureNotificationText.FalseAlarmAction(culture), ToastLaunchArgument.ForFalseAlarm(alarm.Id))
                : null;
            stop.ThrowIfCancellationRequested();
            string? image = AlarmImages?.Prepare(alarm, useSavedSnapshot: false);
            _center.Post(new UserNotificationRequest(_newIdentifier(), lines.Title, body, notification.PlaySound, launch) { AttachmentPath = image, Action = action });
            LogPrintFailureShown(alarm.DetectorId, alarm.PrinterId, image is not null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogPrintFailureShowFailed(ex, alarm.DetectorId, alarm.PrinterId);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Notification shown for the suspected print failure ({DetectorId}) of printer '{PrinterId}' (image: {HasImage}).")]
    private partial void LogPrintFailureShown(string detectorId, string printerId, bool hasImage);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The notification for the suspected print failure ({DetectorId}) of printer '{PrinterId}' could not be shown.")]
    private partial void LogPrintFailureShowFailed(Exception exception, string detectorId, string printerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Notification shown for the recording failure ({Problem}) of printer '{PrinterId}'.")]
    private partial void LogRecordingShown(Core.Cameras.Recording.CameraRecordingProblem problem, string printerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The notification for the recording failure ({Problem}) of printer '{PrinterId}' could not be shown.")]
    private partial void LogRecordingShowFailed(Exception exception, Core.Cameras.Recording.CameraRecordingProblem problem, string printerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Notification shown for the new version {Version}.")]
    private partial void LogUpdateShown(string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The notification for the new version {Version} could not be shown.")]
    private partial void LogUpdateShowFailed(Exception exception, string version);

    /// <summary>Asks the OS (the user only the first time); a denial or a failure is logged when it changes.</summary>
    private async Task<bool> IsAllowedAsync(CancellationToken stop)
    {
        bool allowed;
        try
        {
            allowed = await _center.RequestAuthorizationAsync(stop).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (_lastAllowed is not false)
            {
                LogPermissionUnknown(ex);
            }

            _lastAllowed = false;
            return false;
        }

        if (allowed != _lastAllowed)
        {
            if (allowed)
            {
                LogAllowed(_center.BundleIdentifier);
            }
            else
            {
                LogDenied(_center.BundleIdentifier);
            }
        }

        _lastAllowed = allowed;
        return allowed;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Notifications of {BundleIdentifier} are allowed in macOS.")]
    private partial void LogAllowed(string bundleIdentifier);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notifications of {BundleIdentifier} are turned off in macOS (System Settings > Notifications); notifications will not be shown until they are allowed there.")]
    private partial void LogDenied(string bundleIdentifier);

    [LoggerMessage(Level = LogLevel.Error, Message = "macOS did not say whether notifications are allowed; the notification is not shown.")]
    private partial void LogPermissionUnknown(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The {Kind} notification for printer '{PrinterId}' was not shown: notifications are not allowed.")]
    private partial void LogNotAllowed(PrintEventKind kind, string printerId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The {Kind} notification for printer '{PrinterId}' was handed to macOS.")]
    private partial void LogShown(PrintEventKind kind, string printerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Showing the {Kind} notification for printer '{PrinterId}' failed.")]
    private partial void LogShowFailed(Exception exception, PrintEventKind kind, string printerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Too many notifications are waiting; dropped the {Kind} notification for printer '{PrinterId}'.")]
    private partial void LogDropped(PrintEventKind kind, string printerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The id of the printer is too long ({Length} characters) for the notification; the {Kind} notification is shown, but a click on it opens nothing.")]
    private partial void LogNoClickTarget(PrintEventKind kind, int length);
}
