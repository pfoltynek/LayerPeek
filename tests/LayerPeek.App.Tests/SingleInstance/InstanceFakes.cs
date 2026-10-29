using System.Threading.Channels;

using LayerPeek.App.SingleInstance;

namespace LayerPeek.App.Tests.SingleInstance;

/// <summary>A transport whose lock, connections and listener are driven by the test.</summary>
internal sealed class FakeInstanceTransport : IInstanceTransport
{
    private readonly Queue<Func<IDisposable?>> _lockResults = new();
    private readonly Queue<Func<TimeSpan, Stream>> _connectResults = new();

    public List<string> Calls { get; } = [];

    public List<TimeSpan> ConnectTimeouts { get; } = [];

    public FakeListener? Listener { get; private set; }

    /// <summary>Result of every lock attempt once the queued ones are used: held by another instance.</summary>
    public Func<IDisposable?> DefaultLock { get; set; } = static () => null;

    /// <summary>Result of every connection attempt once the queued ones are used.</summary>
    public Func<TimeSpan, Stream> DefaultConnect { get; set; } = static _ => new RecordingStream();

    /// <summary>Thrown by <see cref="Listen"/> when set.</summary>
    public Exception? ListenFailure { get; set; }

    /// <summary>Every stream handed out by <see cref="Connect"/>.</summary>
    public List<RecordingStream> Sent { get; } = [];

    public void QueueLock(Func<IDisposable?> result) => _lockResults.Enqueue(result);

    public void QueueConnect(Func<TimeSpan, Stream> result) => _connectResults.Enqueue(result);

    public IDisposable? TryAcquireLock()
    {
        Calls.Add("lock");
        return (_lockResults.TryDequeue(out Func<IDisposable?>? result) ? result : DefaultLock)();
    }

    public IInstanceListener Listen()
    {
        Calls.Add("listen");
        if (ListenFailure is { } failure)
        {
            throw failure;
        }

        Listener = new FakeListener();
        return Listener;
    }

    public Stream Connect(TimeSpan timeout)
    {
        Calls.Add("connect");
        ConnectTimeouts.Add(timeout);
        Stream stream = (_connectResults.TryDequeue(out Func<TimeSpan, Stream>? result) ? result : DefaultConnect)(timeout);
        if (stream is RecordingStream recording)
        {
            Sent.Add(recording);
        }

        return stream;
    }
}

/// <summary>
/// The client's end of a connection: records what was written (also after it was disposed) and reads the running
/// instance's acknowledgement (unless <see cref="Acknowledges"/> is off: the other side closed without one).
/// </summary>
internal sealed class RecordingStream : MemoryStream
{
    private bool _acknowledged;

    public bool Acknowledges { get; init; } = true;

    public byte[] Written { get; private set; } = [];

    public bool Disposed { get; private set; }

    public override int Read(Span<byte> buffer) => throw new InvalidOperationException("Only asynchronous reads can be cancelled.");

    public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Only asynchronous reads can be cancelled.");

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (!Acknowledges || _acknowledged || buffer.Length == 0)
        {
            return ValueTask.FromResult(0);
        }

