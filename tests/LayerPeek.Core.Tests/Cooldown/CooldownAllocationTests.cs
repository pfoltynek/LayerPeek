using Microsoft.Extensions.Time.Testing;

using LayerPeek.Core.Cooldown;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Tests.Monitoring;
using LayerPeek.Tests.Allocation;

using static LayerPeek.Core.Tests.Cooldown.CooldownTestData;

namespace LayerPeek.Core.Tests.Cooldown;

/// <summary>Cool-down detection runs on every poll of every printer; isolated, see <see cref="AllocationSensitive"/>.</summary>
[Collection(nameof(AllocationSensitive))]
public sealed class CooldownAllocationTests
{
    [Fact]
    public void ProcessingWhileCoolingWithAnEstimateAllocatesNothing()
    {
        CooldownUpdate update = default;
        long allocated = AllocationProbe.MinAllocated(() =>
        {
            CooldownTracker tracker = new();
            tracker.Process(Printing(0));
            for (double t = 5; t < 60; t += PollSeconds)
            {
                tracker.Process(Cooling(PrinterState.Finished, 5, t));
            }

            PrinterSnapshot next = Cooling(PrinterState.Finished, 5, 60);
            return AllocationProbe.Measure(() => update = tracker.Process(next));
        });

        Assert.Equal(CooldownPhase.Cooling, update.State.Phase);
        Assert.NotNull(update.State.EstimatedRemaining);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void CoolingDownWithTheEventAllocatesNothing()
    {
        CooldownUpdate update = default;
        long allocated = AllocationProbe.MinAllocated(() =>
        {
            CooldownTracker tracker = new();
            tracker.Process(Printing(0));
            tracker.Process(Snap(PrinterState.Finished, 5, 200, 0, 60, Job()));
            tracker.Process(Snap(PrinterState.Finished, 10, 45, 0, 55, Job()));
            PrinterSnapshot next = Snap(PrinterState.Finished, 15, 44, 0, 54, Job());
            return AllocationProbe.Measure(() => update = tracker.Process(next));
        });

        Assert.Equal(CooldownPhase.CooledDown, update.State.Phase);
        Assert.NotNull(update.State.CooledDownAt);
        Assert.NotNull(update.Event);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ProcessingWithoutCoolDownAllocatesNothing()
    {
        CooldownUpdate update = default;
        long allocated = AllocationProbe.MinAllocated(() =>
        {
            CooldownTracker tracker = new();
            tracker.Process(Printing(0));
            PrinterSnapshot next = Printing(5);
            return AllocationProbe.Measure(() => update = tracker.Process(next));
        });

        Assert.Equal(CooldownState.None, update.State);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void MonitorAllocatesNothingPerSnapshot()
    {
        PrinterDefinition printer = new() { Id = "mk4", Name = "MK4", ConnectionType = ConnectionType.PrusaLink, Address = "h" };
        long allocated = AllocationProbe.MinAllocated(() =>
        {
            using CooldownMonitor monitor = new(new NullMonitor(), new FakeTimeProvider(Start), new ListLogger<CooldownMonitor>());
            int changes = 0;
            monitor.Changed += (_, _) => changes++;
            monitor.Configure([printer]);
            monitor.OnSnapshotUpdated(null, Printing(0));
            for (double t = 5; t < 60; t += PollSeconds)
            {
                monitor.OnSnapshotUpdated(null, Cooling(PrinterState.Finished, 5, t));
            }

            PrinterSnapshot next = Cooling(PrinterState.Finished, 5, 60);
            return AllocationProbe.Measure(() => monitor.OnSnapshotUpdated(null, next));
        });

        Assert.Equal(0, allocated);
    }

    private sealed class NullMonitor : Core.Monitoring.IPrinterMonitor
    {
        public event EventHandler<PrinterSnapshot>? SnapshotUpdated
        {
            add { }
            remove { }
        }

        public IReadOnlyList<PrinterSnapshot> Snapshots => [];

        public bool TryGetSnapshot(string printerId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out PrinterSnapshot? snapshot)
        {
            snapshot = null;
            return false;
        }

        public Task RunAsync(IReadOnlyList<PrinterDefinition> printers, TimeSpan pollingInterval, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
