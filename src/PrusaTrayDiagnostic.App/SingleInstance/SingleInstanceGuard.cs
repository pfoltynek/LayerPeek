using Microsoft.Extensions.Logging;

namespace PrusaTrayDiagnostic.App.SingleInstance;

/// <summary>How a start of the app continues after <see cref="SingleInstanceGuard.Start"/>.</summary>
internal enum InstanceRole
{
    /// <summary>This is the only instance: it holds the lock and listens for later starts; the app runs.</summary>
    Primary,

    /// <summary>Another instance runs and got the command; this start exits (code 0).</summary>
    Forwarded,

    /// <summary>
    /// Another instance runs but could not be reached (it refused the connection, e.g. elevated, or did not listen in
    /// time); logged, and this start exits (code 0) anyway.
    /// </summary>
    NotForwarded,
}

/// <summary>
/// One instance per user and session (T-043). The first start takes the instance lock and listens; a later start sends
/// its command to it and exits. While the lock is taken but nobody listens yet (the first instance is just starting,
/// e.g. autostart together with a click), the later start keeps trying for <see cref="DefaultForwardTimeout"/>; if the
/// running instance exits meanwhile, the later start takes over as the primary instance. Synchronous: runs in
/// <c>Main</c> before Avalonia starts.
/// </summary>
internal sealed partial class SingleInstanceGuard : IDisposable
{
    /// <summary>How long a later start tries to reach the running instance in total.</summary>
    public static readonly TimeSpan DefaultForwardTimeout = TimeSpan.FromSeconds(5);

    /// <summary>One connection attempt; after it the lock is checked again (the running instance may have exited).</summary>
    public static readonly TimeSpan ConnectAttemptTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Pause between attempts that failed at once (e.g. the channel broke while connecting).</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    private readonly IInstanceTransport _transport;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly Action<TimeSpan> _sleep;
    private readonly TimeSpan _forwardTimeout;
    private IDisposable? _lock;
    private InstanceCommandServer? _server;

    /// <param name="sleep">Waits between attempts; tests advance a fake clock instead.</param>
    public SingleInstanceGuard(
        IInstanceTransport transport,
        ILogger logger,
        TimeProvider? time = null,
        Action<TimeSpan>? sleep = null,
        TimeSpan? forwardTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(logger);
        _transport = transport;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _sleep = sleep ?? Thread.Sleep;
        _forwardTimeout = forwardTimeout ?? DefaultForwardTimeout;
    }

    /// <summary>The server of the primary instance (for tests); <see langword="null"/> otherwise.</summary>
    internal InstanceCommandServer? Server => _server;

    /// <summary>
    /// Becomes the primary instance (listening; each received command goes to <paramref name="received"/>, on a
    /// background thread) or sends <paramref name="command"/> to the running one. Never throws for the expected OS
    /// failures; call once.
    /// </summary>
    public InstanceRole Start(InstanceCommand command, Action<InstanceCommand> received)
    {
        ArgumentNullException.ThrowIfNull(received);
        if (_lock is not null)
        {
            throw new InvalidOperationException("The guard was already started.");
        }

        long start = _time.GetTimestamp();
        Exception? lastFailure = null;
        while (true)
        {
            IDisposable? instanceLock;
            try
            {
                instanceLock = _transport.TryAcquireLock();
            }
            catch (UnauthorizedAccessException ex)
            {
                LogLockDenied(ex);
                return InstanceRole.NotForwarded;
            }
            catch (Exception ex) when (ex is IOException or WaitHandleCannotBeOpenedException)
            {
                // E.g. the name is taken by another kind of object: running unguarded beats not running at all.
                LogLockUnavailable(ex);
                return InstanceRole.Primary;
            }

            if (instanceLock is not null)
            {
                return BecomePrimary(instanceLock, received);
            }

            TimeSpan remaining = _forwardTimeout - _time.GetElapsedTime(start);
            if (remaining <= TimeSpan.Zero)
            {
                LogForwardTimedOut(_forwardTimeout, lastFailure);
                return InstanceRole.NotForwarded;
            }

            try
            {
                Send(command, remaining < ConnectAttemptTimeout ? remaining : ConnectAttemptTimeout);
                LogForwarded(command);
                return InstanceRole.Forwarded;
            }
            catch (UnauthorizedAccessException ex)
            {
                // E.g. the running instance is elevated: it runs, but cannot be told anything.
                LogConnectDenied(ex);
                return InstanceRole.NotForwarded;
            }
            catch (TimeoutException ex)
            {
                // Nobody listened within the attempt: check the lock again.
                lastFailure = ex;
            }
            catch (IOException ex)
            {
                lastFailure = ex;
                _sleep(RetryDelay);
            }
        }
    }

    /// <summary>Stops listening and releases the lock (the next start becomes the primary instance).</summary>
    public void Dispose()
    {
        _server?.Dispose();
        _server = null;
        _lock?.Dispose();
        _lock = null;
    }

    /// <summary>Like <see cref="Dispose"/>, waiting for the server's background task (tests).</summary>
    internal async ValueTask DisposeAsync()
    {
        if (_server is { } server)
        {
            _server = null;
            await server.DisposeAsync().ConfigureAwait(false);
        }

        Dispose();
    }

    private InstanceRole BecomePrimary(IDisposable instanceLock, Action<InstanceCommand> received)
    {
        _lock = instanceLock;
        try
        {
            _server = new InstanceCommandServer(_transport.Listen(), received, _logger);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Net.Sockets.SocketException)
        {
            // The app still runs (and holds the lock); only later starts cannot reach it.
            LogListenFailed(ex);
        }

        return InstanceRole.Primary;
    }

    private void Send(InstanceCommand command, TimeSpan timeout)
    {
        using Stream stream = _transport.Connect(timeout);
        stream.Write(InstanceProtocol.Encode(command));
        stream.Flush();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The app already runs; sent {Command} to it.")]
    private partial void LogForwarded(InstanceCommand command);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The app already runs, but its instance lock cannot be opened (another user rights level?); exiting.")]
    private partial void LogLockDenied(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "The instance lock cannot be taken; the app runs without the single-instance guard.")]
    private partial void LogLockUnavailable(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The app already runs, but refused the connection (another user rights level?); exiting.")]
    private partial void LogConnectDenied(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The app already runs, but could not be reached within {Timeout}; exiting.")]
    private partial void LogForwardTimedOut(TimeSpan timeout, Exception? exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Listening for later starts of the app failed; they will not reach this instance.")]
    private partial void LogListenFailed(Exception exception);
}
