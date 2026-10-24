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
}
