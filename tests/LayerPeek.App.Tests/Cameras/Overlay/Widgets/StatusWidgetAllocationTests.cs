using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Cameras.Overlay.Widgets;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Printers;
using LayerPeek.Tests.Allocation;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Cameras.Overlay.Widgets;

/// <summary>
/// A snapshot that changes nothing the Status widget shows costs no allocation (T-082): the content (shortened name,
/// state, whole percent) is computed on the monitor thread and compared, nothing is formatted or posted. Isolated, see
/// <see cref="AllocationSensitive"/>.
/// </summary>
[Collection(nameof(AllocationSensitive))]
public sealed class StatusWidgetAllocationTests : IDisposable
{
    private readonly ManualDispatcher _dispatcher = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly CultureScope _culture = new("en-US");

    public void Dispose() => _culture.Dispose();

    private static PrinterSnapshot Printing(double progress, double nozzle) =>
        Snapshot(PrinterState.Printing, Job(progress, TimeSpan.FromMinutes(65))) with { Telemetry = new Telemetry { Nozzle = new Temperature(nozzle, 215) } };

    [Fact]
    public void SnapshotThatChangesNothingVisibleAllocatesNothing()
    {
        // A long name: the shortened one must be reused, not built per snapshot.
        using CameraOverlayViewModel overlay = new(
            StatusWidgetTests.Registry(),
            new OverlayWidgetContext("mk4", _time),
            _dispatcher,
            NullLogger.Instance,
            Definition(name: new string('N', 60)),
            StatusWidgetTests.Layout(),
            CameraOverlayOpacity.DefaultPercent,
            CameraWindowMode.Normal,
            snapshot: null);
        StatusWidgetViewModel widget = Assert.IsType<StatusWidgetViewModel>(Assert.Single(overlay.Widgets));
        // Both show 42 % (progress is rounded down) and differ only in what the widget does not show.
        PrinterSnapshot first = Printing(42.1, 214.6);
        PrinterSnapshot second = Printing(42.9, 190);
        overlay.OnSnapshot(first);
        _dispatcher.RunAll();
        Assert.Equal("42%", widget.PercentText);
        int changes = 0;
        widget.PropertyChanged += (_, _) => changes++;
        // Warm up (JIT, lazy statics).
        for (int i = 0; i < 100; i++)
        {
            overlay.OnSnapshot(i % 2 == 0 ? second : first);
        }

        int posts = _dispatcher.PostCount;
        const int Snapshots = 1000;

        long allocated = AllocationProbe.MinAllocated(() => AllocationProbe.Measure(() =>
        {
            for (int i = 0; i < Snapshots; i++)
            {
                overlay.OnSnapshot(i % 2 == 0 ? second : first);
            }
        }));

        Assert.Equal(0, allocated);
        Assert.Equal(posts, _dispatcher.PostCount);
        Assert.Equal(0, _dispatcher.Pending);
        Assert.Equal(0, changes);
    }
}
