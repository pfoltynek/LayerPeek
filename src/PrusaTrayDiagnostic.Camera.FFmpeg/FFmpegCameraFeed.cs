using System.Threading.Tasks.Sources;

using PrusaTrayDiagnostic.Core.Cameras;

namespace PrusaTrayDiagnostic.Camera.FFmpeg;

/// <summary>
/// One FFmpeg connection (see <see cref="ICameraFeed"/>). Nothing is opened before the first <c>MoveNextAsync</c>.
/// </summary>
/// <remarks>
/// <para>
/// FFmpeg blocks, so every native call runs on a dedicated decoder thread owned by the enumerator; the caller's thread
/// only hands work over. Decoding is strictly step by step: the thread decodes one frame per <c>MoveNextAsync</c> into
/// its reused buffer and then waits, so a borrowed frame is never overwritten while the consumer holds it.
/// </para>
/// <para>
/// The continuation of <c>MoveNextAsync</c> runs inline on the decoder thread (unless the awaiter captured a
/// synchronization context); the decoder is idle at that time, and this keeps the steady state free of allocations.
/// </para>
/// <para>
/// Cancelling a token or disposing the feed (or the enumerator) raises FFmpeg's interrupt flag, so connecting and
/// reading give up within FFmpeg's polling interval (~100 ms). A pending <c>MoveNextAsync</c> then throws
/// <see cref="OperationCanceledException"/> (token) or <see cref="ObjectDisposedException"/> (dispose).
/// Disposing waits until the decoder thread has released every native resource.
/// </para>
/// </remarks>
internal sealed class FFmpegCameraFeed : ICameraFeed
{
    private readonly string _url;
    private readonly bool _isNetwork;
    private readonly TimeSpan _ioTimeout;
    private readonly Lock _lock = new();
    private bool _enumerated;
    private bool _disposed;
    private FrameReader? _reader;

    public FFmpegCameraFeed(string url, bool isNetwork, TimeSpan ioTimeout)
    {
        _url = url;
        _isNetwork = isNetwork;
        _ioTimeout = ioTimeout;
    }

    /// <summary>Native FFmpeg objects are currently allocated (for tests).</summary>
    internal bool HoldsNativeResources => Volatile.Read(ref _reader)?.HoldsNativeResources ?? false;

