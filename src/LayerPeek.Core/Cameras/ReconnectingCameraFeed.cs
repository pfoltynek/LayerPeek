using System.Globalization;
using System.Runtime.CompilerServices;

using Microsoft.Extensions.Logging;

namespace LayerPeek.Core.Cameras;

/// <summary>
/// Keeps a camera stream running: opens it through an <see cref="ICameraFeedFactory"/>, forwards its frames,
/// detects a stalled stream and reconnects after <see cref="CameraFeedOptions.ReconnectDelay"/> whenever an attempt
/// fails or ends, until cancelled. Stateless: concurrent enumerations are independent connections.
/// </summary>
/// <remarks>
/// Status rules: <see cref="CameraConnectionState.Connecting"/> at the start of every attempt,
/// <see cref="CameraConnectionState.Streaming"/> on its first frame, <see cref="CameraConnectionState.NoSignal"/> when no
/// frame arrives in time or the camera ends the stream, <see cref="CameraConnectionState.Error"/> when creating, opening
/// or reading the stream throws. Forwarding a frame allocates nothing.
/// </remarks>
public sealed partial class ReconnectingCameraFeed
{
    private static readonly CameraStatus StreamEnded = new(CameraConnectionState.NoSignal, "The camera ended the stream.");

    private readonly ICameraFeedFactory _factory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReconnectingCameraFeed> _logger;
    private readonly CameraFeedOptions _options;
    private readonly CameraStatus _connectTimedOut;
    private readonly CameraStatus _frameTimedOut;

    public ReconnectingCameraFeed(
        ICameraFeedFactory factory,
        TimeProvider timeProvider,
        ILogger<ReconnectingCameraFeed> logger,
        CameraFeedOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _factory = factory;
        _timeProvider = timeProvider;
        _logger = logger;
        _options = options ?? CameraFeedOptions.Default;
        _connectTimedOut = new CameraStatus(
            CameraConnectionState.NoSignal,
            string.Create(CultureInfo.InvariantCulture, $"The camera sent no image within {_options.ConnectTimeout.TotalSeconds:0.#} s."));
        _frameTimedOut = new CameraStatus(
            CameraConnectionState.NoSignal,
            string.Create(CultureInfo.InvariantCulture, $"The camera stopped sending images (none within {_options.FrameTimeout.TotalSeconds:0.#} s)."));
    }

    /// <summary>
    /// Streams frames from <paramref name="streamUri"/> until <paramref name="cancellationToken"/> is cancelled; then the
    /// enumeration completes without an exception and the connection is closed. Failures never end the enumeration,
    /// they are reported through <paramref name="status"/> and followed by a reconnect.
    /// </summary>
    /// <param name="streamUri">Absolute stream address. It is passed to the factory as is; logs contain it redacted.</param>
    /// <param name="status">
    /// Receives status changes (consecutive duplicates are suppressed), synchronously on the streaming thread;
    /// use <see cref="Progress{T}"/> to get them on the UI thread. Exceptions thrown by it are logged and ignored.
    /// </param>
    /// <param name="cancellationToken">Stops streaming.</param>
    /// <returns>Borrowed frames, valid until the enumerator advances (see <see cref="CameraFrame"/>).</returns>
    public IAsyncEnumerable<CameraFrame> ReadFramesAsync(Uri streamUri, IProgress<CameraStatus>? status, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(streamUri);
        if (!streamUri.IsAbsoluteUri)
        {
            throw new ArgumentException("The stream address must be absolute.", nameof(streamUri));
        }

        return ReadCoreAsync(streamUri, status, cancellationToken);
    }

