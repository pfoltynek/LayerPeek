using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.Core.Events;
using PrusaTrayDiagnostic.Core.Notifications;

namespace PrusaTrayDiagnostic.App.Notifications;

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
/// Failures never escape: a platform that cannot be opened is logged once and turns the service into a no-op, a
/// failing toast is logged and the next one is still shown. After <see cref="Dispose"/> calls are ignored; disposing
/// does not wait for a toast in progress, the thread releases the platform on its own afterwards.
/// </para>
/// </summary>
internal sealed partial class ToastNotificationService : INotificationService, IDisposable
{
    /// <summary>Notifications waiting for the thread; more are dropped (a burst after a long outage, a hung OS call).</summary>
    public const int MaxPending = 32;

    private readonly IToastPlatform _platform;
    private readonly Action<string?> _activated;
    private readonly Func<CultureInfo> _textCulture;
    private readonly ILogger<ToastNotificationService> _logger;

    // Monitor.Wait/Pulse need a plain object.
    private readonly object _gate = new();

    // Guarded by _gate.
    private readonly Queue<PrintNotification> _pending = new();
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

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _pending.Clear();
            Monitor.PulseAll(_gate);
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
                while (TryTake(out PrintNotification? notification))
                {
                    ShowNow(session, notification);
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
    }

    private IToastSession? Open()
    {
        try
        {
            return _platform.Open(OnActivated);
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _unavailable = true;
                _pending.Clear();
            }

            LogUnavailable(ex);
            return null;
        }
    }

    private bool TryTake([NotNullWhen(true)] out PrintNotification? notification)
    {
        lock (_gate)
        {
            while (true)
            {
                if (_disposed)
                {
                    notification = null;
                    return false;
                }

                if (_pending.TryDequeue(out notification))
                {
                    return true;
                }

                Monitor.Wait(_gate);
            }
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
            session.Show(ToastContentBuilder.Build(notification, _textCulture()));
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

    [LoggerMessage(Level = LogLevel.Error, Message = "System notifications are not available; notifications will not be shown.")]
    private partial void LogUnavailable(Exception exception);

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
}
