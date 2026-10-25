using LayerPeek.App.Cameras;
using LayerPeek.App.Cameras.Mini;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cooldown;
using LayerPeek.Core.Printers;
using LayerPeek.Tests.Allocation;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Cameras.Mini;

/// <summary>
/// A snapshot that changes nothing the mini preview strip shows costs no allocation (T-089): the content (state, whole
/// percent, remaining minutes, cool-down) is computed on the monitor thread and compared; nothing is formatted or
/// posted. Isolated, see <see cref="AllocationSensitive"/>.
/// </summary>
[Collection(nameof(AllocationSensitive))]
public sealed class CameraMiniStripAllocationTests : IDisposable
{
    private readonly ManualDispatcher _dispatcher = new();
    private readonly FakePrinterMonitor _monitor = new();
    private readonly CooldownMonitor _cooldown;
    private readonly CultureScope _culture = new("en-US");

    public CameraMiniStripAllocationTests()
    {
        _cooldown = new CooldownMonitor(_monitor, TimeProvider.System, new ListLogger<CooldownMonitor>());
        _cooldown.Configure([Definition()], TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        _cooldown.Dispose();
        _culture.Dispose();
    }

    private static PrinterSnapshot Printing(double progress, double remainingMinutes, double nozzle) =>
        Snapshot(PrinterState.Printing, Job(progress, TimeSpan.FromMinutes(remainingMinutes))) with
        {
            Telemetry = new Telemetry { Nozzle = new Temperature(nozzle, 215) },
        };

    [Fact]
    public void SnapshotThatChangesNothingVisibleAllocatesNothing()
    {
        using CameraMiniStripViewModel strip = new("mk4", _dispatcher, _cooldown);
        // Both show 42 % and 1 h 5 min and differ only in what the strip does not show.
        PrinterSnapshot first = Printing(42.1, 64.2, 214.6);
        PrinterSnapshot second = Printing(42.9, 64.9, 190);
        strip.OnSnapshot(first);
        _dispatcher.RunAll();
        Assert.Equal("42% · 1 h 5 min", strip.Summary);
        int changes = 0;
        strip.PropertyChanged += (_, _) => changes++;
        // Warm up (JIT, lazy statics).
        for (int i = 0; i < 100; i++)
        {
            strip.OnSnapshot(i % 2 == 0 ? second : first);
        }

        int posts = _dispatcher.PostCount;
        const int Snapshots = 1000;

        long allocated = AllocationProbe.MinAllocated(() => AllocationProbe.Measure(() =>
        {
            for (int i = 0; i < Snapshots; i++)
            {
                strip.OnSnapshot(i % 2 == 0 ? second : first);
            }
        }));

        Assert.Equal(0, allocated);
        Assert.Equal(posts, _dispatcher.PostCount);
        Assert.Equal(0, _dispatcher.Pending);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void ExpandedRowsAllocateNothingForASnapshotThatChangesNothingVisible()
    {
        // T-090: the name row (the same job) and the chart row (the same whole degrees).
        using CameraMiniStripViewModel strip = new("mk4", _dispatcher, _cooldown, rows: new CameraMiniRows(true, true));
        PrinterSnapshot first = Printing(42.1, 64.2, 214.6);
        PrinterSnapshot second = Printing(42.9, 64.9, 215.2);
        AssertNothingAllocatedOrPosted(strip, first, second, "215°");
    }

    [Fact]
    public void CollapsedChartAllocatesNothingWhileTheTemperaturesChange()
    {
        // T-090: the temperatures are not even computed while the chart row is collapsed.
        using CameraMiniStripViewModel strip = new("mk4", _dispatcher, _cooldown, rows: new CameraMiniRows(true, false));
        PrinterSnapshot first = Printing(42.1, 64.2, 214.6);
        PrinterSnapshot second = Printing(42.9, 64.9, 150);
        AssertNothingAllocatedOrPosted(strip, first, second, string.Empty);
    }

    private void AssertNothingAllocatedOrPosted(CameraMiniStripViewModel strip, PrinterSnapshot first, PrinterSnapshot second, string nozzle)
    {
        strip.OnSnapshot(first);
        _dispatcher.RunAll();
        Assert.True(strip.ShowsNameRow);
        Assert.Equal(nozzle, strip.NozzleText);
        int changes = 0;
        strip.PropertyChanged += (_, _) => changes++;
        strip.NameRow.PropertyChanged += (_, _) => changes++;
        for (int i = 0; i < 100; i++)
        {
            strip.OnSnapshot(i % 2 == 0 ? second : first);
        }

        int posts = _dispatcher.PostCount;
        const int Snapshots = 1000;

        long allocated = AllocationProbe.MinAllocated(() => AllocationProbe.Measure(() =>
        {
            for (int i = 0; i < Snapshots; i++)
            {
                strip.OnSnapshot(i % 2 == 0 ? second : first);
            }
        }));

        Assert.Equal(0, allocated);
        Assert.Equal(posts, _dispatcher.PostCount);
        Assert.Equal(0, _dispatcher.Pending);
        Assert.Equal(0, changes);
    }
}
