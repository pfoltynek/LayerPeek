namespace LayerPeek.App.SingleInstance;

/// <summary>
/// The OS primitives of the single-instance guard (T-043): a lock that only one instance per user and session holds, and a
/// local channel to that instance. Windows: named mutex + named pipe; macOS: lock file + Unix domain socket.
/// </summary>
internal interface IInstanceTransport
{
    /// <summary>Takes the instance lock (held until the result is disposed).</summary>
    /// <returns><see langword="null"/> when another instance holds it.</returns>
    /// <exception cref="UnauthorizedAccessException">
    /// The lock exists but this process may not open it (e.g. created by an elevated instance): another instance runs.
    /// </exception>
    /// <exception cref="IOException">
    /// The lock cannot be used for another reason (<see cref="InstanceLockUnavailableException"/>, a name taken by another
    /// kind of object...): the app runs without the guard.
    /// </exception>
    IDisposable? TryAcquireLock();

    /// <summary>Starts listening for commands; call while holding the lock. The channel exists when this returns.</summary>
    IInstanceListener Listen();

    /// <summary>Connects to the running instance, waiting at most <paramref name="timeout"/> for it to listen.</summary>
    /// <exception cref="TimeoutException">Nobody listened within <paramref name="timeout"/>.</exception>
    /// <exception cref="UnauthorizedAccessException">The running instance refuses this process (e.g. it is elevated).</exception>
    /// <exception cref="IOException">The connection failed; worth another attempt.</exception>
    Stream Connect(TimeSpan timeout);
}

/// <summary>The listening side of <see cref="IInstanceTransport"/>; disposing it stops listening and removes the channel.</summary>
internal interface IInstanceListener : IDisposable
{
    /// <summary>Waits for the next client and returns its connection (the caller disposes it).</summary>
    /// <exception cref="InstancePeerRefusedException">The client was refused (another user); accept the next one.</exception>
    Task<Stream> AcceptAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The instance lock cannot be used at all (e.g. no rights to its file or folder), which says nothing about another
/// instance: the app then runs without the guard instead of waiting for an instance that does not exist.
/// </summary>
internal sealed class InstanceLockUnavailableException : IOException
{
    public InstanceLockUnavailableException()
    {
    }

    public InstanceLockUnavailableException(string message)
        : base(message)
    {
    }

    public InstanceLockUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// A client of the listener was refused because it runs as another user (or its user cannot be told), T-053. The
/// connection is already closed, nothing was read; the server logs it and accepts the next client.
/// </summary>
internal sealed class InstancePeerRefusedException : IOException
{
    public InstancePeerRefusedException()
        : this(peerUserId: null)
    {
    }

    /// <param name="peerUserId">The effective user id of the client; <see langword="null"/>: the OS could not tell.</param>
    public InstancePeerRefusedException(uint? peerUserId)
        : base("A process of another user connected to the instance channel.")
    {
        PeerUserId = peerUserId;
    }

    public InstancePeerRefusedException(string message)
        : base(message)
    {
    }

    public InstancePeerRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The effective user id of the refused client; <see langword="null"/> when unknown.</summary>
    public uint? PeerUserId { get; }
}
