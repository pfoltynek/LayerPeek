using Microsoft.Extensions.Logging;

namespace LayerPeek.App.Tray;

/// <summary>
/// "Quit" from the tray menu: stops monitoring (waiting at most the stop timeout) and then always shuts the
/// application down, even when stopping fails or hangs. Runs once; later requests are ignored.
/// </summary>
internal sealed partial class QuitCoordinator
{
    private readonly Func<TimeSpan, Task> _stop;
    private readonly Action _shutdown;
    private readonly TimeSpan _stopTimeout;
    private readonly TimeSpan _hardLimit;
    private readonly ILogger _logger;
    private int _quitting;

    /// <param name="stop">Stops monitoring and releases services, waiting at most the given timeout for the printers.</param>
    /// <param name="shutdown">Ends the application; called on the caller's (UI) context.</param>
    /// <param name="stopTimeout">How long <paramref name="stop"/> may wait for printer clients (3 s, T-007).</param>
    /// <param name="grace">
    /// Extra time for releasing services after the timeout; if <paramref name="stop"/> still has not finished,
    /// the app shuts down anyway so that quitting can never hang.
    /// </param>
    public QuitCoordinator(Func<TimeSpan, Task> stop, Action shutdown, TimeSpan stopTimeout, TimeSpan grace, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(stop);
        ArgumentNullException.ThrowIfNull(shutdown);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThan(stopTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(grace, TimeSpan.Zero);
        _stop = stop;
        _shutdown = shutdown;
        _stopTimeout = stopTimeout;
        _hardLimit = stopTimeout + grace;
        _logger = logger;
    }

    /// <summary>Quitting started (from the menu); an exit initiated by the OS afterwards needs no extra stop request.</summary>
    public bool IsQuitting => Volatile.Read(ref _quitting) != 0;

    /// <summary>Never throws. Completes after the shutdown was requested.</summary>
    public async Task QuitAsync()
    {
        if (Interlocked.Exchange(ref _quitting, 1) != 0)
        {
            return;
        }

        try
        {
            await _stop(_stopTimeout).WaitAsync(_hardLimit).ConfigureAwait(true);
        }
        catch (TimeoutException)
        {
            TryLog(static (self, _) => self.LogStopAbandoned(self._hardLimit), (Exception?)null);
        }
        catch (Exception ex)
        {
            // Quitting must succeed even if a service fails while shutting down.
            TryLog(static (self, failure) => self.LogStopFailed(failure!), ex);
        }
        finally
        {
            _shutdown();
        }
    }

    /// <summary>
    /// Logging must never break quitting: the logger may already be unusable (a provider disposed or failing while the
    /// app shuts down), and an exception here would escape into an <c>async void</c> UI handler and crash the app.
    /// </summary>
    private void TryLog(Action<QuitCoordinator, Exception?> log, Exception? exception)
    {
        try
        {
            log(this, exception);
        }
        catch (Exception)
        {
            // Nothing left to report to.
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stopping did not finish within {Limit}; quitting anyway.")]
    private partial void LogStopAbandoned(TimeSpan limit);

    [LoggerMessage(Level = LogLevel.Error, Message = "Stopping failed while quitting; quitting anyway.")]
    private partial void LogStopFailed(Exception exception);
}
