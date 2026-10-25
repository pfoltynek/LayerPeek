using LayerPeek.App.Cameras;
using LayerPeek.Core.Cameras;

namespace LayerPeek.App.Tests.Cameras;

/// <summary>
/// The mini preview with its info strip (T-089): height from width, aspect ratio and strip, growth away from the nearest
/// work area edge, staying on the work area, the minimum width and the remembered image placement.
/// </summary>
public sealed class CameraMiniGeometryTests
{
    private const double Strip = 33;

    /// <summary>The primary monitor of <see cref="FakeScreenCatalog.ThreeMonitors"/>: 2560 × 1440 at 100 % with a 48 px taskbar.</summary>
    private static readonly PixelBounds Primary = new(0, 0, 2560, 1392);

    /// <summary>The LG of <see cref="FakeScreenCatalog.ThreeMonitors"/>: 1920 × 1080 at 150 % left of the primary one.</summary>
    private static readonly PixelBounds Lg = new(-1920, 200, 1920, 1032);

    private const double FourThree = 0.75;

    [Theory]
    [InlineData(320, null, 213)]
    [InlineData(320, 9.0 / 16.0, 213)]
    [InlineData(320, FourThree, 273)]
    [InlineData(480, null, 303)]
    [InlineData(480, FourThree, 393)]
    public void HeightIsTheImageOfTheStreamsAspectRatioPlusTheStrip(double width, double? ratio, double height)
    {
        CameraWindowPlacement window = CameraMiniGeometry.Correct(new PixelBounds(100, 100, (int)width, 400), width, 1.0, Primary, ratio, Strip);

        Assert.Equal(new CameraWindowPlacement(100, 100, width, height), window);
    }

    [Fact]
    public void WithoutAFrameTheImageIs16By9()
    {
        Assert.Equal(180, CameraMiniGeometry.ImageHeight(320, null));
        Assert.Equal(180, CameraMiniGeometry.ImageHeight(320, double.NaN));
        Assert.Equal(180, CameraMiniGeometry.ImageHeight(320, 0));
        Assert.Equal(240, CameraMiniGeometry.ImageHeight(320, FourThree));
        Assert.Null(CameraMiniGeometry.AspectRatioOf(0, 720));
        Assert.Equal(0.5625, CameraMiniGeometry.AspectRatioOf(1280, 720));
        Assert.Equal(FourThree, CameraMiniGeometry.AspectRatioOf(640, 480));
    }

    [Fact]
    public void NearTheBottomEdgeTheBottomStaysAndTheWindowGrowsUp()
    {
        // 12 px above the taskbar, far from the top.
        CameraWindowPlacement window = CameraMiniGeometry.Correct(new PixelBounds(1000, 1200, 320, 180), 320, 1.0, Primary, null, Strip);

        Assert.Equal(new CameraWindowPlacement(1000, 1380 - 213, 320, 213), window);
    }

    [Fact]
    public void NearTheTopEdgeTheTopStaysAndTheWindowGrowsDown()
    {
        CameraWindowPlacement window = CameraMiniGeometry.Correct(new PixelBounds(1000, 20, 320, 180), 320, 1.0, Primary, null, Strip);

        Assert.Equal(new CameraWindowPlacement(1000, 20, 320, 213), window);
    }

    [Fact]
    public void ShrinkingKeepsTheNearestEdgeToo()
    {
        // A 4:3 window near the bottom becomes 16:9: its bottom edge stays.
        CameraWindowPlacement window = CameraMiniGeometry.Correct(new PixelBounds(1000, 1100, 320, 273), 320, 1.0, Primary, null, Strip);

        Assert.Equal(new CameraWindowPlacement(1000, 1373 - 213, 320, 213), window);
    }

    [Fact]
    public void WindowInTheMiddleGrowsByHalfToBothSides()
    {
        // (1392 - 180) / 2 = 606 above and below.
        CameraWindowPlacement window = CameraMiniGeometry.Correct(new PixelBounds(1000, 606, 320, 180), 320, 1.0, Primary, null, Strip);

        Assert.Equal(new CameraWindowPlacement(1000, 606 - 16, 320, 213), window);
    }

    [Theory]
    [InlineData(-10, -10, 0, 0)]
    [InlineData(2250, -10, 2240, 0)]
    [InlineData(-10, 1222, 0, 1179)]
    [InlineData(2250, 1222, 2240, 1179)]
    public void WindowIsMovedOntoTheWorkAreaInEveryCorner(int x, int y, int expectedX, int expectedY)
    {
        CameraWindowPlacement window = CameraMiniGeometry.Correct(new PixelBounds(x, y, 320, 180), 320, 1.0, Primary, null, Strip);

        Assert.Equal(new CameraWindowPlacement(expectedX, expectedY, 320, 213), window);
        // Never over the taskbar (work area 1392 of 1440).
        Assert.True(window.Y + 213 <= Primary.Bottom);
    }

