using System.Runtime.CompilerServices;
using System.Threading.Tasks.Sources;

namespace LayerPeek.Core.Cameras;

/// <summary>Why a <see cref="CameraStreamSubscription"/> ended.</summary>
public enum CameraStreamEndReason
{
    /// <summary>Its owner disposed it.</summary>
    Disposed,

    /// <summary>The printer has no enabled camera any more (camera switched off, printer removed), or never had one.</summary>
    CameraUnavailable,

    /// <summary>The hub was disposed (the application quits).</summary>
    HubStopped,

    /// <summary>
    /// The subscriber's own callback threw (see <see cref="CameraStreamSubscription.Failure"/>), or the hub's stream loop
    /// failed unexpectedly. The connection goes on for the other subscribers.
    /// </summary>
    Failed,

    /// <summary>A packet subscriber did not keep up and its queue overflowed (<see cref="CameraPacketSubscriptionOptions.QueueCapacity"/>).</summary>
    Overflow,
}

/// <summary>
/// A lease on the shared connection of one printer's camera (<see cref="CameraStreamHub"/>). While it is not disposed the
/// hub keeps the connection open; it receives the connection status and, depending on the kind, decoded frames
/// (<see cref="CameraFrameSubscription"/>) or encoded packets (<see cref="CameraPacketSubscription"/>).
/// </summary>
public abstract class CameraStreamSubscription : IDisposable, IAsyncDisposable
{
    private readonly CameraStreamHub? _hub;
    private readonly IProgress<CameraStatus>? _statusObserver;
    private readonly Action<CameraStreamSubscription>? _endedObserver;
    private readonly TaskCompletionSource<CameraStreamEndReason> _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _statusGate = new();

    // Guarded by _statusGate.
    private CameraStatus? _reported;
    private int _ending;

    private protected CameraStreamSubscription(CameraStreamHub? hub, string printerId, IProgress<CameraStatus>? status, Action<CameraStreamSubscription>? ended)
    {
        _hub = hub;
        PrinterId = printerId;
        _statusObserver = status;
        _endedObserver = ended;
    }

    public string PrinterId { get; }

    /// <summary>The hub of the lease; <see langword="null"/> for one that never belonged to a hub.</summary>
    private protected CameraStreamHub? Hub => _hub;

    /// <summary>The last status reported to this subscriber; <see cref="CameraStatus.Connecting"/> before the first.</summary>
    public CameraStatus Status => Volatile.Read(ref _reported) ?? CameraStatus.Connecting;

    /// <summary>Completes (never faults) when the subscription ended, also when its owner disposed it.</summary>
    public Task<CameraStreamEndReason> Ended => _ended.Task;

    public bool IsEnded => Volatile.Read(ref _ending) != 0;

    /// <summary>Why the subscription ended; <see langword="null"/> while it runs. Set before the <c>ended</c> callback runs.</summary>
    public CameraStreamEndReason? EndReason { get; private set; }

    /// <summary>The exception of <see cref="CameraStreamEndReason.Failed"/>; <see langword="null"/> otherwise.</summary>
    public Exception? Failure { get; private set; }

    /// <summary>
    /// Ends the lease; the hub closes the connection once nobody needs it (after its linger time). Never blocks (closing
    /// happens in the background) and never throws.
    /// </summary>
    public void Dispose()
    {
        if (TryEnd(CameraStreamEndReason.Disposed, failure: null, notify: false))
        {
            _hub?.Unsubscribe(this);
        }

        GC.SuppressFinalize(this);
    }

