using System.Runtime.Versioning;

using LayerPeek.App.Cameras;
using LayerPeek.App.Platform.Windows;

namespace LayerPeek.App.Tests.Platform.Windows;

/// <summary>
/// The frame Windows gives a window style at a DPI (T-071 Q3: computed before the camera window is shown). Asks the real
/// <c>AdjustWindowRectExForDpi</c>, creates no window; the exact sizes depend on the Windows version and theme, so only
/// their shape is checked.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowFrameNativeTests
{
    private const string WindowsOnly = "AdjustWindowRectExForDpi exists only on Windows.";

    // WS_OVERLAPPEDWINDOW (caption, system menu, sizing border, minimize and maximize boxes) and WS_POPUP.
    private const uint OverlappedWindow = 0x00CF0000;
    private const uint Popup = 0x80000000;

    [Fact]
    public void FrameOfANormalWindowGrowsWithTheDpiAndHasATitleBar()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);

        PixelFrame previous = default;
        foreach (uint dpi in new uint[] { 96, 120, 144, 192 })
        {
            PixelFrame frame = WindowFrameNative.FrameForStyle(OverlappedWindow, 0, dpi, visibleBorder: 1)!.Value;

            Assert.Equal(frame.Left, frame.Right);
            Assert.Equal(frame.Left, frame.Bottom);
            Assert.True(frame.Top > frame.Bottom, $"{dpi} dpi: {frame}");
            Assert.True(frame.Left > previous.Left || (frame.Left == previous.Left && frame.Top > previous.Top), $"{dpi} dpi: {frame} after {previous}");
            Assert.Equal(1, frame.VisibleBorder);
            previous = frame;
        }
    }

    [Theory]
    [InlineData(96u, 1)]
    [InlineData(120u, 1)]
    [InlineData(144u, 2)]
    [InlineData(168u, 2)]
    [InlineData(192u, 2)]
    [InlineData(240u, 3)]
    public void VisibleBorderScalesLikeWindowsRoundsPixels(uint dpi, int expected)
    {
        // Measured on Windows 11 (T-071, 2026-10-23): 1 px at 100 % and 125 %, 2 px at 150 % (MulDiv(1, dpi, 96)).
        Assert.Equal(expected, WindowFrameNative.ScaledVisibleBorder(dpi));
    }

    [Fact]
    public void BorderlessWindowHasNoFrame()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);

        Assert.Equal(new PixelFrame(0, 0, 0, 0, 0), WindowFrameNative.FrameForStyle(Popup, 0, 144, visibleBorder: 0));
    }

    [Fact]
    public void NoWindowHasNoFrameToPredict()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);

        Assert.Null(WindowFrameNative.TryPredictFrame(0, 120));
    }
}
