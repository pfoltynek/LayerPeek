using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Printers;
using LayerPeek.Tests.Allocation;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Cameras.Overlay;

/// <summary>
/// A snapshot that changes nothing visible costs the overlay no allocation (T-081): every widget computes its content on
/// the monitor thread, nothing is posted. Isolated, see <see cref="AllocationSensitive"/>.
/// </summary>
[Collection(nameof(AllocationSensitive))]
public sealed class OverlayAllocationTests : IDisposable
{
    private readonly ManualDispatcher _dispatcher = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly CultureScope _culture = new("en-US");

    public void Dispose() => _culture.Dispose();

    private static PrinterSnapshot Printing(double progress) =>
        Snapshot(PrinterState.Printing, Job(progress, TimeSpan.FromMinutes(65))) with { Telemetry = new Telemetry { Nozzle = new Temperature(214.6, 215) } };

    [Fact]
    public void SnapshotThatChangesNothingVisibleAllocatesNothing()
    {
        string[] ids = ["test-a", "test-b", "test-c"];
        using CameraOverlayViewModel overlay = new(
            TestOverlayWidgets.Registry(ids),
            new OverlayWidgetContext("mk4", _time),
            _dispatcher,
            NullLogger.Instance,
            Definition(),
            new CameraOverlayLayout(ids.Select(static id => new OverlayWidgetPlacement(id, OverlayAnchor.TopLeft))),
            CameraOverlayOpacity.DefaultPercent,
            CameraWindowMode.Normal,
            snapshot: null);
        TestOverlayWidget[] widgets = [.. overlay.Widgets.Cast<TestOverlayWidget>()];
        // Both show 42 % (progress is rounded down), so only the first one is visible.
        PrinterSnapshot first = Printing(42.1);
        PrinterSnapshot second = Printing(42.9);
        overlay.OnSnapshot(first);
        _dispatcher.RunAll();
        Assert.All(widgets, static widget => Assert.True(widget.IsShown));
        // Warm up (JIT, lazy statics).
        for (int i = 0; i < 100; i++)
        {
            overlay.OnSnapshot(i % 2 == 0 ? second : first);
        }

        int[] computedBefore = [.. widgets.Select(static widget => widget.ComputeCount)];
        int[] formattedBefore = [.. widgets.Select(static widget => widget.FormatCount)];
        const int Snapshots = 1000;
        int passes = 0;

        long allocated = AllocationProbe.MinAllocated(() =>
        {
            passes++;
            return AllocationProbe.Measure(() =>
            {
                for (int i = 0; i < Snapshots; i++)
                {
                    overlay.OnSnapshot(i % 2 == 0 ? second : first);
                }
            });
        });

        Assert.Equal(0, allocated);
        // The measured path really ran: every widget computed every snapshot, none formatted, nothing was posted.
        Assert.All(widgets, (widget, index) => Assert.Equal(computedBefore[index] + (passes * Snapshots), widget.ComputeCount));
        Assert.All(widgets, (widget, index) => Assert.Equal(formattedBefore[index], widget.FormatCount));
        Assert.Equal(0, _dispatcher.Pending);
    }
}
