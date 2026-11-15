using LayerPeek.Core.Cameras;

namespace LayerPeek.Core.Tests.Cameras;

/// <summary>
/// T-195: the frame for display is scaled down by the decoder only when it gets clearly smaller than the camera's image;
/// a little smaller stays in full resolution (the renderer scales it), with hysteresis between the two thresholds.
/// </summary>
public sealed class CameraDisplayScalerTests
{
    private const int SourceWidth = 1280;
    private const int SourceHeight = 720;

    /// <summary>A display size fitting the 16:9 source to <paramref name="width"/> px wide.</summary>
    private static CameraFrameSize Wide(int width) => new(width, SourceHeight * 4);

    /// <summary>The width of <paramref name="percent"/> % of the source's.</summary>
    private static int WidthOf(int percent) => SourceWidth * percent / 100;

    [Fact]
    public void TheThresholdsLeaveABandForTheHysteresis()
    {
        Assert.InRange(CameraDisplayScaler.ScaleFromPercent, 1, CameraDisplayScaler.FullFromPercent - 1);
        Assert.InRange(CameraDisplayScaler.FullFromPercent, CameraDisplayScaler.ScaleFromPercent + 1, 100);
    }

    [Theory]
    // The mini preview on a Retina display (640 × 384 device pixels) and a quarter of the source.
    [InlineData(640, 384, 640, 360)]
    [InlineData(320, 320, 320, 180)]
    // A window wider than the image: the height limits.
    [InlineData(4000, 300, 533, 300)]
    public void AClearlySmallerFrameIsScaledDown(int maxWidth, int maxHeight, int width, int height)
    {
        CameraDisplayScaler scaler = new();

        Assert.Equal(new CameraFrameSize(width, height), scaler.Target(new CameraFrameSize(maxWidth, maxHeight), SourceWidth, SourceHeight));
        Assert.True(scaler.IsScaling);
    }

    [Fact]
    public void AFrameAtTheThresholdIsScaledDown()
    {
        CameraDisplayScaler scaler = new();
        int width = WidthOf(CameraDisplayScaler.ScaleFromPercent);

        Assert.Equal(Wide(width).Fit(SourceWidth, SourceHeight), scaler.Target(Wide(width), SourceWidth, SourceHeight));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(64)]
    public void AFrameOnlyALittleSmallerStaysInFullResolution(int pixelsAboveTheThreshold)
    {
        CameraDisplayScaler scaler = new();

        Assert.Null(scaler.Target(Wide(WidthOf(CameraDisplayScaler.ScaleFromPercent) + pixelsAboveTheThreshold), SourceWidth, SourceHeight));
        Assert.Null(scaler.Target(Wide(SourceWidth - 32), SourceWidth, SourceHeight));
        Assert.False(scaler.IsScaling);
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    [InlineData(1280, 4000)]
    public void ASourceThatFitsStaysInFullResolution(int maxWidth, int maxHeight)
    {
        CameraDisplayScaler scaler = new();
        // Scaling before: a source that fits ends it however the thresholds are set.
        scaler.Target(new CameraFrameSize(320, 180), SourceWidth, SourceHeight);

        Assert.Null(scaler.Target(new CameraFrameSize(maxWidth, maxHeight), SourceWidth, SourceHeight));
        Assert.False(scaler.IsScaling);
    }

    [Fact]
    public void ResizingAroundTheThresholdsDoesNotSwitchBackAndForth()
    {
        CameraDisplayScaler scaler = new();
        int scaleFrom = WidthOf(CameraDisplayScaler.ScaleFromPercent);
        int fullFrom = WidthOf(CameraDisplayScaler.FullFromPercent);
        int band = (scaleFrom + fullFrom) / 2;

        // The mini preview, then a window growing into the band: scaled down to each new size.
        Assert.Equal(640, scaler.Target(Wide(640), SourceWidth, SourceHeight)?.Width);
        Assert.Equal(scaleFrom + 1, scaler.Target(Wide(scaleFrom + 1), SourceWidth, SourceHeight)?.Width);
        Assert.Equal(band, scaler.Target(Wide(band), SourceWidth, SourceHeight)?.Width);
        Assert.Equal(fullFrom - 1, scaler.Target(Wide(fullFrom - 1), SourceWidth, SourceHeight)?.Width);

        // At the upper threshold: full resolution, and it stays there back in the band.
        Assert.Null(scaler.Target(Wide(fullFrom), SourceWidth, SourceHeight));
        Assert.Null(scaler.Target(Wide(fullFrom - 1), SourceWidth, SourceHeight));
        Assert.Null(scaler.Target(Wide(band), SourceWidth, SourceHeight));
        Assert.Null(scaler.Target(Wide(scaleFrom + 1), SourceWidth, SourceHeight));

        // At the lower threshold it scales again.
        Assert.Equal(scaleFrom, scaler.Target(Wide(scaleFrom), SourceWidth, SourceHeight)?.Width);
        Assert.True(scaler.IsScaling);
        Assert.Equal(band, scaler.Target(Wide(band), SourceWidth, SourceHeight)?.Width);
    }

    [Fact]
    public void ARejectedSourceKeepsTheDecision()
    {
        CameraDisplayScaler scaler = new();
        scaler.Target(new CameraFrameSize(320, 180), SourceWidth, SourceHeight);

        Assert.Throws<ArgumentOutOfRangeException>(() => scaler.Target(new CameraFrameSize(320, 180), 0, SourceHeight));
        Assert.True(scaler.IsScaling);
    }
}
