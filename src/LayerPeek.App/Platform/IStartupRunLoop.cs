namespace LayerPeek.App.Platform;

/// <summary>
/// The run loop of the main thread while the app waits before Avalonia starts (T-099, macOS without an active display):
/// the main thread keeps handling what the OS delivers there (the main dispatch queue: callbacks of the notification
/// center) and wakes up for work posted to it. macOS: <see cref="MacOS.MacStartupRunLoop"/>; elsewhere and in tests
/// <see cref="ManagedStartupRunLoop"/>. Create, run and dispose on the thread it runs on; <see cref="Wake"/> from any thread.
/// </summary>
internal interface IStartupRunLoop : IDisposable
{
    /// <summary>Runs the loop on the calling thread for at most <paramref name="timeout"/>; returns early after <see cref="Wake"/>.</summary>
    void RunFor(TimeSpan timeout);

    /// <summary>
    /// Any thread: the current or next <see cref="RunFor"/> returns soon (a wake before it is not lost). Does nothing after
    /// <see cref="IDisposable.Dispose"/>.
    /// </summary>
    void Wake();
}

/// <summary>A run loop of nothing but a wake-up signal: tests, and the fallback when the system's cannot be set up.</summary>
internal sealed class ManagedStartupRunLoop : IStartupRunLoop
{
    private readonly SemaphoreSlim _wake = new(0, 1);
    private int _disposed;

    public void RunFor(TimeSpan timeout)
    {
        if (Volatile.Read(ref _disposed) == 0 && timeout > TimeSpan.Zero)
        {
            _ = _wake.Wait(timeout);
        }
    }

    public void Wake()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            _ = _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already signalled: the next RunFor returns at once anyway.
        }
        catch (ObjectDisposedException)
        {
            // Disposed meanwhile.
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _wake.Dispose();
        }
    }
}