    public IAsyncEnumerable<CameraFrame> ReadFramesAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_enumerated)
            {
                throw new InvalidOperationException("A camera feed can be enumerated only once.");
            }

            _enumerated = true;
        }

        return new FrameEnumerable(this, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        FrameReader? reader;
        lock (_lock)
        {
            _disposed = true;
            reader = _reader;
        }

        return reader?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    private FrameReader CreateReader(CancellationToken feedToken, CancellationToken enumeratorToken)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_reader is not null)
            {
                throw new InvalidOperationException("A camera feed can be enumerated only once.");
            }

            FrameReader reader = new(_url, _isNetwork, _ioTimeout, feedToken, enumeratorToken);
            Volatile.Write(ref _reader, reader);
            return reader;
        }
    }

    private sealed class FrameEnumerable(FFmpegCameraFeed owner, CancellationToken feedToken) : IAsyncEnumerable<CameraFrame>
    {
        public IAsyncEnumerator<CameraFrame> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            owner.CreateReader(feedToken, cancellationToken);
    }

    /// <summary>The enumerator; owns the decoder thread and, through it, the native decoder.</summary>
    private sealed class FrameReader : IAsyncEnumerator<CameraFrame>, IValueTaskSource<bool>
    {
        private const int DecoderThreadStackSize = 4 * 1024 * 1024;

        private readonly FFmpegDecoder _decoder;
        private readonly InterruptFlag _interrupt;
        private readonly CancellationTokenSource? _linked;
        private readonly CancellationToken _token;
        private readonly CancellationTokenRegistration _registration;
        private readonly SemaphoreSlim _request = new(0);
        private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Lock _lock = new();

        // Inline continuations: see the remarks of FFmpegCameraFeed.
        private ManualResetValueTaskSourceCore<bool> _source;
        private CameraFrame _current;
        private Thread? _thread;

        // Guarded by _lock.
        private bool _pending;
        private bool _stopping;
        private bool _threadExited;

        // Written by the decoder thread before it completes a step, read by the consumer after awaiting it.
        private volatile bool _finished;

        public FrameReader(string url, bool isNetwork, TimeSpan ioTimeout, CancellationToken feedToken, CancellationToken enumeratorToken)
        {
            _interrupt = new InterruptFlag();
            _decoder = new FFmpegDecoder(url, isNetwork, ioTimeout, _interrupt);
            if (feedToken.CanBeCanceled && enumeratorToken.CanBeCanceled && feedToken != enumeratorToken)
            {
                _linked = CancellationTokenSource.CreateLinkedTokenSource(feedToken, enumeratorToken);
                _token = _linked.Token;
            }
            else
            {
                _token = feedToken.CanBeCanceled ? feedToken : enumeratorToken;
            }

            _registration = _token.UnsafeRegister(static state => ((InterruptFlag)state!).Set(), _interrupt);
        }

        public CameraFrame Current => _current;

        public bool HoldsNativeResources => _decoder.HoldsNativeResources;

        public ValueTask<bool> MoveNextAsync()
        {
            lock (_lock)
            {
                if (_stopping)
                {
                    return ValueTask.FromException<bool>(new ObjectDisposedException(nameof(FFmpegCameraFeed)));
                }

                if (_pending)
                {
                    throw new InvalidOperationException("The previous MoveNextAsync has not completed yet.");
                }

                if (_finished || _threadExited)
                {
                    return new ValueTask<bool>(false);
                }

                if (_token.IsCancellationRequested)
                {
                    return ValueTask.FromCanceled<bool>(_token);
                }

                _pending = true;
                _source.Reset();
                if (_thread is null)
                {
                    _thread = new Thread(static state => ((FrameReader)state!).Run(), DecoderThreadStackSize)
                    {
                        IsBackground = true,
                        Name = "Camera decoder",
                    };
                    _thread.Start(this);
                }

                _request.Release();
                return new ValueTask<bool>(this, _source.Version);
            }
        }

        public ValueTask DisposeAsync()
        {
            lock (_lock)
            {
                if (!_stopping)
                {
                    _stopping = true;
                    _interrupt.Set();
                    if (_thread is null)
                    {
                        _threadExited = true;
                        Cleanup();
                    }
                    else if (!_threadExited)
                    {
                        _request.Release();
                    }
                }
            }

            return new ValueTask(_exited.Task);
        }

        bool IValueTaskSource<bool>.GetResult(short token) => _source.GetResult(token);

        ValueTaskSourceStatus IValueTaskSource<bool>.GetStatus(short token) => _source.GetStatus(token);

        void IValueTaskSource<bool>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
            _source.OnCompleted(continuation, state, token, flags);

        /// <summary>Decoder thread: one step per request until the stream ends, fails, or the reader is disposed.</summary>
        private void Run()
        {
            bool opened = false;
            try
            {
                while (true)
                {
                    _request.Wait();
                    lock (_lock)
                    {
                        if (_stopping)
                        {
                            break;
                        }
                    }

                    bool hasFrame;
                    try
                    {
                        if (!opened)
                        {
                            opened = true;
                            _decoder.Open();
                        }

                        hasFrame = _decoder.TryReadFrame(out _current);
                    }
                    catch (Exception ex)
                    {
                        Finish();
                        Complete(Translate(ex));
                        break;
                    }

                    if (!hasFrame)
                    {
                        Finish();
                        Complete(null, hasFrame: false);
                        break;
                    }

                    Complete(null, hasFrame: true);
                }
            }
            finally
            {
                bool failPending;
                lock (_lock)
                {
                    _threadExited = true;
                    failPending = _pending;
                }

                _decoder.Dispose();
                if (failPending)
                {
                    // Disposed before the decoder thread picked up the request.
                    Complete(new ObjectDisposedException(nameof(FFmpegCameraFeed)));
                }

                Cleanup();
            }
        }

        /// <summary>The enumeration is over: release native resources before the consumer learns about it.</summary>
        private void Finish()
        {
            _decoder.Dispose();
            _finished = true;
        }

        private void Complete(Exception? error, bool hasFrame = false)
        {
            lock (_lock)
            {
                _pending = false;
            }

            if (error is null)
            {
                _source.SetResult(hasFrame);
            }
            else
            {
                _source.SetException(error);
            }
        }

        private Exception Translate(Exception exception)
        {
            bool stopping;
            lock (_lock)
            {
                stopping = _stopping;
            }

            if (stopping)
            {
                return new ObjectDisposedException(nameof(FFmpegCameraFeed));
            }

            if (_token.IsCancellationRequested)
            {
                return new OperationCanceledException(_token);
            }

            return exception is CameraFeedException
                ? exception
                : new CameraFeedException($"The video decoder failed ({exception.GetType().Name}).", exception);
        }

        private void Cleanup()
        {
            _registration.Dispose();
            _linked?.Dispose();
            _exited.TrySetResult();
        }
    }
}
