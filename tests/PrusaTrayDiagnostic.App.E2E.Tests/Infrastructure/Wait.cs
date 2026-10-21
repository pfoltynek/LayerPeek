namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>Polling with a deadline: the real app answers asynchronously (window creation, streams, settings saves).</summary>
internal static class Wait
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    public static Task UntilAsync(Func<bool> condition, TimeSpan timeout, string what) =>
        UntilAsync(() => Task.FromResult(condition()), timeout, what);

    public static async Task UntilAsync(Func<Task<bool>> condition, TimeSpan timeout, string what)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            if (await condition())
            {
                return;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"Waited {timeout.TotalSeconds:0.#} s for {what}.");
            }

            await Task.Delay(Interval);
        }
    }

    /// <summary>Waits until <paramref name="read"/> returns the same value twice in a row (layout settled).</summary>
    public static async Task<T> StableAsync<T>(Func<T> read, TimeSpan timeout, string what)
    {
        T previous = read();
        T current = previous;
        await UntilAsync(
            () =>
            {
                current = read();
                bool stable = EqualityComparer<T>.Default.Equals(current, previous);
                previous = current;
                return stable;
            },
            timeout,
            what);
        return current;
    }
}
