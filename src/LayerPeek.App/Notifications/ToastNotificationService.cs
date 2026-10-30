using System.Globalization;

using Microsoft.Extensions.Logging;

using LayerPeek.App.Platform;
using LayerPeek.App.Platform.Windows;
using LayerPeek.Core.Events;
using LayerPeek.Core.Notifications;

namespace LayerPeek.App.Notifications;

/// <summary>
/// <see cref="INotificationService"/> on top of an <see cref="IToastPlatform"/> (Windows toasts, T-010).
/// <para>
/// <see cref="Show"/> only queues the notification (bounded, never blocks): one background thread opens the platform
/// lazily (first <see cref="Start"/> or <see cref="Show"/>), builds the toast XML and shows the toasts one after another,
/// so slow or failing OS calls never reach the monitor threads.
/// </para>
/// <para>
/// The language of the texts is read for every toast right before it is built, so a language switch (T-029) applies to
/// the next toast without a restart.
/// </para>
/// <para>
/// The settings window (T-101) asks the same thread whether Windows shows the app's toasts (<see cref="ReadStateAsync"/>)
/// and has it show the test toast (<see cref="ShowTestAsync"/>, tagged, so a new one replaces the previous one); such a
/// request goes before waiting notifications and starts the thread like a notification.
/// </para>
/// <para>
/// Failures never escape: a platform that cannot be opened is logged once and turns the service into a no-op, a
/// failing toast is logged and the next one is still shown. After <see cref="Dispose"/> calls are ignored; disposing
/// does not wait for a toast in progress, the thread releases the platform on its own afterwards. A request of the settings
/// window that can no longer be served ends as unknown / failed.
/// </para>
/// </summary>
internal sealed partial class ToastNotificationService : INotificationService, IDisposable
{
    /// <summary>Notifications waiting for the thread; more are dropped (a burst after a long outage, a hung OS call).</summary>
    public const int MaxPending = 32;

    /// <summary>Requests of settings windows waiting for the thread; more end as failed at once (one window, one click at a time).</summary>
    public const int MaxPendingRequests = 8;

    private readonly IToastPlatform _platform;
    private readonly Action<string?> _activated;
    private readonly Func<CultureInfo> _textCulture;
    private readonly ILogger<ToastNotificationService> _logger;

    // Monitor.Wait/Pulse need a plain object.
    private readonly object _gate = new();

    // Completed when the thread has ended (or, once disposed, when it never started).
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Guarded by _gate.
    // PrintNotification or UpdateNotification (T-058), in the order they came.
    private readonly Queue<object> _pending = new();
    private readonly Queue<SettingsRequest> _requests = new();
    private bool _started;
    private bool _unavailable;

    // Written under _gate, read without it by click callbacks.
    private volatile bool _disposed;

