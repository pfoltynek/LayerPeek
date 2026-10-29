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
    private readonly Channel<PrintNotification> _pending = Channel.CreateBounded<PrintNotification>(
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
                while (!stop.IsCancellationRequested && _pending.Reader.TryRead(out PrintNotification? notification))
                {
                    await ShowNowAsync(notification, stop).ConfigureAwait(false);
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
