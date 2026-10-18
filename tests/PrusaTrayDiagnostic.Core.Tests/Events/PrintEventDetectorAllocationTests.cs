using PrusaTrayDiagnostic.Core.Events;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Tests.Allocation;

using static PrusaTrayDiagnostic.Core.Tests.Events.PrintEventDetectorTests;

namespace PrusaTrayDiagnostic.Core.Tests.Events;

/// <summary>Allocation guarantees of <see cref="PrintEventDetector"/>; isolated, see <see cref="AllocationSensitive"/>.</summary>
[Collection(nameof(AllocationSensitive))]
public sealed class PrintEventDetectorAllocationTests
{
    [Fact]
    public void NoEventsAreAllocatedWithoutChange()
    {
        IReadOnlyList<PrintEvent> events = [];
        long allocated = AllocationProbe.MinAllocated(() =>
        {
            // Fresh input per pass: a new detector in the same steady state.
            PrintEventDetector detector = new(Options);
            detector.Process(Snapshot(PrinterState.Printing, Start, Job("a", remainingMinutes: 30)));
            PrinterSnapshot same = Snapshot(PrinterState.Printing, Start + Step, Job("a", remainingMinutes: 29));
            detector.Process(same);

            long before = GC.GetAllocatedBytesForCurrentThread();
            events = detector.Process(same);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        });

        Assert.Empty(events);
        Assert.Equal(0, allocated);
    }
}
