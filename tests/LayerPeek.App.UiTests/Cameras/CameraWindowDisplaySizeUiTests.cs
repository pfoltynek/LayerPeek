using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

using LayerPeek.App.Cameras;
using LayerPeek.App.Tests.Cameras;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;
using LayerPeek.Core.Cameras;

namespace LayerPeek.App.UiTests.Cameras;

/// <summary>
/// T-184: the camera window asks for frames of the size of its image area (device pixels) and keeps asking when the area
/// changes with the mode, on the same connection; the frames go straight into its bitmap. Headless renders at 100 %.
/// </summary>
public sealed class CameraWindowDisplaySizeUiTests(HeadlessSession session)
{
    private const uint Red = 0xFFFF0000;

    [Fact]
    public Task TheWindowAsksForItsImageAreaInEveryModeOnOneConnection() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors());
            (CameraWindow window, FakeCameraFeed feed) = await harness.OpenStreamingAsync(1280, 720, Red);
            CameraViewModel viewModel = (CameraViewModel)window.DataContext!;
            CameraFrameSize normal = await SettleAsync(harness, window);
            await ShowAsync(window, feed);

            Assert.Equal(normal, viewModel.RequestedVideoSize);
            Assert.Equal(Fitted(normal), ShownSize(window));

            viewModel.ShowMiniCommand.Execute(null);
            CameraFrameSize mini = await SettleAsync(harness, window);
            await ShowAsync(window, feed);

            Assert.True(mini.Width < normal.Width);
            Assert.Equal(mini, viewModel.RequestedVideoSize);
            Assert.Equal(Fitted(mini), ShownSize(window));
            // The mini preview's height follows the camera, not the rounded frame.
            Assert.Equal(720.0 / 1280.0, viewModel.VideoAspectRatio);

            viewModel.ShowFullScreenCommand.Execute(null);
            CameraFrameSize fullScreen = await SettleAsync(harness, window);
            await ShowAsync(window, feed);

            Assert.Equal(fullScreen, viewModel.RequestedVideoSize);
            Assert.Equal(Fitted(fullScreen), ShownSize(window));
            Assert.Single(harness.Factory.Feeds);
        });

    [Fact]
    public Task ClosingTheWindowWhileFramesArriveDisposesItsBitmap() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors());
            (CameraWindow window, FakeCameraFeed feed) = await harness.OpenStreamingAsync(1280, 720, Red);
            Image video = window.Video;
            for (int i = 0; i < 20; i++)
            {
                feed.SendColor(1280, 720, Red);
            }

            window.Close();
            Dispatcher.UIThread.RunJobs();
            await feed.Disposed.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();

            Assert.Null(video.Source);
        });

    /// <summary>The frame size for the image area of the window now.</summary>
    private static CameraFrameSize AreaSize(CameraWindow window) =>
        CameraViewModel.VideoSizeOf(window.Surface.Bounds.Width, window.Surface.Bounds.Height, window.RenderScaling)!.Value;

    /// <summary>A 1280 × 720 camera fitted into <paramref name="size"/>.</summary>
    private static (int Width, int Height) Fitted(CameraFrameSize size) => size.Fit(1280, 720) is var fit ? (fit.Width, fit.Height) : default;

    private static (int Width, int Height) ShownSize(CameraWindow window) =>
        Assert.IsType<WriteableBitmap>(window.Video.Source).PixelSize is var size ? (size.Width, size.Height) : default;

    /// <summary>Lays the window out and lets its changed area settle; returns the size of the area.</summary>
    private static async Task<CameraFrameSize> SettleAsync(CameraHarness harness, CameraWindow window)
    {
        window.Settle();
        harness.Time.Advance(CameraViewModel.VideoSizeSettleTime);
        await Ui.UntilAsync(() => ((CameraViewModel)window.DataContext!).RequestedVideoSize == AreaSize(window), "new size requested");
        return AreaSize(window);
    }

    /// <summary>Sends camera frames until the window shows one of the size it asked for.</summary>
    private static Task ShowAsync(CameraWindow window, FakeCameraFeed feed)
    {
        (int Width, int Height) wanted = Fitted(((CameraViewModel)window.DataContext!).RequestedVideoSize!.Value);
        return Ui.UntilAsync(
            () =>
            {
                feed.SendColor(1280, 720, Red);
                return window.Video.Source is WriteableBitmap { PixelSize: var size } && (size.Width, size.Height) == wanted;
            },
            $"{wanted} frame shown");
    }
}
