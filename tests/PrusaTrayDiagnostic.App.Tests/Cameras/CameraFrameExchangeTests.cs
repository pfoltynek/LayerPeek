using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Tests.Allocation;

namespace PrusaTrayDiagnostic.App.Tests.Cameras;

public sealed class CameraFrameExchangeTests
{
    private static CameraFrame Frame(byte fill, int width = 4, int height = 2, int padding = 0)
    {
        int stride = (width * CameraFrame.BytesPerPixel) + padding;
        byte[] pixels = new byte[stride * height];
        pixels.AsSpan().Fill(fill);
        return new CameraFrame(width, height, stride, pixels);
    }

    [Fact]
    public void NothingToTakeBeforeTheFirstFrame()
    {
        CameraFrameExchange exchange = new();

        Assert.Null(exchange.TakeLatest());
    }

    [Fact]
    public void FirstFrameAsksForNotificationAndIsTakenOnce()
    {
        CameraFrameExchange exchange = new();

        Assert.True(exchange.Publish(Frame(7)));
        CameraFrameBuffer? taken = exchange.TakeLatest();

        Assert.NotNull(taken);
        Assert.Equal(4, taken.Width);
        Assert.Equal(2, taken.Height);
        Assert.Equal(16, taken.Stride);
        Assert.All(taken.Pixels.ToArray(), static b => Assert.Equal(7, b));
        Assert.Null(exchange.TakeLatest());
    }

    [Fact]
    public void BusyUiGetsOneNotificationAndOnlyTheNewestFrame()
    {
        CameraFrameExchange exchange = new();

        Assert.True(exchange.Publish(Frame(1)));
        Assert.False(exchange.Publish(Frame(2)));
        Assert.False(exchange.Publish(Frame(3)));

        Assert.Equal(3, exchange.TakeLatest()!.Pixels[0]);
        Assert.Equal(2, exchange.DroppedFrames);
        // Taking cleared the pending notification: the next frame notifies again.
        Assert.True(exchange.Publish(Frame(4)));
    }

    [Fact]
    public void TakenFrameStaysIntactWhileNewFramesArrive()
    {
        CameraFrameExchange exchange = new();
        exchange.Publish(Frame(1));
        CameraFrameBuffer shown = exchange.TakeLatest()!;

        exchange.Publish(Frame(2));
        exchange.Publish(Frame(3));

        Assert.Equal(1, shown.Pixels[0]);
    }

    [Fact]
    public void PaddedRowsAreCopiedTightlyPacked()
    {
        CameraFrameExchange exchange = new();
        byte[] pixels = new byte[(12 * 2) - 4];
        // Row 0: 8 bytes of 1 + 4 padding bytes of 9; row 1: 8 bytes of 2 (no padding after the last row).
        pixels.AsSpan(0, 8).Fill(1);
        pixels.AsSpan(8, 4).Fill(9);
        pixels.AsSpan(12, 8).Fill(2);

        exchange.Publish(new CameraFrame(2, 2, 12, pixels));
        CameraFrameBuffer taken = exchange.TakeLatest()!;

        Assert.Equal([1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2], taken.Pixels.ToArray());
    }

    [Fact]
    public void ResolutionChangeIsFollowed()
    {
        CameraFrameExchange exchange = new();
        exchange.Publish(Frame(1, width: 4, height: 2));
        exchange.TakeLatest();

        exchange.Publish(Frame(2, width: 8, height: 4));
        CameraFrameBuffer taken = exchange.TakeLatest()!;

        Assert.Equal((8, 4, 128), (taken.Width, taken.Height, taken.Pixels.Length));
    }

    [Fact]
    public void DefaultFrameIsRejected()
    {
        CameraFrameExchange exchange = new();

        Assert.Throws<ArgumentException>(() => exchange.Publish(default));
    }
}

[Collection(nameof(AllocationSensitive))]
public sealed class CameraFrameExchangeAllocationTests
{
    [Fact]
    public void SteadyStreamingAllocatesNothing()
    {
        CameraFrameExchange exchange = new();
        byte[] pixels = new byte[1280 * 4 * 720];
        CameraFrame frame = new(1280, 720, 1280 * 4, pixels);
        // Warm up: every one of the three buffers gets its pixels once.
        for (int i = 0; i < 4; i++)
        {
            exchange.Publish(frame);
            exchange.Publish(frame);
            exchange.TakeLatest();
        }

        long allocated = AllocationProbe.MinAllocated(() => AllocationProbe.Measure(() =>
        {
            for (int i = 0; i < 20; i++)
            {
                exchange.Publish(frame);
                if (i % 2 == 0)
                {
                    exchange.TakeLatest();
                }
            }
        }));

        Assert.Equal(0, allocated);
    }
}
