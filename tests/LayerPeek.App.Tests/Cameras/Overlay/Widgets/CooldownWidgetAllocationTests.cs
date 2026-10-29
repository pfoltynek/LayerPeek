using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Cameras.Overlay.Widgets;
using LayerPeek.App.Composition;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Cooldown;
using LayerPeek.Core.Printers;
using LayerPeek.Tests.Allocation;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Cameras.Overlay.Widgets;

/// <summary>
/// Polls and clock ticks that change nothing the Cool-down widget shows cost no allocation (T-088): the content is computed
/// on the monitor thread (and the clock's thread) and compared, nothing is formatted or posted. Isolated, see
/// <see cref="AllocationSensitive"/>.
/// </summary>
[Collection(nameof(AllocationSensitive))]
public sealed class CooldownWidgetAllocationTests : IDisposable
{
    private readonly ManualDispatcher _dispatcher = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly FakePrinterMonitor _monitor = new();
    private readonly CooldownMonitor _cooldown;
    private readonly CultureScope _culture = new("en-US");

    public CooldownWidgetAllocationTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
        _cooldown = new CooldownMonitor(_monitor, _time, NullLogger<CooldownMonitor>.Instance);
        _cooldown.Configure([NozzleDecides(Definition())], TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        _cooldown.Dispose();
        _culture.Dispose();
    }

    private CameraOverlayViewModel Create()
    {
        CameraOverlayViewModel overlay = new(
            OverlayWidgets.CreateRegistry(),
            new OverlayWidgetContext("mk4", _time, Cooldown: _cooldown),
            _dispatcher,
            NullLogger.Instance,
            Definition(),
            new CameraOverlayLayout([new OverlayWidgetPlacement(OverlayWidgetIds.Cooldown, OverlayAnchor.BottomLeft, OverlayWidgetSize.Medium, emphasized: true)]),
            CameraOverlayOpacity.DefaultPercent,
            CameraWindowMode.Normal,
            snapshot: null);
        _monitor.SnapshotUpdated += (_, snapshot) => overlay.OnSnapshot(snapshot);
        return overlay;
    }

    [Fact]
    public void CooledDownPollsThatChangeNothingVisibleAllocateNothing()
    {
        using CameraOverlayViewModel overlay = Create();
        CooldownWidgetViewModel widget = (CooldownWidgetViewModel)Assert.Single(overlay.Widgets);
        _monitor.Raise(CooldownWidgetTests.Printing());
        for (double t = 5; t <= 200; t += 5)
        {
            _time.SetUtcNow(Now.AddSeconds(t));
            _monitor.Raise(CooldownWidgetTests.Ended(t, CooldownWidgetTests.Newton(t - 5)));
        }

        _dispatcher.RunAll();
        Assert.True(widget.IsCooled);
        // Two polls that round to the same degrees; each one updates the monitor's state (and raises its Changed).
        PrinterSnapshot first = CooldownWidgetTests.Ended(205, 40.2);
        PrinterSnapshot second = CooldownWidgetTests.Ended(210, 39.8);
        // Warm up (JIT, lazy statics).
        for (int i = 0; i < 100; i++)
        {
            _monitor.Raise(i % 2 == 0 ? second : first);
        }

        _dispatcher.RunAll();
        int posts = _dispatcher.PostCount;
        int computes = widget.ComputeCount;
        const int Snapshots = 1000;

        long allocated = AllocationProbe.MinAllocated(() => AllocationProbe.Measure(() =>
        {
            for (int i = 0; i < Snapshots; i++)
            {
                _monitor.Raise(i % 2 == 0 ? second : first);
            }
        }));

        Assert.Equal(0, allocated);
        // Computed for the snapshot and for the monitor's change, never posted.
        Assert.True(widget.ComputeCount - computes >= Snapshots);
        Assert.Equal(posts, _dispatcher.PostCount);
        Assert.Equal(0, _dispatcher.Pending);
    }

    [Fact]
    public void CoolingPollsInTheSameDegreeAndMinuteAllocateNothing()
    {
        using CameraOverlayViewModel overlay = Create();
        CooldownWidgetViewModel widget = (CooldownWidgetViewModel)Assert.Single(overlay.Widgets);
        _monitor.Raise(CooldownWidgetTests.Printing());
        _time.SetUtcNow(Now.AddSeconds(5));
        _monitor.Raise(CooldownWidgetTests.Ended(5, 200));
        _dispatcher.RunAll();
        Assert.True(widget.IsCooling);
        // Without an estimate (too few readings yet): the same degree and progress, the monitor changes only its time.
        PrinterSnapshot first = CooldownWidgetTests.Ended(10, 199.6) with { Timestamp = Now.AddSeconds(5) };
        for (int i = 0; i < 100; i++)
        {
            _monitor.Raise(first);
        }

        _dispatcher.RunAll();
        int posts = _dispatcher.PostCount;
        const int Snapshots = 1000;

        long allocated = AllocationProbe.MinAllocated(() => AllocationProbe.Measure(() =>
        {
            for (int i = 0; i < Snapshots; i++)
            {
                _monitor.Raise(first);
            }
        }));

        Assert.Equal(0, allocated);
        Assert.Equal(posts, _dispatcher.PostCount);
        Assert.Equal(0, _dispatcher.Pending);
    }
}
