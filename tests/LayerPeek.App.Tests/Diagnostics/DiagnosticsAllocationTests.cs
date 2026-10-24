using LayerPeek.App.Diagnostics.Chart;
using LayerPeek.Core.History;
using LayerPeek.Core.Printers;
using LayerPeek.Tests.Allocation;

using static LayerPeek.App.Tests.Diagnostics.TemperatureChartDataTests;
using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Diagnostics;

/// <summary>Zero-allocation guarantees of the diagnostics chart; isolated, see <see cref="AllocationSensitive"/>.</summary>
[Collection(nameof(AllocationSensitive))]
public sealed class DiagnosticsAllocationTests
{
    private const int UpdatesPerPass = 50;

    [Fact]
    public void SteadyStateChartUpdatesDoNotAllocate()
    {
        TelemetryHistory history = new();
        history.Configure([Definition()], TimeSpan.FromSeconds(1));
        int capacity = history.GetCapacity("mk4");
        PrinterSnapshot[] snapshots = new PrinterSnapshot[capacity + (3 * UpdatesPerPass)];
        for (int i = 0; i < snapshots.Length; i++)
        {
            snapshots[i] = Sample(i, nozzle: 200 + (i % 10), nozzleTarget: 215, bed: i % 100 == 0 ? null : 60, bedTarget: 60);
        }

        // Fill the history (the ring reaches its capacity) and warm up the chart buffers.
        for (int i = 0; i < capacity; i++)
        {
            history.Record(snapshots[i]);
        }

        TemperatureChartData data = new();
        data.Update(history, "mk4", T0, T0.AddSeconds(capacity), 640);

        int next = capacity;
        long allocated = AllocationProbe.MinAllocated(() =>
        {
            // Fresh input per pass: every update sees a new sample and a moved range.
            long bytes = 0;
            for (int end = next + UpdatesPerPass; next < end; next++)
            {
                history.Record(snapshots[next]);
                DateTimeOffset to = T0.AddSeconds(next);
                long before = GC.GetAllocatedBytesForCurrentThread();
                bool changed = data.Update(history, "mk4", to.AddDays(-1), to, 640);
                bytes += GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.True(changed);
            }

            return bytes;
        });

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ReusingLabelKeysDoesNotAllocate()
    {
        ChartLabelCache<long, string> cache = new(static key => "label " + key, capacity: 4);
        cache.Get(1);
        cache.Get(2);

        long allocated = AllocationProbe.MinAllocated(() => AllocationProbe.Measure(() =>
        {
            for (int i = 0; i < 1000; i++)
            {
                cache.Get(1);
                cache.Get(2);
            }
        }));

        Assert.Equal(0, allocated);
    }
}
