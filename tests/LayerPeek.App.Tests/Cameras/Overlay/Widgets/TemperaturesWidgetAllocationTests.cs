using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Cameras.Overlay.Widgets;
using LayerPeek.App.Composition;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.History;
using LayerPeek.Core.Printers;
using LayerPeek.Tests.Allocation;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Cameras.Overlay.Widgets;

/// <summary>
/// Polls that keep the whole degrees cost the Temperatures widget no allocation (T-084): its content is computed on the
/// monitor thread and nothing is posted. Isolated, see <see cref="AllocationSensitive"/>.
/// </summary>
[Collection(nameof(AllocationSensitive))]
public sealed class TemperaturesWidgetAllocationTests : IDisposable
{
    private readonly ManualDispatcher _dispatcher = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly CultureScope _culture = new("en-US");

    public void Dispose() => _culture.Dispose();

    [Fact]
    public void PollWithTheSameWholeDegreesAllocatesNothing()
    {
        using CameraOverlayViewModel overlay = new(
            OverlayWidgets.CreateRegistry(),
            new OverlayWidgetContext("mk4", _time, new TelemetryHistory()),
            _dispatcher,
            NullLogger.Instance,
            Definition(),
            new CameraOverlayLayout([new OverlayWidgetPlacement(OverlayWidgetIds.Temperatures, OverlayAnchor.BottomRight)]),
            CameraOverlayOpacity.DefaultPercent,
            CameraWindowMode.Normal,
            snapshot: null);
        TemperaturesWidgetViewModel widget = (TemperaturesWidgetViewModel)Assert.Single(overlay.Widgets);
        PrinterSnapshot first = TemperaturesWidgetTests.Heating(new Temperature(214.6, 215), new Temperature(59.8, 60));
        PrinterSnapshot second = TemperaturesWidgetTests.Heating(new Temperature(215.3, 215), new Temperature(60.2, 60), at: Now.AddSeconds(2));
        overlay.OnSnapshot(first);
        _dispatcher.RunAll();
        Assert.True(widget.IsShown);
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
        Assert.Equal("215", widget.NozzleActual);
    }
}
