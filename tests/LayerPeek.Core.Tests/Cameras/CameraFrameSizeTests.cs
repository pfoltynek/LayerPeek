using Microsoft.Extensions.Time.Testing;

using LayerPeek.Core.Cameras;

namespace LayerPeek.Core.Tests.Cameras;

/// <summary>T-184: the display size of a frame subscriber and what the decoder makes of it.</summary>
public sealed class CameraFrameSizeTests
{
    [Theory]
    // 720p into the mini preview, a normal window and a window taller than the image: the width limits.
    [InlineData(1280, 720, 320, 180, 320, 180)]
    [InlineData(1280, 720, 640, 640, 640, 360)]
    [InlineData(1280, 720, 800, 2000, 800, 450)]
    // A window wider than the image: the height limits, the width keeps the aspect ratio (rounded).
    [InlineData(1280, 720, 1000, 300, 533, 300)]
    [InlineData(1280, 720, 4000, 100, 178, 100)]
    // A portrait camera.
    [InlineData(720, 1280, 400, 400, 225, 400)]
    // Odd sizes round to the nearest pixel, never to zero.
    [InlineData(1920, 1080, 427, 1000, 427, 240)]
    [InlineData(16000, 10, 100, 100, 100, 1)]
    public void FitKeepsTheAspectRatioWithinTheSize(int sourceWidth, int sourceHeight, int maxWidth, int maxHeight, int width, int height)
    {
        Assert.Equal(new CameraFrameSize(width, height), new CameraFrameSize(maxWidth, maxHeight).Fit(sourceWidth, sourceHeight));
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1281, 720)]
    [InlineData(3840, 2160)]
    public void FitNeverScalesUp(int maxWidth, int maxHeight)
    {
        Assert.Equal(new CameraFrameSize(1280, 720), new CameraFrameSize(maxWidth, maxHeight).Fit(1280, 720));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    [InlineData(CameraFrameSize.MaxSide + 1, 1)]
    [InlineData(1, CameraFrameSize.MaxSide + 1)]
    public void InvalidSizesAreRejected(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraFrameSize(width, height));
    }

    [Fact]
    public void FitRejectsAnEmptySource()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraFrameSize(10, 10).Fit(0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraFrameSize(10, 10).Fit(10, 0));
    }

    [Fact]
    public void PackedSizesRoundTrip()
    {
        Assert.Equal(0, CameraFrameSize.Pack(null));
        Assert.Null(CameraFrameSize.Unpack(0));
        CameraFrameSize size = new(CameraFrameSize.MaxSide, 1);
        Assert.Equal(size, CameraFrameSize.Unpack(CameraFrameSize.Pack(size)));
    }

    [Fact]
    public void UnionContainsBoth()
    {
        Assert.Equal(new CameraFrameSize(640, 400), CameraFrameSize.Union(new CameraFrameSize(640, 100), new CameraFrameSize(300, 400)));
    }

    [Fact]
    public void ByDefaultTheDemandWantsEveryFrameInFullResolutionAndNoneForDisplay()
    {
        CameraStreamDemand demand = new(frames: true, packets: false);

        Assert.True(demand.FullResolutionDue);
        Assert.Null(demand.DisplaySize);
    }

    [Fact]
    public void TheDemandWantsFullResolutionOnlyOnceItIsDue()
    {
        FakeTimeProvider time = new();
        CameraStreamDemand demand = new(frames: true, packets: false, time);

        demand.RequestFullResolution(time.GetTimestamp() + time.TimestampFrequency);
        Assert.False(demand.FullResolutionDue);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(demand.FullResolutionDue);

        demand.CancelFullResolution();
        time.Advance(TimeSpan.FromDays(1));
        Assert.False(demand.FullResolutionDue);

        demand.RequestFullResolution(long.MinValue);
        Assert.True(demand.FullResolutionDue);
    }

    [Fact]
    public void TheDemandKeepsItsDisplaySize()
    {
        CameraStreamDemand demand = new(frames: true, packets: false) { DisplaySize = new CameraFrameSize(640, 360) };

        Assert.Equal(new CameraFrameSize(640, 360), demand.DisplaySize);
        demand.DisplaySize = null;
        Assert.Null(demand.DisplaySize);
    }

    [Fact]
    public void AFrameWithoutASourceSizeIsItsOwnSource()
    {
        CameraFrame frame = new(4, 2, 16, new byte[32]);

        Assert.Equal((4, 2), (frame.SourceWidth, frame.SourceHeight));
    }

    [Fact]
    public void AScaledFrameKeepsItsSourceSizeIntoTheCopy()
    {
        CameraFrame frame = new(4, 2, 16, new byte[32], sourceWidth: 1280, sourceHeight: 640);
        CameraFrameExchange exchange = new();

        exchange.Publish(frame);
        CameraFrameBuffer copy = exchange.TakeLatest()!;

        Assert.Equal((4, 2, 1280, 640), (copy.Width, copy.Height, copy.SourceWidth, copy.SourceHeight));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraFrame(4, 2, 16, new byte[32], sourceWidth: 0, sourceHeight: 2));
    }

    [Fact]
    public void ABufferGrowingStepByStepAllocatesWithHeadroom()
    {
        // A window being resized: the display frame grows a little with every frame.
        CameraFrameBuffer buffer = new();
        buffer.CopyFrom(new CameraFrame(100, 100, 400, new byte[400 * 100]));
        Assert.Equal(40_000, buffer.Capacity);

        buffer.CopyFrom(new CameraFrame(101, 100, 404, new byte[404 * 100]));
        int grown = buffer.Capacity;
        Assert.Equal(60_000, grown);
        buffer.CopyFrom(new CameraFrame(110, 110, 440, new byte[440 * 110]));
        Assert.Equal(grown, buffer.Capacity);
    }
}
