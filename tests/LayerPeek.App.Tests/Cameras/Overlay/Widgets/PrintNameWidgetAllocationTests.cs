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
/// Polls of the same job cost the Print name widget no allocation (T-085): the file name is compared (not parsed again)
/// on the monitor thread and nothing is posted, also when the poll brings a new string instance of the name or the print
/// finishes. Isolated, see <see cref="AllocationSensitive"/>.
/// </summary>
[Collection(nameof(AllocationSensitive))]
public sealed class PrintNameWidgetAllocationTests : IDisposable
{
    private readonly ManualDispatcher _dispatcher = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly CultureScope _culture = new("en-US");

    public void Dispose() => _culture.Dispose();

    [Fact]
    public void PollOfTheSameJobAllocatesNothing()
    {
        using CameraOverlayViewModel overlay = new(
            OverlayWidgets.CreateRegistry(),
            new OverlayWidgetContext("mk4", _time),
            _dispatcher,
            NullLogger.Instance,
            Definition(),
            new CameraOverlayLayout([new OverlayWidgetPlacement(OverlayWidgetIds.PrintName, OverlayAnchor.TopCenter)]),
            CameraOverlayOpacity.DefaultPercent,
            CameraWindowMode.Normal,
            snapshot: null);
        PrintNameWidgetViewModel widget = (PrintNameWidgetViewModel)Assert.Single(overlay.Widgets);
        PrinterSnapshot first = PrintNameWidgetTests.Job(progress: 42.7);
        PrinterSnapshot second = PrintNameWidgetTests.Job(fileName: new string(PrintNameWidgetTests.Foldable.AsSpan()), progress: 43.1);
        PrinterSnapshot finished = PrintNameWidgetTests.Job(state: PrinterState.Finished, progress: 100);
        PrinterSnapshot[] polls = [first, second, finished];
        overlay.OnSnapshot(first);
        _dispatcher.RunAll();
        Assert.True(widget.IsShown);
        // Warm up (JIT, lazy statics).
        for (int i = 0; i < 100; i++)
        {
            overlay.OnSnapshot(polls[i % polls.Length]);
        }

        int posts = _dispatcher.PostCount;
        const int Snapshots = 1000;

        long allocated = AllocationProbe.MinAllocated(() => AllocationProbe.Measure(() =>
        {
            for (int i = 0; i < Snapshots; i++)
            {
                overlay.OnSnapshot(polls[i % polls.Length]);
            }
        }));

        Assert.Equal(0, allocated);
        Assert.Equal(posts, _dispatcher.PostCount);
        Assert.Equal(0, _dispatcher.Pending);
        Assert.Equal(1, widget.ParseCount);
        Assert.Equal("Foldable book holder v1", widget.Title);
    }
}
