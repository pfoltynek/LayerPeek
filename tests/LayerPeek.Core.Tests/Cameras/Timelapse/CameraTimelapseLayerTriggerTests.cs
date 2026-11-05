using LayerPeek.Core.Cameras.Timelapse;
using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Tests.Cameras.Timelapse;

/// <summary>T-159: the layer trigger asks for a frame at the start, at every N-th layer change and at the end of the print.</summary>
public sealed class CameraTimelapseLayerTriggerTests : IAsyncDisposable
{
    private const long Job = 354;

    private readonly FakeTimelapse _timelapse = new("core-one", "/tmp/never-written.mp4", null);

    public ValueTask DisposeAsync() => _timelapse.DisposeAsync();

    [Fact]
    public void ANewTriggerAsksForNothingUntilItCaptures()
    {
        using CameraTimelapseLayerTrigger trigger = new(_timelapse, 1);
        Layers(trigger, 3);

        Assert.Equal(0, _timelapse.Requests);
        Assert.Equal(1, trigger.EveryNthLayer);
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraTimelapseLayerTrigger(_timelapse, 0));
        Assert.Throws<ArgumentNullException>(() => new CameraTimelapseLayerTrigger(null!, 1));
    }

    [Fact]
    public void ItTakesAFrameAtTheStartAndOneAtEveryLayerChange()
    {
        using CameraTimelapseLayerTrigger trigger = new(_timelapse, 1);

        trigger.SetCapturing(true);
        Assert.Equal(1, _timelapse.Requests);
        trigger.SetCapturing(true);

        // Ten layers: the first one is where the print starts (no change), then nine changes.
        Layers(trigger, 10);
        Assert.Equal(10, _timelapse.Requests);
    }

    [Theory]
    [InlineData(2, 4)]
    [InlineData(3, 3)]
    [InlineData(10, 0)]
    public void EveryNthLayerTakesAFrameEveryNthChange(int everyNthLayer, int layerFrames)
    {
        using CameraTimelapseLayerTrigger trigger = new(_timelapse, everyNthLayer);
        trigger.SetCapturing(true);

        Layers(trigger, 10);

        Assert.Equal(1 + layerFrames, _timelapse.Requests);
    }

    [Fact]
    public void APauseTakesNoFrameWithdrawsTheWaitingOneAndTheResumeTakesNoneOfItsOwn()
    {
        using CameraTimelapseLayerTrigger trigger = new(_timelapse, 1);
        trigger.SetCapturing(true);
        Layers(trigger, 3);
        Assert.Equal(3, _timelapse.Requests);
        Assert.True(_timelapse.IsRequestPending);

        trigger.OnSnapshot(Snapshot(PrinterState.Paused, 0.8));
        trigger.SetCapturing(false);
        Assert.False(_timelapse.IsRequestPending);
        Assert.Equal(1, _timelapse.CancelCalls);
        trigger.SetCapturing(false);
        Assert.Equal(1, _timelapse.CancelCalls);

        trigger.OnSnapshot(Snapshot(PrinterState.Printing, 0.6));
        trigger.SetCapturing(true);
        Assert.Equal(3, _timelapse.Requests);
        // The layer of the pause goes on; its change is the next frame.
        Layer(trigger, 0.6);
        Assert.Equal(3, _timelapse.Requests);
        Layer(trigger, 0.8);
        Assert.Equal(4, _timelapse.Requests);
    }

    [Fact]
    public void ALayerChangeWhileNotCapturingTakesNoFrame()
    {
        using CameraTimelapseLayerTrigger trigger = new(_timelapse, 1);
        trigger.SetCapturing(true);
        trigger.SetCapturing(false);

        Layers(trigger, 5);

        Assert.Equal(1, _timelapse.Requests);
    }

    [Theory]
    [InlineData(PrinterState.Finished)]
    [InlineData(PrinterState.Stopped)]
    public void TheEndOfThePrintTakesTheFrameOfTheFinishedPart(PrinterState end)
    {
        using CameraTimelapseLayerTrigger trigger = new(_timelapse, 5);
        trigger.SetCapturing(true);
        Layers(trigger, 3);
        Assert.Equal(1, _timelapse.Requests);

        trigger.OnSnapshot(Snapshot(end, 225.6));
        trigger.SetCapturing(true);
        Assert.Equal(2, _timelapse.Requests);

        // Only once: the snapshots after it are no new end.
        trigger.OnSnapshot(Snapshot(end, 225.6));
        trigger.OnSnapshot(Snapshot(PrinterState.Idle, 225.6));
        Assert.Equal(2, _timelapse.Requests);
    }

    [Fact]
    public void AStopDuringAPauseTakesTheLastFrameToo()
    {
        using CameraTimelapseLayerTrigger trigger = new(_timelapse, 1);
        trigger.SetCapturing(true);
        trigger.OnSnapshot(Snapshot(PrinterState.Paused, 1.0));
        trigger.SetCapturing(false);

        trigger.OnSnapshot(Snapshot(PrinterState.Stopped, 1.0));
        trigger.SetCapturing(true);

        Assert.Equal(2, _timelapse.Requests);
    }

    [Theory]
    [InlineData(PrinterState.Finished)]
    [InlineData(PrinterState.Stopped)]
    public void AnEndAfterThePrinterWaitedForTheUserTakesTheLastFrameToo(PrinterState end)
    {
        using CameraTimelapseLayerTrigger trigger = new(_timelapse, 1);
        trigger.SetCapturing(true);
        trigger.OnSnapshot(Snapshot(PrinterState.Attention, 1.0));
        trigger.SetCapturing(false);

        trigger.OnSnapshot(Snapshot(end, 1.0));
        trigger.SetCapturing(true);

        Assert.Equal(2, _timelapse.Requests);
    }

    [Fact]
    public void AResumeAfterThePrinterWaitedForTheUserTakesOnlyOneEndFrame()
    {
        using CameraTimelapseLayerTrigger trigger = new(_timelapse, 100);
        trigger.SetCapturing(true);
        trigger.OnSnapshot(Snapshot(PrinterState.Attention, 1.0));
        trigger.SetCapturing(false);
        trigger.OnSnapshot(Snapshot(PrinterState.Printing, 1.0));
        trigger.SetCapturing(true);
        Assert.Equal(1, _timelapse.Requests);

        trigger.OnSnapshot(Snapshot(PrinterState.Finished, 1.0));
        trigger.SetCapturing(true);
        trigger.OnSnapshot(Snapshot(PrinterState.Finished, 1.0));

        Assert.Equal(2, _timelapse.Requests);
    }

    [Fact]
    public void AWaitForTheUserThatEndsWithoutAPrintTakesNoEndFrame()
    {
        using CameraTimelapseLayerTrigger trigger = new(_timelapse, 1);
        trigger.SetCapturing(true);
        trigger.OnSnapshot(Snapshot(PrinterState.Attention, 1.0));
        trigger.SetCapturing(false);

        trigger.OnSnapshot(Snapshot(PrinterState.Idle, 1.0));
        trigger.SetCapturing(true);
        trigger.OnSnapshot(Snapshot(PrinterState.Stopped, 1.0));

        Assert.Equal(1, _timelapse.Requests);
    }

    [Fact]
    public void WithoutZDataOnlyTheStartAndTheEndTakeFrames()
    {
        using CameraTimelapseLayerTrigger trigger = new(_timelapse, 1);
        trigger.SetCapturing(true);
        PrinterSnapshot printing = new() { PrinterId = "core-one", State = PrinterState.Printing, Timestamp = DateTimeOffset.UnixEpoch };
        for (int poll = 0; poll < 50; poll++)
        {
            trigger.OnSnapshot(printing);
        }

        trigger.OnSnapshot(printing with { State = PrinterState.Finished });

        Assert.Equal(2, _timelapse.Requests);
    }

    [Fact]
    public void ADisposedTriggerAsksForNothing()
    {
        CameraTimelapseLayerTrigger trigger = new(_timelapse, 1);
        trigger.SetCapturing(true);
        trigger.Dispose();

        Layers(trigger, 5);
        trigger.OnSnapshot(Snapshot(PrinterState.Finished, 225.6));
        trigger.SetCapturing(false);

        Assert.Equal(1, _timelapse.Requests);
        Assert.Equal(0, _timelapse.CancelCalls);
    }

    [Fact]
    public void ASnapshotAllocatesNothing()
    {
        using CameraTimelapseLayerTrigger trigger = new(_timelapse, 1);
        trigger.SetCapturing(true);
        PrinterSnapshot[] snapshots = [.. Enumerable.Range(0, 400).Select(static poll => Snapshot(PrinterState.Printing, Math.Round(0.2 + (poll / 4 * 0.2), 1)))];
        foreach (PrinterSnapshot snapshot in snapshots)
        {
            trigger.OnSnapshot(snapshot);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        foreach (PrinterSnapshot snapshot in snapshots)
        {
            trigger.OnSnapshot(snapshot);
            trigger.SetCapturing(true);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.True(_timelapse.Requests > 100);
    }

    private static PrinterSnapshot Snapshot(PrinterState state, double z) => new()
    {
        PrinterId = "core-one",
        State = state,
        Timestamp = DateTimeOffset.UnixEpoch,
        Job = new PrintJob { Id = Job },
        Telemetry = new Telemetry { AxisZ = z },
    };

    /// <summary>Four polls at <paramref name="z"/>, the snapshots of a layer at 5 s polling.</summary>
    private static void Layer(CameraTimelapseLayerTrigger trigger, double z)
    {
        for (int poll = 0; poll < 4; poll++)
        {
            trigger.OnSnapshot(Snapshot(PrinterState.Printing, z));
        }
    }

    /// <summary><paramref name="count"/> layers of 0.2 mm from 0.2 mm.</summary>
    private static void Layers(CameraTimelapseLayerTrigger trigger, int count)
    {
        for (int layer = 1; layer <= count; layer++)
        {
            Layer(trigger, Math.Round(layer * 0.2, 1));
        }
    }
}
