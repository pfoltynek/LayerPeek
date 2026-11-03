using Microsoft.Extensions.Logging;

using LayerPeek.App.Platform;
using LayerPeek.App.Tray;
using LayerPeek.App.Windows;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Monitoring;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Cameras;

/// <summary>
/// Closes the camera window of a switched-off printer and opens it again once the printer is back (T-151): feeds the state
/// machine <see cref="CameraWindowAutoVisibility"/> (T-150) with the monitoring runs (<see cref="IMonitoringObserver"/>),
/// the snapshots of the monitor, the resume of the computer and what happens to the windows
/// (<see cref="ICameraWindowAutoTarget"/>), and executes its commands on the UI thread: an automatic close is the close of
/// the close button reported as automatic, an automatic open is "Show camera" without taking the focus. The "closed
/// automatically" flags go to <see cref="ICameraWindowStateStore"/>. No notification (user decision 2026-11-03 #3).
/// </summary>
/// <remarks>
/// The state machine starts once the window state is loaded (its flags of the last run) and the windows are attached
/// (<see cref="Attach"/>); until then the latest settings are kept and snapshots are ignored (the delays start with the
/// next one). One timer, on the <see cref="TimeProvider"/>, wakes it at <see cref="CameraWindowAutoVisibility.NextDueAt"/>.
/// The <see cref="IMonitoringObserver"/> members, the snapshots and the resume event may come from any thread and are
/// posted to the UI thread; everything else is for the UI thread.
/// </remarks>
internal sealed partial class CameraWindowAutoVisibilityObserver : IMonitoringObserver, IDisposable
{
    private readonly IPrinterMonitor _monitor;
    private readonly ICameraWindowStateStore _states;
    private readonly IUiDispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly CameraWindowAutoVisibilityOptions _options;
    private readonly ISystemResumeEvents _resumeEvents;
    private readonly ILogger _logger;
    private readonly Queue<CameraWindowAutoCommand> _commands = new();
    private readonly ITimer _timer;
    private int _loadingStarted;

    // UI thread only.
    private CameraWindowAutoVisibility? _automaton;
    private ICameraWindowAutoTarget? _target;
    private bool _loaded;
    private bool _disposed;
    private bool _executing;

    // The settings of the latest monitoring run or update: applied when the state machine starts.
    private AppSettings? _settings;

    /// <param name="options">Delays (T-150 decision 1: 30 s / 10 s); the Debug test mode shortens them for the E2E tests.</param>
    /// <param name="resumeEvents">The resume of the computer after sleep (decision 9); <see langword="null"/>: none.</param>
    public CameraWindowAutoVisibilityObserver(
        IPrinterMonitor monitor,
        ICameraWindowStateStore states,
        IUiDispatcher dispatcher,
        TimeProvider timeProvider,
        ILogger<CameraWindowAutoVisibilityObserver> logger,
        CameraWindowAutoVisibilityOptions? options = null,
        ISystemResumeEvents? resumeEvents = null)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _monitor = monitor;
        _states = states;
        _dispatcher = dispatcher;
        _timeProvider = timeProvider;
        _logger = logger;
        _options = options ?? CameraWindowAutoVisibilityOptions.Default;
        _resumeEvents = resumeEvents ?? NoSystemResumeEvents.Instance;
        _timer = timeProvider.CreateTimer(static state => ((CameraWindowAutoVisibilityObserver)state!).OnTimer(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _monitor.SnapshotUpdated += OnSnapshotUpdated;
        _resumeEvents.Resumed += OnResumed;
    }

    /// <summary>The state machine runs (window state loaded and windows attached); for tests.</summary>
    internal bool IsRunning => _automaton is not null;

    /// <summary>The state machine, once it runs; for tests and diagnostics.</summary>
    internal CameraWindowAutoVisibility? Automaton => _automaton;

    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        StartLoading();
        _dispatcher.Post(() => Configure(settings, newRun: true));
    }

