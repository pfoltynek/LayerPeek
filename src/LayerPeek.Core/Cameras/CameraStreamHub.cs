using Microsoft.Extensions.Logging;

using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Cameras;

/// <summary>
/// One shared camera connection per printer (T-143) for every consumer: camera window, its mini preview, the camera test
/// of a saved camera, later recording (T-144) and print failure detection (T-153). Consumers take a lease
/// (<see cref="SubscribeFrames"/>, <see cref="SubscribePackets"/>); the connection (a <see cref="ReconnectingCameraFeed"/>)
/// opens with the first lease and closes <see cref="Linger"/> after the last one ended (user decision 2026-11-03: 10 s,
/// so that closing and reopening a window does not wait for a new key frame again).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Decoding runs only while a frame subscriber exists (or for <see cref="Linger"/> after the last one left);
/// packet subscribers alone only read the stream (<see cref="CameraStreamDemand"/>).</item>
/// <item>Every frame subscriber gets its own copy (<see cref="CameraFrameExchange"/>, newest frame wins), every packet
/// subscriber its own bounded queue, so a slow subscriber never slows the stream or the others.</item>
/// <item>All subscribers of a printer get the connection status; <see cref="Configure"/> restarts the connection when
/// the camera address changed and ends all leases when the camera was switched off or the printer removed.</item>
/// <item>A lease may name the address it expects: when it differs from the current one (a camera window opened right
/// after "Apply", before the monitoring run delivered the saved settings, T-040), the connection restarts with it for
/// everyone. Without an address the configured one is used.</item>
/// <item>Thread-safe. Callbacks of subscribers (status, frame available, ended) run synchronously on the streaming
/// thread or the calling thread and must return quickly; status callbacks run under the hub's lock.</item>
/// </list>
/// </remarks>
public sealed partial class CameraStreamHub : IAsyncDisposable, IDisposable
{
    /// <summary>User decision 2026-11-03.</summary>
    public static readonly TimeSpan DefaultLinger = TimeSpan.FromSeconds(10);

    private static readonly CameraFrameSubscriptionOptions NoFrameOptions = new();
    private static readonly CameraPacketSubscriptionOptions NoPacketOptions = new();

    private readonly ReconnectingCameraFeed _feed;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CameraStreamHub> _logger;
    private readonly Lock _gate = new();

    // Guarded by _gate.
    private readonly Dictionary<string, PrinterStream> _streams = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, Uri> _configured = new(StringComparer.OrdinalIgnoreCase);
    private Task? _disposal;

