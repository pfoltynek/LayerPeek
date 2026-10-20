namespace PrusaTrayDiagnostic.App.SingleInstance;

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
    Task<Stream> AcceptAsync(CancellationToken cancellationToken);
}
