using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;

using Microsoft.Extensions.Logging.Abstractions;

using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.App.Tests.Cameras;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.App.UiTests.Infrastructure;
using PrusaTrayDiagnostic.App.Windows;
using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.Printers;

using static PrusaTrayDiagnostic.App.Tests.Tray.TestPrinters;

namespace PrusaTrayDiagnostic.App.UiTests.Cameras;

/// <summary>The camera window (T-014) rendered headless with Skia: real XAML, bindings, input and drawing; fake stream.</summary>
public sealed class CameraWindowUiTests(HeadlessSession session)
{    private const uint Red = 0xFFFF0000;

    private static CameraViewModel ViewModel(CameraWindow window) => (CameraViewModel)window.DataContext!;

    private static Button WindowButton(CameraWindow window, string toolTip) =>
        Ui.Find<Button>(window, button => ToolTip.GetTip(button) as string == toolTip);

    private static void Resize(CameraWindow window, double width, double height)
    {
        window.Width = width;
        window.Height = height;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    // Consecutive double-clicks land on different points: a click away from the previous one starts a new click series,
    // so the second double-click is not taken as the 3rd and 4th click of the first (ignored on purpose by the window).
    private static int _doubleClicks;

    /// <summary>A double-click near the middle of the image, without waiting out the double-click time.</summary>
    private static void DoubleClickImage(CameraWindow window)
    {
        window.Settle();
        double offset = (_doubleClicks++ % 4) * 20;
        Point point = new((window.ClientSize.Width / 2) - 30 + offset, window.ClientSize.Height / 2);
        Ui.Click(window, point);
        Ui.Click(window, point);
        window.Settle();
    }

    // ---- Opening ----

    [Fact]
    public Task ShowCameraOpensOneWindowPerPrinterAndActivatesItAgain() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new();

            CameraWindow alpha = harness.Open("a");
            CameraWindow again = harness.Open("A");
            CameraWindow beta = harness.Open("b");

            Assert.Same(alpha, again);
            Assert.NotSame(alpha, beta);
            Assert.True(alpha.IsVisible && beta.IsVisible);
            Assert.Equal(2, harness.Windows.OpenWindows.Count);
            // The second request created no second view model (no second connection).
            Assert.Equal(2, harness.Created.Count);
            // Headless windows report no activation; that the second request reaches Activate is covered by AuxiliaryWindowSlotTests.
            Assert.Equal("Alpha – Camera", alpha.Title);
            await harness.Factory.WaitForFeedAsync(2);
        });

    [Fact]
    public Task PrinterWithoutCameraOpensNothing() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new();

            Assert.False(harness.Windows.Show("c"));
            Assert.False(harness.Windows.Show("missing"));
            Assert.Empty(harness.Windows.OpenWindows);
        });

    [Fact]
    public Task ShutdownClosesAllWindowsAndRefusesNewOnes() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new();
            CameraWindow alpha = harness.Open("a");
            harness.Open("b");

            harness.Windows.Shutdown();

            Assert.False(alpha.IsVisible);
            Assert.Empty(harness.Windows.OpenWindows);
            Assert.False(harness.Windows.Show("a"));
        });

    // ---- Rendering ----

    [Fact]
    public Task DeliveredFrameIsDrawnAndLetterboxedInATallWindow() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new();
            (CameraWindow window, _) = await harness.OpenStreamingAsync(160, 90, Red);
            Resize(window, 400, 400);

            using WriteableBitmap frame = window.CaptureRenderedFrame()!;

            Assert.Equal(new PixelSize(400, 400), frame.PixelSize);
            // 16:9 in a square: the image fills the width, black bands above and below (aspect ratio kept).
            Assert.Equal((255, 0, 0), Ui.Pixel(frame, 200, 200));
            Assert.Equal((255, 0, 0), Ui.Pixel(frame, 10, 110));
            Assert.Equal((0, 0, 0), Ui.Pixel(frame, 200, 30));
            Assert.Equal((0, 0, 0), Ui.Pixel(frame, 200, 370));
        });

    [Fact]
    public Task WideWindowGetsSideBands() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new();
            (CameraWindow window, _) = await harness.OpenStreamingAsync(160, 90, Red);
            Resize(window, 800, 200);

            using WriteableBitmap frame = window.CaptureRenderedFrame()!;

            Assert.Equal((255, 0, 0), Ui.Pixel(frame, 400, 100));
            Assert.Equal((0, 0, 0), Ui.Pixel(frame, 60, 100));
            Assert.Equal((0, 0, 0), Ui.Pixel(frame, 740, 100));
        });

    [Fact]
    public Task NewFrameReplacesTheImageAndResolutionChangesAreFollowed() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new();
            (CameraWindow window, FakeCameraFeed feed) = await harness.OpenStreamingAsync(160, 90, Red);
            Resize(window, 400, 400);

            feed.SendColor(40, 40, 0xFF0000FF);
            await Ui.UntilAsync(() => feed.Consumed >= 2, "second frame consumed");
            await Ui.UntilAsync(() => Ui.Pixel(window.CaptureRenderedFrame()!, 200, 200) == (0, 0, 255), "blue square drawn");

            using WriteableBitmap frame = window.CaptureRenderedFrame()!;
            // Square image in a square window: no bands any more.
            Assert.Equal((0, 0, 255), Ui.Pixel(frame, 200, 30));
        });

    // ---- Modes ----

    [Fact]
    public Task F11EscAndDoubleClickSwitchFullScreen() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new();
            (CameraWindow window, _) = await harness.OpenStreamingAsync(160, 90, Red);
            CameraViewModel viewModel = ViewModel(window);

            window.KeyPress(Key.F11, RawInputModifiers.None, PhysicalKey.F11, null);
            Assert.True(viewModel.IsFullScreen);
            Assert.Equal(WindowState.FullScreen, window.WindowState);

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.True(viewModel.IsNormal);
            Assert.Equal(WindowState.Normal, window.WindowState);

            DoubleClickImage(window);
            Assert.True(viewModel.IsFullScreen);
            Assert.Equal(WindowState.FullScreen, window.WindowState);

            DoubleClickImage(window);
            Assert.Equal(WindowState.Normal, window.WindowState);
        });

    [Fact]
    public Task MiniPreviewIsBorderlessTopmostAndSmallAndTheCornerButtonReturns() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new();
            (CameraWindow window, _) = await harness.OpenStreamingAsync(160, 90, Red);
            Resize(window, 800, 450);
            CameraViewModel viewModel = ViewModel(window);

            Ui.Click(window, Ui.CentreOf(WindowButton(window, "Mini preview, always on top"), window));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.True(viewModel.IsMini);
            Assert.Equal(WindowDecorations.None, window.WindowDecorations);
            Assert.True(window.Topmost);
            Assert.Equal(new Size(320, 180), window.ClientSize);

            Ui.Click(window, Ui.CentreOf(WindowButton(window, "Back to the normal window (double-click)"), window));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.True(viewModel.IsNormal);
            Assert.Equal(WindowDecorations.Full, window.WindowDecorations);
            Assert.False(window.Topmost);
            Assert.Equal(new Size(800, 450), window.ClientSize);
        });

    [Fact]
    public Task DoubleClickInTheMiniPreviewReturnsToNormal() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new();
            (CameraWindow window, _) = await harness.OpenStreamingAsync(160, 90, Red);
            CameraViewModel viewModel = ViewModel(window);
            viewModel.ShowMiniCommand.Execute(null);

            DoubleClickImage(window);

            Assert.True(viewModel.IsNormal);
            Assert.Equal(WindowDecorations.Full, window.WindowDecorations);
        });

    [Fact]
    public Task MiniPreviewImageIsDrawnToo() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new();
            (CameraWindow window, _) = await harness.OpenStreamingAsync(160, 90, Red);
            ViewModel(window).ShowMiniCommand.Execute(null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            using WriteableBitmap frame = window.CaptureRenderedFrame()!;

            Assert.Equal(new PixelSize(320, 180), frame.PixelSize);
            Assert.Equal((255, 0, 0), Ui.Pixel(frame, 160, 90));
        });

    // ---- Overlays ----

    [Fact]
    public Task StatusOverlayShowsConnectingThenErrorWithReason() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new();
            CameraWindow window = harness.Open();
            FakeCameraFeed feed = await harness.Factory.WaitForFeedAsync(1);
            Assert.True(Ui.ShowsText(window, "Connecting to the camera…"));

            feed.Fail(new CameraFeedException("Decoding failed."));

            await Ui.UntilAsync(() => Ui.ShowsText(window, "Camera error"), "error shown");
            Assert.True(Ui.ShowsText(window, "Decoding failed."));
        });

    [Fact]
    public Task StatusOverlayShowsNoSignalOverTheLastFrame() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new();
            (CameraWindow window, FakeCameraFeed feed) = await harness.OpenStreamingAsync(160, 90, Red);
            Resize(window, 400, 400);
            Assert.False(Ui.ShowsText(window, "No signal"));

            feed.End();

            await Ui.UntilAsync(() => Ui.ShowsText(window, "No signal"), "no signal shown");
            Assert.True(Ui.ShowsText(window, "The camera ended the stream."));
            using WriteableBitmap frame = window.CaptureRenderedFrame()!;
            // The last image stays visible around the overlay.
            Assert.Equal((255, 0, 0), Ui.Pixel(frame, 10, 110));
        });

    [Fact]
    public Task PrintOverlayIsVisibleInEveryMode() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new();
            (CameraWindow window, _) = await harness.OpenStreamingAsync(160, 90, Red);
            CameraViewModel viewModel = ViewModel(window);
            Assert.False(Ui.ShowsText(window, "Printing – 42%"));

            harness.Monitor.Raise(Snapshot(PrinterState.Printing, Job(42.7, TimeSpan.FromMinutes(65)), printerId: "a"));
            await Ui.UntilAsync(() => Ui.ShowsText(window, "Printing – 42%"), "print state shown");

            foreach (Action switchMode in new Action[]
            {
                () => { },
                () => viewModel.ToggleFullScreenCommand.Execute(null),
                () => viewModel.ShowMiniCommand.Execute(null),
            })
            {
                switchMode();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.True(Ui.ShowsText(window, "Printing – 42%"), viewModel.Mode.ToString());
                Assert.True(Ui.ShowsText(window, "1 h 5 min remaining"), viewModel.Mode.ToString());
            }
        });

    // ---- Stream lifetime ----

    [Fact]
    public Task ClosingTheWindowClosesTheStream() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new();
            (CameraWindow window, FakeCameraFeed feed) = await harness.OpenStreamingAsync(160, 90, Red);

            window.Close();

            await feed.Disposed.WaitAsync(TestTimeouts.Wait);
            await feed.EnumerationEnded.WaitAsync(TestTimeouts.Wait);
            Assert.Empty(harness.Windows.OpenWindows);
            Assert.Single(harness.Factory.Feeds);
        });

    [Fact]
    public Task MinimizingStopsTheStreamAndRestoringReconnects() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new();
            (CameraWindow window, FakeCameraFeed first) = await harness.OpenStreamingAsync(160, 90, Red);

            window.WindowState = WindowState.Minimized;
            await first.Disposed.WaitAsync(TestTimeouts.Wait);
            Assert.False(ViewModel(window).IsStreaming);

            window.WindowState = WindowState.Normal;
            FakeCameraFeed second = await harness.Factory.WaitForFeedAsync(2);

            Assert.False(second.IsDisposed);
            Assert.True(ViewModel(window).IsStreaming);
        });

    // ---- Remembered state ----

    [Fact]
    public Task ModePositionAndSizeAreRememberedBetweenRuns() =>
        session.RunAsync(async scope =>
        {
            string directory = UiTestDirectories.NewDirectoryPath();
            try
            {
                JsonCameraWindowStateStore firstStore = new(directory, NullLogger<JsonCameraWindowStateStore>.Instance);
                await using (CameraHarness first = new(firstStore))
                {
                    CameraWindow window = first.Open();
                    Resize(window, 700, 400);
                    window.Position = new PixelPoint(120, 80);
                    ViewModel(window).ShowMiniCommand.Execute(null);
                    Resize(window, 400, 225);
                    window.Position = new PixelPoint(300, 200);
                    window.Close();
                    Assert.True(await firstStore.WaitForPendingSaveAsync(TestTimeouts.Wait));
                }

                JsonCameraWindowStateStore secondStore = new(directory, NullLogger<JsonCameraWindowStateStore>.Instance);
                await secondStore.LoadAsync(TestContext.Current.CancellationToken);
                Assert.Equal(CameraWindowMode.Mini, secondStore.Get("a").Mode);
                await using (CameraHarness second = new(secondStore))
                {
                    CameraWindow restored = second.Open();
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                    Assert.True(ViewModel(restored).IsMini);
                    Assert.Equal(WindowDecorations.None, restored.WindowDecorations);
                    Assert.Equal(new Size(400, 225), restored.ClientSize);
                    Assert.Equal(new PixelPoint(300, 200), restored.Position);

                    ViewModel(restored).ShowNormalCommand.Execute(null);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    Assert.Equal(new Size(700, 400), restored.ClientSize);
                    Assert.Equal(new PixelPoint(120, 80), restored.Position);
                }

                // Closing saved again in the background; the folder is deleted only after that write.
                Assert.True(await secondStore.WaitForPendingSaveAsync(TestTimeouts.Wait));
            }
            finally
            {
                UiTestDirectories.Delete(directory);
            }
        });
}
