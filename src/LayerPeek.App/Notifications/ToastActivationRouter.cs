using Microsoft.Extensions.Logging;

using LayerPeek.App.Tray;
using LayerPeek.App.Tray.Menu;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Notifications;

/// <summary>
/// Handles a click on a notification: launch argument → configured printer → its web interface
/// (<see cref="PrinterWebAddress"/>, the same rules as the tray menu) → <see cref="PrinterWebOpener"/>, so a PrusaLink
/// printer with a stored password opens logged in through the local proxy, exactly like from the tray menu (T-028).
/// Knows the printers of the current monitoring run, so an old notification opens the current address and one of a
/// removed printer opens nothing. Thread-safe: clicks arrive on OS threads.
/// <para>
/// T-068: a click that started the app (Windows starts it through the COM toast activator) waits for the first settings
/// of monitoring, at most <see cref="SettingsWait"/>. Windows may report one click twice (the toast's own event and the
/// COM activator): the same argument again within <see cref="DuplicateWindow"/> is ignored. Disposing (the container, when
/// the app quits) ends every wait at once; nothing opens after that.
/// </para>
/// <para>
/// T-101: a click on the test notification (<see cref="ToastLaunchArgument.Settings"/>) opens no web: it brings the settings
/// window to the front on the General tab through the opener the UI attaches (<see cref="AttachSettingsOpener"/>). A click
/// that comes before the UI runs (macOS waiting for a display, T-099) is held, once, and honoured when the opener is attached.
/// </para>
/// </summary>
internal sealed partial class ToastActivationRouter : IMonitoringObserver, IDisposable
{
    /// <summary>How long a click waits for the printers of the first monitoring run (the app was just started for it).</summary>
    public static readonly TimeSpan SettingsWait = TimeSpan.FromSeconds(30);

    /// <summary>The same click reported again within this time opens nothing more.</summary>
    public static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(2);

    private readonly PrinterWebOpener _webOpener;
    private readonly ILogger<ToastActivationRouter> _logger;
    private readonly TimeProvider _time;
    private readonly TaskCompletionSource _firstSettings = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _recentLock = new();
    private string? _recentArgument;
    private long _recentTimestamp;

    // Guarded by _recentLock.
    private Action? _settingsOpener;
    private bool _settingsPending;

    // Replaced as a whole, never mutated after publishing.
    private volatile Dictionary<string, PrinterDefinition> _printers = new(StringComparer.OrdinalIgnoreCase);

    public ToastActivationRouter(PrinterWebOpener webOpener, ILogger<ToastActivationRouter> logger, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(webOpener);
        ArgumentNullException.ThrowIfNull(logger);
        _webOpener = webOpener;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Opens the web interface of the printer in <paramref name="argument"/>, or for the test notification the settings
    /// window (T-101); never throws.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the browser was started, or the settings window was asked for (or waits for the UI).
    /// </returns>
    public async Task<bool> ActivateAsync(string? argument)
    {
        if (ToastLaunchArgument.IsSettings(argument))
        {
            return OpenSettings(argument);
        }

        if (!ToastLaunchArgument.TryGetPrinterId(argument, out string? printerId))
        {
            LogMalformedArgument();
            return false;
        }

        if (IsRepeated(argument))
        {
            LogRepeated(printerId);
            return false;
        }

        if (!_firstSettings.Task.IsCompleted)
        {
            try
            {
                // Ends early when the app quits.
                await Task.WhenAny(_firstSettings.Task, _stopped.Task).WaitAsync(SettingsWait, _time).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                LogNoSettings(SettingsWait);
                return false;
            }
        }

        if (_stopped.Task.IsCompleted)
        {
            LogStopped(printerId);
            return false;
        }

        if (!_printers.TryGetValue(printerId, out PrinterDefinition? printer))
        {
            // Removed since the notification was shown; nothing to open.
            LogUnknownPrinter(printerId);
            return false;
        }

        Uri? address = PrinterWebAddress.For(printer);
        if (address is null)
        {
            // The address itself is not logged: it could contain credentials.
            LogNoWebAddress(printer.Id);
            return false;
        }

        // Never throws; when quitting, the disposed gateway makes it open nothing.
        return await _webOpener.OpenAsync(address, printer, CancellationToken.None).ConfigureAwait(false);
    }

    void IMonitoringObserver.ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Dictionary<string, PrinterDefinition> printers = new(settings.Printers.Count, StringComparer.OrdinalIgnoreCase);
        foreach (PrinterDefinition printer in settings.Printers)
        {
            printers.TryAdd(printer.Id, printer);
        }

        _printers = printers;
        _firstSettings.TrySetResult();
    }

