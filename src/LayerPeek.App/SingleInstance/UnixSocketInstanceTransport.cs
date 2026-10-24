using System.Diagnostics;
using System.Net.Sockets;

namespace LayerPeek.App.SingleInstance;

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

    /// <exception cref="InstanceLockUnavailableException">
    /// The lock file cannot be used at all (no rights, the folder cannot be created...): not another instance.
    /// </exception>
    public IDisposable? TryAcquireLock()
    {
        try
        {
            if (Path.GetDirectoryName(_lockPath) is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
            }

            return new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex) when (IsLockContention(ex))
        {
            // Locked by the running instance.
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InstanceLockUnavailableException($"The instance lock file '{_lockPath}' cannot be used.", ex);
        }
    }

    /// <summary>
    /// Whether opening the lock file failed only because another process holds it: a sharing or lock violation on
    /// Windows; on Unix .NET reports the failed <c>flock</c> as an <see cref="IOException"/> carrying the raw errno
    /// EWOULDBLOCK (35 on macOS, 11 on Linux).
    /// </summary>
    internal static bool IsLockContention(IOException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        const int ErrorSharingViolation = unchecked((int)0x80070020);
        const int ErrorLockViolation = unchecked((int)0x80070021);
        const int MacWouldBlock = 35;
        const int LinuxWouldBlock = 11;
        if (exception is FileNotFoundException or DirectoryNotFoundException or PathTooLongException)
        {
            return false;
        }

        return OperatingSystem.IsWindows()
            ? exception.HResult is ErrorSharingViolation or ErrorLockViolation
            : exception.HResult == (OperatingSystem.IsMacOS() ? MacWouldBlock : LinuxWouldBlock);
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
        long start = Stopwatch.GetTimestamp();
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
                if (Stopwatch.GetElapsedTime(start) >= timeout)
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
