namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// One kind of asynchronous native call whose completion handler is a global block without state (<see cref="ObjCBlocks"/>):
/// the calls take turns, so the reply that arrives belongs to the call that waits. A reply after the wait gave up is
/// dropped (or, at worst, answers the next call of the same kind with a value of the same question). Thread-safe;
/// independent of the OS (unit tests).
/// </summary>
internal sealed class NativeReply<T> : IDisposable
{
    private readonly SemaphoreSlim _turn = new(1, 1);
    private TaskCompletionSource<T>? _pending;

    /// <summary>Waits for its turn, calls <paramref name="send"/> and waits for <see cref="Complete"/>.</summary>
    /// <exception cref="TimeoutException">No reply within <paramref name="timeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<T> AskAsync(Action send, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(send);
        await _turn.WaitAsync(cancellationToken).ConfigureAwait(false);
        TaskCompletionSource<T> reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            Volatile.Write(ref _pending, reply);
            send();
            return await reply.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.CompareExchange(ref _pending, null, reply);
            _turn.Release();
        }
    }

    /// <summary>Releases the turn; the static replies of the app live as long as the process and are never disposed.</summary>
    public void Dispose() => _turn.Dispose();

    /// <summary>The reply of the native call (any thread); without a waiting call it is dropped. Never throws.</summary>
    public void Complete(T value) => Interlocked.Exchange(ref _pending, null)?.TrySetResult(value);
}