    /// <inheritdoc cref="Dispose"/>
    public ValueTask DisposeAsync()
    {
        Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    /// <summary>Hub: the connection status changed. Streaming thread or subscriber thread; never throws.</summary>
    internal virtual void OnConnectionStatus(CameraStatus status) => Report(status);

    /// <summary>Reports <paramref name="status"/> unless it is the last one reported; observer failures are logged.</summary>
    private protected void Report(CameraStatus status)
    {
        lock (_statusGate)
        {
            if (_ending != 0 || status == _reported)
            {
                return;
            }

            Volatile.Write(ref _reported, status);
        }

        try
        {
            _statusObserver?.Report(status);
        }
        catch (Exception ex)
        {
            _hub?.OnStatusObserverFailed(this, ex);
        }
    }

    /// <summary>
    /// Ends the subscription once; the first caller wins. <paramref name="notify"/>: the hub ended it, so the owner's
    /// <c>ended</c> callback runs (synchronously, before the hub goes on).
    /// </summary>
    internal bool TryEnd(CameraStreamEndReason reason, Exception? failure, bool notify = true)
    {
        lock (_statusGate)
        {
            if (_ending != 0)
            {
                return false;
            }

            _ending = 1;
            Failure = failure;
            EndReason = reason;
        }

        OnEnded();
        if (notify && _endedObserver is { } observer)
        {
            try
            {
                observer(this);
            }
            catch (Exception ex)
            {
                _hub?.OnStatusObserverFailed(this, ex);
            }
        }

        _ended.TrySetResult(reason);
        return true;
    }

    /// <summary>Releases what the subscription holds; called once when it ends.</summary>
    private protected virtual void OnEnded()
    {
    }

    /// <summary>A subscription that ended at once (no camera, or the hub is stopped).</summary>
    internal static T EndAtOnce<T>(T subscription, CameraStreamEndReason reason)
        where T : CameraStreamSubscription
    {
        subscription.TryEnd(reason, failure: null, notify: false);
        return subscription;
    }
}

/// <summary>Receives a borrowed frame on the streaming thread (see <see cref="CameraFrameSubscriptionOptions.FrameReceived"/>).</summary>
public delegate void CameraFrameHandler(in CameraFrame frame);

/// <summary>Options of <see cref="CameraStreamHub.SubscribeFrames"/>.</summary>
public sealed class CameraFrameSubscriptionOptions
{
    /// <summary>
    /// Called on the streaming thread when a new frame is ready to be taken (<see cref="CameraFrameSubscription.TakeLatest"/>)
    /// and the previous call has not been answered by taking it yet, so normally at most one call is pending. Must return
    /// quickly and never block (e.g. post to the UI thread). If it throws, the subscription ends with
    /// <see cref="CameraStreamEndReason.Failed"/>.
    /// </summary>
    public Action? FrameAvailable { get; init; }

    /// <summary>
    /// Receives status changes synchronously (streaming thread, or the subscribing thread for the first one); consecutive
    /// duplicates are suppressed. A connection that is streaming counts as <see cref="CameraConnectionState.Connecting"/>
    /// for this subscriber until its first frame arrived. Exceptions are logged and ignored.
    /// </summary>
    public IProgress<CameraStatus>? Status { get; init; }

    /// <summary>Called synchronously when the hub ends the subscription (not when its owner disposes it). Must not block.</summary>
    public Action<CameraStreamSubscription>? Ended { get; init; }

