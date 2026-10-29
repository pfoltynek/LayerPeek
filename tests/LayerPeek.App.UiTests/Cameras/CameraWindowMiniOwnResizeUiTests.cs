using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

using LayerPeek.App.Cameras;
using LayerPeek.App.Tests.Cameras;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;
using LayerPeek.Core.Cameras;

namespace LayerPeek.App.UiTests.Cameras;

/// <summary>
/// T-038: on macOS Avalonia.Native has no resize drag (<c>BeginResizeDrag</c> does nothing in 12.1.4) and the borderless
/// mini preview has no resizable edges, so the window follows a dragged grip itself (<see cref="CameraWindow.ResizesItself"/>,
/// switched on here on every OS). Primary monitor of <see cref="FakeScreenCatalog.ThreeMonitors"/>: 2560 × 1440 at 100 %.
/// </summary>
public sealed class CameraWindowMiniOwnResizeUiTests(HeadlessSession session)
{
    private const uint Red = 0xFFFF0000;
    private const double Strip = 33;

    private static readonly CameraWindowDefaults MiniFirst = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini };

    [Fact]
    public Task ItIsOnExactlyOnMacOS() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            CameraWindow window = harness.Open();
            window.Settle();

            Assert.Equal(OperatingSystem.IsMacOS(), window.ResizesItself);
        });

    [Fact]
    public Task BottomRightGripFollowsThePointerAndTheHeightFollowsTheWidthOnRelease() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            (CameraWindow window, _) = await harness.OpenStreamingAsync(1280, 720, Red);
            window.ResizesItself = true;
            PixelPoint position = window.Position;
            Size start = window.ClientSize;
            PixelPoint grip = ScreenPoint(window, start.Width - 2, start.Height - 2);

            Press(window, grip);
            Move(window, grip + new PixelPoint(80, 10));

            // While dragging: the edges follow the pointer, the top left corner stays, nothing is corrected yet.
            Assert.True(window.IsResizingItself);
            Assert.Equal(new Size(start.Width + 80, start.Height + 10), window.ClientSize);
            Assert.Equal(position, window.Position);

            Release(window, grip + new PixelPoint(80, 10));

            Assert.False(window.IsResizingItself);
            Assert.Equal(start.Width + 80, window.ClientSize.Width);
            Assert.Equal(((start.Width + 80) * 9 / 16) + Strip, window.ClientSize.Height, 1.0);
            // The mini preview sat in the bottom right corner: the wider window is moved back onto the work area.
            AssertOnTheWorkArea(window);
        });

    [Fact]
    public Task LeftGripKeepsTheRightEdgeWhileItIsDraggedInSeveralSteps() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            (CameraWindow window, _) = await harness.OpenStreamingAsync(1280, 720, Red);
            window.ResizesItself = true;
            int right = window.Position.X + (int)window.ClientSize.Width;
            double startWidth = window.ClientSize.Width;
            PixelPoint grip = ScreenPoint(window, 2, window.ClientSize.Height / 2);

            Press(window, grip);
            foreach (int step in new[] { -20, -45, -100 })
            {
                Move(window, grip + new PixelPoint(step, 7));

                Assert.Equal(startWidth - step, window.ClientSize.Width);
                Assert.Equal(right, window.Position.X + (int)window.ClientSize.Width);
            }

            Release(window, grip + new PixelPoint(-100, 7));

            Assert.Equal(startWidth + 100, window.ClientSize.Width);
            Assert.Equal(right, window.Position.X + (int)window.ClientSize.Width);
            // Whole pixels: the window has 236.25 + 33 DIP at most a pixel off.
            Assert.Equal(((startWidth + 100) * 9 / 16) + Strip, window.ClientSize.Height, 1.0);
            AssertOnTheWorkArea(window);
        });

    [Fact]
    public Task GripStopsAtTheNarrowestMiniPreview() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            (CameraWindow window, _) = await harness.OpenStreamingAsync(1280, 720, Red);
            window.ResizesItself = true;
            PixelPoint grip = ScreenPoint(window, window.ClientSize.Width - 2, window.ClientSize.Height / 2);

            Press(window, grip);
            Move(window, grip + new PixelPoint(-2000, 0));
            Release(window, grip + new PixelPoint(-2000, 0));

            Assert.Equal(CameraMiniGeometry.MinWidth, window.ClientSize.Width);
            Assert.Equal((CameraMiniGeometry.MinWidth * 9 / 16) + Strip, window.ClientSize.Height, 1.0);
        });

    [Fact]
    public Task AClickOnAGripWithoutMovingChangesNothing() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            (CameraWindow window, _) = await harness.OpenStreamingAsync(1280, 720, Red);
            window.ResizesItself = true;
            (PixelPoint position, Size size) = (window.Position, window.ClientSize);
            PixelPoint grip = ScreenPoint(window, 2, 2);

            Press(window, grip);
            Release(window, grip);

            Assert.False(window.IsResizingItself);
            Assert.Equal(position, window.Position);
            Assert.Equal(size, window.ClientSize);
        });

    [Fact]
    public Task WithoutItTheGripLeavesTheResizeToTheSystem() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            (CameraWindow window, _) = await harness.OpenStreamingAsync(1280, 720, Red);
            window.ResizesItself = false;
            Size size = window.ClientSize;
            PixelPoint grip = ScreenPoint(window, size.Width - 2, size.Height - 2);

            Press(window, grip);
            Move(window, grip + new PixelPoint(80, 10));

            // Headless has no system size loop: nothing follows the pointer.
            Assert.False(window.IsResizingItself);
            Assert.Equal(size, window.ClientSize);
            Release(window, grip + new PixelPoint(80, 10));
            CameraHarness.FinishMiniResize(window);
        });

    /// <summary>A point of the client area (DIP, 100 %) in screen pixels.</summary>
    private static PixelPoint ScreenPoint(CameraWindow window, double x, double y) =>
        window.Position + new PixelPoint((int)x, (int)y);

    /// <summary>The headless mouse takes client coordinates; the window may have moved since the press.</summary>
    private static Point Client(CameraWindow window, PixelPoint screen) =>
        new(screen.X - window.Position.X, screen.Y - window.Position.Y);

    private static void Press(CameraWindow window, PixelPoint screen)
    {
        window.MouseDown(Client(window, screen), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Move(CameraWindow window, PixelPoint screen)
    {
        window.MouseMove(Client(window, screen), RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();
        window.Settle();
    }

    private static void Release(CameraWindow window, PixelPoint screen)
    {
        window.MouseUp(Client(window, screen), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.Settle();
    }

    private static void AssertOnTheWorkArea(CameraWindow window)
    {
        // The work area of the primary monitor (task bar 48 px).
        Assert.InRange(window.Position.X, 0, 2560 - (int)window.ClientSize.Width);
        Assert.InRange(window.Position.Y, 0, 1392 - (int)Math.Round(window.ClientSize.Height));
    }
}
