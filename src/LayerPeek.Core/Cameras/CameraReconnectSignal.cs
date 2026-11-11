namespace LayerPeek.Core.Cameras;

/// <summary>
/// Ends the wait of a <see cref="ReconnectingCameraFeed"/> between two connection attempts (T-188, user decision
/// 2026-11-11: opening or restoring the camera window retries at once): the next attempt starts right away and the backoff
/// starts over. <see cref="CameraStreamHub"/> keeps one per printer and passes it to every connection of that printer.
/// </summary>
/// <remarks>
/// Only a wait is ended: while an attempt connects or streams (or nothing reads), <see cref="Retry"/> does nothing and is
/// not remembered, so a failing attempt is still followed by its normal delay. The wait stays the feed's single
/// <c>Task.Delay</c>; this signal only cancels its token. Nothing polls, nothing runs while nobody calls it. Thread-safe and
/// never blocks: the next attempt runs on the thread pool, never on the caller's thread (connecting may block in the decoder).
/// </remarks>
public sealed class CameraReconnectSignal
{
    private readonly Lock _gate = new();

    // The token source of the delay the feed is waiting in; guarded by _gate.
    private CancellationTokenSource? _wait;

    /// <summary>Starts the next attempt now if the feed is waiting for it; any thread.</summary>
    /// <returns>Whether a wait was ended (it was not ended before, and the feed is not stopping).</returns>
    public bool Retry()
    {
        lock (_gate)
        {
            // Also cancelled when the feed stops (linked to its stop token): nothing to shorten then.
            if (_wait is not { IsCancellationRequested: false } wait)
            {
                return false;
            }

            // Asynchronously: the delay's continuation (the next attempt) must not run on this thread, which may hold the
            // hub's lock. Under _gate, so the feed cannot dispose the source meanwhile (EndWait takes _gate first).
            _ = wait.CancelAsync();
            return true;
        }
    }

    /// <summary>Feed: waits in a delay with <paramref name="wait"/>'s token from now on.</summary>
    internal void BeginWait(CancellationTokenSource wait)
    {
        lock (_gate)
        {
            _wait = wait;
        }
    }

    /// <summary>Feed: the wait with <paramref name="wait"/> is over (before the source is disposed).</summary>
    internal void EndWait(CancellationTokenSource wait)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_wait, wait))
            {
                _wait = null;
            }
        }
    }
}