    /// <summary>
    /// Frames closer together than this are skipped (not copied), e.g. the sampling interval of print failure detection
    /// (T-153, 2–5 s); <see cref="TimeSpan.Zero"/>: every frame (camera window).
    /// </summary>
    public TimeSpan MinFrameInterval
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            field = value;
        }
    }

    /// <summary>
    /// Only key frames, at most one per <see cref="MinFrameInterval"/> (T-153: print failure detection and its alarm
    /// snapshot). This subscriber never makes the hub decode every frame: while no other subscriber of the printer needs
    /// every frame, the decoder decodes only the first key frame after the interval and nothing in between (the frames of
    /// the other key frames are not decoded either). While another subscriber (the camera window) has every frame decoded
    /// anyway, this one gets the first decoded frame after its interval, which need not be a key frame.
    /// </summary>
    public bool KeyFramesOnly { get; init; }

    /// <summary>
    /// 8-bit grayscale copies of this size instead of BGRA copies (T-153, see <see cref="CameraGrayscaleSize"/>); the feed
    /// scales them straight from the picture's luma (T-189, <see cref="CameraStreamItem.GrayscaleFrame"/>), so no BGRA frame
    /// in full resolution is converted for this subscriber. <see langword="null"/> (the default): BGRA as decoded. While
    /// subscribers of several sizes watch one camera, the feed converts one of them; the others are converted from full
    /// frames while copying, as for a feed that does not convert to grayscale.
    /// </summary>
    public CameraGrayscaleSize? Grayscale
    {
        get;
        init
        {
            if (value is { IsValid: false })
            {
                throw new ArgumentException("The grayscale size is empty.", nameof(value));
            }

            field = value;
        }
    }

    /// <summary>
    /// T-184: frames for display only, scaled down to fit this size in device pixels (aspect ratio kept, never scaled up;
    /// see <see cref="CameraFrameSize.Fit"/>); changed later with <see cref="CameraFrameSubscription.SetDisplaySize"/>.
    /// <see langword="null"/> (the default): frames in the camera's resolution. Snapshots, exports, recordings and print
    /// failure detection always use full resolution (user decision 2026-11-10; detection's grayscale samples are scaled
    /// from the full picture, never from a display frame, T-189); a display reader that needs one full
    /// frame takes a short lease of its own without this size. While several display readers watch one camera, each gets
    /// the frame of the largest size any of them wants (it may be larger than its own).
    /// </summary>
    public CameraFrameSize? DisplaySize { get; init; }

    /// <summary>
    /// T-185: at most this many frames per second (the mini preview, user decision 2026-11-11); changed later with
    /// <see cref="CameraFrameSubscription.SetMaxFrameRate"/>. <see langword="null"/> (the default): every frame. The frames
    /// in between are skipped before they are copied; for a display reader they are not even converted (the decoder still
    /// decodes every frame, the next ones depend on it). The pace follows a fixed grid of 1 / rate, so a source that is not a
    /// multiple of the rate still gets the rate on average; a frame up to a quarter of the interval early counts as on time
    /// (network jitter must not halve a rate close to the camera's).
    /// </summary>
    public int? MaxFrameRate
    {
        get;
        init
        {
            CameraFrameSubscription.ValidateFrameRate(value);
            field = value;
        }
    }

    /// <summary>
    /// T-184: hands every frame this subscriber takes directly to this callback, on the streaming thread, instead of
    /// copying it into the subscription's own buffers: the frame is borrowed and valid only during the call, so the
    /// callback copies it where it is needed (e.g. straight into the bitmap that is shown) and returns quickly.
    /// <see cref="CameraFrameSubscription.TakeLatest"/> then returns nothing and <see cref="FrameAvailable"/> is not called.
    /// Cannot be combined with <see cref="Grayscale"/>. If it throws, the subscription ends with
    /// <see cref="CameraStreamEndReason.Failed"/>.
    /// </summary>
    public CameraFrameHandler? FrameReceived { get; init; }

    /// <summary>
    /// T-188 (user decision 2026-11-11): the camera window opened or was restored. When this lease is taken while the
    /// connection waits between reconnect attempts, the next attempt starts at once and its backoff starts over. Off for
    /// print failure detection, recording and every other consumer: they never shorten the wait.
    /// </summary>
    public bool ShortensReconnectWait { get; init; }
}

/// <summary>
/// Decoded frames of the shared connection. Each frame is copied once into this subscriber's own buffers (triple
/// buffering, <see cref="CameraFrameExchange"/>), so a slow subscriber only drops frames and never slows the decoder or
/// the other subscribers. One consumer thread takes the frames.
/// </summary>
public sealed class CameraFrameSubscription : CameraStreamSubscription
{
    /// <summary><see cref="_lastPublished"/> before the first frame.</summary>
    private const long NeverPublished = long.MinValue;

    /// <summary><see cref="_nextPaced"/> while the next frame is taken whenever it comes.</summary>
    private const long NotPaced = long.MinValue;

    /// <summary>A frame up to 1 / this of the interval early counts as on time (T-185).</summary>
    private const long EarlyFraction = 4;

    private readonly long _timestampFrequency;

    private readonly CameraFrameExchange? _exchange;
    private readonly CameraFrameHandler? _frameReceived;
    private readonly Action? _frameAvailable;
    private readonly long _minInterval;
    private readonly Lock _gate = new();

    // CameraFrameSize.Pack of the display size; 0: full resolution. Read without a lock by the streaming thread.
    private long _displaySize;

    // Written by the streaming thread; read by the hub under its lock (NextKeyFrameDue).
    private long _lastPublished = NeverPublished;

