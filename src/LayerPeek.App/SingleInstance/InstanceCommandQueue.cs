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
        Action<InstanceCommand> handler;
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

            handler = _handler;
            dispatcher = _dispatcher;
        }

        dispatcher.Post(() => Deliver(handler, command));
    }

    /// <summary>
    /// The app is ready: commands from now on (and those that waited) go to <paramref name="handler"/> on the UI thread.
    /// Attach once.
    /// </summary>
    /// <param name="logger">Records a handler failure (which never escapes into the dispatcher).</param>
    public void Attach(Action<InstanceCommand> handler, IUiDispatcher dispatcher, ILogger logger)
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

            if (_handler is not null)
            {
                throw new InvalidOperationException("A handler is already attached.");
            }

            _handler = handler;
            _dispatcher = dispatcher;
            _logger = logger;
            waiting = [.. _pending];
            _pending.Clear();
        }

        foreach (InstanceCommand command in waiting)
        {
            dispatcher.Post(() => Deliver(handler, command));
        }
    }

    /// <summary>The app is quitting: waiting and later commands are dropped, also those already posted. Safe to repeat.</summary>
    public void Close()
    {
        lock (_lock)
        {
            _closed = true;
            _handler = null;
            _pending.Clear();
        }
    }

    private void Deliver(Action<InstanceCommand> handler, InstanceCommand command)
    {
        ILogger? logger;
        lock (_lock)
        {
            if (_closed || !ReferenceEquals(_handler, handler))
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
