#if DEBUG
using Avalonia.Threading;

namespace PrusaTrayDiagnostic.App.Composition;

/// <summary>
/// Debug builds only, in the isolated test mode: deliberate crashes for the end-to-end tests of T-046, so that a test can
/// see the stack trace of a real crash in the log. The exceptions come from named methods, whose names the stack trace
/// must show (also in Native AOT builds).
/// </summary>
internal static class TestCrash
{
    /// <summary>
    /// <c>ui</c>: an exception on the UI thread (ends the app); <c>background</c>: on a thread of its own (ends the app);
    /// <c>task</c>: a task nobody observes, collected at once (the app goes on). Anything else does nothing.
    /// </summary>
    public static void Run(string? kind)
    {
        switch (kind)
        {
            case "ui":
                Dispatcher.UIThread.Post(ThrowOnUiThread);
                break;
            case "background":
                new Thread(ThrowOnBackgroundThread) { IsBackground = true, Name = "E2E test crash" }.Start();
                break;
            case "task":
                _ = Task.Run(ThrowInUnobservedTask);
                _ = Task.Run(CollectUnobservedTasksAsync);
                break;
        }
    }

    private static void ThrowOnUiThread() => throw new InvalidOperationException("E2E test crash on the UI thread.");

    private static void ThrowOnBackgroundThread() => throw new InvalidOperationException("E2E test crash on a background thread.");

    private static void ThrowInUnobservedTask() => throw new InvalidOperationException("E2E test exception of an unobserved task.");

    /// <summary>
    /// The faulted task is reported when its finalizer runs: forced here instead of waiting for a collection, a few times
    /// in case a pool thread still held a reference at first.
    /// </summary>
    private static async Task CollectUnobservedTasksAsync()
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}
#endif
