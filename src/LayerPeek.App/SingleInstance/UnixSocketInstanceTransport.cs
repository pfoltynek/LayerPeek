using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;

namespace LayerPeek.App.SingleInstance;

/// <summary>
/// macOS (T-043, verified on a Mac in T-018/T-019): the lock is a lock file held open with <see cref="FileShare.None"/>
/// (an exclusive <c>flock</c> in .NET on Unix, released by the OS when the process ends, so a crash leaves nothing
/// locked); the channel is a Unix domain socket in the user's own folder. The lock file stays on disk; the socket file is
/// removed when the listener stops, and a stale one (after a crash) is replaced by the next primary instance, which holds
/// the lock. Unix domain sockets also exist on Windows 10+, so the logic is tested there too.
/// <para>
/// With <see cref="IUnixCredentials"/> (macOS, T-053, finding L2) the socket is trusted only in a folder of this user: before
/// the lock is taken and before listening, the socket's folder must be a real folder (not a symbolic link) owned by the
/// effective user and not writable by anybody else; a private folder (the fallback in the temp folder) must be exactly
/// <c>0700</c>. Otherwise the guard is off (<see cref="InstanceLockUnavailableException"/>, the app runs unguarded). The
/// listener closes connections from another user without reading them (<see cref="InstancePeerRefusedException"/>).
/// Without credentials (Windows, Linux) none of this is checked.
/// </para>
/// </summary>
internal sealed class UnixSocketInstanceTransport : IInstanceTransport
{
    /// <summary>Pause between connection attempts while nobody listens yet.</summary>
    private static readonly TimeSpan ConnectRetryDelay = TimeSpan.FromMilliseconds(100);

    /// <summary>The only mode a private socket folder may have.</summary>
    internal const UnixFileMode PrivateDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private const UnixFileMode GroupAndOthers =
        UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    private const UnixFileMode WritableByOthers = UnixFileMode.GroupWrite | UnixFileMode.OtherWrite;

    private readonly string _lockPath;
    private readonly string _socketPath;
    private readonly IUnixCredentials? _credentials;
    private readonly bool _privateSocketDirectory;

    /// <param name="credentials">Who may connect and own the socket's folder; <see langword="null"/>: nothing is checked.</param>
    /// <param name="privateSocketDirectory">
    /// The socket's folder belongs to the socket alone: created <c>0700</c> and accepted only as exactly <c>0700</c>.
    /// </param>
    public UnixSocketInstanceTransport(string lockPath, string socketPath, IUnixCredentials? credentials = null, bool privateSocketDirectory = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(socketPath);
        _lockPath = lockPath;
        _socketPath = socketPath;
        _credentials = credentials;
        _privateSocketDirectory = privateSocketDirectory;
    }

    /// <exception cref="InstanceLockUnavailableException">
    /// The lock file cannot be used at all (no rights, the folder cannot be created...): not another instance.
    /// </exception>
    public IDisposable? TryAcquireLock()
    {
        PrepareSocketDirectory();
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

    /// <exception cref="InstanceLockUnavailableException">The socket's folder is not (or no longer) trusted.</exception>
    public IInstanceListener Listen()
    {
        if (_credentials is not null)
        {
            // Again: the folder could have been replaced since the lock was taken.
            PrepareSocketDirectory();
        }
        else if (Path.GetDirectoryName(_socketPath) is { Length: > 0 } directory)
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
            return new Listener(socket, _socketPath, _credentials);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Why <paramref name="entry"/> is no folder to keep the socket in, or <see langword="null"/> when it is one: a real
    /// folder of <paramref name="userId"/>, writable by nobody else, and with no rights for anybody else when private.
    /// </summary>
    internal static string? SocketDirectoryProblem(UnixEntry? entry, uint userId, bool privateDirectory)
    {
        if (entry is not { } existing)
        {
            return "does not exist";
        }

        if (existing.Kind == UnixEntryKind.SymbolicLink)
        {
            return "is a symbolic link";
        }

        if (existing.Kind != UnixEntryKind.Directory)
        {
            return "is not a folder";
        }

        if (existing.OwnerId != userId)
        {
            return string.Create(CultureInfo.InvariantCulture, $"belongs to another user (uid {existing.OwnerId}, this process {userId})");
        }

        UnixFileMode forbidden = privateDirectory ? GroupAndOthers : WritableByOthers;
        if ((existing.Mode & forbidden) != 0)
        {
            return "is open to other users (mode " + Convert.ToString((int)existing.Mode, 8) + (privateDirectory ? ", must be 700)" : ")");
        }

        return null;
    }

    /// <summary>Creates the socket's folder when missing and checks it (with credentials only).</summary>
    /// <exception cref="InstanceLockUnavailableException">The folder cannot be created or is not trusted.</exception>
    private void PrepareSocketDirectory()
    {
        if (_credentials is null)
        {
            return;
        }

        string directory = Path.GetDirectoryName(Path.GetFullPath(_socketPath))
            ?? throw new InstanceLockUnavailableException($"The instance socket '{_socketPath}' has no folder.");
        string? problem;
        try
        {
            if (_privateSocketDirectory && !OperatingSystem.IsWindows())
            {
                // Only a missing folder is created (0700, the umask cannot widen it); an existing one is checked as it is.
                Directory.CreateDirectory(directory, PrivateDirectoryMode);
            }
            else
            {
                Directory.CreateDirectory(directory);
            }

            problem = SocketDirectoryProblem(_credentials.GetEntry(directory), _credentials.EffectiveUserId, _privateSocketDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InstanceLockUnavailableException($"The folder of the instance socket '{directory}' cannot be used.", ex);
        }

        if (problem is not null)
        {
            // Another user could take over or replace the socket there: no guard rather than a guard somebody else controls.
            throw new InstanceLockUnavailableException($"The folder of the instance socket '{directory}' {problem}.");
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
        private readonly IUnixCredentials? _credentials;
        private int _disposed;

        public Listener(Socket socket, string path, IUnixCredentials? credentials)
        {
            _socket = socket;
            _path = path;
            _credentials = credentials;
        }

        /// <exception cref="InstancePeerRefusedException">The client is another user (or cannot be identified); already closed.</exception>
        public async Task<Stream> AcceptAsync(CancellationToken cancellationToken)
        {
            Socket client = await _socket.AcceptAsync(cancellationToken).ConfigureAwait(false);
            if (_credentials is not null)
            {
                bool known = _credentials.TryGetPeerUserId(client, out uint peer);
                if (!known || peer != _credentials.EffectiveUserId)
                {
                    // Closed before anything is read: another user's process never gets its bytes into this one.
                    client.Dispose();
                    throw new InstancePeerRefusedException(known ? peer : null);
                }
            }

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
