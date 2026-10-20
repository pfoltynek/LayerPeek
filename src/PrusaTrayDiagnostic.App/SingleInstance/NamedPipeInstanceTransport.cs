using System.IO.Pipes;
using System.Runtime.Versioning;

using PrusaTrayDiagnostic.App.Platform.Windows;

namespace PrusaTrayDiagnostic.App.SingleInstance;

/// <summary>
/// Windows (T-043): the lock is a named mutex in the session namespace, of which only creation counts (it is never
/// owned, so nothing can be abandoned); the channel is a named pipe that only the current user may open
/// (<see cref="PipeOptions.CurrentUserOnly"/>, on both ends). Both names carry the user's SID; the pipe name also the
/// session id (pipe names are machine-wide, <c>Local\</c> mutexes are per session already). Before sending, the client
/// allows the running instance to take the foreground, which it may then use to activate its window.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class NamedPipeInstanceTransport : IInstanceTransport
{
    private readonly string _mutexName;
    private readonly string _pipeName;

    /// <param name="mutexName">E.g. <c>Local\PrusaTrayDiagnostic-&lt;SID&gt;</c>.</param>
    /// <param name="pipeName">Without the <c>\\.\pipe\</c> prefix.</param>
    public NamedPipeInstanceTransport(string mutexName, string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        _mutexName = mutexName;
        _pipeName = pipeName;
    }

    public IDisposable? TryAcquireLock()
    {
        // initiallyOwned: false - only existence matters; the handle keeps the mutex alive until disposed or the process ends.
        Mutex mutex = new(initiallyOwned: false, _mutexName, out bool createdNew);
        if (createdNew)
        {
            return mutex;
        }

        mutex.Dispose();
        return null;
    }

    public IInstanceListener Listen() => new Listener(_pipeName);

    public Stream Connect(TimeSpan timeout)
    {
        NamedPipeClientStream client = new(".", _pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
        try
        {
            client.Connect(timeout);
            // The user started this process, so it may pass the foreground on to the running instance.
            if (WindowShellNative.TryGetPipeServerProcessId(client.SafePipeHandle, out uint serverProcessId))
            {
                WindowShellNative.AllowForeground(serverProcessId);
            }

            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Always keeps one waiting pipe instance: the next one is created before the connected one is handed out, so a
    /// client never finds no pipe while the server handles another.
    /// </summary>
    private sealed class Listener : IInstanceListener
    {
        private readonly string _pipeName;
        private readonly Lock _lock = new();
        private NamedPipeServerStream? _waiting;
        private bool _disposed;

        public Listener(string pipeName)
        {
            _pipeName = pipeName;
            _waiting = Create();
        }

        public async Task<Stream> AcceptAsync(CancellationToken cancellationToken)
        {
            NamedPipeServerStream server;
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                server = _waiting ?? Create();
                _waiting = null;
            }

            try
            {
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                server.Dispose();
                throw;
            }

            lock (_lock)
            {
                if (!_disposed)
                {
                    try
                    {
                        _waiting = Create();
                    }
                    catch (IOException)
                    {
                        // Created again by the next accept.
                    }
                }
            }

            return server;
        }

        public void Dispose()
        {
            NamedPipeServerStream? waiting;
            lock (_lock)
            {
                _disposed = true;
                waiting = _waiting;
                _waiting = null;
            }

            waiting?.Dispose();
        }

        private NamedPipeServerStream Create() => new(
            _pipeName,
            PipeDirection.In,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    }
}