    [Fact]
    public void OnAMonitorWithAnotherScalingPixelsFollowItsScaling()
    {
        // 150 %: the 320 × 180 image in the bottom right corner with 24 px margin (16 DIP) is 480 × 270 px.
        PixelBounds image = new(-504, 938, 480, 270);

        CameraWindowPlacement window = CameraMiniGeometry.Correct(image, 320, 1.5, Lg, null, Strip);

        // 213 DIP = 319.5 → 320 px, grown up from the bottom edge (1208), which keeps its margin.
        Assert.Equal(new CameraWindowPlacement(-504, 1208 - 320, 320, 213), window);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void RememberedImageComesBackAsTheSameWindowAtAnyScaling(double scaling)
    {
        PixelBounds area = new(0, 0, (int)(1600 * scaling), (int)(900 * scaling));
        foreach (int top in new[] { 0, (int)(40 * scaling), (int)(300 * scaling), (int)(600 * scaling), area.Bottom - (int)Math.Round(213 * scaling, MidpointRounding.AwayFromZero) })
        {
            CameraWindowPlacement window = CameraMiniGeometry.FromImage(
                new CameraWindowPlacement(100, top, 320, 180), scaling, area, null, Strip);
            PixelBounds pixels = Pixels(window, scaling);

            CameraWindowPlacement remembered = CameraMiniGeometry.ToImage(pixels, scaling, area, Strip);
            CameraWindowPlacement again = CameraMiniGeometry.FromImage(remembered, scaling, area, null, Strip);

            Assert.Equal(window.X, again.X);
            Assert.Equal(window.Y, again.Y);
            Assert.Equal(window.Width, again.Width, 0.01);
            Assert.Equal(window.Height, again.Height, 1 / scaling);
        }
    }

    [Theory]
    [InlineData(1.0, 0, 0, 1600, 900)]
    [InlineData(1.25, 0, 0, 2000, 1125)]
    [InlineData(1.5, 0, 0, 2400, 1350)]
    [InlineData(1.75, 0, 0, 2800, 1575)]
    [InlineData(2.0, 0, 0, 3200, 1800)]
    // A monitor left of and above the primary one (negative coordinates), odd and even work area heights.
    [InlineData(1.0, -1920, -1033, 1920, 1033)]
    [InlineData(1.25, -2400, -347, 2400, 1290)]
    [InlineData(1.75, -3360, -1890, 3360, 1849)]
    public void WindowMovedAnywhereComesBackAtTheSamePlaceAtAnyScaling(double scaling, int areaX, int areaY, int areaWidth, int areaHeight)
    {
        // Wherever the user drags the window (every pixel row, including the rows around the middle where it grows by
        // half to both sides), the remembered image opens as the same window again.
        PixelBounds area = new(areaX, areaY, areaWidth, areaHeight);
        CameraWindowPlacement size = CameraMiniGeometry.Correct(new PixelBounds(areaX + 100, areaY, 1, 1), 320, scaling, area, null, Strip);
        PixelBounds sized = Pixels(size, scaling);
        for (int top = area.Y; top <= area.Bottom - sized.Height; top++)
        {
            PixelBounds closed = sized with { Y = top };

            CameraWindowPlacement remembered = CameraMiniGeometry.ToImage(closed, scaling, area, Strip);
            CameraWindowPlacement again = CameraMiniGeometry.FromImage(remembered, scaling, area, null, Strip);

            Assert.Equal((closed.X, closed.Y, closed.Width, closed.Height), (again.X, again.Y, Pixels(again, scaling).Width, Pixels(again, scaling).Height));
        }
    }

    [Fact]
    public void RememberedImageNearTheBottomStaysThereWhenOpenedAgain()
    {
        // A window closed 16 px above the taskbar: its image is remembered below the strip, so it opens at the same place.
        PixelBounds closed = new(2224, 1163, 320, 213);

        CameraWindowPlacement image = CameraMiniGeometry.ToImage(closed, 1.0, Primary, Strip);

        Assert.Equal(new CameraWindowPlacement(2224, 1196, 320, 180), image);
        Assert.Equal(new CameraWindowPlacement(2224, 1163, 320, 213), CameraMiniGeometry.FromImage(image, 1.0, Primary, null, Strip));
    }

    [Fact]
    public void OldRememberedWindowWithoutAStripGrowsByItAwayFromTheNearestEdge()
    {
        // Remembered before T-089: the 320 × 180 window in the default corner, 16 px above the taskbar. It is the image now.
        CameraWindowPlacement window = CameraMiniGeometry.FromImage(new CameraWindowPlacement(2224, 1196, 320, 180), 1.0, Primary, null, Strip);

        Assert.Equal(new CameraWindowPlacement(2224, 1163, 320, 213), window);
    }

    [Fact]
    public void OldRememberedWindowKeepsItsAspectRatioUntilTheStreamTellsItsOwn()
    {
        // 400 × 300 (a 4:3 camera, or a window resized freely before T-089), near the top.
        CameraWindowPlacement image = new(500, 50, 400, 300);

        Assert.Equal(new CameraWindowPlacement(500, 50, 400, 333), CameraMiniGeometry.FromImage(image, 1.0, Primary, null, Strip));
        Assert.Equal(new CameraWindowPlacement(500, 50, 400, 258), CameraMiniGeometry.FromImage(image, 1.0, Primary, 9.0 / 16.0, Strip));
    }

    [Fact]
    public void OldRememberedWindowNarrowerThan240IsMadeWiderAndStaysOnTheScreen()
    {
        // The smallest mini preview before T-089 (160 × 90) in the bottom right corner.
        CameraWindowPlacement window = CameraMiniGeometry.FromImage(new CameraWindowPlacement(2384, 1286, 160, 90), 1.0, Primary, null, Strip);

        // 240 × 135 + 33; the bottom edge stays (16 px above the taskbar), the right edge is moved onto the work area.
        Assert.Equal(new CameraWindowPlacement(2560 - 240, 1376 - 168, 240, 168), window);
    }

    [Fact]
    public void WindowIsNeverNarrowerThan240()
    {
        CameraWindowPlacement window = CameraMiniGeometry.Correct(new PixelBounds(100, 100, 200, 300), 200, 1.0, Primary, null, Strip);

        Assert.Equal(new CameraWindowPlacement(100, 100, 240, 168), window);
        Assert.Equal(CameraWindowSize.MinMiniWidth, CameraMiniGeometry.MinWidth);
    }

    [Fact]
    public void WindowTooHighForTheMonitorBecomesNarrowerToFit()
    {
        PixelBounds small = new(0, 0, 1280, 600);

        CameraWindowPlacement window = CameraMiniGeometry.Correct(new PixelBounds(0, 0, 1200, 600), 1200, 1.0, small, null, Strip);

        // (600 - 33) × 16 / 9 = 1008 wide, exactly as high as the work area.
        Assert.Equal(new CameraWindowPlacement(0, 0, 1008, 600), window);
    }

    [Fact]
    public void WindowWiderThanTheMonitorIsCutToIt()
    {
        PixelBounds narrow = new(0, 0, 800, 1200);

        CameraWindowPlacement window = CameraMiniGeometry.Correct(new PixelBounds(100, 100, 1000, 600), 1000, 1.0, narrow, null, Strip);

        Assert.Equal(new CameraWindowPlacement(0, 100, 800, 483), window);
    }

    [Fact]
    public void CorrectingTwiceChangesNothingMore()
    {
        foreach (PixelBounds start in new[] { new PixelBounds(1000, 1200, 333, 180), new PixelBounds(-10, 606, 517, 420), new PixelBounds(2400, 20, 251, 100) })
        {
            CameraWindowPlacement first = CameraMiniGeometry.Correct(start, start.Width, 1.0, Primary, FourThree, Strip);
            CameraWindowPlacement second = CameraMiniGeometry.Correct(Pixels(first, 1.0), first.Width, 1.0, Primary, FourThree, Strip);

            Assert.Equal(first, second);
        }
    }

    [Fact]
    public void WithoutAWorkAreaTheTopLeftCornerStays()
    {
        CameraWindowPlacement window = CameraMiniGeometry.Correct(new PixelBounds(5000, 5000, 320, 180), 320, 1.0, null, null, Strip);

        Assert.Equal(new CameraWindowPlacement(5000, 5000, 320, 213), window);
        Assert.Equal(new CameraWindowPlacement(5000, 5000, 320, 180), CameraMiniGeometry.ToImage(new PixelBounds(5000, 5000, 320, 213), 1.0, null, Strip));
    }

    [Fact]
    public void GrowKeepsTheLeftEdgeAndOnlyMovesOntoTheWorkArea()
    {
        Assert.Equal(new PixelBounds(100, 1392 - 300, 400, 300), CameraMiniGeometry.Grow(new PixelBounds(100, 1200, 300, 192), 400, 300, Primary));
        Assert.Equal(new PixelBounds(100, 10, 400, 300), CameraMiniGeometry.Grow(new PixelBounds(100, 10, 300, 192), 400, 300, Primary));
        // Bigger than the work area: as big as it.
        Assert.Equal(Primary, CameraMiniGeometry.Grow(new PixelBounds(100, 10, 300, 192), 3000, 2000, Primary));
    }

    private static PixelBounds Pixels(CameraWindowPlacement window, double scaling) => new(
        window.X,
        window.Y,
        (int)Math.Round(window.Width * scaling, MidpointRounding.AwayFromZero),
        (int)Math.Round(window.Height * scaling, MidpointRounding.AwayFromZero));
}