    // T-185: the frame interval of MaxFrameRate in timestamp ticks (0: no limit) and its rate, written by any thread; the
    // time the next frame is due on the grid (NotPaced: the next frame), written by the streaming thread and reset by a
    // new rate.
    private long _frameInterval;
    private int _maxFrameRate;
    private long _nextPaced = NotPaced;

    // Written under _gate, read without it on the streaming thread.
    private volatile bool _hasFrame;
    private CameraStatus _connection = CameraStatus.Connecting;

    internal CameraFrameSubscription(CameraStreamHub? hub, string printerId, CameraFrameSubscriptionOptions options, TimeProvider timeProvider)
        : base(hub, printerId, options.Status, options.Ended)
    {
        _frameAvailable = options.FrameAvailable;
        _timestampFrequency = timeProvider.TimestampFrequency;
        SetFrameRate(options.MaxFrameRate);
        _minInterval = options.MinFrameInterval == TimeSpan.Zero
            ? 0
            : (long)(options.MinFrameInterval.TotalSeconds * timeProvider.TimestampFrequency);
        KeyFramesOnly = options.KeyFramesOnly;
        ShortensReconnectWait = options.ShortensReconnectWait;
        _displaySize = CameraFrameSize.Pack(options.DisplaySize);
        Grayscale = options.Grayscale;
        _frameReceived = options.FrameReceived;
        if (_frameReceived is not null && options.Grayscale is not null)
        {
            throw new ArgumentException("Frames handed to a callback cannot be converted to grayscale.", nameof(options));
        }

        // A frame receiver copies the frames itself: no buffers of the subscription.
        _exchange = _frameReceived is not null ? null
            : options.Grayscale is { } grayscale ? new CameraFrameExchange(grayscale)
            : new CameraFrameExchange();
    }

    /// <summary>Frames replaced before the consumer took them.</summary>
    public long DroppedFrames => _exchange?.DroppedFrames ?? 0;

    /// <summary>
    /// The size display frames are scaled to fit (T-184, see <see cref="CameraFrameSubscriptionOptions.DisplaySize"/>);
    /// <see langword="null"/>: full resolution.
    /// </summary>
    public CameraFrameSize? DisplaySize => CameraFrameSize.Unpack(Interlocked.Read(ref _displaySize));

    /// <summary>
    /// Any thread: a new display size (the window was resized, switched to the mini preview or full screen, or moved to a
    /// monitor with another scaling); <see langword="null"/> switches to full resolution. The running stream reconfigures
    /// its conversion from the next frame on, without reconnecting; a frame converted before may still arrive in the old
    /// size. Cheap when the size did not change.
    /// </summary>
    public void SetDisplaySize(CameraFrameSize? size)
    {
        long packed = CameraFrameSize.Pack(size);
        if (Interlocked.Exchange(ref _displaySize, packed) != packed && !IsEnded)
        {
            Hub?.OnScheduleChanged(this);
        }
    }

    /// <summary>
    /// At most this many frames per second (T-185, see <see cref="CameraFrameSubscriptionOptions.MaxFrameRate"/>);
    /// <see langword="null"/>: every frame.
    /// </summary>
    public int? MaxFrameRate => Volatile.Read(ref _maxFrameRate) is > 0 and int rate ? rate : null;

    /// <summary>
    /// Any thread: a new frame rate limit (the window switched to or from the mini preview, or the setting changed);
    /// <see langword="null"/>: every frame. Applies from the next frame on, without reconnecting: the next frame is taken
    /// right away and the new pace starts from it. Cheap when the rate did not change.
    /// </summary>
    public void SetMaxFrameRate(int? rate)
    {
        ValidateFrameRate(rate);
        if (SetFrameRate(rate) && !IsEnded)
        {
            Hub?.OnScheduleChanged(this);
        }
    }

