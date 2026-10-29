namespace LayerPeek.App.Tray;

/// <summary>
/// The UI thread's dispatcher before Avalonia exists (T-099, macOS waiting for a display): work posted meanwhile is queued
/// and run on the main thread by <see cref="RunPending"/> (the startup loop of <c>Program.Main</c>); once Avalonia runs,
/// <see cref="AttachTo"/> runs what is still waiting and forwards everything later to Avalonia's dispatcher. The services
/// built before the UI (view models of the tray included) thus keep one UI thread, the main thread, all the time.
/// <see cref="Post"/> is thread-safe and never blocks; posting order is kept, also across <see cref="AttachTo"/>.
/// </summary>
/// <param name="wake">Called (any thread) after work was queued, so that the waiting main thread runs it soon.</param>
internal sealed class StartupUiDispatcher(Action? wake = null) : IUiDispatcher
{
    private readonly Lock _lock = new();
    private readonly Queue<Action> _pending = new();
    private IUiDispatcher? _target;

    /// <summary>Work waiting for <see cref="RunPending"/> or <see cref="AttachTo"/> (for tests and the log).</summary>
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

    /// <summary>Whether Avalonia's dispatcher took over.</summary>
    public bool IsAttached
    {
        get
        {
            lock (_lock)
            {
                return _target is not null;
            }
        }
    }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        IUiDispatcher? target;
        lock (_lock)
        {
            target = _target;
            if (target is null)
            {
                _pending.Enqueue(action);
            }
        }

        if (target is not null)
        {
            target.Post(action);
            return;
        }

        wake?.Invoke();
    }

    /// <summary>
    /// Main thread, before <see cref="AttachTo"/>: runs the queued work in posting order, also work posted while it runs.
    /// An exception of the work escapes (as it would from Avalonia's dispatcher: the crash is logged).
    /// </summary>
    /// <returns>How many actions ran.</returns>
    public int RunPending()
    {
        int ran = 0;
        while (TryTake() is { } next)
        {
            next();
            ran++;
        }

        return ran;
    }

    /// <summary>
    /// UI thread, once Avalonia runs: the work still waiting runs now, in order, and everything posted from now on goes to
    /// <paramref name="target"/>. Later work therefore never overtakes earlier work: it runs on the same thread after this
    /// call returns. Attach once.
    /// </summary>
    public void AttachTo(IUiDispatcher target)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (_lock)
        {
            if (_target is not null)
            {
                throw new InvalidOperationException("The startup dispatcher is already attached.");
            }

            // Work posted from now on is queued at the target, behind the work run below on this (its) thread.
            _target = target;
        }

        _ = RunPending();
    }

    private Action? TryTake()
    {
        lock (_lock)
        {
            return _pending.TryDequeue(out Action? action) ? action : null;
        }
    }
}
