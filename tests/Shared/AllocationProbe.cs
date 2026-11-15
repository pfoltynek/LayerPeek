namespace LayerPeek.Tests.Allocation;

/// <summary>
/// Collection of the zero-allocation tests. It runs without any other test in parallel: allocations and GCs on other
/// threads can make <see cref="GC.GetAllocatedBytesForCurrentThread"/> include the unused rest of this thread's
/// allocation context (~8 KB) although the measured code allocated nothing.
/// </summary>
[CollectionDefinition(nameof(AllocationSensitive), DisableParallelization = true)]
public sealed class AllocationSensitive;

/// <summary>Measures allocations of a code block robustly (see <see cref="AllocationSensitive"/>).</summary>
internal static class AllocationProbe
{
    /// <summary>Bytes allocated by <paramref name="action"/> on the calling thread.</summary>
    public static long Measure(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    /// <summary>
    /// Runs <paramref name="measuredPass"/> (which prepares its own fresh input and returns the bytes allocated by the
    /// measured part, e.g. via <see cref="Measure"/>) up to <paramref name="attempts"/> times and returns the minimum.
    /// A real allocation shows on every pass; a miscount caused by another thread does not repeat on every pass.
    /// Stops early at 0.
    /// </summary>
    public static long MinAllocated(Func<long> measuredPass, int attempts = 3)
    {
        ArgumentNullException.ThrowIfNull(measuredPass);
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        long minimum = long.MaxValue;
        for (int i = 0; i < attempts && minimum > 0; i++)
        {
            minimum = Math.Min(minimum, measuredPass());
        }

        return minimum;
    }

    /// <summary>How long no thread of the process may allocate for <see cref="WaitForQuietProcess"/> to return.</summary>
    public static readonly TimeSpan QuietWindow = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Timeout of <see cref="WaitForQuietBeforeTest"/>, before the test creates anything: long enough for the test
    /// platform's backlog on a loaded machine (13–17 s seen on 4 loaded CPUs, more on the CI runner).
    /// </summary>
    public static readonly TimeSpan QuietBeforeTest = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Timeout of <see cref="WaitForQuietProcess"/> right before a pass, with the code under test running but idle: shorter
    /// than the frame timeout of a camera feed on the real clock (<c>CameraFeedOptions.FrameTimeout</c>, 10 s), so that
    /// waiting never makes the feed reconnect.
    /// </summary>
    public static readonly TimeSpan QuietBeforePass = TimeSpan.FromSeconds(5);

    // Waits of the current test that timed out (the collection runs one test at a time).
    private static int s_notQuiet;

    /// <summary>
    /// <see cref="WaitForQuietProcess"/> with <see cref="QuietBeforeTest"/> at the start of a test, before it creates
    /// anything; also starts the count behind <see cref="QuietNote"/> for this test.
    /// </summary>
    public static void WaitForQuietBeforeTest()
    {
        Volatile.Write(ref s_notQuiet, 0);
        WaitForQuietProcess(QuietBeforeTest);
    }

    /// <summary>
    /// T-220: blocks (sleeping, allocation free) until no thread of the process allocated for <see cref="QuietWindow"/>, at
    /// most <paramref name="timeout"/> (<see cref="QuietBeforePass"/>; at the start of a test
    /// <see cref="WaitForQuietBeforeTest"/>). Call it right before each pass that counts the allocations of all threads
    /// (<see cref="GC.GetTotalAllocatedBytes(bool)"/>), with the code under test idle, never inside the measured part.
    /// Throws when the test is cancelled.
    /// </summary>
    /// <remarks>
    /// The test platform keeps sending the results of the tests that ran before over its pipe to <c>dotnet test</c> on
    /// thread-pool threads (Microsoft.Testing.Platform.IPC: <c>SuccessfulTestResultMessage</c>, <c>TestNodeDetails</c>,
    /// <c>NamedPipeConnectionBase.WriteMessageAsync</c>); on a loaded machine (the 4-CPU CI runner) that backlog drains for
    /// many seconds after this collection started and adds ~200 KB to every pass. Waiting for it to drain keeps the
    /// guarantee intact: the pass still counts every thread, it only no longer starts in the middle of foreign work.
    /// A wait that times out is counted for <see cref="QuietNote"/>.
    /// </remarks>
    /// <returns><see langword="false"/> when the process kept allocating until the timeout (the pass then measures anyway).</returns>
    public static bool WaitForQuietProcess(TimeSpan timeout)
    {
        CancellationToken cancellation = TestContext.Current.CancellationToken;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        long quietSince = started;
        long last = GC.GetTotalAllocatedBytes(precise: true);
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            Thread.Sleep(10);
            long now = GC.GetTotalAllocatedBytes(precise: true);
            if (now != last)
            {
                last = now;
                quietSince = System.Diagnostics.Stopwatch.GetTimestamp();
            }
            else if (System.Diagnostics.Stopwatch.GetElapsedTime(quietSince) >= QuietWindow)
            {
                return true;
            }

            if (System.Diagnostics.Stopwatch.GetElapsedTime(started) >= timeout)
            {
                Interlocked.Increment(ref s_notQuiet);
                return false;
            }
        }
    }

    /// <summary>
    /// For a failure message: empty when every <see cref="WaitForQuietProcess"/> of the current test found the process
    /// quiet, otherwise how many timed out (other threads may then have allocated inside a pass).
    /// </summary>
    public static string QuietNote
    {
        get
        {
            int notQuiet = Volatile.Read(ref s_notQuiet);
            return notQuiet == 0
                ? string.Empty
                : $" {notQuiet} wait(s) for a quiet process timed out (WaitForQuietProcess; the start of the test included), so threads outside the code under test may have allocated during a pass.";
        }
    }

    /// <summary>Asserts that a measurement (e.g. the minimum of the passes) allocated nothing; the message carries <see cref="QuietNote"/>.</summary>
    public static void AssertNoneAllocated(long allocated) =>
        Assert.True(allocated == 0, $"Allocated {allocated} B (expected 0).{QuietNote}");
}