    internal static void ValidateFrameRate(int? rate)
    {
        if (rate is { } value)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1, nameof(rate));
        }
    }

    /// <summary>Takes the frames for display (<see cref="CameraStreamItem.DisplayFrame"/>), not the full resolution ones.</summary>
    internal bool IsDisplay => Interlocked.Read(ref _displaySize) != 0;

    /// <summary>
    /// Takes at most one frame per <see cref="CameraFrameSubscriptionOptions.MinFrameInterval"/>, or has a
    /// <see cref="MaxFrameRate"/> (T-185).
    /// </summary>
    internal bool IsThrottled => _minInterval > 0 || Interlocked.Read(ref _frameInterval) > 0;

    /// <summary>See <see cref="CameraFrameSubscriptionOptions.KeyFramesOnly"/>.</summary>
    internal bool KeyFramesOnly { get; }

    /// <summary>See <see cref="CameraFrameSubscriptionOptions.ShortensReconnectWait"/>.</summary>
    internal bool ShortensReconnectWait { get; }

    /// <summary>See <see cref="CameraFrameSubscriptionOptions.Grayscale"/>.</summary>
    internal CameraGrayscaleSize? Grayscale { get; }

    /// <summary>When this subscriber wants its next frame (a timestamp of the hub's clock); <see cref="long.MinValue"/>: now.</summary>
    internal long NextFrameDue
    {
        get
        {
            long last = Interlocked.Read(ref _lastPublished);
            long due = last == NeverPublished ? NeverPublished : last + _minInterval;
            if (Interlocked.Read(ref _frameInterval) > 0)
            {
                // The grid's time minus the tolerance of TryTake (NotPaced is long.MinValue: now).
                long paced = Interlocked.Read(ref _nextPaced);
                due = Math.Max(due, paced == NotPaced ? NotPaced : paced - (Interlocked.Read(ref _frameInterval) / EarlyFraction));
            }

            return due;
        }
    }

    /// <summary>
    /// Consumer thread: the newest frame since the last call, or <see langword="null"/> (always with
    /// <see cref="CameraFrameSubscriptionOptions.FrameReceived"/>). The buffer is owned by the subscription and stays valid
    /// until the next call.
    /// </summary>
    public CameraFrameBuffer? TakeLatest() => IsEnded ? null : _exchange?.TakeLatest();

    internal override void OnConnectionStatus(CameraStatus status)
    {
        CameraStatus effective;
        lock (_gate)
        {
            _connection = status;
            if (status.State == CameraConnectionState.Connecting)
            {
                // A new attempt: this subscriber waits for its first frame again.
                _hasFrame = false;
            }

            effective = status.State == CameraConnectionState.Streaming && !_hasFrame ? CameraStatus.Connecting : status;
        }

        Report(effective);
    }

    /// <summary>Streaming thread: copies <paramref name="frame"/> (borrowed, valid only now) unless it is skipped.</summary>
    /// <returns>Whether the frame was taken (not skipped).</returns>
    internal bool Publish(in CameraFrame frame, long timestamp)
    {
        if (!TryTake(timestamp))
        {
            return false;
        }

        bool notify;
        if (_exchange is null)
        {
            _frameReceived!(frame);
            notify = false;
        }
        else
        {
            notify = _exchange.Publish(frame);
        }

        Published(notify);
        return true;
    }

    /// <summary>
    /// Streaming thread: copies the grayscale <paramref name="frame"/> of this subscriber's <see cref="Grayscale"/> size
    /// (T-189, borrowed, valid only now) unless it is skipped.
    /// </summary>
    /// <returns>Whether the frame was taken (not skipped).</returns>
    internal bool PublishGrayscale(in CameraGrayscaleFrame frame, long timestamp)
    {
        if (!TryTake(timestamp))
        {
            return false;
        }

        // A grayscale subscriber always has its exchange (it cannot have a frame receiver).
        Published(_exchange!.PublishGrayscale(frame));
        return true;
    }

    /// <summary>Streaming thread: whether a frame at <paramref name="timestamp"/> is taken; then it counts as the last one.</summary>
    private bool TryTake(long timestamp)
    {
        if (IsEnded)
        {
            // Removed meanwhile; the stream still had it in its list.
            return false;
        }

        long last = Interlocked.Read(ref _lastPublished);
        if (_minInterval > 0 && last != NeverPublished && timestamp - last < _minInterval)
        {
            return false;
        }

        long interval = Interlocked.Read(ref _frameInterval);
        if (interval > 0 && !TryPace(timestamp, interval))
        {
            return false;
        }

        Interlocked.Exchange(ref _lastPublished, timestamp);
        return true;
    }

    /// <summary>
    /// Streaming thread (T-185): whether a frame at <paramref name="timestamp"/> keeps the <see cref="MaxFrameRate"/>; then
    /// the grid moves on by one <paramref name="interval"/>. A frame up to a quarter interval early counts as on time; one
    /// late by a whole interval or more (the first frame, a stall) starts a new grid from itself.
    /// </summary>
    private bool TryPace(long timestamp, long interval)
    {
        long next = Interlocked.Read(ref _nextPaced);
        if (next != NotPaced && timestamp < next - (interval / EarlyFraction))
        {
            return false;
        }

        long following = next == NotPaced || timestamp - next >= interval ? timestamp + interval : next + interval;
        // A new rate set meanwhile reset the grid: the reset wins (its next frame is taken right away).
        Interlocked.CompareExchange(ref _nextPaced, following, next);
        return true;
    }

    /// <summary>Any thread: the interval of <paramref name="rate"/>; <see langword="true"/> when it changed.</summary>
    private bool SetFrameRate(int? rate)
    {
        int value = rate ?? 0;
        if (Interlocked.Exchange(ref _maxFrameRate, value) == value)
        {
            return false;
        }

        long interval = value == 0 ? 0 : Math.Max(1, (long)Math.Round(_timestampFrequency / (double)value));
        Interlocked.Exchange(ref _frameInterval, interval);
        // The next frame is taken right away, the new pace starts from it.
        Interlocked.Exchange(ref _nextPaced, NotPaced);
        return true;
    }

    /// <summary>Streaming thread: a frame was taken; reports the first one and notifies the consumer when it must be.</summary>
    private void Published(bool notify)
    {
        if (!_hasFrame)
        {
            bool streaming;
            lock (_gate)
            {
                _hasFrame = true;
                streaming = _connection.State == CameraConnectionState.Streaming;
            }

            if (streaming)
            {
                Report(CameraStatus.Streaming);
            }
        }

        if (notify)
        {
            _frameAvailable?.Invoke();
        }
    }
}