        _acknowledged = true;
        buffer.Span[0] = (byte)'\n';
        return ValueTask.FromResult(1);
    }

    protected override void Dispose(bool disposing)
    {
        if (!Disposed)
        {
            Written = ToArray();
            Disposed = true;
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// A connection whose writes never complete until cancelled (the other side never reads); a real wait would take the
/// time until the cancellation fires, so the fake clock is advanced by up to 10 s in small steps until it does.
/// </summary>
internal sealed class StalledStream(Microsoft.Extensions.Time.Testing.FakeTimeProvider time) : MemoryStream
{
    public TimeSpan WaitedFor { get; private set; }

    public bool Disposed { get; private set; }

    public override void Write(ReadOnlySpan<byte> buffer) => throw new InvalidOperationException("Only asynchronous writes can be cancelled.");

    public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Only asynchronous writes can be cancelled.");

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        TimeSpan step = TimeSpan.FromMilliseconds(100);
        while (!cancellationToken.IsCancellationRequested && WaitedFor < TimeSpan.FromSeconds(10))
        {
            time.Advance(step);
            WaitedFor += step;
        }

        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }
}

/// <summary>The server's end of a connection: reads the client's message, records the reply.</summary>
internal sealed class FakeClient(byte[] message) : Stream
{
    private readonly MemoryStream _input = new(message);
    private readonly MemoryStream _output = new();

    public FakeClient(string message)
        : this(System.Text.Encoding.ASCII.GetBytes(message))
    {
    }

    public byte[] Reply => _output.ToArray();

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => _input.Read(buffer, offset, count);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => _output.Write(buffer, offset, count);
}

/// <summary>A lock that records its release.</summary>
internal sealed class FakeLock : IDisposable
{
    public int DisposeCount { get; private set; }

    public void Dispose() => DisposeCount++;
}

/// <summary>Hands out the client streams the test adds.</summary>
internal sealed class FakeListener : IInstanceListener
{
    private readonly Channel<Stream> _clients = Channel.CreateUnbounded<Stream>();

    public bool Disposed { get; private set; }

    public void AddClient(Stream client) => _clients.Writer.TryWrite(client);

    public void AddFailure(Exception failure) => _clients.Writer.TryWrite(new FailingAccept(failure));

    public async Task<Stream> AcceptAsync(CancellationToken cancellationToken)
    {
        Stream next = await _clients.Reader.ReadAsync(cancellationToken);
        return next is FailingAccept failing ? throw failing.Failure : next;
    }

    public void Dispose()
    {
        Disposed = true;
        _clients.Writer.TryComplete();
    }

    private sealed class FailingAccept(Exception failure) : MemoryStream
    {
        public Exception Failure { get; } = failure;
    }
}

/// <summary>Collects received commands and lets a test wait for them.</summary>
internal sealed class ReceivedCommands
{
    private readonly Channel<InstanceCommand> _received = Channel.CreateUnbounded<InstanceCommand>();

    public void Add(InstanceCommand command) => _received.Writer.TryWrite(command);

    public async Task<InstanceCommand> NextAsync(TimeSpan timeout)
    {
        using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancel.CancelAfter(timeout);
        return await _received.Reader.ReadAsync(cancel.Token);
    }

    public bool TryNext(out InstanceCommand command) => _received.Reader.TryRead(out command);
}

/// <summary>
/// Credentials of the Unix socket transport (T-053) that can play another user: by default the real ones on macOS, elsewhere
/// a folder that exists counts as a private folder of <see cref="EffectiveUserId"/> and every peer as this user.
/// </summary>
internal sealed class FakeUnixCredentials : IUnixCredentials
{
    private readonly IUnixCredentials? _real;
    private int _peerChecks;

    public FakeUnixCredentials()
    {
        _real = RealOrNull;
        EffectiveUserId = _real?.EffectiveUserId ?? 501;
    }

    /// <summary>The real credentials on macOS, <see langword="null"/> elsewhere.</summary>
    public static IUnixCredentials? RealOrNull
    {
        get
        {
            if (OperatingSystem.IsMacOS())
            {
                return MacUnixCredentials.Current;
            }

            return null;
        }
    }

    public uint EffectiveUserId { get; init; }

    /// <summary>The user of each connected client; <see langword="null"/> result: the OS cannot tell. Unset: the real one.</summary>
    public Func<uint?>? Peer { get; init; }

    public int PeerChecks => Volatile.Read(ref _peerChecks);

    public bool TryGetPeerUserId(System.Net.Sockets.Socket socket, out uint userId)
    {
        Interlocked.Increment(ref _peerChecks);
        if (Peer is not null)
        {
            uint? peer = Peer();
            userId = peer ?? 0;
            return peer.HasValue;
        }

        if (_real is not null)
        {
            return _real.TryGetPeerUserId(socket, out userId);
        }

        userId = EffectiveUserId;
        return true;
    }

    public UnixEntry? GetEntry(string path) =>
        _real is not null
            ? _real.GetEntry(path)
            : Directory.Exists(path) ? new UnixEntry(UnixEntryKind.Directory, UnixSocketInstanceTransport.PrivateDirectoryMode, EffectiveUserId) : null;
}
