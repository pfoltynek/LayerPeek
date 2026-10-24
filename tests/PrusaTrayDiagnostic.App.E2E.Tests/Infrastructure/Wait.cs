using System.Runtime.InteropServices;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>Polling with a deadline: the real app answers asynchronously (window creation, streams, settings saves).</summary>
internal static class Wait
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    public static Task UntilAsync(Func<bool> condition, TimeSpan timeout, string what) =>
        UntilAsync(() => Task.FromResult(condition()), timeout, what);

    /// <summary>
    /// Polls <paramref name="condition"/> until it holds or <paramref name="timeout"/> passes. A UI Automation call in the
    /// condition that times out (UIA_E_TIMEOUT: the window's UI thread was busy for a moment, e.g. while a system dialog of
    /// the app closes, T-073) counts as "not yet" and is asked again; when the deadline passes after one, the timeout
    /// names it. The timeout of a nested wait (<see cref="WaitTimeoutException"/>) ends this wait as well.
    /// </summary>
    /// <exception cref="WaitTimeoutException">The condition did not hold in time.</exception>
    public static async Task UntilAsync(Func<Task<bool>> condition, TimeSpan timeout, string what)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        Exception? busy = null;
        while (true)
        {
            try
            {
                if (await condition())
                {
                    return;
                }

                busy = null;
            }
            catch (Exception ex) when (IsUiaTimeout(ex))
            {
                busy = ex;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw busy is null
                    ? new WaitTimeoutException($"Waited {timeout.TotalSeconds:0.#} s for {what}.")
                    : new WaitTimeoutException($"Waited {timeout.TotalSeconds:0.#} s for {what}; the last UI Automation call timed out ({busy.GetType().Name}: {busy.Message}).", busy);
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

    /// <summary>
    /// A UI Automation call that timed out: UIA_E_TIMEOUT (0x80131505, the same value as COR_E_TIMEOUT) as a
    /// <see cref="COMException"/>, or as the <see cref="TimeoutException"/> the runtime or FlaUI makes of it. The timeout
    /// of a nested wait is not one.
    /// </summary>
    internal static bool IsUiaTimeout(Exception exception) => exception switch
    {
        WaitTimeoutException => false,
        COMException com => com.HResult == UiaETimeout,
        TimeoutException => true,
        _ => false,
    };

    /// <summary>UIA_E_TIMEOUT.</summary>
    private const int UiaETimeout = unchecked((int)0x80131505);
}

/// <summary>A <see cref="Wait"/> whose condition did not hold in time.</summary>
internal sealed class WaitTimeoutException : TimeoutException
{
    public WaitTimeoutException()
    {
    }

    public WaitTimeoutException(string message)
        : base(message)
    {
    }

    public WaitTimeoutException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