    /// <param name="activated">Receives the launch argument of a clicked toast (an OS thread); failures are logged.</param>
    /// <param name="textCulture">
    /// Language of the toast texts, called on the toast thread for every toast right before it is built (the app passes
    /// <see cref="Localization.UiLanguage.CurrentTextCulture"/>); may throw, the toast is then skipped and logged.
    /// </param>
    public ToastNotificationService(IToastPlatform platform, Action<string?> activated, Func<CultureInfo> textCulture, ILogger<ToastNotificationService> logger)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(activated);
        ArgumentNullException.ThrowIfNull(textCulture);
        ArgumentNullException.ThrowIfNull(logger);
        _platform = platform;
        _activated = activated;
        _textCulture = textCulture;
        _logger = logger;
    }

    /// <summary>The OS side (composition tests).</summary>
    internal IToastPlatform Platform => _platform;

    /// <summary>
    /// Completes once the thread has ended for good (disposed, or the platform turned out unavailable; right at
    /// <see cref="Dispose"/> when it never started), so nothing touches the OS (e.g. writes the Start menu shortcut while
    /// registering) any more. Never faults. The test mode waits for it before removing what its
    /// toasts left behind (T-067).
    /// </summary>
    internal Task Completion => _completion.Task;

    /// <summary>
    /// Opens the platform in the background now (on Windows: registers the app identity), so that the first toast is
    /// not delayed and the OS has time to pick up a newly created Start menu shortcut. Idempotent, never blocks.
    /// </summary>
    public void Start()
    {
        lock (_gate)
        {
            EnsureStarted();
        }
    }

    public void Show(PrintNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        lock (_gate)
        {
            if (_disposed || _unavailable)
            {
                return;
            }

            if (_pending.Count < MaxPending)
            {
                _pending.Enqueue(notification);
                EnsureStarted();
                Monitor.Pulse(_gate);
                return;
            }
        }

        LogDropped(notification.Kind, notification.Printer.Id);
    }

    /// <summary>The toast about a new version (T-058), queued like a printer toast; tagged, so a newer one replaces it.</summary>
    public void ShowUpdateAvailable(UpdateNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        lock (_gate)
        {
            if (_disposed || _unavailable || _pending.Count >= MaxPending)
            {
                return;
            }

            _pending.Enqueue(notification);
            EnsureStarted();
            Monitor.Pulse(_gate);
        }
    }

    /// <summary>
    /// Whether Windows shows the app's toasts now (<c>ToastNotifier.Setting</c>, T-101), read on the toast thread. Never
    /// throws: unavailable toasts, a failed read and a cancellation are <see cref="SystemNotificationState.Unknown"/>.
    /// </summary>
    public Task<SystemNotificationState> ReadStateAsync(CancellationToken cancellationToken) =>
        RequestAsync(new SettingsRequest(PlaySound: null), static result => result.State, SystemNotificationState.Unknown, cancellationToken);

    /// <summary>
    /// Shows the test toast (T-101) on the toast thread, in the language of <see cref="Localization.UiLanguage"/> at that
    /// moment, when Windows shows the app's toasts; otherwise says why not. Never throws: unavailable toasts, a failure and
    /// a cancellation are <see cref="TestNotificationOutcome.Failed"/>.
    /// </summary>
    public Task<TestNotificationOutcome> ShowTestAsync(bool playSound, CancellationToken cancellationToken) =>
        RequestAsync(new SettingsRequest(playSound), static result => result.Outcome, TestNotificationOutcome.Failed, cancellationToken);

    public void Dispose()
    {
        SettingsRequest[] abandoned;
        lock (_gate)
        {
            _disposed = true;
            _pending.Clear();
            abandoned = [.. _requests];
            _requests.Clear();
            Monitor.PulseAll(_gate);
            if (!_started)
            {
                // No thread will ever run (EnsureStarted refuses from now on).
                _completion.TrySetResult();
            }
        }

        Abandon(abandoned);
    }

    private async Task<T> RequestAsync<T>(SettingsRequest request, Func<SettingsResult, T> select, T fallback, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_disposed || _unavailable || _requests.Count >= MaxPendingRequests)
            {
                return fallback;
            }

            _requests.Enqueue(request);
            EnsureStarted();
            Monitor.Pulse(_gate);
        }

        try
        {
            // The thread completes the request; a cancelled wait leaves it to finish there (nothing waits for it).
            return select(await request.Result.Task.WaitAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            return fallback;
        }
    }

    private static void Abandon(IEnumerable<SettingsRequest> requests)
    {
        foreach (SettingsRequest request in requests)
        {
            request.Result.TrySetResult(SettingsResult.Abandoned);
        }
    }

    // Caller holds _gate.
    private void EnsureStarted()
    {
        if (_started || _disposed || _unavailable)
        {
            return;
        }

        _started = true;
        // Background: never keeps the process alive. A dedicated thread, not the pool: WinRT objects live in its apartment.
        Thread thread = new(Run) { IsBackground = true, Name = "Toast notifications" };
        thread.Start();
    }

    private void Run()
    {
        try
        {
            IToastSession? session = Open();
            if (session is null)
            {
                return;
            }

            try
            {
                while (TryTake(out object? notification, out SettingsRequest? request))
                {
                    if (request is not null)
                    {
                        Serve(session, request);
                    }
                    else
                    {
                        if (notification is UpdateNotification update)
                        {
                            ShowUpdate(session, update);
                        }
                        else
                        {
                            ShowNow(session, (PrintNotification)notification!);
                        }
                    }
                }
            }
            finally
            {
                Release(session);
            }
        }
        catch (Exception)
        {
            // Only a logger that itself fails (e.g. disposed during quit) gets here. An exception escaping a thread
            // would end the process; losing notifications is the lesser evil.
        }
        finally
        {
            SettingsRequest[] left;
            lock (_gate)
            {
                left = [.. _requests];
                _requests.Clear();
            }

            Abandon(left);
            _completion.TrySetResult();
        }
    }

    private IToastSession? Open()
    {
        try
        {
            return _platform.Open(OnActivated);
        }
        catch (Exception ex)
        {
            SettingsRequest[] abandoned;
            lock (_gate)
            {
                _unavailable = true;
                _pending.Clear();
                abandoned = [.. _requests];
                _requests.Clear();
            }

            Abandon(abandoned);

            LogUnavailable(ex);
            return null;
        }
    }

    /// <summary>The next request of a settings window, otherwise the next notification; waits for one.</summary>
    /// <returns><see langword="false"/> once disposed.</returns>
    private bool TryTake(out object? notification, out SettingsRequest? request)
    {
        lock (_gate)
        {
            while (true)
            {
                notification = null;
                request = null;
                if (_disposed)
                {
                    return false;
                }

                if (_requests.TryDequeue(out request) || _pending.TryDequeue(out notification))
                {
                    return true;
                }

                Monitor.Wait(_gate);
            }
        }
    }

    /// <summary>A request of a settings window (T-101): the setting of Windows, and the test toast when it is allowed.</summary>
    private void Serve(IToastSession session, SettingsRequest request)
    {
        SettingsResult result;
        try
        {
            SystemNotificationState state = ReadState(session);
            result = request.PlaySound is not { } playSound ? new SettingsResult(state, TestNotificationOutcome.Failed) : new SettingsResult(state, ShowTest(session, state, playSound));
        }
        catch (Exception)
        {
            // Only a failing logger gets here.
            result = SettingsResult.Abandoned;
        }

        request.Result.TrySetResult(result);
    }

    private SystemNotificationState ReadState(IToastSession session)
    {
        try
        {
            ToastNotifierSetting setting = session.Setting;
            SystemNotificationState state = StateOf(setting);
            if (state != SystemNotificationState.Allowed)
            {
                LogNotShown(setting);
            }

            return state;
        }
        catch (Exception ex)
        {
            LogSettingUnreadable(ex);
            return SystemNotificationState.Unknown;
        }
    }

    /// <summary>
    /// <c>NotificationSetting</c> of Windows: off for the app or for the user is the user's switch in Settings, a policy
    /// only the organization changes, anything else (a manifest of a packaged app, a new value) is unknown.
    /// </summary>
    internal static SystemNotificationState StateOf(ToastNotifierSetting setting) => setting switch
    {
        ToastNotifierSetting.Enabled => SystemNotificationState.Allowed,
        ToastNotifierSetting.DisabledForApplication or ToastNotifierSetting.DisabledForUser => SystemNotificationState.TurnedOff,
        ToastNotifierSetting.DisabledByGroupPolicy => SystemNotificationState.BlockedByPolicy,
        _ => SystemNotificationState.Unknown,
    };

    private TestNotificationOutcome ShowTest(IToastSession session, SystemNotificationState state, bool playSound)
    {
        switch (state)
        {
            case SystemNotificationState.TurnedOff:
                // Windows would drop the toast silently.
                return TestNotificationOutcome.TurnedOff;
            case SystemNotificationState.BlockedByPolicy:
                return TestNotificationOutcome.BlockedByPolicy;
            case SystemNotificationState.Unknown:
                // Windows did not say (or said something the app does not know): the toast may still be shown.
                break;
        }

        try
        {
            session.Show(ToastContentBuilder.BuildTest(_textCulture(), playSound), TestNotification.Identifier);
            LogTestShown(playSound);
            return TestNotificationOutcome.Sent;
        }
        catch (Exception ex)
        {
            LogTestFailed(ex);
            return TestNotificationOutcome.Failed;
        }
    }

    private void ShowUpdate(IToastSession session, UpdateNotification notification)
    {
        try
        {
            session.Show(ToastContentBuilder.BuildUpdate(notification, _textCulture()), UpdateNotificationText.Identifier);
            LogUpdateShown(notification.Version);
        }
        catch (Exception ex)
        {
            LogUpdateShowFailed(ex, notification.Version);
        }
    }

    private void ShowNow(IToastSession session, PrintNotification notification)
    {
        try
        {
            if (!ToastLaunchArgument.TryForPrinter(notification.Printer.Id, out _))
            {
                LogNoClickTarget(notification.Kind, notification.Printer.Id.Length);
            }

            // The language is resolved once per toast, right now, and every text of the toast is read in it (the thread's
            // own UI culture is never changed). Number formats follow CurrentCulture, as in the tray.
            session.Show(ToastContentBuilder.Build(notification, _textCulture()), tag: null);
            LogShown(notification.Kind, notification.Printer.Id);
        }
        catch (Exception ex)
        {
            LogShowFailed(ex, notification.Kind, notification.Printer.Id);
        }
    }

    private void Release(IToastSession session)
    {
        try
        {
            session.Dispose();
        }
        catch (Exception ex)
        {
            LogReleaseFailed(ex);
        }
    }

    private void OnActivated(string? argument)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _activated(argument);
        }
        catch (Exception ex)
        {
            try
            {
                LogActivationFailed(ex);
            }
            catch (Exception)
            {
                // The logger is gone (quitting); the click is simply lost.
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The test notification was handed to Windows (sound: {PlaySound}).")]
    private partial void LogTestShown(bool playSound);

    [LoggerMessage(Level = LogLevel.Information, Message = "Toast shown for the new version {Version}.")]
    private partial void LogUpdateShown(string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The toast for the new version {Version} could not be shown.")]
    private partial void LogUpdateShowFailed(Exception exception, string version);

    [LoggerMessage(Level = LogLevel.Error, Message = "Showing the test notification failed.")]
    private partial void LogTestFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Windows does not show the notifications of the app: {Setting}.")]
    private partial void LogNotShown(ToastNotifierSetting setting);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Whether Windows shows the notifications of the app could not be read.")]
    private partial void LogSettingUnreadable(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "System notifications are not available; notifications will not be shown.")]
    private partial void LogUnavailable(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The {Kind} notification for printer '{PrinterId}' was handed to Windows.")]
    private partial void LogShown(PrintEventKind kind, string printerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Showing the {Kind} notification for printer '{PrinterId}' failed.")]
    private partial void LogShowFailed(Exception exception, PrintEventKind kind, string printerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Too many notifications are waiting; dropped the {Kind} notification for printer '{PrinterId}'.")]
    private partial void LogDropped(PrintEventKind kind, string printerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The id of the printer is too long ({Length} characters) for the toast; the {Kind} notification is shown, but a click on it opens nothing.")]
    private partial void LogNoClickTarget(PrintEventKind kind, int length);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Releasing the system notification resources failed.")]
    private partial void LogReleaseFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Handling a click on a notification failed.")]
    private partial void LogActivationFailed(Exception exception);

    /// <summary>A request of a settings window; <paramref name="PlaySound"/> <see langword="null"/>: only read the setting.</summary>
    private sealed record SettingsRequest(bool? PlaySound)
    {
        public TaskCompletionSource<SettingsResult> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record SettingsResult(SystemNotificationState State, TestNotificationOutcome Outcome)
    {
        public static SettingsResult Abandoned { get; } = new(SystemNotificationState.Unknown, TestNotificationOutcome.Failed);
    }
}
