using Microsoft.Extensions.Logging;

using LayerPeek.App.Tray;

namespace LayerPeek.App.SingleInstance;

/// <summary>
/// Hands commands of later starts (and the macOS reopen event) to the running app (T-043). Commands that arrive while the
/// app is still starting wait here until <see cref="Attach"/>; then each is delivered on the UI thread through the
/// dispatcher. The same command waits only once (two quick starts activate once). After <see cref="Close"/> (quitting)
/// commands are dropped. <see cref="Post"/> is thread-safe; the handler always runs on the UI thread.
/// </summary>
internal sealed partial class InstanceCommandQueue
{
    private readonly Lock _lock = new();
    private readonly List<InstanceCommand> _pending = [];
    private Action<InstanceCommand>? _handler;
    private IUiDispatcher? _dispatcher;
    private ILogger? _logger;
    private bool _provisional;
    private bool _closed;

    /// <summary>Commands waiting for the handler (for tests).</summary>
    public int PendingCount
    {
        get
        {
            lock (_lock)
            {
                return _pending.Count;
            }
        }
    }

    /// <summary>Any thread: queues <paramref name="command"/>, or posts it to the UI thread once a handler is attached.</summary>
    public void Post(InstanceCommand command)
    {
        IUiDispatcher dispatcher;
        lock (_lock)
        {
            if (_closed)
            {
                return;
            }

            if (_handler is null || _dispatcher is null)
            {
                if (!_pending.Contains(command))
                {
                    _pending.Add(command);
                }

                return;
            }

            dispatcher = _dispatcher;
        }

        dispatcher.Post(() => Deliver(command));
    }

    /// <summary>
    /// The app is ready: commands from now on (and those that waited) go to <paramref name="handler"/> on the UI thread.
    /// Attach once; only a provisional handler (<see cref="AttachProvisional"/>) may be replaced by it.
    /// </summary>
    /// <param name="logger">Records a handler failure (which never escapes into the dispatcher).</param>
    public void Attach(Action<InstanceCommand> handler, IUiDispatcher dispatcher, ILogger logger) =>
        AttachCore(handler, dispatcher, logger, provisional: false);

    /// <summary>
    /// The services run but the UI does not yet (T-099, macOS waiting for a display): commands go to
    /// <paramref name="handler"/> until <see cref="Attach"/> replaces it. Commands posted but not yet delivered then go to
    /// the new handler.
    /// </summary>
    public void AttachProvisional(Action<InstanceCommand> handler, IUiDispatcher dispatcher, ILogger logger) =>
        AttachCore(handler, dispatcher, logger, provisional: true);

    private void AttachCore(Action<InstanceCommand> handler, IUiDispatcher dispatcher, ILogger logger, bool provisional)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(logger);
        InstanceCommand[] waiting;
        lock (_lock)
        {
            if (_closed)
            {
                return;
            }

            if (_handler is not null && !_provisional)
            {
                throw new InvalidOperationException("A handler is already attached.");
            }

            if (_handler is not null && provisional)
            {
                throw new InvalidOperationException("A provisional handler is already attached.");
            }

            _handler = handler;
            _dispatcher = dispatcher;
            _logger = logger;
            _provisional = provisional;
            waiting = [.. _pending];
            _pending.Clear();
        }

        foreach (InstanceCommand command in waiting)
        {
            dispatcher.Post(() => Deliver(command));
        }
    }

    /// <summary>The app is quitting: waiting and later commands are dropped, also those already posted. Safe to repeat.</summary>
    public void Close()
    {
        lock (_lock)
        {
            _closed = true;
            _handler = null;
            _provisional = false;
            _pending.Clear();
        }
    }

    /// <summary>UI thread: the handler attached now gets the command (the final one, when it replaced a provisional one meanwhile).</summary>
    private void Deliver(InstanceCommand command)
    {
        Action<InstanceCommand>? handler;
        ILogger? logger;
        lock (_lock)
        {
            handler = _handler;
            if (_closed || handler is null)
            {
                return;
            }

            logger = _logger;
        }

        try
        {
            handler(command);
        }
        catch (Exception ex)
        {
            // A start from the jump list must never crash the running app.
            if (logger is not null)
            {
                LogHandlerFailed(logger, command, ex);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Handling the instance command {Command} failed.")]
    private static partial void LogHandlerFailed(ILogger logger, InstanceCommand command, Exception exception);
}
