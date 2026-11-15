using Microsoft.Extensions.Time.Testing;

using LayerPeek.Core.Cameras;

namespace LayerPeek.Core.Tests.Cameras;

/// <summary>T-189: grayscale frames converted by the feed, the demand that asks for them and the exchange that copies them.</summary>
public sealed class CameraGrayscaleFrameTests
{
    private static readonly CameraGrayscaleSize Size = new(16, 16);

    /// <summary>A grayscale frame of <paramref name="size"/> whose rows are padded to <paramref name="stride"/> with 255.</summary>
    private static CameraGrayscaleFrame Frame(CameraGrayscaleSize size, int stride, Func<int, int, byte> luma)
    {
        byte[] pixels = new byte[stride * size.Height];
        pixels.AsSpan().Fill(255);
        for (int y = 0; y < size.Height; y++)
        {
            for (int x = 0; x < size.Width; x++)
            {
                pixels[(y * stride) + x] = luma(x, y);
            }
        }

        return new CameraGrayscaleFrame(size.Width, size.Height, stride, pixels, 1280, 720);
    }

    [Fact]
    public void ByDefaultTheDemandWantsNoGrayscale()
    {
        CameraStreamDemand demand = new(frames: true, packets: false);

        Assert.False(demand.GrayscaleDue);
        Assert.Null(demand.GrayscaleSize);
    }

    [Fact]
    public void TheDemandWantsGrayscaleOfItsSizeOnlyOnceItIsDue()
    {
        FakeTimeProvider time = new();
        CameraStreamDemand demand = new(frames: true, packets: false, time);

        demand.RequestGrayscale(new CameraGrayscaleSize(320, 180), time.GetTimestamp() + time.TimestampFrequency);
        Assert.False(demand.GrayscaleDue);
        Assert.Equal(new CameraGrayscaleSize(320, 180), demand.GrayscaleSize);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(demand.GrayscaleDue);

        demand.CancelGrayscale();
        time.Advance(TimeSpan.FromDays(1));
        Assert.False(demand.GrayscaleDue);
        Assert.Null(demand.GrayscaleSize);

        demand.RequestGrayscale(Size, long.MinValue);
        Assert.True(demand.GrayscaleDue);
        Assert.Throws<ArgumentException>(() => demand.RequestGrayscale(default, long.MinValue));
    }

    [Fact]
    public void TheExchangeCopiesTheRowsWithoutTheirPadding()
    {
        CameraFrameExchange exchange = new(Size);

        Assert.True(exchange.PublishGrayscale(Frame(Size, stride: 64, (x, y) => (byte)(x + (16 * y))), generation: 3));
        CameraFrameBuffer frame = exchange.TakeLatest()!;

        Assert.Equal((CameraPixelFormat.Gray8, 16, 16, 16, 3), (frame.Format, frame.Width, frame.Height, frame.Stride, frame.Generation));
        Assert.Equal((1280, 720), (frame.SourceWidth, frame.SourceHeight));
        Assert.Equal(Enumerable.Range(0, 256).Select(static i => (byte)i), frame.Pixels.ToArray());
    }

    [Fact]
    public void TheExchangeReusesItsBuffersForGrayscaleFrames()
    {
        CameraFrameExchange exchange = new(Size);
        CameraGrayscaleFrame frame = Frame(Size, stride: 16, static (_, _) => 7);
        for (int i = 0; i < 3; i++)
        {
            exchange.PublishGrayscale(frame);
            exchange.TakeLatest();
        }

        exchange.PublishGrayscale(frame);
        CameraFrameBuffer buffer = exchange.TakeLatest()!;

        Assert.Equal(256, buffer.Capacity);
        Assert.All(buffer.Pixels.ToArray(), static value => Assert.Equal(7, value));
    }

    [Fact]
    public void TheExchangeRefusesGrayscaleOfAnotherSizeOrForBgra()
    {
        CameraGrayscaleFrame frame = Frame(new CameraGrayscaleSize(32, 18), stride: 32, static (_, _) => 1);

        Assert.Throws<ArgumentException>(() => new CameraFrameExchange(Size).PublishGrayscale(frame));
        Assert.Throws<ArgumentException>(() => new CameraFrameExchange().PublishGrayscale(frame));
        Assert.Throws<ArgumentException>(() => new CameraFrameExchange(Size).PublishGrayscale(default));
    }

    [Fact]
    public void AGrayscaleFrameIsValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraGrayscaleFrame(16, 16, 15, new byte[256], 16, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraGrayscaleFrame(16, 16, 16, new byte[255], 16, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraGrayscaleFrame(0, 16, 16, new byte[256], 16, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraGrayscaleFrame(16, 16, 16, new byte[256], 0, 16));
        // The last row needs no padding.
        Assert.Equal(16, new CameraGrayscaleFrame(16, 16, 20, new byte[(20 * 15) + 16], 16, 16).Height);
    }
}