/// <summary>Options of <see cref="CameraStreamHub.SubscribePackets"/>.</summary>
public sealed class CameraPacketSubscriptionOptions
{
    /// <summary>Default <see cref="QueueCapacity"/>: about 25 s of a 20 fps camera.</summary>
    public const int DefaultQueueCapacity = 512;

    /// <summary>
    /// Items (packets and stream descriptions) the subscription queues for its reader. When the reader falls this far
    /// behind, the subscription ends with <see cref="CameraStreamEndReason.Overflow"/>; the other subscribers go on.
    /// </summary>
    public int QueueCapacity
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = DefaultQueueCapacity;

    /// <summary>
    /// Only key frames are queued (timelapse, T-158): the other packets are skipped on the streaming thread before they are
    /// copied, so the reader is not woken for them. Stream descriptions are queued as always.
    /// </summary>
    public bool KeyFramesOnly { get; init; }

    /// <inheritdoc cref="CameraFrameSubscriptionOptions.Status"/>
    public IProgress<CameraStatus>? Status { get; init; }

    /// <inheritdoc cref="CameraFrameSubscriptionOptions.Ended"/>
    public Action<CameraStreamSubscription>? Ended { get; init; }
}

/// <summary>
/// Encoded packets of the shared connection, without decoding (recording, T-144): every connection starts with a
/// <see cref="CameraStreamItemKind.StreamStarted"/> item (a reconnect starts a new one), followed by its packets. The
/// hub copies each packet into a buffer of this subscription's own small pool and queues it, so the reader runs at its
/// own pace on its own thread (it is woken on the thread pool, never on the streaming thread); a reader that falls behind
/// by <see cref="CameraPacketSubscriptionOptions.QueueCapacity"/> items ends the subscription
/// (<see cref="CameraStreamEndReason.Overflow"/>) instead of slowing the stream. The steady state allocates nothing.
/// </summary>
public sealed class CameraPacketSubscription : CameraStreamSubscription
{
    private readonly PacketQueue _queue;
    private readonly bool _keyFramesOnly;
    private int _reading;

