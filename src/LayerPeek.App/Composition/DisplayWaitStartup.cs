using Microsoft.Extensions.Logging;

using LayerPeek.App.Platform;
using LayerPeek.App.Tray;

namespace LayerPeek.App.Composition;

/// <summary>How <see cref="DisplayWaitStartup{TServices}.Run"/> ended.</summary>
/// <param name="Result">Whether the UI may start (<see cref="StartupDisplayWaitResult.Cancelled"/>: the process quits).</param>
/// <param name="Services">
/// The services started while the app waited, for the UI to attach to; <see langword="null"/> when a display was active at
/// once (the normal start: the UI creates them) or the process quits (they were stopped).
/// </param>
internal readonly record struct DisplayWaitOutcome<TServices>(StartupDisplayWaitResult Result, Task<TServices>? Services)
    where TServices : class;

/// <summary>
/// The start of the macOS app while no display is active (T-096, T-099): Avalonia cannot start before a display wakes, but
/// monitoring and notifications must run meanwhile (user decision 2026-10-29). With an active display nothing happens here
/// (the normal start). Otherwise, at the first check without a display, the services are started on the main thread
/// (<paramref name="startServices"/>, with <see cref="StartupLoop.Dispatcher"/> as the UI thread's dispatcher and the main
/// thread's synchronization context), and while it waits the main thread runs their UI-thread work and its run loop
/// (<see cref="StartupLoop"/>). Once a display is active the UI attaches to the same services. Asked to quit meanwhile, the
/// services are stopped within <paramref name="stopLimit"/>. Generic so that tests use fake services.
/// </summary>
/// <param name="displays">The active displays.</param>
/// <param name="loop">The main thread's loop of this start.</param>
/// <param name="startServices">Starts the services (main thread; continuations come back to it).</param>
/// <param name="stopServices">Stops them (quitting while waiting).</param>
/// <param name="loggers">The startup log.</param>
/// <param name="pollInterval">Time between two checks; <see cref="StartupDisplayWait.DefaultPollInterval"/> when null.</param>
/// <param name="stopLimit">How long quitting waits for the services at most; <see cref="DefaultStopLimit"/> when null.</param>
internal sealed partial class DisplayWaitStartup<TServices>(
    IActiveDisplays displays,
    StartupLoop loop,
    Func<IUiDispatcher, Task<TServices>> startServices,
    Func<TServices, Task> stopServices,
    ILoggerFactory loggers,
    TimeSpan? pollInterval = null,
    TimeSpan? stopLimit = null)
    where TServices : class
{
    /// <summary>The quit limits of the app (3 s for the printer clients, 2 s grace) also apply while it waits.</summary>
    public static readonly TimeSpan DefaultStopLimit = TimeSpan.FromSeconds(5);

    private readonly IActiveDisplays _displays = displays ?? throw new ArgumentNullException(nameof(displays));
    private readonly StartupLoop _loop = loop ?? throw new ArgumentNullException(nameof(loop));
    private readonly Func<IUiDispatcher, Task<TServices>> _startServices = startServices ?? throw new ArgumentNullException(nameof(startServices));
    private readonly Func<TServices, Task> _stopServices = stopServices ?? throw new ArgumentNullException(nameof(stopServices));
    private readonly ILoggerFactory _loggers = loggers ?? throw new ArgumentNullException(nameof(loggers));
    private readonly ILogger _logger = loggers.CreateLogger<DisplayWaitStartup<TServices>>();
    private readonly TimeSpan _stopLimit = stopLimit ?? DefaultStopLimit;
    private Task<TServices>? _services;

    /// <summary>
    /// Main thread: waits for an active display (without a time limit), running the services meanwhile. An exception of
    /// starting the services ends the wait like a failed start of the UI would (it escapes; the crash is logged).
    /// </summary>
    public DisplayWaitOutcome<TServices> Run(CancellationToken quit)
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new StartupSynchronizationContext(_loop.Dispatcher));
        try
        {
            StartupDisplayWait wait = new(_displays, Pause, _loggers.CreateLogger<StartupDisplayWait>(), pollInterval);
            StartupDisplayWaitResult result = wait.WaitForDisplay(quit);
            if (result != StartupDisplayWaitResult.Cancelled)
            {
                if (_services is not null)
                {
                    LogUiAttaches();
                }

                return new DisplayWaitOutcome<TServices>(result, _services);
            }

            if (_services is not null)
            {
                Task stopping = StopAsync(_services);
                if (!_loop.RunUntil(stopping, _stopLimit))
                {
                    LogStopTimedOut(_stopLimit);
                }
                else if (stopping.Exception is { } failure)
                {
                    LogStopFailed(failure.GetBaseException());
                }
            }

            return new DisplayWaitOutcome<TServices>(result, null);
        }
        finally
        {
            // Avalonia installs its own; continuations captured meanwhile keep posting to the startup dispatcher.
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    /// <summary>The pause of the display wait: the first one starts the services, each one runs the main thread's work.</summary>
    private bool Pause(TimeSpan interval, CancellationToken cancellationToken)
    {
        if (_services is null)
        {
            LogStartingServices();
            _services = _startServices(_loop.Dispatcher);
        }

        bool cancelled = _loop.Pause(interval, cancellationToken);
        if (_services.IsFaulted)
        {
            // Like a failed start of the UI: the app cannot run without its services.
            _services.GetAwaiter().GetResult();
        }

        return cancelled;
    }

    private async Task StopAsync(Task<TServices> services)
    {
        TServices started = await services.ConfigureAwait(true);
        await _stopServices(started).ConfigureAwait(true);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting printer monitoring and notifications while waiting for an active display.")]
    private partial void LogStartingServices();

    [LoggerMessage(Level = LogLevel.Information, Message = "The menu bar item attaches to the monitoring that already runs.")]
    private partial void LogUiAttaches();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stopping the monitoring did not finish within {Limit} while quitting; the process ends anyway.")]
    private partial void LogStopTimedOut(TimeSpan limit);

    [LoggerMessage(Level = LogLevel.Error, Message = "Stopping the monitoring failed while quitting.")]
    private partial void LogStopFailed(Exception exception);
}
