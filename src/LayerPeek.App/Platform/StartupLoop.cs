using LayerPeek.App.Tray;

namespace LayerPeek.App.Platform;

/// <summary>
/// The main thread before Avalonia starts while macOS has no active display (T-099): it runs the work posted to the UI
/// thread (<see cref="Dispatcher"/>: monitor updates of the tray view models, continuations of the services started on the
/// main thread) and the run loop of the main thread (<see cref="IStartupRunLoop"/>: what the OS delivers there) instead of
/// sleeping. Used only on the main thread; posting works from any thread.
/// </summary>
internal sealed class StartupLoop : IDisposable
{
    private readonly IStartupRunLoop _runLoop;
    private readonly TimeProvider _clock;

    /// <param name="runLoop">The run loop of the calling (main) thread; owned and disposed by this loop.</param>
    /// <param name="clock">Measures the pauses; <see cref="TimeProvider.System"/> when null.</param>
    public StartupLoop(IStartupRunLoop runLoop, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(runLoop);
        _runLoop = runLoop;
        _clock = clock ?? TimeProvider.System;
        Dispatcher = new StartupUiDispatcher(runLoop.Wake);
    }

    /// <summary>The UI thread's dispatcher until Avalonia's takes over (<see cref="StartupUiDispatcher.AttachTo"/>).</summary>
    public StartupUiDispatcher Dispatcher { get; }

    /// <summary>
    /// The pause of <see cref="StartupDisplayWait"/>: runs posted work and the run loop for <paramref name="interval"/>, or
    /// until <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <returns><see langword="true"/> when the token was cancelled.</returns>
    public bool Pause(TimeSpan interval, CancellationToken cancellationToken)
    {
        using CancellationTokenRegistration wake = cancellationToken.Register(static state => ((IStartupRunLoop)state!).Wake(), _runLoop);
        long started = _clock.GetTimestamp();
        while (true)
        {
            _ = Dispatcher.RunPending();
            if (cancellationToken.IsCancellationRequested)
            {
                return true;
            }

            TimeSpan remaining = interval - _clock.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            _runLoop.RunFor(remaining);
        }
    }

    /// <summary>
    /// Runs posted work and the run loop until <paramref name="task"/> completed or <paramref name="limit"/> passed (quitting
    /// while waiting: the services stop with continuations on this thread). Never throws for the task.
    /// </summary>
    /// <returns>Whether the task completed in time.</returns>
    public bool RunUntil(Task task, TimeSpan limit)
    {
        ArgumentNullException.ThrowIfNull(task);
        _ = task.ContinueWith(static (_, state) => ((IStartupRunLoop)state!).Wake(), _runLoop, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        long started = _clock.GetTimestamp();
        while (true)
        {
            _ = Dispatcher.RunPending();
            if (task.IsCompleted)
            {
                return true;
            }

            TimeSpan remaining = limit - _clock.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            _runLoop.RunFor(remaining);
        }
    }

    /// <summary>Releases the run loop's source; the dispatcher keeps queuing until it is attached.</summary>
    public void Dispose() => _runLoop.Dispose();
}
