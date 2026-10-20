using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.Core.Printers;

namespace PrusaTrayDiagnostic.Core.Monitoring;

/// <summary>
/// Polls every printer in its own loop, so a slow or failing printer never delays the others.
/// Reachable printers are polled at a fixed cadence; unreachable ones with exponential backoff counted
/// from the end of the failed poll.
/// </summary>
public sealed partial class PrinterMonitor : IPrinterMonitor
{
    /// <summary>Technical lower bound of the polling interval (timer resolution); user settings enforce a higher one.</summary>
    public static readonly TimeSpan MinPollingInterval = TimeSpan.FromMilliseconds(1);

    /// <summary>Upper bound of the polling interval (timers support at most ~49 days).</summary>
    public static readonly TimeSpan MaxPollingInterval = TimeSpan.FromDays(1);

    private readonly IPrinterClientFactory _clientFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PrinterMonitor> _logger;
    private readonly PrinterMonitorOptions _options;
    private readonly Lock _handlersLock = new();

    /// <summary>
    /// How often one run asks a printer for its model when asking fails (offline right after a successful poll, timeout);
    /// a definitive answer ends the asking at once.
    /// </summary>
    internal const int MaxModelDetectionAttempts = 3;

    // Copy-on-write so that publishing needs neither a lock nor an allocation.
    private EventHandler<PrinterSnapshot>[] _handlers = [];
    private EventHandler<PrinterModelDetected>[] _modelHandlers = [];
    private SnapshotTable _snapshots = SnapshotTable.Empty;
    private int _running;

    public PrinterMonitor(IPrinterClientFactory clientFactory, TimeProvider timeProvider, ILogger<PrinterMonitor> logger, PrinterMonitorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _clientFactory = clientFactory;
        _timeProvider = timeProvider;
        _logger = logger;
        _options = options ?? PrinterMonitorOptions.Default;
    }

