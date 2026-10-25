using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Cameras.Overlay.Widgets;
using LayerPeek.App.Composition;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Printers;
using LayerPeek.Tests.Allocation;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Cameras.Overlay.Widgets;

/// <summary>
/// Polls within the same minute cost the Times widget no allocation (T-083): its content (minutes and the local finish
/// time, also in a zone with daylight saving) is computed on the monitor thread and nothing is posted. Isolated, see
/// <see cref="AllocationSensitive"/>.
/// </summary>
[Collection(nameof(AllocationSensitive))]
public sealed class TimesWidgetAllocationTests : IDisposable
{
    private readonly ManualDispatcher _dispatcher = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly CultureScope _culture = new("en-US");

    public void Dispose() => _culture.Dispose();

    [Fact]
    public void PollWithinTheSameMinuteAllocatesNothing()
    {
        _time.SetLocalTimeZone(TimesWidgetTests.CentralEurope());
        using CameraOverlayViewModel overlay = new(
            OverlayWidgets.CreateRegistry(),
            new OverlayWidgetContext("mk4", _time),
            _dispatcher,
            NullLogger.Instance,
            Definition(),
            new CameraOverlayLayout([new OverlayWidgetPlacement(OverlayWidgetIds.Times, OverlayAnchor.BottomLeft)]),
            CameraOverlayOpacity.DefaultPercent,
            CameraWindowMode.Normal,
            snapshot: null);
        TimesWidgetViewModel widget = (TimesWidgetViewModel)Assert.Single(overlay.Widgets);
        // Five seconds apart, both show 65 min left, 130 min printed and the same finish minute.
        PrinterSnapshot first = TimesWidgetTests.Printing(remaining: TimeSpan.FromMinutes(65), elapsed: TimeSpan.FromSeconds(7790));
        PrinterSnapshot second = TimesWidgetTests.Printing(remaining: TimeSpan.FromSeconds(3895), elapsed: TimeSpan.FromSeconds(7795), at: Now.AddSeconds(5));
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
        Assert.Equal("1\u00A0h 5\u00A0min", widget.Remaining);
    }
}
