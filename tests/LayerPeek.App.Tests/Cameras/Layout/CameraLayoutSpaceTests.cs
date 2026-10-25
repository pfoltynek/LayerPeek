using Avalonia;

using LayerPeek.App.Cameras;
using LayerPeek.App.Cameras.Layout;
using LayerPeek.Core.Cameras;

namespace LayerPeek.App.Tests.Cameras.Layout;

/// <summary>
/// Room for the layout mode (T-076 Q4) and the temporary enlargement of a small window when the mode is started from the
/// settings window (T-091 Q5, Q1).
/// </summary>
public sealed class CameraLayoutSpaceTests
{
    private static readonly WindowChrome Chrome = new(new Size(16, 39), new Thickness(7, 0, 7, 7));

    private static CameraScreen Screen(int x, int y, int width, int height, double scaling = 1.0, int taskbar = 48) =>
        new("Monitor", new PixelBounds(x, y, width, height), new PixelBounds(x, y, width, height - taskbar), scaling, IsPrimary: x == 0 && y == 0);

    [Theory]
    [InlineData(480, 300, true)]
    [InlineData(960, 540, true)]
    [InlineData(479, 300, false)]
    [InlineData(480, 299.5, false)]
    [InlineData(320, 180, false)]
    public void WindowNeedsAtLeast480By300(double width, double height, bool fits) =>
        Assert.Equal(fits, CameraLayoutSpace.Fits(width, height));

    [Fact]
    public void BigEnoughWindowIsNotChanged() =>
        Assert.Null(CameraLayoutSpace.Enlarged(new CameraWindowPlacement(100, 100, 480, 300), [Screen(0, 0, 1920, 1080)], Chrome));

    [Fact]
    public void SmallWindowGrowsTo640By360AtTheSamePlace() =>
        Assert.Equal(
            new CameraWindowPlacement(100, 120, 640, 360),
            CameraLayoutSpace.Enlarged(new CameraWindowPlacement(100, 120, 400, 225), [Screen(0, 0, 1920, 1080)], Chrome));

    [Fact]
    public void LargerDimensionStays() =>
        Assert.Equal(
            new CameraWindowPlacement(100, 120, 1000, 360),
            CameraLayoutSpace.Enlarged(new CameraWindowPlacement(100, 120, 1000, 250), [Screen(0, 0, 1920, 1080)], Chrome));

    [Fact]
    public void WindowThatWouldLeaveTheWorkAreaMovesInside()
    {
        // Bottom right corner: the visible window (client + visible frame) ends exactly at the work area.
        CameraWindowPlacement? placement = CameraLayoutSpace.Enlarged(new CameraWindowPlacement(1600, 850, 300, 170), [Screen(0, 0, 1920, 1080)], Chrome);

        Assert.NotNull(placement);
        Assert.Equal(640, placement.Value.Width);
        Assert.Equal(360, placement.Value.Height);
        // Visible right = X + 7 + 640 + 2 (visible borders) = 1920; visible bottom = Y + 39 + 360 - 7 = 1032 (taskbar 48).
        Assert.Equal(1920 - (640 + 16 - 7), placement.Value.X);
        Assert.Equal(1032 - (360 + 39 - 7), placement.Value.Y);
    }

    [Fact]
    public void WindowOnAScaledSecondMonitorMovesInsideItsWorkArea()
    {
        CameraScreen right = Screen(1920, 0, 2560, 1440, scaling: 1.5);
        CameraWindowPlacement? placement = CameraLayoutSpace.Enlarged(new CameraWindowPlacement(4300, 1200, 320, 180), [Screen(0, 0, 1920, 1080), right], Chrome);

        Assert.NotNull(placement);
        int visibleRight = placement.Value.X + (int)Math.Round((640 + 16 - 7) * 1.5);
        int visibleBottom = placement.Value.Y + (int)Math.Round((360 + 39 - 7) * 1.5);
        Assert.Equal(right.UsableArea.Right, visibleRight);
        Assert.Equal(right.UsableArea.Bottom, visibleBottom);
    }

    [Fact]
    public void WindowAboveOrLeftOfTheWorkAreaMovesIn()
    {
        CameraWindowPlacement? placement = CameraLayoutSpace.Enlarged(new CameraWindowPlacement(-50, -20, 300, 170), [Screen(0, 0, 1920, 1080)], Chrome);

        Assert.NotNull(placement);
        Assert.Equal(-7, placement.Value.X);
        Assert.Equal(0, placement.Value.Y);
    }

    [Fact]
    public void WithoutMonitorsOnlyTheSizeChanges() =>
        Assert.Equal(
            new CameraWindowPlacement(5000, 5000, 640, 360),
            CameraLayoutSpace.Enlarged(new CameraWindowPlacement(5000, 5000, 200, 100), [], Chrome));
}
