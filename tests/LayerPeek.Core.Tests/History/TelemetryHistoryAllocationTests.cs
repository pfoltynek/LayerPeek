using LayerPeek.Core.History;
using LayerPeek.Core.Printers;
using LayerPeek.Tests.Allocation;

using static LayerPeek.Core.Tests.History.TelemetryHistoryTests;

namespace LayerPeek.Core.Tests.History;

/// <summary>Allocation guarantees of <see cref="TelemetryHistory"/>; isolated, see <see cref="AllocationSensitive"/>.</summary>
[Collection(nameof(AllocationSensitive))]
public sealed class TelemetryHistoryAllocationTests
{
    [Fact]
    public void MemoryGrowsWithSamplesNotWithCapacity()
    {
        // A one-day history at 1 s holds up to ~4 MB; a fresh printer must not allocate it up front.
        long allocated = AllocationProbe.MinAllocated(() => AllocationProbe.Measure(() =>
        {
            TelemetryHistory history = new();
            history.Configure([Mk4], Second);
            history.Record(Snapshot(Start, 1));
        }));

        Assert.InRange(allocated, 0, 64 * 1024);
    }

    [Fact]
    public void RecordDoesNotAllocateInSteadyState()
    {
        const int PerPass = 32;
        TelemetryHistory history = Create(TimeSpan.FromSeconds(10), Second, Mk4);
        history.Changed += static (_, _) => { };
        PrinterSnapshot[] snapshots = new PrinterSnapshot[PerPass * 4];
        for (int i = 0; i < snapshots.Length; i++)
        {
            snapshots[i] = Snapshot(Start + (i * Second), i);
        }

        // Fill and wrap the buffer first (growth allocates).
        for (int i = 0; i < PerPass; i++)
        {
            history.Record(snapshots[i]);
        }

        int next = PerPass;
        long allocated = AllocationProbe.MinAllocated(() =>
        {
            // Fresh input per pass: newer samples than in any previous pass.
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int end = next + PerPass; next < end; next++)
            {
                history.Record(snapshots[next]);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        });

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ReadingDoesNotAllocate()
    {
        TelemetryHistory history = Create(TimeSpan.FromSeconds(10), Second, Mk4);
        PrinterSnapshot[] snapshots = new PrinterSnapshot[24];
        for (int i = 0; i < snapshots.Length; i++)
        {
            snapshots[i] = Snapshot(Start + (i * Second), i);
        }

        for (int i = 0; i < 20; i++)
        {
            history.Record(snapshots[i]);
        }

        TelemetrySample[] destination = new TelemetrySample[16];
        DateTimeOffset to = Start + TimeSpan.FromMinutes(1);
        history.CopySamples("mk4", Start, to, destination, out _);

        int next = 20;
        int count = 0;
        int copied = 0;
        long version = 0;
        long allocated = AllocationProbe.MinAllocated(() =>
        {
            // Fresh input per pass: one more sample (recorded outside the measurement).
            history.Record(snapshots[next++]);
            long before = GC.GetAllocatedBytesForCurrentThread();
            count = history.CountSamples("mk4", Start, to);
            copied = history.CopySamples("mk4", Start, to, destination, out _);
            version = history.GetVersion("mk4");
            return GC.GetAllocatedBytesForCurrentThread() - before;
        });

        Assert.Equal(0, allocated);
        Assert.Equal(11, count);
        Assert.Equal(11, copied);
        Assert.True(version > 0);
    }
}