    /// <inheritdoc/>
    /// <remarks>Each handler is isolated: an exception in one is logged and the others still run.</remarks>
    public event EventHandler<PrinterSnapshot>? SnapshotUpdated
    {
        add
        {
            if (value is not null)
            {
                lock (_handlersLock)
                {
                    _handlers = [.. _handlers, value];
                }
            }
        }

        remove
        {
            if (value is not null)
            {
                lock (_handlersLock)
                {
                    int index = Array.LastIndexOf(_handlers, value);
                    if (index >= 0)
                    {
                        _handlers = [.. _handlers.AsSpan(0, index), .. _handlers.AsSpan(index + 1)];
                    }
                }
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>Each handler is isolated: an exception in one is logged and the others still run.</remarks>
    public event EventHandler<PrinterModelDetected>? ModelDetected
    {
        add
        {
            if (value is not null)
            {
                lock (_handlersLock)
                {
                    _modelHandlers = [.. _modelHandlers, value];
                }
            }
        }

        remove
        {
            if (value is not null)
            {
                lock (_handlersLock)
                {
                    int index = Array.LastIndexOf(_modelHandlers, value);
                    if (index >= 0)
                    {
                        _modelHandlers = [.. _modelHandlers.AsSpan(0, index), .. _modelHandlers.AsSpan(index + 1)];
                    }
                }
            }
        }
    }

    public IReadOnlyList<PrinterSnapshot> Snapshots => Volatile.Read(ref _snapshots).ToArray();

    public bool TryGetSnapshot(string printerId, [NotNullWhen(true)] out PrinterSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(printerId);
        return Volatile.Read(ref _snapshots).TryGet(printerId, out snapshot);
    }

    /// <inheritdoc/>
    /// <remarks>Stop with <see cref="CancellationTokenSource.CancelAsync"/> so that shutdown work does not run on the calling thread.</remarks>
    public async Task RunAsync(IReadOnlyList<PrinterDefinition> printers, TimeSpan pollingInterval, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(printers);
        ArgumentOutOfRangeException.ThrowIfLessThan(pollingInterval, MinPollingInterval);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pollingInterval, MaxPollingInterval);
        SnapshotTable table = new(printers);

        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            throw new InvalidOperationException("The monitor is already running.");
        }

        try
        {
            Volatile.Write(ref _snapshots, table);
            if (printers.Count == 0)
            {
                await WaitForCancellationAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            Task[] loops = new Task[printers.Count];
            for (int slot = 0; slot < loops.Length; slot++)
            {
                PrinterPollLoop loop = new(this, printers[slot], slot, table, pollingInterval);
                // Task.Run: a client that blocks synchronously must not delay the start of the other loops.
                loops[slot] = Task.Run(() => loop.RunAsync(cancellationToken), CancellationToken.None);
            }

            await Task.WhenAll(loops).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private void Publish(SnapshotTable table, int slot, PrinterSnapshot snapshot)
    {
        table.Set(slot, snapshot);
        foreach (EventHandler<PrinterSnapshot> handler in Volatile.Read(ref _handlers))
        {
            try
            {
                handler(this, snapshot);
            }
            catch (Exception ex)
            {
                LogSubscriberFailed(ex, snapshot.PrinterId);
            }
        }
    }

    private void PublishModel(PrinterModelDetected detected)
    {
        foreach (EventHandler<PrinterModelDetected> handler in Volatile.Read(ref _modelHandlers))
        {
            try
            {
                handler(this, detected);
            }
            catch (Exception ex)
            {
                LogModelSubscriberFailed(ex, detected.PrinterId);
            }
        }
    }

    private static async Task WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Polling printer '{PrinterId}' failed.")]
    private partial void LogPollFailed(Exception exception, string printerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "An abandoned poll of printer '{PrinterId}' failed later.")]
    private partial void LogAbandonedPollFailed(Exception exception, string printerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Creating the client for printer '{PrinterId}' failed.")]
    private partial void LogClientCreationFailed(Exception exception, string printerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Disposing the client of printer '{PrinterId}' failed.")]
    private partial void LogClientDisposeFailed(Exception exception, string printerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "A SnapshotUpdated handler failed for printer '{PrinterId}'.")]
    private partial void LogSubscriberFailed(Exception exception, string printerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "A ModelDetected handler failed for printer '{PrinterId}'.")]
    private partial void LogModelSubscriberFailed(Exception exception, string printerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Asking printer '{PrinterId}' for its model failed.")]
    private partial void LogModelDetectionFailed(Exception exception, string printerId);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Monitoring of printer '{PrinterId}' stopped because of an internal error.")]
    private partial void LogLoopCrashed(Exception exception, string printerId);

    /// <summary>
    /// Polling loop of one printer. While the printer is reachable, nothing is allocated besides what the client
    /// allocates: the timer and the timeout source are reused. A new timer is created only while backing off.
    /// </summary>
    private sealed class PrinterPollLoop
    {
        private readonly PrinterMonitor _monitor;
        private readonly PrinterDefinition _printer;
        private readonly int _slot;
        private readonly SnapshotTable _table;
        private readonly TimeSpan _interval;
        private readonly TimeSpan _maxDelay;
        private readonly string _timeoutMessage;

        private static readonly TimeSpan AbandonGracePeriod = TimeSpan.FromMilliseconds(100);

        private IPrinterClient? _client;

        // A poll or model request that ignored its timeout; the client is not called again until it finishes.
        private Task? _abandonedPoll;
        private int _consecutiveFailures;
        private int _modelAttemptsLeft = MaxModelDetectionAttempts;

        public PrinterPollLoop(PrinterMonitor monitor, PrinterDefinition printer, int slot, SnapshotTable table, TimeSpan interval)
        {
            _monitor = monitor;
            _printer = printer;
            _slot = slot;
            _table = table;
            _interval = interval;
            _maxDelay = interval > monitor._options.MaxBackoff ? interval : monitor._options.MaxBackoff;
            _timeoutMessage = $"The printer did not respond within {monitor._options.PollTimeout.TotalSeconds:0.#} s.";
        }

        public async Task RunAsync(CancellationToken stopToken)
        {
            PeriodicTimer timer = new(_interval, _monitor._timeProvider);
            using PollTimeout timeout = new(_monitor._timeProvider, _monitor._options.PollTimeout, stopToken);
            try
            {
                while (!stopToken.IsCancellationRequested)
                {
                    PrinterSnapshot snapshot = await PollAsync(timeout, stopToken).ConfigureAwait(false);
                    if (stopToken.IsCancellationRequested)
                    {
                        // The run is stopping (e.g. new settings): a result that finished meanwhile may reflect a change
                        // already made for the next run (a removed printer's deleted password) and must not reach observers.
                        break;
                    }

                    // Reschedule before publishing, so that observers always see the next poll already planned.
                    timer = Reschedule(timer, snapshot.State.IsReachable());
                    _monitor.Publish(_table, _slot, snapshot);

                    // After the snapshot is out: asking for the model must never delay the state shown to the user.
                    if (_modelAttemptsLeft > 0 && snapshot.State.IsReachable() && _client is IPrinterModelSource modelSource)
                    {
                        await DetectModelAsync(modelSource, timeout, stopToken).ConfigureAwait(false);
                    }

                    if (!await timer.WaitForNextTickAsync(stopToken).ConfigureAwait(false))
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
            {
                // Normal shutdown.
            }
            catch (Exception ex)
            {
                // Defensive: everything expected is handled in PollAsync; this keeps the other loops and RunAsync intact.
                _monitor.LogLoopCrashed(ex, _printer.Id);
                _monitor.Publish(_table, _slot, Unavailable(PrinterState.Unknown, "Monitoring stopped because of an internal error."));
            }
            finally
            {
                timer.Dispose();
                await DisposeClientAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Success keeps the timer and therefore the cadence. A failure restarts the timer with the backoff delay,
        /// which counts from now (the end of the poll) and drops a tick that became due during a slow poll.
        /// </summary>
        private PeriodicTimer Reschedule(PeriodicTimer timer, bool reachable)
        {
            bool wasFailing = _consecutiveFailures > 0;
            _consecutiveFailures = reachable ? 0 : _consecutiveFailures + 1;
            if (reachable && !wasFailing)
            {
                return timer;
            }

            timer.Dispose();
            return new PeriodicTimer(reachable ? _interval : BackoffDelay(), _monitor._timeProvider);
        }

        private async Task<PrinterSnapshot> PollAsync(PollTimeout timeout, CancellationToken stopToken)
        {
            if (_abandonedPoll is { IsCompleted: false })
            {
                // Never call a client concurrently with itself: wait until it finishes the request it ignored the timeout for.
                return Unavailable(PrinterState.Offline, _timeoutMessage);
            }

            _abandonedPoll = null;
            IPrinterClient? client = _client ?? CreateClient();
            if (client is null)
            {
                return Unavailable(PrinterState.Unknown, "No client is available for this printer.");
            }

            CancellationToken pollToken = timeout.Start();
            Task<PrinterSnapshot>? poll = null;
            try
            {
                poll = client.GetSnapshotAsync(pollToken);
                // WaitAsync: a client that ignores cancellation must not stall the loop.
                PrinterSnapshot? snapshot = await poll.WaitAsync(pollToken).ConfigureAwait(false);
                return Normalize(snapshot);
            }
            catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
            {
                // The client is disposed right after this; log a late failure of a call that ignored the stop.
                if (poll is { IsCompleted: false })
                {
                    LogLateFault(poll);
                }

                throw;
            }
            catch (OperationCanceledException) when (pollToken.IsCancellationRequested)
            {
                await AbandonIfStillRunningAsync(poll).ConfigureAwait(false);
                return Unavailable(PrinterState.Offline, _timeoutMessage);
            }
            catch (Exception ex)
            {
                _monitor.LogPollFailed(ex, _printer.Id);
                // Exception texts may contain hosts or URLs; the UI gets only the type, details go to the log.
                return Unavailable(PrinterState.Unknown, $"Polling failed ({ex.GetType().Name}).");
            }
            finally
            {
                timeout.Stop();
            }
        }

        /// <summary>
        /// Asks the client for the model within the poll timeout. A definitive answer (a code, or none reported) ends the
        /// asking for this run; a failure leaves another attempt for the next successful poll, up to
        /// <see cref="MaxModelDetectionAttempts"/>. Never throws except for the stop of the run.
        /// </summary>
        private async Task DetectModelAsync(IPrinterModelSource source, PollTimeout timeout, CancellationToken stopToken)
        {
            if (_abandonedPoll is { IsCompleted: false })
            {
                return;
            }

            _modelAttemptsLeft--;
            CancellationToken requestToken = timeout.Start();
            Task<PrinterModelDetection>? request = null;
            try
            {
                request = source.DetectModelAsync(requestToken);
                PrinterModelDetection detection = await request.WaitAsync(requestToken).ConfigureAwait(false);
                if (detection.Status == PrinterModelDetectionStatus.Failed)
                {
                    return;
                }

                _modelAttemptsLeft = 0;
                if (detection is { Status: PrinterModelDetectionStatus.Detected, Code: { } code } && !stopToken.IsCancellationRequested)
                {
                    _monitor.PublishModel(new PrinterModelDetected(_printer.Id, code));
                }
            }
            catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
            {
                if (request is { IsCompleted: false })
                {
                    LogLateFault(request);
                }

                throw;
            }
            catch (OperationCanceledException) when (requestToken.IsCancellationRequested)
            {
                await AbandonIfStillRunningAsync(request).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _monitor.LogModelDetectionFailed(ex, _printer.Id);
            }
            finally
            {
                timeout.Stop();
            }
        }

        /// <summary>
        /// A client that honours cancellation finishes right after the timeout; only one that does not finish within
        /// a short grace period (real time: it covers thread scheduling, not printer behaviour) is treated as ignoring it.
        /// </summary>
        private async Task AbandonIfStillRunningAsync(Task? poll)
        {
            if (poll is null || poll.IsCompleted)
            {
                return;
            }

            // Waits for completion only; the poll's own outcome (cancelled or faulted) and the grace timeout are irrelevant here.
            await poll.WaitAsync(AbandonGracePeriod).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            if (!poll.IsCompleted)
            {
                _abandonedPoll = poll;
                LogLateFault(poll);
            }
        }

        /// <summary>Observes a late failure, so that it is logged instead of becoming an unobserved task exception.</summary>
        private void LogLateFault(Task poll) =>
            _ = poll.ContinueWith(
                static (task, state) =>
                {
                    PrinterPollLoop loop = (PrinterPollLoop)state!;
                    loop._monitor.LogAbandonedPollFailed(task.Exception!.GetBaseException(), loop._printer.Id);
                },
                this,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

        private IPrinterClient? CreateClient()
        {
            try
            {
                return _client = _monitor._clientFactory.Create(_printer);
            }
            catch (Exception ex)
            {
                _monitor.LogClientCreationFailed(ex, _printer.Id);
                return null;
            }
        }

        private PrinterSnapshot Normalize(PrinterSnapshot? snapshot) => snapshot switch
        {
            null => Unavailable(PrinterState.Unknown, "The printer client returned no data."),
            _ when !string.Equals(snapshot.PrinterId, _printer.Id, StringComparison.Ordinal) => snapshot with { PrinterId = _printer.Id },
            _ => snapshot,
        };

        private PrinterSnapshot Unavailable(PrinterState state, string message) =>
            PrinterSnapshot.Unavailable(_printer.Id, state, _monitor._timeProvider.GetUtcNow(), message);

        /// <summary>Interval, doubled for every consecutive failure after the first, capped at the maximum delay.</summary>
        private TimeSpan BackoffDelay()
        {
            long ticks = _interval.Ticks;
            for (int failure = 1; failure < _consecutiveFailures && ticks < _maxDelay.Ticks; failure++)
            {
                ticks *= 2;
            }

            return ticks < _maxDelay.Ticks ? TimeSpan.FromTicks(ticks) : _maxDelay;
        }

        private async Task DisposeClientAsync()
        {
            try
            {
                switch (_client)
                {
                    case IAsyncDisposable asyncDisposable:
                        await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                        break;
                    case IDisposable disposable:
                        disposable.Dispose();
                        break;
                }
            }
            catch (Exception ex)
            {
                _monitor.LogClientDisposeFailed(ex, _printer.Id);
            }
        }
    }

    /// <summary>
    /// Reusable per-poll timeout driven by <see cref="TimeProvider"/> and linked to the stop token.
    /// The underlying source is replaced only after it was actually cancelled.
    /// </summary>
    private sealed class PollTimeout : IDisposable
    {
        private readonly TimeProvider _timeProvider;
        private readonly TimeSpan _timeout;
        private readonly CancellationToken _stopToken;
        private CancellationTokenSource _source;
        private CancellationTokenRegistration _stopRegistration;

        public PollTimeout(TimeProvider timeProvider, TimeSpan timeout, CancellationToken stopToken)
        {
            _timeProvider = timeProvider;
            _timeout = timeout;
            _stopToken = stopToken;
            _source = CreateSource();
        }

        public CancellationToken Start()
        {
            // A timer callback racing with the previous Stop() may have cancelled the idle source.
            if (_source.IsCancellationRequested && !_stopToken.IsCancellationRequested)
            {
                Replace();
            }

            _source.CancelAfter(_timeout);
            return _source.Token;
        }

        public void Stop()
        {
            if (_source.IsCancellationRequested)
            {
                if (!_stopToken.IsCancellationRequested)
                {
                    Replace();
                }
            }
            else if (!_source.TryReset())
            {
                if (ReferenceEquals(_timeProvider, TimeProvider.System))
                {
                    // For System a failed TryReset means the timeout callback is already queued; it could cancel the next poll.
                    Replace();
                }
                else
                {
                    // TryReset never succeeds for other providers; their timer is disarmed explicitly.
                    _source.CancelAfter(Timeout.InfiniteTimeSpan);
                }
            }
        }

        public void Dispose()
        {
            _stopRegistration.Dispose();
            _source.Dispose();
        }

        private void Replace()
        {
            Dispose();
            _source = CreateSource();
        }

        private CancellationTokenSource CreateSource()
        {
            // An infinite delay keeps timeProvider for later CancelAfter calls without arming a timer.
            CancellationTokenSource source = new(Timeout.InfiniteTimeSpan, _timeProvider);
            _stopRegistration = _stopToken.UnsafeRegister(static state => ((CancellationTokenSource)state!).Cancel(), source);
            return source;
        }
    }
}