    private async IAsyncEnumerable<CameraFrame> ReadCoreAsync(
        Uri streamUri,
        IProgress<CameraStatus>? progress,
        [EnumeratorCancellation] CancellationToken stopToken)
    {
        string address = UriRedaction.Redact(streamUri);
        StatusReporter status = new(this, progress, address);
        using Watchdog watchdog = new(_timeProvider);

        while (!stopToken.IsCancellationRequested)
        {
            status.Report(CameraStatus.Connecting);
            CameraStatus? failure;
            ICameraFeed? stream = null;
            IAsyncEnumerator<CameraFrame>? frames = null;
            using (CancellationTokenSource attempt = CancellationTokenSource.CreateLinkedTokenSource(stopToken))
            {
                watchdog.Attach(attempt);
                try
                {
                    failure = TryOpen(streamUri, address, status, attempt.Token, out stream, out frames);
                    bool streaming = false;
                    while (frames is not null)
                    {
                        // Armed only while waiting for the stream: the consumer's time between frames is not counted.
                        watchdog.Arm(streaming ? _options.FrameTimeout : _options.ConnectTimeout);
                        bool moved;
                        try
                        {
                            // Awaited right here: the iterator's own state machine is reused for every frame, so a read
                            // that completes asynchronously (the normal case with a real decoder) allocates nothing.
                            moved = await frames.MoveNextAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            failure = ReadFailed(ex, watchdog, streaming, address, status, stopToken);
                            break;
                        }
                        finally
                        {
                            watchdog.Disarm();
                        }

                        if (!moved)
                        {
                            failure = Ended(address);
                            break;
                        }

                        if (!streaming)
                        {
                            streaming = true;
                            status.Report(CameraStatus.Streaming);
                            status.ClearFailures();
                        }

                        yield return frames.Current;
                    }
                }
                finally
                {
                    watchdog.Detach();
                    await CloseAsync(frames, stream, address).ConfigureAwait(false);
                }
            }

            if (stopToken.IsCancellationRequested)
            {
                break;
            }

            // The delay is scheduled before the failure is reported, so whoever observes the status sees the retry planned.
            Task delay = Task.Delay(_options.ReconnectDelay, _timeProvider, stopToken);
            status.Report(failure ?? StreamEnded);
            try
            {
                await delay.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <returns><see langword="null"/> when the stream was opened, otherwise the failure status.</returns>
    private CameraStatus? TryOpen(
        Uri streamUri,
        string address,
        StatusReporter status,
        CancellationToken attemptToken,
        out ICameraFeed? stream,
        out IAsyncEnumerator<CameraFrame>? frames)
    {
        stream = null;
        frames = null;
        try
        {
            stream = _factory.Create(streamUri) ?? throw new CameraFeedException("No camera stream is available.");
            frames = stream.ReadFramesAsync(attemptToken).GetAsyncEnumerator(attemptToken);
            return null;
        }
        catch (Exception ex)
        {
            LogFailure(ex, address, status, opening: true);
            return Failed(ex);
        }
    }

    /// <summary>
    /// The full exception once, then a short line while the same failure repeats (an unreachable camera fails on every
    /// attempt); a different failure or a frame in between starts over.
    /// </summary>
    private void LogFailure(Exception exception, string address, StatusReporter status, bool opening)
    {
        if (status.IsRepeatedFailure(exception))
        {
            LogFailedAgain(address, Failed(exception).Reason);
        }
        else if (opening)
        {
            LogOpenFailed(exception, address);
        }
        else
        {
            LogStreamFailed(exception, address);
        }
    }

    private CameraStatus Ended(string address)
    {
        LogStreamEnded(address);
        return StreamEnded;
    }

    private CameraStatus ReadFailed(Exception exception, Watchdog watchdog, bool streaming, string address, StatusReporter status, CancellationToken stopToken)
    {
        if (exception is OperationCanceledException)
        {
            if (stopToken.IsCancellationRequested)
            {
                // Stopping; the caller checks the stop token and reports nothing.
                return StreamEnded;
            }

            if (watchdog.HasFired)
            {
                TimeSpan timeout = streaming ? _options.FrameTimeout : _options.ConnectTimeout;
                LogNoFrame(address, timeout.TotalSeconds);
                return streaming ? _frameTimedOut : _connectTimedOut;
            }
        }

        LogFailure(exception, address, status, opening: false);
        return Failed(exception);
    }

    private async Task CloseAsync(IAsyncEnumerator<CameraFrame>? frames, ICameraFeed? stream, string address)
    {
        try
        {
            if (frames is not null)
            {
                await frames.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            LogCloseFailed(ex, address);
        }

        try
        {
            if (stream is not null)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            LogCloseFailed(ex, address);
        }
    }

    // Messages of other exceptions may contain addresses or credentials; the status gets only the type, the log the details.
    private static CameraStatus Failed(Exception exception) => new(
        CameraConnectionState.Error,
        exception is CameraFeedException ? exception.Message : $"The camera stream failed ({exception.GetType().Name}).");

    [LoggerMessage(Level = LogLevel.Warning, Message = "Opening camera stream '{Address}' failed.")]
    private partial void LogOpenFailed(Exception exception, string address);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Camera stream '{Address}' failed.")]
    private partial void LogStreamFailed(Exception exception, string address);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Camera stream '{Address}' failed again: {Reason}")]
    private partial void LogFailedAgain(string address, string? reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Camera stream '{Address}' ended.")]
    private partial void LogStreamEnded(string address);

    [LoggerMessage(Level = LogLevel.Information, Message = "Camera stream '{Address}' sent no frame within {TimeoutSeconds} s.")]
    private partial void LogNoFrame(string address, double timeoutSeconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "Closing camera stream '{Address}' failed.")]
    private partial void LogCloseFailed(Exception exception, string address);

    [LoggerMessage(Level = LogLevel.Error, Message = "A status observer of camera stream '{Address}' failed.")]
    private partial void LogStatusObserverFailed(Exception exception, string address);

    /// <summary>Forwards status changes, suppressing consecutive duplicates and isolating observer failures.</summary>
    private sealed class StatusReporter(ReconnectingCameraFeed owner, IProgress<CameraStatus>? progress, string address)
    {
        private CameraStatus? _last;
        private string? _lastFailure;

        public void Report(CameraStatus status)
        {
            if (status == _last)
            {
                return;
            }

            _last = status;
            try
            {
                progress?.Report(status);
            }
            catch (Exception ex)
            {
                owner.LogStatusObserverFailed(ex, address);
            }
        }

        /// <summary>Whether <paramref name="exception"/> repeats the previous failure (same type and message).</summary>
        public bool IsRepeatedFailure(Exception exception)
        {
            string key = string.Concat(exception.GetType().FullName, "\n", exception.Message);
            bool repeated = string.Equals(key, _lastFailure, StringComparison.Ordinal);
            _lastFailure = key;
            return repeated;
        }

        /// <summary>Frames arrived: the next failure is a new one.</summary>
        public void ClearFailures() => _lastFailure = null;
    }

    /// <summary>
    /// Cancels the current attempt when the stream does not deliver a frame in time. One timer per enumeration,
    /// re-armed without allocation for every frame.
    /// </summary>
    /// <remarks>
    /// The timer only wakes the watchdog up; whether a timeout happened is decided from the arming record: the attempt
    /// it was armed for and the time it was armed at (<see cref="TimeProvider.GetTimestamp"/>). So a callback that was
    /// already running when a frame arrived (disarmed meanwhile) or that belongs to an earlier arming (deadline not
    /// reached yet) changes nothing; an early one re-arms the timer for the rest of the time. The record is read and
    /// written under one lock (allocated once), so <see cref="Fire"/> never sees a half-updated record and never cancels
    /// for an arming that was replaced meanwhile.
    /// </remarks>
    private sealed class Watchdog : IDisposable
    {
        private readonly TimeProvider _timeProvider;
        private readonly ITimer _timer;
        private readonly Lock _lock = new();

        // Guarded by _lock.
        private CancellationTokenSource? _attempt;
        private CancellationTokenSource? _armedFor;
        private long _armedAt;
        private TimeSpan _timeout;

        // The attempt the watchdog cancelled, so that HasFired never applies to a later attempt. Guarded by _lock.
        private CancellationTokenSource? _firedFor;

        public Watchdog(TimeProvider timeProvider)
        {
            _timeProvider = timeProvider;
            _timer = timeProvider.CreateTimer(static state => ((Watchdog)state!).Fire(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        /// <summary>The current attempt was cancelled because of a timeout.</summary>
        public bool HasFired
        {
            get
            {
                lock (_lock)
                {
                    return _attempt is not null && ReferenceEquals(_firedFor, _attempt);
                }
            }
        }

        public void Attach(CancellationTokenSource attempt)
        {
            lock (_lock)
            {
                _attempt = attempt;
            }
        }

        public void Detach()
        {
            lock (_lock)
            {
                DisarmCore();
                _attempt = null;
            }
        }

        public void Arm(TimeSpan timeout)
        {
            lock (_lock)
            {
                _timeout = timeout;
                _armedAt = _timeProvider.GetTimestamp();
                _armedFor = _attempt;
                _timer.Change(timeout, Timeout.InfiniteTimeSpan);
            }
        }

        public void Disarm()
        {
            lock (_lock)
            {
                DisarmCore();
            }
        }

        public void Dispose() => _timer.Dispose();

        private void DisarmCore()
        {
            _armedFor = null;
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        private void Fire()
        {
            CancellationTokenSource attempt;
            lock (_lock)
            {
                if (_attempt is null || !ReferenceEquals(_armedFor, _attempt))
                {
                    // Disarmed meanwhile (a frame arrived) or armed for another attempt.
                    return;
                }

                TimeSpan remaining = _timeout - _timeProvider.GetElapsedTime(_armedAt);
                if (remaining > TimeSpan.Zero)
                {
                    // From an earlier arming, or the timer ran a little early: wait for the rest.
                    _timer.Change(remaining, Timeout.InfiniteTimeSpan);
                    return;
                }

                // Marked before cancelling: whoever sees the cancellation must also see why. Disarmed, so that a
                // second callback cannot cancel again.
                attempt = _attempt;
                _firedFor = attempt;
                _armedFor = null;
            }

            try
            {
                // Outside the lock: cancellation runs the token's callbacks synchronously.
                attempt.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The attempt ended meanwhile; nothing to cancel.
            }
        }
    }
}
