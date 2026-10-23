using Microsoft.Extensions.Logging;

namespace PrusaTrayDiagnostic.App.Notifications;

/// <summary>
/// Clicks on toasts that Windows reports through the COM toast activator (T-068), on COM threads. Only clicks of the app's
/// own AppUserModelID with a well-formed launch argument (<see cref="ToastLaunchArgument.IsWellFormed"/>) count. Until the app says where they go (<see cref="Attach"/>) they wait
/// here (at most <see cref="MaxPending"/>): a start by Windows first waits for its click (<see cref="WaitForFirst"/>) to
/// forward it to a running instance, or handles it itself once it runs. Thread-safe; <see cref="Receive"/> never throws.
/// The argument is only passed on, never interpreted here: <see cref="ToastActivationRouter"/> accepts nothing but a
/// printer id.
/// </summary>
internal sealed partial class ToastActivationInbox
{
    /// <summary>Clicks kept before the app is ready; more are dropped (a user clicks a few toasts at most).</summary>
    public const int MaxPending = 16;

    private readonly string _appUserModelId;
    private readonly ILogger _logger;
    // A plain monitor (not Lock): WaitForFirst waits on it for the first click.
    private readonly object _lock = new();
    private readonly List<string> _pending = [];
    private Action<string>? _target;

    public ToastActivationInbox(string appUserModelId, ILogger<ToastActivationInbox> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserModelId);
        ArgumentNullException.ThrowIfNull(logger);
        _appUserModelId = appUserModelId;
        _logger = logger;
    }

    /// <summary>A click reported by Windows (any thread).</summary>
    public void Receive(string? appUserModelId, string? arguments)
    {
        if (!string.Equals(appUserModelId, _appUserModelId, StringComparison.OrdinalIgnoreCase))
        {
            LogForeignApp(appUserModelId?.Length ?? 0);
            return;
        }

        if (string.IsNullOrEmpty(arguments))
        {
            // E.g. a click on the header of the toast: nothing to open.
            LogNoArgument();
            return;
        }

        if (!ToastLaunchArgument.IsWellFormed(arguments))
        {
            // Not an argument of the app's own toasts: it can neither take the place of the click a start by Windows waits
            // for nor fill the queue. The argument itself is not logged.
            LogForeignArgument(arguments.Length);
            return;
        }

        LogReceived();
        Action<string>? target;
        lock (_lock)
        {
            target = _target;
            if (target is null)
            {
                if (_pending.Count < MaxPending)
                {
                    _pending.Add(arguments);
                }
                else
                {
                    LogDropped(MaxPending);
                }

                Monitor.PulseAll(_lock);
                return;
            }
        }

        Deliver(target, arguments);
    }

    /// <summary>
    /// The first click that came or comes within <paramref name="timeout"/>, or <see langword="null"/>. It stays for
    /// <see cref="Attach"/>.
    /// </summary>
    public string? WaitForFirst(TimeSpan timeout)
    {
        long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        lock (_lock)
        {
            while (_pending.Count == 0)
            {
                long left = deadline - Environment.TickCount64;
                if (left <= 0 || !Monitor.Wait(_lock, TimeSpan.FromMilliseconds(left)))
                {
                    break;
                }
            }

            return _pending.Count > 0 ? _pending[0] : null;
        }
    }

    /// <summary>The app is ready: waiting clicks (in order) and all later ones go to <paramref name="target"/>. Attach once.</summary>
    public void Attach(Action<string> target)
    {
        ArgumentNullException.ThrowIfNull(target);
        string[] waiting;
        lock (_lock)
        {
            if (_target is not null)
            {
                throw new InvalidOperationException("The inbox is already attached.");
            }

            _target = target;
            waiting = [.. _pending];
            _pending.Clear();
        }

        foreach (string arguments in waiting)
        {
            Deliver(target, arguments);
        }
    }

    private void Deliver(Action<string> target, string arguments)
    {
        try
        {
            target(arguments);
        }
        catch (Exception ex)
        {
            // Called by Windows: nothing may escape into COM.
            LogDeliveryFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "A click on a notification reached the app through its toast activator.")]
    private partial void LogReceived();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignored a toast activation of another app id ({Length} characters).")]
    private partial void LogForeignApp(int length);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignored a toast activation with an unrecognized argument ({Length} characters).")]
    private partial void LogForeignArgument(int length);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Ignored a toast activation without an argument.")]
    private partial void LogNoArgument();

    [LoggerMessage(Level = LogLevel.Warning, Message = "More than {Max} toast activations before the app was ready; the later ones were dropped.")]
    private partial void LogDropped(int max);

    [LoggerMessage(Level = LogLevel.Error, Message = "Handing over a toast activation failed.")]
    private partial void LogDeliveryFailed(Exception exception);
}