    internal CameraPacketSubscription(CameraStreamHub? hub, string printerId, CameraPacketSubscriptionOptions options)
        : base(hub, printerId, options.Status, options.Ended)
    {
        _queue = new PacketQueue(options.QueueCapacity);
        _keyFramesOnly = options.KeyFramesOnly;
    }

    /// <summary>
    /// The queued items in order: stream descriptions and packets (never frames). A packet's data is valid until the
    /// enumerator advances (its buffer is then reused). Completes after the subscription ended and the queue is empty; see
    /// <see cref="CameraStreamSubscription.Ended"/> for why. One reader at a time.
    /// </summary>
    public IAsyncEnumerable<CameraStreamItem> ReadAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _reading, 1) != 0)
        {
            throw new InvalidOperationException("The packets of a subscription can be read by one reader at a time.");
        }

        return ReadCoreAsync(cancellationToken);
    }

    /// <summary>Hub (under its lock or on the streaming thread): a connection started.</summary>
    /// <returns><see langword="false"/> when the queue is full.</returns>
    internal bool TryEnqueue(CameraStreamInfo info) => _queue.TryWrite(info, default) || IsEnded;

    /// <summary>
    /// Streaming thread: copies <paramref name="packet"/> (borrowed) into a pooled buffer and queues it; a packet this
    /// subscriber does not take (<see cref="CameraPacketSubscriptionOptions.KeyFramesOnly"/>) is skipped.
    /// </summary>
    /// <returns><see langword="false"/> when the queue is full (the packet was not queued).</returns>
    internal bool TryEnqueue(in CameraPacket packet) => (_keyFramesOnly && !packet.IsKeyFrame) || _queue.TryWrite(null, packet) || IsEnded;

    private protected override void OnEnded()
    {
        _queue.Complete();
        if (Volatile.Read(ref _reading) == 0)
        {
            // Nobody reads: the queued buffers are released now (a reader that starts later finds the queue empty).
            _queue.Clear();
        }
    }

    private async IAsyncEnumerable<CameraStreamItem> ReadCoreAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // One registration per enumeration, not per wait.
        _queue.ResetCancellation();
        CancellationTokenRegistration registration = cancellationToken.UnsafeRegister(static state => ((PacketQueue)state!).CancelWait(), _queue);
        try
        {
            while (true)
            {
                // Also while items keep coming: a cancelled reader stops at once, not only when the queue runs dry.
                cancellationToken.ThrowIfCancellationRequested();
                if (!_queue.TryRead(out QueuedItem item))
                {
                    if (_queue.IsCompleted)
                    {
                        yield break;
                    }

                    await _queue.WaitAsync().ConfigureAwait(false);
                    continue;
                }

                try
                {
                    yield return item.ToItem();
                }
                finally
                {
                    _queue.Release(item);
                }
            }
        }
        finally
        {
            await registration.DisposeAsync().ConfigureAwait(false);
            if (IsEnded)
            {
                _queue.Clear();
            }

            Volatile.Write(ref _reading, 0);
        }
    }

    /// <summary>A queued description or packet copy; the buffer belongs to the queue's pool.</summary>
    internal readonly struct QueuedItem(CameraStreamInfo? info, byte[]? buffer, CameraPacket packet)
    {
        public byte[]? Buffer { get; } = buffer;

        public CameraStreamItem ToItem() => info is { } description ? CameraStreamItem.Started(description) : CameraStreamItem.FromPacket(packet);
    }

    /// <summary>
    /// Bounded FIFO of one subscription with one asynchronous reader and a small pool of packet buffers; all state under
    /// one lock. Allocation-free once warm: a ring of items, reused buffers, one reusable value task source for the reader.
    /// </summary>
    private sealed class PacketQueue : IValueTaskSource
    {
        /// <summary>Buffers kept for reuse; a backlog beyond this is left to the garbage collector when it drains.</summary>
        private const int PoolSize = 16;

        private readonly Lock _gate = new();
        private readonly QueuedItem[] _items;
        private readonly byte[]?[] _pool = new byte[PoolSize][];
        private int _pooled;
        private int _head;
        private int _count;
        private bool _completed;
        private bool _waiting;
        private bool _cancelled;

        // The reader is woken on the thread pool: the writer is the streaming thread, which must never run the reader's work.
        private ManualResetValueTaskSourceCore<bool> _waiter = new() { RunContinuationsAsynchronously = true };

        public PacketQueue(int capacity) => _items = new QueuedItem[capacity];

        public bool IsCompleted
        {
            get
            {
                lock (_gate)
                {
                    return _completed && _count == 0;
                }
            }
        }

        /// <summary>Queues a description (<paramref name="info"/>) or a copy of <paramref name="packet"/>.</summary>
        public bool TryWrite(CameraStreamInfo? info, in CameraPacket packet)
        {
            bool wake;
            lock (_gate)
            {
                if (_completed || _count == _items.Length)
                {
                    return false;
                }

                QueuedItem item;
                if (info is not null)
                {
                    item = new QueuedItem(info, null, default);
                }
                else
                {
                    byte[] buffer = Rent(packet.Data.Length);
                    packet.Data.Span.CopyTo(buffer);
                    item = new QueuedItem(null, buffer, packet with { Data = buffer.AsMemory(0, packet.Data.Length) });
                }

                _items[(_head + _count) % _items.Length] = item;
                _count++;
                wake = _waiting;
                _waiting = false;
            }

            if (wake)
            {
                _waiter.SetResult(true);
            }

            return true;
        }

        public bool TryRead(out QueuedItem item)
        {
            lock (_gate)
            {
                if (_count == 0)
                {
                    item = default;
                    return false;
                }

                item = _items[_head];
                _items[_head] = default;
                _head = (_head + 1) % _items.Length;
                _count--;
                return true;
            }
        }

        /// <summary>Completes when an item is queued, the queue is completed or the wait is cancelled.</summary>
        public ValueTask WaitAsync()
        {
            lock (_gate)
            {
                if (_count > 0 || _completed || _cancelled)
                {
                    return ValueTask.CompletedTask;
                }

                _waiter.Reset();
                _waiting = true;
                return new ValueTask(this, _waiter.Version);
            }
        }

        /// <summary>The reader's token was cancelled: a pending or later wait returns (the reader then throws).</summary>
        public void CancelWait()
        {
            lock (_gate)
            {
                _cancelled = true;
            }

            Wake();
        }

        /// <summary>A new reader starts.</summary>
        public void ResetCancellation()
        {
            lock (_gate)
            {
                _cancelled = false;
            }
        }

        /// <summary>No more items are accepted; the reader gets the rest, then the end.</summary>
        public void Complete()
        {
            lock (_gate)
            {
                _completed = true;
            }

            Wake();
        }

        /// <summary>Releases every queued buffer.</summary>
        public void Clear()
        {
            while (TryRead(out QueuedItem item))
            {
                Release(item);
            }
        }

        /// <summary>The reader is done with <paramref name="item"/>: its buffer goes back to the pool.</summary>
        public void Release(in QueuedItem item)
        {
            if (item.Buffer is not { } buffer)
            {
                return;
            }

            lock (_gate)
            {
                if (_pooled < PoolSize)
                {
                    _pool[_pooled++] = buffer;
                }
            }
        }

        void IValueTaskSource.GetResult(short token) => _waiter.GetResult(token);

        ValueTaskSourceStatus IValueTaskSource.GetStatus(short token) => _waiter.GetStatus(token);

        void IValueTaskSource.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
            _waiter.OnCompleted(continuation, state, token, flags);

        private void Wake()
        {
            bool wake;
            lock (_gate)
            {
                wake = _waiting;
                _waiting = false;
            }

            if (wake)
            {
                _waiter.SetResult(true);
            }
        }

        /// <summary>Under the lock: a pooled buffer of at least <paramref name="length"/> bytes (a bigger one replaces a small one).</summary>
        private byte[] Rent(int length)
        {
            while (_pooled > 0)
            {
                byte[] buffer = _pool[--_pooled]!;
                _pool[_pooled] = null;
                if (buffer.Length >= length)
                {
                    return buffer;
                }
            }

            // Rounded up, so that slightly bigger packets reuse it later (H.264 packet sizes vary).
            return GC.AllocateUninitializedArray<byte>((int)Math.Min(Array.MaxLength, System.Numerics.BitOperations.RoundUpToPowerOf2((uint)Math.Max(length, 4096))));
        }
    }
}
