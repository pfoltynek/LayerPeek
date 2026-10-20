using System.Net.Sockets;

namespace PrusaTrayDiagnostic.App.SingleInstance;

/// <summary>
/// macOS (T-043, verified on a Mac in T-018/T-019): the lock is a lock file held open with <see cref="FileShare.None"/>
/// (an exclusive <c>flock</c> in .NET on Unix, released by the OS when the process ends, so a crash leaves nothing
/// locked); the channel is a Unix domain socket in the user's own folder. The lock file stays on disk; the socket file is
/// removed when the listener stops, and a stale one (after a crash) is replaced by the next primary instance, which holds
/// the lock. Unix domain sockets also exist on Windows 10+, so the logic is tested there too.
/// </summary>
internal sealed class UnixSocketInstanceTransport : IInstanceTransport
{
    /// <summary>Pause between connection attempts while nobody listens yet.</summary>
    private static readonly TimeSpan ConnectRetryDelay = TimeSpan.FromMilliseconds(100);

    private readonly string _lockPath;
    private readonly string _socketPath;

    public UnixSocketInstanceTransport(string lockPath, string socketPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(socketPath);
        _lockPath = lockPath;
        _socketPath = socketPath;
    }

    public IDisposable? TryAcquireLock()
    {
        if (Path.GetDirectoryName(_lockPath) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }

        try
        {
            return new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            // Locked by the running instance (a sharing violation on Windows, EWOULDBLOCK of flock on Unix).
            return null;
        }
    }

    public IInstanceListener Listen()
    {
        if (Path.GetDirectoryName(_socketPath) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }

        // Only the lock holder gets here, so an existing socket file is a leftover of a crashed instance.
        File.Delete(_socketPath);
        Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            socket.Bind(new UnixDomainSocketEndPoint(_socketPath));
            socket.Listen(4);
            return new Listener(socket, _socketPath);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public Stream Connect(TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                socket.Connect(new UnixDomainSocketEndPoint(_socketPath));
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                if (ex.SocketErrorCode == SocketError.AccessDenied)
                {
                    throw new UnauthorizedAccessException("The running instance refused the connection.", ex);
                }

                // No socket file yet, or nobody listening on it yet (the running instance is still starting).
                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException("The running instance did not listen in time.", ex);
                }

                Thread.Sleep(ConnectRetryDelay);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    }

    private sealed class Listener : IInstanceListener
    {
        private readonly Socket _socket;
        private readonly string _path;
        private int _disposed;

        public Listener(Socket socket, string path)
        {
            _socket = socket;
            _path = path;
        }

        public async Task<Stream> AcceptAsync(CancellationToken cancellationToken)
        {
            Socket client = await _socket.AcceptAsync(cancellationToken).ConfigureAwait(false);
            return new NetworkStream(client, ownsSocket: true);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _socket.Dispose();
            try
            {
                File.Delete(_path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Replaced by the next primary instance.
            }
        }
    }
}