    public void UpdateSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _dispatcher.Post(() => Configure(settings, newRun: false));
    }

    public void ReportMonitoringFailed() => _dispatcher.Post(() => Run(static automaton => automaton.MonitoringStopped()));

    /// <summary>
    /// The camera windows of the app (UI thread): the state machine follows them from now on and starts once the window
    /// state is loaded. A second call replaces the first.
    /// </summary>
    public void Attach(ICameraWindowAutoTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (_disposed)
        {
            return;
        }

        Detach();
        // The UI runs now (macOS: AppKit is loaded).
        _resumeEvents.Start();
        _target = target;
        target.OpenChanged += OnOpenChanged;
        target.CloseDeferralChanged += OnCloseDeferralChanged;
        if (_automaton is null)
        {
            TryStart();
        }
        else
        {
            ReportOpenWindows();
        }
    }

    /// <summary>
    /// The windows are going away (quitting, UI thread): nothing is executed or followed any more, so that the closes of
    /// quitting are not taken for closes by the user. The flags stay as saved.
    /// </summary>
    public void Detach()
    {
        if (_target is { } target)
        {
            target.OpenChanged -= OnOpenChanged;
            target.CloseDeferralChanged -= OnCloseDeferralChanged;
            _target = null;
        }

        // The state machine knew these windows; a later Attach starts it again from the saved flags.
        _automaton = null;
        _commands.Clear();
        if (!_disposed)
        {
            _ = _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _monitor.SnapshotUpdated -= OnSnapshotUpdated;
        _resumeEvents.Resumed -= OnResumed;
        _timer.Dispose();
        // The windows' events are the UI thread's.
        _dispatcher.Post(Detach);
    }

    private void StartLoading()
    {
        if (Interlocked.Exchange(ref _loadingStarted, 1) != 0)
        {
            return;
        }

        // Never faults for file problems (the store logs them); the flags are read once it finished.
        _ = WaitForStatesAsync();
    }

    private async Task WaitForStatesAsync()
    {
        try
        {
            await _states.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogLoadFailed(_logger, ex);
        }

        _dispatcher.Post(() =>
        {
            _loaded = true;
            TryStart();
        });
    }

    private void TryStart()
    {
        if (_disposed || _automaton is not null || !_loaded || _target is null)
        {
            return;
        }

        _automaton = new CameraWindowAutoVisibility(_timeProvider, _options, _states.GetAutoClosedPrinters());
        // Windows the user opened before (the state machine never saw them open).
        ReportOpenWindows();
        if (_settings is { } settings)
        {
            // A new state machine has observed nothing yet, like a new monitoring run.
            Run(automaton => automaton.ApplySettings(settings));
        }
    }

    private void ReportOpenWindows()
    {
        if (_target is not { } target)
        {
            return;
        }

        foreach (string printerId in target.OpenPrinterIds.ToArray())
        {
            Run(automaton => automaton.WindowOpened(printerId));
            bool deferred = target.IsCloseDeferred(printerId);
            Run(automaton => automaton.SetCloseDeferred(printerId, deferred));
        }
    }

    private void Configure(AppSettings settings, bool newRun)
    {
        if (_disposed)
        {
            return;
        }

        // Applied when the state machine starts (it may not run yet, or run again after Detach).
        _settings = settings;
        Run(automaton => newRun ? automaton.ApplySettings(settings) : automaton.UpdateSettings(settings));
    }

    private void OnSnapshotUpdated(object? sender, PrinterSnapshot snapshot)
    {
        if (!_disposed)
        {
            _dispatcher.Post(() => Run(automaton => automaton.Process(snapshot)));
        }
    }

    private void OnResumed(object? sender, EventArgs e)
    {
        if (!_disposed)
        {
            _dispatcher.Post(() =>
            {
                LogResumed(_logger);
                Run(static automaton => automaton.SystemResumed());
            });
        }
    }

    private void OnTimer()
    {
        if (!_disposed)
        {
            _dispatcher.Post(() => Run(static automaton => automaton.Evaluate()));
        }
    }

    private void OnOpenChanged(object? sender, CameraWindowOpenChangedEventArgs e)
    {
        if (e.IsOpen)
        {
            Run(automaton => automaton.WindowOpened(e.PrinterId));
            bool deferred = _target?.IsCloseDeferred(e.PrinterId) ?? false;
            Run(automaton => automaton.SetCloseDeferred(e.PrinterId, deferred));
        }
        else if (!e.Automatic)
        {
            // Every close that does not execute a Close command (T-150 notes): the user, the settings, quitting.
            Run(automaton => automaton.WindowClosedByUser(e.PrinterId));
        }
    }

    private void OnCloseDeferralChanged(object? sender, CameraWindowCloseDeferralChangedEventArgs e) =>
        Run(automaton => automaton.SetCloseDeferred(e.PrinterId, e.Deferred));

    /// <summary>
    /// Feeds the state machine and executes its commands in order. Executing one may report back synchronously (the window
    /// closes or opens): those calls only queue their commands, which run after the current one.
    /// </summary>
    private void Run(Func<CameraWindowAutoVisibility, IReadOnlyList<CameraWindowAutoCommand>> input)
    {
        if (_disposed || _automaton is not { } automaton)
        {
            return;
        }

        foreach (CameraWindowAutoCommand command in input(automaton))
        {
            _commands.Enqueue(command);
        }

        if (_executing)
        {
            return;
        }

        _executing = true;
        try
        {
            while (_commands.TryDequeue(out CameraWindowAutoCommand command))
            {
                Execute(command);
            }
        }
        finally
        {
            _executing = false;
        }

        Reschedule();
    }

    private void Execute(CameraWindowAutoCommand command)
    {
        _states.SetAutoClosed(command.PrinterId, command.AutoClosed);
        switch (command.Action)
        {
            case CameraWindowAutoAction.Close:
                LogClosing(_logger, command.PrinterId);
                if (_target?.CloseAutomatically(command.PrinterId) != true)
                {
                    // Already closed: nothing to do; the flag makes it open again with the printer.
                    LogNothingToClose(_logger, command.PrinterId);
                }

                break;
            case CameraWindowAutoAction.Open:
                LogOpening(_logger, command.PrinterId);
                if (_target?.ShowAutomatically(command.PrinterId) != true && _automaton is { } automaton)
                {
                    // The camera was disabled meanwhile, or the app is quitting: the window stays closed for good.
                    LogOpenFailed(_logger, command.PrinterId);
                    foreach (CameraWindowAutoCommand next in automaton.WindowClosedByUser(command.PrinterId))
                    {
                        _commands.Enqueue(next);
                    }
                }

                break;
            case CameraWindowAutoAction.ForgetAutoClosed:
            default:
                break;
        }
    }

    private void Reschedule()
    {
        if (_disposed)
        {
            return;
        }

        if (_automaton?.NextDueAt is { } due)
        {
            TimeSpan delay = due - _timeProvider.GetUtcNow();
            _ = _timer.Change(delay > TimeSpan.Zero ? delay : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }
        else
        {
            _ = _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Closing the camera window of printer {PrinterId}: the printer is offline.")]
    private static partial void LogClosing(ILogger logger, string printerId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The camera window of printer {PrinterId} was already closed.")]
    private static partial void LogNothingToClose(ILogger logger, string printerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Opening the camera window of printer {PrinterId} again: the printer is online.")]
    private static partial void LogOpening(ILogger logger, string printerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "The camera window of printer {PrinterId} could not open again; it stays closed.")]
    private static partial void LogOpenFailed(ILogger logger, string printerId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The computer resumed from sleep: the camera window delays start again.")]
    private static partial void LogResumed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading the camera window state failed; no window is reopened from the last run.")]
    private static partial void LogLoadFailed(ILogger logger, Exception exception);
}
