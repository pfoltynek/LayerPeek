using System.Runtime.CompilerServices;

using Microsoft.Extensions.Logging;

namespace PrusaTrayDiagnostic.Core.Cameras;

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
            $"The camera sent no image within {_options.ConnectTimeout.TotalSeconds:0.#} s.");
        _frameTimedOut = new CameraStatus(
            CameraConnectionState.NoSignal,
            $"The camera stopped sending images (none within {_options.FrameTimeout.TotalSeconds:0.#} s).");
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
                    failure = TryOpen(streamUri, address, attempt.Token, out stream, out frames);
                    bool streaming = false;
                    while (frames is not null)
                    {
                        failure = await ReadNextAsync(frames, watchdog, streaming, address, stopToken).ConfigureAwait(false);
                        if (failure is not null)
                        {
                            break;
                        }

                        if (!streaming)
                        {
                            streaming = true;
                            status.Report(CameraStatus.Streaming);
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
            LogOpenFailed(ex, address);
            return Failed(ex);
        }
    }

    /// <returns><see langword="null"/> when a frame is available in <see cref="IAsyncEnumerator{T}.Current"/>, otherwise why not.</returns>
    /// <remarks>
    /// Not an async method: a frame the stream has ready is handled synchronously without any state machine,
    /// only a pending read goes through <see cref="AwaitNextAsync"/>.
    /// </remarks>
    private ValueTask<CameraStatus?> ReadNextAsync(
        IAsyncEnumerator<CameraFrame> frames,
        Watchdog watchdog,
        bool streaming,
        string address,
        CancellationToken stopToken)
    {
        // Armed only while waiting for the stream: the consumer's time between frames is not counted.
        watchdog.Arm(streaming ? _options.FrameTimeout : _options.ConnectTimeout);
        ValueTask<bool> move;
        try
        {
            move = frames.MoveNextAsync();
        }
        catch (Exception ex)
        {
            watchdog.Disarm();
            return new ValueTask<CameraStatus?>(ReadFailed(ex, watchdog, streaming, address, stopToken));
        }

        if (!move.IsCompletedSuccessfully)
        {
            return AwaitNextAsync(move, watchdog, streaming, address, stopToken);
        }

        watchdog.Disarm();
        return new ValueTask<CameraStatus?>(move.Result ? null : Ended(address));
    }

    private async ValueTask<CameraStatus?> AwaitNextAsync(
        ValueTask<bool> move,
        Watchdog watchdog,
        bool streaming,
        string address,
        CancellationToken stopToken)
    {
        try
        {
            return await move.ConfigureAwait(false) ? null : Ended(address);
        }
        catch (Exception ex)
        {
            return ReadFailed(ex, watchdog, streaming, address, stopToken);
        }
        finally
        {
            watchdog.Disarm();
        }
    }

    private CameraStatus Ended(string address)
    {
        LogStreamEnded(address);
        return StreamEnded;
    }

    private CameraStatus ReadFailed(Exception exception, Watchdog watchdog, bool streaming, string address, CancellationToken stopToken)
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

        LogStreamFailed(exception, address);
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
    }

    /// <summary>
    /// Cancels the current attempt when the stream does not deliver a frame in time. One timer per enumeration,
    /// re-armed without allocation for every frame.
    /// </summary>
    private sealed class Watchdog : IDisposable
    {
        private readonly ITimer _timer;
        private CancellationTokenSource? _attempt;
        private volatile bool _fired;

        public Watchdog(TimeProvider timeProvider)
        {
            _timer = timeProvider.CreateTimer(static state => ((Watchdog)state!).Fire(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        /// <summary>The current attempt was cancelled because of a timeout.</summary>
        public bool HasFired => _fired;

        public void Attach(CancellationTokenSource attempt)
        {
            _fired = false;
            Volatile.Write(ref _attempt, attempt);
        }

        public void Detach()
        {
            Disarm();
            Volatile.Write(ref _attempt, null);
        }

        public void Arm(TimeSpan timeout) => _timer.Change(timeout, Timeout.InfiniteTimeSpan);

        public void Disarm() => _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        public void Dispose() => _timer.Dispose();

        private void Fire()
        {
            if (Volatile.Read(ref _attempt) is not { } attempt)
            {
                return;
            }

            _fired = true;
            try
            {
                attempt.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The attempt ended meanwhile; nothing to cancel.
            }
        }
    }
}