    /// <param name="linger">How long a connection stays open after its last lease; <see langword="null"/>: <see cref="DefaultLinger"/>; zero: closes at once.</param>
    public CameraStreamHub(ReconnectingCameraFeed feed, TimeProvider timeProvider, ILogger<CameraStreamHub> logger, TimeSpan? linger = null)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        TimeSpan value = linger ?? DefaultLinger;
        ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero, nameof(linger));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, CameraFeedOptions.MaxValue, nameof(linger));
        _feed = feed;
        _timeProvider = timeProvider;
        _logger = logger;
        Linger = value;
    }

    public TimeSpan Linger { get; }

    /// <summary>Connections currently open or opening (for tests and diagnostics).</summary>
    public int OpenConnections
    {
        get
        {
            lock (_gate)
            {
                int count = 0;
                foreach (PrinterStream stream in _streams.Values)
                {
                    count += stream.Run is null ? 0 : 1;
                }

                return count;
            }
        }
    }

    /// <summary>
    /// The printers of a monitoring run (or its update): the enabled cameras. Only what changed since the previous call
    /// acts: a new address restarts that printer's connection for all its subscribers, a camera that is no longer enabled
    /// (switched off, printer removed) ends all its leases with <see cref="CameraStreamEndReason.CameraUnavailable"/>.
    /// </summary>
    public void Configure(IReadOnlyList<PrinterDefinition> printers)
    {
        ArgumentNullException.ThrowIfNull(printers);
        Dictionary<string, Uri> cameras = new(StringComparer.OrdinalIgnoreCase);
        foreach (PrinterDefinition printer in printers)
        {
            if (printer is { Camera: { Enabled: true, StreamUri: { } uri } })
            {
                cameras[printer.Id] = uri;
            }
        }

        Pending pending = default;
        lock (_gate)
        {
            if (_disposal is not null)
            {
                return;
            }

            Dictionary<string, Uri> previous = _configured;
            _configured = cameras;
            foreach (string id in (string[])[.. _streams.Keys])
            {
                PrinterStream stream = _streams[id];
                Uri? before = previous.GetValueOrDefault(id);
                Uri? now = cameras.GetValueOrDefault(id);
                if (SameUri(before, now))
                {
                    continue;
                }

                if (now is null)
                {
                    pending.End(stream.TakeAll());
                    StopCore(stream, ref pending);
                    // The entry stays (without an address): a later connection still waits for this one to close, and
                    // disposing the hub still waits for it.
                    stream.Uri = null;
                    LogCameraRemoved(id);
                }
                else if (!SameUri(stream.Uri, now))
                {
                    stream.Uri = now;
                    StopCore(stream, ref pending);
                    EvaluateCore(stream, ref pending);
                    LogAddressChanged(id);
                }
            }
        }

        pending.Run(CameraStreamEndReason.CameraUnavailable);
    }

    /// <summary>Whether <paramref name="streamUri"/> is the enabled camera of <paramref name="printerId"/> in the last <see cref="Configure"/>.</summary>
    public bool IsConfigured(string printerId, Uri streamUri)
    {
        ArgumentNullException.ThrowIfNull(printerId);
        ArgumentNullException.ThrowIfNull(streamUri);
        lock (_gate)
        {
            return _configured.TryGetValue(printerId, out Uri? configured) && SameUri(configured, streamUri);
        }
    }

    /// <summary>
    /// A lease on the decoded frames of <paramref name="printerId"/>'s camera. Never throws for an unknown camera: the lease
    /// then has already ended (<see cref="CameraStreamEndReason.CameraUnavailable"/>, or <see cref="CameraStreamEndReason.HubStopped"/>).
    /// </summary>
    /// <param name="streamUri">The address the subscriber expects (see the type remarks); <see langword="null"/>: the configured one.</param>
    public CameraFrameSubscription SubscribeFrames(string printerId, Uri? streamUri = null, CameraFrameSubscriptionOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        ValidateUri(streamUri);
        CameraFrameSubscription subscription = new(this, printerId, options ?? NoFrameOptions, _timeProvider);
        Add(subscription, streamUri);
        return subscription;
    }

    /// <summary>
    /// A lease on the encoded packets of <paramref name="printerId"/>'s camera (see <see cref="CameraPacketSubscription"/>);
    /// nothing is decoded for it. Never throws for an unknown camera (see <see cref="SubscribeFrames"/>).
    /// </summary>
    public CameraPacketSubscription SubscribePackets(string printerId, Uri? streamUri = null, CameraPacketSubscriptionOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        ValidateUri(streamUri);
        CameraPacketSubscription subscription = new(this, printerId, options ?? NoPacketOptions);
        Add(subscription, streamUri);
        return subscription;
    }

    /// <summary>Ends every lease (<see cref="CameraStreamEndReason.HubStopped"/>) and waits until every connection is closed.</summary>
    public ValueTask DisposeAsync() => new(Stop());

    /// <summary>Ends every lease and starts closing the connections without waiting for them.</summary>
    public void Dispose() => _ = Stop();

    private Task Stop()
    {
        Pending pending = default;
        List<Task> loops = [];
        Task disposal;
        lock (_gate)
        {
            if (_disposal is { } running)
            {
                return running;
            }

            foreach (PrinterStream stream in _streams.Values)
            {
                pending.End(stream.TakeAll());
                StopCore(stream, ref pending);
                stream.DisposeTimer();
                loops.Add(stream.Loop);
            }

            _streams.Clear();
            disposal = Task.WhenAll(loops);
            _disposal = disposal;
        }

        pending.Run(CameraStreamEndReason.HubStopped);
        return disposal;
    }

    /// <summary>A lease ended by its owner, or by the hub.</summary>
    internal void Unsubscribe(CameraStreamSubscription subscription)
    {
        Pending pending = default;
        lock (_gate)
        {
            if (_streams.TryGetValue(subscription.PrinterId, out PrinterStream? stream) && stream.Remove(subscription))
            {
                EvaluateCore(stream, ref pending);
            }
        }

        pending.Run(CameraStreamEndReason.Disposed);
    }

    internal void OnStatusObserverFailed(CameraStreamSubscription subscription, Exception exception) =>
        LogObserverFailed(exception, subscription.PrinterId);

    private static bool SameUri(Uri? a, Uri? b) => string.Equals(a?.OriginalString, b?.OriginalString, StringComparison.Ordinal);

    private static void ValidateUri(Uri? streamUri)
    {
        if (streamUri is { IsAbsoluteUri: false })
        {
            throw new ArgumentException("The stream address must be absolute.", nameof(streamUri));
        }
    }

    private void Add(CameraStreamSubscription subscription, Uri? streamUri)
    {
        Pending pending = default;
        CameraStreamEndReason? refused = null;
        lock (_gate)
        {
            Uri? uri = streamUri ?? _configured.GetValueOrDefault(subscription.PrinterId);
            if (_disposal is not null)
            {
                refused = CameraStreamEndReason.HubStopped;
            }
            else if (uri is null)
            {
                refused = CameraStreamEndReason.CameraUnavailable;
            }
            else
            {
                if (!_streams.TryGetValue(subscription.PrinterId, out PrinterStream? stream))
                {
                    stream = new PrinterStream(subscription.PrinterId);
                    _streams.Add(subscription.PrinterId, stream);
                }

                if (!SameUri(stream.Uri, uri))
                {
                    if (stream.Uri is not null)
                    {
                        LogAddressChanged(subscription.PrinterId);
                    }

                    stream.Uri = uri;
                    StopCore(stream, ref pending);
                }

                // The current status first (under the lock, before the stream can deliver anything to the new subscriber),
                // so that it never overtakes a newer one.
                subscription.OnConnectionStatus(stream.Run is null ? CameraStatus.Connecting : stream.Status);
                if (subscription is CameraPacketSubscription packets && stream.Info is { } info)
                {
                    // A connection already runs: its description comes before its next packet.
                    packets.TryEnqueue(info);
                }

                stream.Add(subscription);
                EvaluateCore(stream, ref pending);
            }
        }

        if (refused is { } reason)
        {
            CameraStreamSubscription.EndAtOnce(subscription, reason);
        }

        pending.Run(CameraStreamEndReason.Disposed);
    }

    /// <summary>Under the lock: starts, keeps or stops the connection and sets its demand after the subscribers changed.</summary>
    private void EvaluateCore(PrinterStream stream, ref Pending pending)
    {
        bool frames = stream.Frames.Length > 0;
        bool packets = stream.Packets.Length > 0;
        stream.Demand.Packets = packets;
        if (frames)
        {
            stream.Demand.Frames = true;
            stream.FramesLinger = false;
        }

        if (frames || packets)
        {
            stream.StopLinger = false;
            if (stream.Run is null)
            {
                stream.Demand.Frames = frames;
                StartCore(stream);
            }
            else if (!frames && stream.Demand.Frames && !stream.FramesLinger)
            {
                // The last frame subscriber left: decoding goes on for a while, in case it comes back.
                if (Linger == TimeSpan.Zero)
                {
                    stream.Demand.Frames = false;
                }
                else
                {
                    stream.FramesLinger = true;
                    ArmLinger(stream);
                }
            }
        }
        else if (stream.Run is not null && !stream.StopLinger)
        {
            if (Linger == TimeSpan.Zero)
            {
                StopCore(stream, ref pending);
            }
            else
            {
                stream.StopLinger = true;
                ArmLinger(stream);
            }
        }

        if (!stream.FramesLinger && !stream.StopLinger)
        {
            stream.Timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        KeepReading(stream);
    }

    /// <summary>
    /// Under the lock: a running connection always wants something. With no demand at all the feed yields nothing, so the
    /// stall watchdog of <see cref="ReconnectingCameraFeed"/> (<see cref="CameraFeedOptions.FrameTimeout"/>) would drop and
    /// reconnect a lingering connection of a packet subscriber. Packets nobody receives cost one copy each, no decoding.
    /// </summary>
    private static void KeepReading(PrinterStream stream)
    {
        if (stream.Run is not null && !stream.Demand.Frames && !stream.Demand.Packets)
        {
            stream.Demand.Packets = true;
        }
    }

    private void ArmLinger(PrinterStream stream)
    {
        stream.Timer ??= _timeProvider.CreateTimer(static state => ((LingerState)state!).Hub.OnLingerDue(((LingerState)state).Stream), new LingerState(this, stream), Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        stream.LingerSince = _timeProvider.GetTimestamp();
        stream.Timer.Change(Linger, Timeout.InfiniteTimeSpan);
    }

    // Timer thread.
    private void OnLingerDue(PrinterStream stream)
    {
        Pending pending = default;
        lock (_gate)
        {
            if (_disposal is not null || stream.Timer is null || (!stream.FramesLinger && !stream.StopLinger))
            {
                return;
            }

            TimeSpan remaining = Linger - _timeProvider.GetElapsedTime(stream.LingerSince);
            if (remaining > TimeSpan.Zero)
            {
                // Armed again meanwhile, or the timer ran a little early.
                stream.Timer.Change(remaining, Timeout.InfiniteTimeSpan);
                return;
            }

            if (stream.StopLinger && stream.Frames.Length == 0 && stream.Packets.Length == 0)
            {
                StopCore(stream, ref pending);
                LogClosedAfterLinger(stream.PrinterId);
            }
            else if (stream.FramesLinger && stream.Frames.Length == 0)
            {
                stream.Demand.Frames = false;
            }

            stream.FramesLinger = false;
            stream.StopLinger = false;
            KeepReading(stream);
        }

        pending.Run(CameraStreamEndReason.Disposed);
    }

    /// <summary>Under the lock: opens the connection with the stream's address; a previous one closes first.</summary>
    private void StartCore(PrinterStream stream)
    {
        Uri uri = stream.Uri ?? throw new InvalidOperationException("A camera stream needs an address.");
        int generation = ++stream.Generation;
        CancellationTokenSource run = new();
        stream.Run = run;
        stream.Status = CameraStatus.Connecting;
        stream.Info = null;
        Task previous = stream.Loop;
        // Task.Run: connecting may block in the decoder; nothing of the stream runs on the subscriber's thread.
        stream.Loop = Task.Run(() => RunAsync(stream, uri, generation, previous, run), CancellationToken.None);
    }

    /// <summary>Under the lock: stops the connection (cancelled after the lock is released) and forgets its state.</summary>
    private static void StopCore(PrinterStream stream, ref Pending pending)
    {
        if (stream.Run is { } run)
        {
            pending.Cancel(run);
            stream.Run = null;
        }

        // Items and statuses of the stopped run are ignored from now on.
        stream.Generation++;
        stream.Status = CameraStatus.Connecting;
        stream.Info = null;
        stream.Demand.Frames = false;
        stream.Demand.Packets = false;
        stream.FramesLinger = false;
        stream.StopLinger = false;
        // An idle printer keeps no timer.
        stream.DisposeTimer();
    }

    private async Task RunAsync(PrinterStream stream, Uri uri, int generation, Task previous, CancellationTokenSource run)
    {
        CancellationToken token = run.Token;
        Exception? failure = null;
        try
        {
            // One connection per printer: the previous one must have closed first.
            await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            if (!token.IsCancellationRequested)
            {
                StatusSink status = new(this, stream, generation);
                await foreach (CameraStreamItem item in _feed.ReadAsync(uri, stream.Demand, status, token).ConfigureAwait(false))
                {
                    if (stream.Generation != generation)
                    {
                        break;
                    }

                    Dispatch(stream, generation, item);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
        {
            // The feed reports stream failures as statuses; this is only a safety net.
            failure = ex;
            LogLoopFailed(ex, stream.PrinterId);
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }
        finally
        {
            Pending pending = default;
            lock (_gate)
            {
                if (ReferenceEquals(stream.Run, run))
                {
                    // Ended by itself, which a reconnecting feed never does: the leases learn it, a new lease starts again.
                    stream.Run = null;
                    stream.Generation++;
                    stream.Info = null;
                    pending.End(stream.TakeAll());
                }
            }

            run.Dispose();
            pending.Run(CameraStreamEndReason.Failed, failure ?? new InvalidOperationException("The camera stream ended unexpectedly."));
        }
    }

    /// <summary>Streaming thread: hands one item to the subscribers of its kind.</summary>
    private void Dispatch(PrinterStream stream, int generation, in CameraStreamItem item)
    {
        switch (item.Kind)
        {
            case CameraStreamItemKind.Frame:
            {
                CameraFrameSubscription[] subscribers = stream.Frames;
                if (subscribers.Length == 0)
                {
                    return;
                }

                long now = _timeProvider.GetTimestamp();
                foreach (CameraFrameSubscription subscriber in subscribers)
                {
                    try
                    {
                        subscriber.Publish(item.Frame, now);
                    }
                    catch (Exception ex)
                    {
                        LogSubscriberFailed(ex, stream.PrinterId);
                        if (subscriber.TryEnd(CameraStreamEndReason.Failed, ex))
                        {
                            Unsubscribe(subscriber);
                        }
                    }
                }

                break;
            }

            case CameraStreamItemKind.Packet:
                foreach (CameraPacketSubscription subscriber in stream.Packets)
                {
                    if (!subscriber.TryEnqueue(item.Packet))
                    {
                        Overflowed(subscriber);
                    }
                }

                break;

            case CameraStreamItemKind.StreamStarted:
            {
                List<CameraPacketSubscription>? overflowed = null;
                lock (_gate)
                {
                    if (stream.Generation != generation)
                    {
                        return;
                    }

                    // Under the lock: a new packet subscriber gets this description exactly once (see Add).
                    stream.Info = item.Info;
                    foreach (CameraPacketSubscription subscriber in stream.Packets)
                    {
                        if (!subscriber.TryEnqueue(item.Info!))
                        {
                            (overflowed ??= []).Add(subscriber);
                        }
                    }
                }

                foreach (CameraPacketSubscription subscriber in overflowed ?? [])
                {
                    Overflowed(subscriber);
                }

                break;
            }
        }
    }

    private void Overflowed(CameraPacketSubscription subscriber)
    {
        if (subscriber.TryEnd(CameraStreamEndReason.Overflow, failure: null))
        {
            LogOverflow(subscriber.PrinterId);
            Unsubscribe(subscriber);
        }
    }

    /// <summary>Streaming thread: a status of the connection, for every subscriber.</summary>
    private void OnStatus(PrinterStream stream, int generation, CameraStatus status)
    {
        lock (_gate)
        {
            if (stream.Generation != generation)
            {
                return;
            }

            stream.Status = status;
            foreach (CameraFrameSubscription subscriber in stream.Frames)
            {
                subscriber.OnConnectionStatus(status);
            }

            foreach (CameraPacketSubscription subscriber in stream.Packets)
            {
                subscriber.OnConnectionStatus(status);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The camera address of printer '{PrinterId}' changed; its shared camera stream restarts.")]
    private partial void LogAddressChanged(string printerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Printer '{PrinterId}' has no enabled camera any more; its camera stream subscriptions end.")]
    private partial void LogCameraRemoved(string printerId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The shared camera stream of printer '{PrinterId}' closed after its last subscriber left.")]
    private partial void LogClosedAfterLinger(string printerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "The shared camera stream of printer '{PrinterId}' failed unexpectedly.")]
    private partial void LogLoopFailed(Exception exception, string printerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "A frame subscriber of the camera of printer '{PrinterId}' failed; its subscription ends.")]
    private partial void LogSubscriberFailed(Exception exception, string printerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A packet subscriber of the camera of printer '{PrinterId}' fell behind; its subscription ends.")]
    private partial void LogOverflow(string printerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "An observer of a camera stream subscription of printer '{PrinterId}' failed.")]
    private partial void LogObserverFailed(Exception exception, string printerId);

    /// <summary>What to do after the lock is released: cancel connections and end leases (their callbacks run then).</summary>
    private struct Pending
    {
        private List<CancellationTokenSource>? _cancel;
        private List<CameraStreamSubscription>? _end;

        public void Cancel(CancellationTokenSource run) => (_cancel ??= []).Add(run);

        public void End(List<CameraStreamSubscription>? subscriptions)
        {
            if (subscriptions is { Count: > 0 })
            {
                (_end ??= []).AddRange(subscriptions);
            }
        }

        public readonly void Run(CameraStreamEndReason reason, Exception? failure = null)
        {
            foreach (CancellationTokenSource run in _cancel ?? [])
            {
                try
                {
                    run.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // The loop ended by itself meanwhile (it saw the new generation, or the feed ended) and disposed it.
                }
            }

            foreach (CameraStreamSubscription subscription in _end ?? [])
            {
                subscription.TryEnd(reason, failure);
            }
        }
    }

    private sealed record LingerState(CameraStreamHub Hub, PrinterStream Stream);

    private sealed class StatusSink(CameraStreamHub hub, PrinterStream stream, int generation) : IProgress<CameraStatus>
    {
        public void Report(CameraStatus value) => hub.OnStatus(stream, generation, value);
    }

    /// <summary>The shared connection of one printer; every field except the subscriber arrays is guarded by the hub's lock.</summary>
    private sealed class PrinterStream(string printerId)
    {
        private volatile int _generation;
        private volatile CameraFrameSubscription[] _frames = [];
        private volatile CameraPacketSubscription[] _packets = [];

        public string PrinterId { get; } = printerId;

        public Uri? Uri { get; set; }

        public CameraStreamDemand Demand { get; } = new(frames: false, packets: false);

        /// <summary>Read without the lock by the streaming thread (copy on write).</summary>
        public CameraFrameSubscription[] Frames => _frames;

        /// <summary>Read without the lock by the streaming thread (copy on write).</summary>
        public CameraPacketSubscription[] Packets => _packets;

        /// <summary>Changes with every start and stop; read without the lock by the streaming thread.</summary>
        public int Generation
        {
            get => _generation;
            set => _generation = value;
        }

        public CancellationTokenSource? Run { get; set; }

        public Task Loop { get; set; } = Task.CompletedTask;

        public CameraStatus Status { get; set; } = CameraStatus.Connecting;

        public CameraStreamInfo? Info { get; set; }

        public ITimer? Timer { get; set; }

        public long LingerSince { get; set; }

        /// <summary>No frame subscriber any more; decoding stops when the linger time passes.</summary>
        public bool FramesLinger { get; set; }

        /// <summary>No subscriber at all; the connection closes when the linger time passes.</summary>
        public bool StopLinger { get; set; }

        public void Add(CameraStreamSubscription subscription)
        {
            if (subscription is CameraFrameSubscription frames)
            {
                _frames = [.. _frames, frames];
            }
            else
            {
                _packets = [.. _packets, (CameraPacketSubscription)subscription];
            }
        }

        public bool Remove(CameraStreamSubscription subscription)
        {
            if (subscription is CameraFrameSubscription frames)
            {
                int index = Array.IndexOf(_frames, frames);
                if (index < 0)
                {
                    return false;
                }

                _frames = [.. _frames.AsSpan(0, index), .. _frames.AsSpan(index + 1)];
                return true;
            }

            int packetIndex = Array.IndexOf(_packets, (CameraPacketSubscription)subscription);
            if (packetIndex < 0)
            {
                return false;
            }

            _packets = [.. _packets.AsSpan(0, packetIndex), .. _packets.AsSpan(packetIndex + 1)];
            return true;
        }

        /// <summary>Removes and returns every subscriber.</summary>
        public List<CameraStreamSubscription>? TakeAll()
        {
            if (_frames.Length == 0 && _packets.Length == 0)
            {
                return null;
            }

            List<CameraStreamSubscription> all = [.. _frames, .. _packets];
            _frames = [];
            _packets = [];
            return all;
        }

        public void DisposeTimer()
        {
            Timer?.Dispose();
            Timer = null;
        }
    }
}
