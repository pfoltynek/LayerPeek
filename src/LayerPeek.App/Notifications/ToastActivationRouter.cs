using Microsoft.Extensions.Logging;

using LayerPeek.App.Detection;
using LayerPeek.App.Settings;
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
/// A click on the notification about a failed recording (<see cref="ToastLaunchArgument.RecordingSettings"/>, T-146) does
/// the same with the Recording tab.
/// </para>
/// <para>
/// T-058: a click on the notification about a new version (<see cref="ToastLaunchArgument.Update"/>) starts the update action
/// the UI attaches (<see cref="AttachUpdateOpener"/>), held the same way until it is attached.
/// </para>
/// <para>
/// T-154: a click on the notification of a suspected print failure (<see cref="ToastLaunchArgument.TryForCamera"/>) opens the
/// camera window of its printer through the opener the UI attaches (<see cref="AttachCameraOpener"/>), held like the settings
/// until it is attached, and only for a printer that is still configured. Its "False alarm" button
/// (<see cref="ToastLaunchArgument.ForFalseAlarm"/>) is handed to the <see cref="IFalseAlarmReporter"/> (a local record only).
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
    private Action<SettingsPage>? _settingsOpener;
    private SettingsPage? _settingsPending;
    private Action? _updateOpener;
    private bool _updatePending;
    private Action<string>? _cameraOpener;
    private string? _cameraPending;

    private readonly IFalseAlarmReporter? _falseAlarms;

    // Replaced as a whole, never mutated after publishing.
    private volatile Dictionary<string, PrinterDefinition> _printers = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="falseAlarms">Records the "False alarm" button of a print failure notification (T-154); <see langword="null"/>: ignored.</param>
    public ToastActivationRouter(PrinterWebOpener webOpener, ILogger<ToastActivationRouter> logger, TimeProvider? time = null, IFalseAlarmReporter? falseAlarms = null)
    {
        ArgumentNullException.ThrowIfNull(webOpener);
        ArgumentNullException.ThrowIfNull(logger);
        _webOpener = webOpener;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _falseAlarms = falseAlarms;
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
        if (ToastLaunchArgument.TryGetSettingsPage(argument, out SettingsPage page))
        {
            return OpenSettings(argument, page);
        }

        if (ToastLaunchArgument.IsUpdate(argument))
        {
            return OpenUpdate(argument);
        }

        if (ToastLaunchArgument.TryGetFalseAlarmId(argument, out string? alarmId))
        {
            return await ReportFalseAlarmAsync(argument, alarmId).ConfigureAwait(false);
        }

        if (!TryGetTarget(argument, out bool camera, out string? printerId))
        {
            LogMalformedArgument();
            return false;
        }

        if (IsRepeated(argument))
        {
            LogRepeated(printerId);
            return false;
        }

        if (!await WaitForPrintersAsync().ConfigureAwait(false))
        {
            return false;
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

        if (camera)
        {
            return OpenCamera(printer.Id);
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
    /// The UI runs (T-101): <paramref name="open"/> brings the settings window to the front on the given tab (it posts to
    /// the UI thread itself; called on any thread, never throws). A click that waited for it is handled now (the last one
    /// when clicks on different notifications waited). Attach once.
    /// </summary>
    public void AttachSettingsOpener(Action<SettingsPage> open)
    {
        ArgumentNullException.ThrowIfNull(open);
        SettingsPage? pending;
        lock (_recentLock)
        {
            if (_settingsOpener is not null)
            {
                throw new InvalidOperationException("The settings opener is already attached.");
            }

            _settingsOpener = open;
            pending = _stopped.Task.IsCompleted ? null : _settingsPending;
            _settingsPending = null;
        }

        if (pending is { } page)
        {
            LogSettingsClickHonoured();
            Invoke(open, page);
        }
    }

    /// <summary>
    /// The UI runs (T-058): <paramref name="open"/> starts the update action (the release page, or the update of a Velopack
    /// installation; it posts to the UI thread itself, called on any thread, never throws). A click that waited for it is
    /// handled now. Attach once.
    /// </summary>
    public void AttachUpdateOpener(Action open)
    {
        ArgumentNullException.ThrowIfNull(open);
        bool pending;
        lock (_recentLock)
        {
            if (_updateOpener is not null)
            {
                throw new InvalidOperationException("The update opener is already attached.");
            }

            _updateOpener = open;
            pending = _updatePending && !_stopped.Task.IsCompleted;
            _updatePending = false;
        }

        if (pending)
        {
            LogUpdateClickHonoured();
            Invoke(open);
        }
    }

    /// <summary>
    /// The UI runs (T-154): <paramref name="open"/> opens (or brings to the front) the camera window of a printer (it posts to
    /// the UI thread itself; called on any thread, never throws). A click that waited for it is handled now (the last one).
    /// Attach once.
    /// </summary>
    public void AttachCameraOpener(Action<string> open)
    {
        ArgumentNullException.ThrowIfNull(open);
        string? pending;
        lock (_recentLock)
        {
            if (_cameraOpener is not null)
            {
                throw new InvalidOperationException("The camera opener is already attached.");
            }

            _cameraOpener = open;
            pending = _stopped.Task.IsCompleted ? null : _cameraPending;
            _cameraPending = null;
        }

        if (pending is not null)
        {
            LogCameraClickHonoured(pending);
            Invoke(open, pending);
        }
    }

    /// <summary>The app quits: waiting clicks end without opening anything, later ones open nothing. Safe to repeat.</summary>
    public void Dispose() => _stopped.TrySetResult();

    /// <summary>A click on the test or recording notification: the settings window, now or once the UI attached its opener.</summary>
    private bool OpenSettings(string argument, SettingsPage page)
    {
        if (IsRepeated(argument))
        {
            LogSettingsRepeated();
            return false;
        }

        Action<SettingsPage>? open;
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
                // Coalesced like a waiting "activate": one window opens however often it was clicked, on the last tab asked for.
                _settingsPending = page;
                LogSettingsClickHeld();
                return true;
            }
        }

        Invoke(open, page);
        return true;
    }

    /// <summary>A click on the update notification: the update action, now or once the UI attached its opener.</summary>
    private bool OpenUpdate(string argument)
    {
        if (IsRepeated(argument))
        {
            LogUpdateRepeated();
            return false;
        }

        Action? open;
        lock (_recentLock)
        {
            if (_stopped.Task.IsCompleted)
            {
                return false;
            }

            open = _updateOpener;
            if (open is null)
            {
                _updatePending = true;
                LogUpdateClickHeld();
                return true;
            }
        }

        Invoke(open);
        return true;
    }

    /// <summary>The printer of a printer argument (its web) or of a camera argument (its camera window, T-154).</summary>
    private static bool TryGetTarget([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? argument, out bool camera, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? printerId)
    {
        camera = ToastLaunchArgument.TryGetCameraPrinterId(argument, out printerId);
        return camera || ToastLaunchArgument.TryGetPrinterId(argument, out printerId);
    }

    /// <summary>
    /// Waits for the printers of the first monitoring run (the click may have started the app), at most
    /// <see cref="SettingsWait"/>; ends early when the app quits.
    /// </summary>
    /// <returns><see langword="false"/> when they did not come in time.</returns>
    private async Task<bool> WaitForPrintersAsync()
    {
        if (_firstSettings.Task.IsCompleted)
        {
            return true;
        }

        try
        {
            await Task.WhenAny(_firstSettings.Task, _stopped.Task).WaitAsync(SettingsWait, _time).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            LogNoSettings(SettingsWait);
            return false;
        }
    }

    /// <summary>A click on a print failure notification: the camera window, now or once the UI attached its opener.</summary>
    private bool OpenCamera(string printerId)
    {
        Action<string>? open;
        lock (_recentLock)
        {
            if (_stopped.Task.IsCompleted)
            {
                return false;
            }

            open = _cameraOpener;
            if (open is null)
            {
                _cameraPending = printerId;
                LogCameraClickHeld(printerId);
                return true;
            }
        }

        Invoke(open, printerId);
        return true;
    }

    /// <summary>The "False alarm" button: a local record of the alarm (T-153), when this run still knows it.</summary>
    private async Task<bool> ReportFalseAlarmAsync(string argument, string alarmId)
    {
        if (IsRepeated(argument))
        {
            LogFalseAlarmRepeated(alarmId);
            return false;
        }

        if (_falseAlarms is null || _stopped.Task.IsCompleted)
        {
            return false;
        }

        try
        {
            return await _falseAlarms.ReportAsync(alarmId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogFalseAlarmFailed(ex, alarmId);
            return false;
        }
    }

    private void Invoke(Action<string> open, string printerId)
    {
        try
        {
            open(printerId);
        }
        catch (Exception ex)
        {
            LogCameraFailed(ex, printerId);
        }
    }

    private void Invoke(Action<SettingsPage> open, SettingsPage page)
    {
        try
        {
            open(page);
        }
        catch (Exception ex)
        {
            LogSettingsFailed(ex);
        }
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

    [LoggerMessage(Level = LogLevel.Information, Message = "A click on the update notification waits for the UI; the update starts once it runs.")]
    private partial void LogUpdateClickHeld();

    [LoggerMessage(Level = LogLevel.Information, Message = "The UI runs: the update starts for a click on the update notification that waited.")]
    private partial void LogUpdateClickHonoured();

    [LoggerMessage(Level = LogLevel.Debug, Message = "A click on the update notification reported again; the update started once.")]
    private partial void LogUpdateRepeated();

    [LoggerMessage(Level = LogLevel.Information, Message = "A click on a print failure notification of printer '{PrinterId}' waits for the UI; its camera window opens once it runs.")]
    private partial void LogCameraClickHeld(string printerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "The UI runs: the camera window of printer '{PrinterId}' opens for a click on a print failure notification that waited.")]
    private partial void LogCameraClickHonoured(string printerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Opening the camera window of printer '{PrinterId}' for a click on a print failure notification failed.")]
    private partial void LogCameraFailed(Exception exception, string printerId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The \"False alarm\" button of alarm {AlarmId} reported again; recorded once.")]
    private partial void LogFalseAlarmRepeated(string alarmId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Recording the false alarm {AlarmId} failed.")]
    private partial void LogFalseAlarmFailed(Exception exception, string alarmId);

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