    /// <summary>The printers stay configured; clicks keep working.</summary>
    void IMonitoringObserver.ReportMonitoringFailed()
    {
    }

    /// <summary>
    /// The UI runs (T-101): <paramref name="open"/> brings the settings window to the front on the General tab (it posts to
    /// the UI thread itself; called on any thread, never throws). A click that waited for it is handled now. Attach once.
    /// </summary>
    public void AttachSettingsOpener(Action open)
    {
        ArgumentNullException.ThrowIfNull(open);
        bool pending;
        lock (_recentLock)
        {
            if (_settingsOpener is not null)
            {
                throw new InvalidOperationException("The settings opener is already attached.");
            }

            _settingsOpener = open;
            pending = _settingsPending && !_stopped.Task.IsCompleted;
            _settingsPending = false;
        }

        if (pending)
        {
            LogSettingsClickHonoured();
            Invoke(open);
        }
    }

    /// <summary>The app quits: waiting clicks end without opening anything, later ones open nothing. Safe to repeat.</summary>
    public void Dispose() => _stopped.TrySetResult();

    /// <summary>A click on the test notification: the settings window, now or once the UI attached its opener.</summary>
    private bool OpenSettings(string argument)
    {
        if (IsRepeated(argument))
        {
            LogSettingsRepeated();
            return false;
        }

        Action? open;
        lock (_recentLock)
        {
            if (_stopped.Task.IsCompleted)
            {
                LogSettingsStopped();
                return false;
            }

            open = _settingsOpener;
            if (open is null)
            {
                // Coalesced like a waiting "activate": one window opens however often it was clicked.
                _settingsPending = true;
                LogSettingsClickHeld();
                return true;
            }
        }

        Invoke(open);
        return true;
    }

    private void Invoke(Action open)
    {
        try
        {
            open();
        }
        catch (Exception ex)
        {
            LogSettingsFailed(ex);
        }
    }

    /// <summary>Whether the same argument came within <see cref="DuplicateWindow"/>; remembers this one otherwise.</summary>
    private bool IsRepeated(string argument)
    {
        lock (_recentLock)
        {
            long now = _time.GetTimestamp();
            if (string.Equals(argument, _recentArgument, StringComparison.Ordinal) && _time.GetElapsedTime(_recentTimestamp, now) < DuplicateWindow)
            {
                return true;
            }

            _recentArgument = argument;
            _recentTimestamp = now;
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "A click on the test notification waits for the UI; the settings window opens once it runs.")]
    private partial void LogSettingsClickHeld();

    [LoggerMessage(Level = LogLevel.Information, Message = "The UI runs: the settings window opens for a click on the test notification that waited.")]
    private partial void LogSettingsClickHonoured();

    [LoggerMessage(Level = LogLevel.Debug, Message = "A click on the test notification reported again; the settings window opened once.")]
    private partial void LogSettingsRepeated();

    [LoggerMessage(Level = LogLevel.Debug, Message = "A click on the test notification while the app quits; nothing to open.")]
    private partial void LogSettingsStopped();

    [LoggerMessage(Level = LogLevel.Error, Message = "Opening the settings window for a click on the test notification failed.")]
    private partial void LogSettingsFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignored a notification click with an unrecognized argument.")]
    private partial void LogMalformedArgument();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Notification click for printer '{PrinterId}' reported again; opened once.")]
    private partial void LogRepeated(string printerId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Notification click for printer '{PrinterId}' while the app quits; nothing to open.")]
    private partial void LogStopped(string printerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification click before monitoring knew its printers ({Wait}); nothing to open.")]
    private partial void LogNoSettings(TimeSpan wait);

    [LoggerMessage(Level = LogLevel.Information, Message = "Notification click for printer '{PrinterId}', which is no longer configured; nothing to open.")]
    private partial void LogUnknownPrinter(string printerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification click for printer '{PrinterId}', whose address cannot be opened in a browser.")]
    private partial void LogNoWebAddress(string printerId);
}
