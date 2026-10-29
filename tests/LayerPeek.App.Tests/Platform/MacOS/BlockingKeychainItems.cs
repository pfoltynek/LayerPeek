using LayerPeek.App.Platform.MacOS;

namespace LayerPeek.App.Tests.Platform.MacOS;

/// <summary>
/// <see cref="IKeychainItems"/> whose every call blocks until <see cref="Release"/>, like a Keychain call that waits for
/// the user to answer a macOS dialog. Records the thread of each call and whether it ran inline, inside a store call
/// made through <see cref="FromCaller{T}"/>; never touches the real Keychain.
/// </summary>
internal sealed class BlockingKeychainItems : IKeychainItems, IDisposable
{
    [ThreadStatic]
    private static bool t_insideCaller;

    private int _inlineCalls;

    private readonly ManualResetEventSlim _release = new(initialState: false);
    private readonly SemaphoreSlim _entered = new(0);
    private readonly Dictionary<(string Service, string Account), string> _items = [];

    /// <summary>Managed thread ids of the calls, in order.</summary>
    public List<int> CallThreads { get; } = [];

    /// <summary>Calls that ran synchronously inside <see cref="FromCaller{T}"/> (on the caller's stack).</summary>
    public int InlineCalls => Volatile.Read(ref _inlineCalls);

    /// <summary>
    /// Makes a store call the way a caller (e.g. the UI thread) does. A Keychain call that runs before the store call
    /// returns counts as <see cref="InlineCalls"/> (thread ids alone cannot tell: the pool may reuse the caller's thread later).
    /// </summary>
    public static T FromCaller<T>(Func<T> storeCall)
    {
        t_insideCaller = true;
        try
        {
            return storeCall();
        }
        finally
        {
            t_insideCaller = false;
        }
    }

    /// <summary>Waits until a call is inside the "native" part (blocked).</summary>
    public Task<bool> WaitUntilBlockedAsync(TimeSpan timeout) => _entered.WaitAsync(timeout);

    /// <summary>Lets all current and later calls finish.</summary>
    public void Release() => _release.Set();

    public string? Read(string service, string account)
    {
        Block();
        lock (_items)
        {
            return _items.GetValueOrDefault((service, account));
        }
    }

    public void Write(string service, string account, string secret, int byteCount)
    {
        Block();
        lock (_items)
        {
            _items[(service, account)] = secret;
        }
    }

    public bool Delete(string service, string account)
    {
        Block();
        lock (_items)
        {
            return _items.Remove((service, account));
        }
    }

    public void Dispose()
    {
        _release.Set();
        _release.Dispose();
        _entered.Dispose();
    }

    private void Block()
    {
        lock (CallThreads)
        {
            CallThreads.Add(Environment.CurrentManagedThreadId);
        }

        if (t_insideCaller)
        {
            Interlocked.Increment(ref _inlineCalls);
            // Inline means the caller would now be blocked; do not hang the test, report it.
            return;
        }

        _entered.Release();
        // Bounded so that a broken test cannot hang the run.
        if (!_release.Wait(TimeSpan.FromSeconds(30)))
        {
            throw new TimeoutException("The blocking Keychain fake was never released.");
        }
    }
}
