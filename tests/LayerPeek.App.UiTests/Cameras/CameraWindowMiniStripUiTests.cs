using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

using LayerPeek.App.Cameras;
using LayerPeek.App.Cameras.Mini;
using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Localization;
using LayerPeek.App.Tests.Cameras;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.UiTests.Cameras;

/// <summary>
/// The mini preview with its info strip below the image (T-089, design T-076 4) in the real camera window on the fake
/// monitors of <see cref="FakeScreenCatalog.ThreeMonitors"/> (primary 2560 × 1440 at 100 % with a 48 px taskbar).
/// </summary>
public sealed class CameraWindowMiniStripUiTests(HeadlessSession session)
{
    private const uint Red = 0xFFFF0000;
    private const double Strip = 33;

    private static readonly CameraWindowDefaults MiniFirst = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini };

    /// <summary>Nozzle readings after the first one of a finished print, down below the default threshold of 50 °C.</summary>
    private static readonly double[] CoolingReadings = [120, 70, 45, 40];

    private static CameraViewModel ViewModel(CameraWindow window) => (CameraViewModel)window.DataContext!;

    private static Rect BoundsIn(Control control, Visual root) =>
        new(control.TranslatePoint(default, root) ?? throw new InvalidOperationException("Not in the window."), control.Bounds.Size);

    private static CameraMiniStrip StripOf(CameraWindow window) => window.Find<CameraMiniStrip>("CameraMiniInfoStrip");

    private static TextBlock SummaryText(CameraWindow window) =>
        StripOf(window).GetVisualDescendants().OfType<TextBlock>().Single(static text => text.Name == "SummaryText");

    private static Image Image(CameraWindow window) => window.GetVisualDescendants().OfType<Image>().Single(static image => image.Name == "Video");

    private static void Show(CameraHarness harness, CameraWindow window, PrinterSnapshot snapshot)
    {
        harness.Monitor.Raise(snapshot with { PrinterId = "a" });
        Dispatcher.UIThread.RunJobs();
        window.Settle();
    }

    [Theory]
    [InlineData(1280, 720, 240)]
    [InlineData(1280, 720, 320)]
    [InlineData(1280, 720, 480)]
    [InlineData(640, 480, 240)]
    [InlineData(640, 480, 320)]
    [InlineData(640, 480, 480)]
    public Task ImageHasTheStreamsAspectRatioAndTheStripIsBelowIt(int frameWidth, int frameHeight, double width) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            (CameraWindow window, _) = await harness.OpenStreamingAsync(frameWidth, frameHeight, Red);

            CameraHarness.ResizeMiniWithGrip(window, width, 600);

            double imageHeight = width * frameHeight / frameWidth;
            Assert.Equal(width, window.ClientSize.Width);
            Assert.Equal(imageHeight + Strip, window.ClientSize.Height, 0.01);
            Rect image = BoundsIn(Image(window), window);
            Rect strip = BoundsIn(StripOf(window), window);
            Assert.Equal(imageHeight, image.Height, 1.0);
            Assert.Equal(width, image.Width, 0.01);
            // The strip right below the image, as wide as the window, 3 + 30 high.
            Assert.Equal(image.Bottom, strip.Top, 1.0);
            Assert.Equal(new Size(width, Strip), strip.Size);
            Assert.Equal(window.ClientSize.Height, strip.Bottom, 0.01);

            // The picture fills the image row (no letterbox bars), the strip is drawn below it.
            using WriteableBitmap frame = window.CaptureRenderedFrame()!;
            Assert.Equal((255, 0, 0), Ui.Pixel(frame, 2, (int)imageHeight - 2));
            Assert.Equal((255, 0, 0), Ui.Pixel(frame, (int)width - 3, 2));
            Assert.NotEqual((255, 0, 0), Ui.Pixel(frame, (int)width / 2, (int)imageHeight + 20));
        });

    [Fact]
    public Task ResizingCorrectsTheHeightAndKeepsTheEdgeNearTheTaskbar() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            (CameraWindow window, _) = await harness.OpenStreamingAsync(1280, 720, Red);
            // The default corner: 16 px from the right and the taskbar.
            Assert.Equal(new PixelPoint(2224, 1376 - 213), window.Position);
            Assert.Equal(new Size(320, 213), window.ClientSize);

            // Narrower (the right edge dragged in): the height follows the width, the bottom edge keeps its margin.
            CameraHarness.ResizeMiniWithGrip(window, 280, 213);

            // 190.5 DIP: whole pixels.
            Assert.Equal(new Size(280, 191), window.ClientSize);
            Assert.Equal(new PixelPoint(2224, 1376 - 191), window.Position);

            // Wider and much too high, out over the taskbar: the height follows the width, the window grows up from the
            // bottom of the work area.
            window.Position = new PixelPoint(1800, 1163);
            CameraHarness.ResizeMiniWithGrip(window, 480, 700);

            Assert.Equal(new Size(480, 303), window.ClientSize);
            Assert.Equal(new PixelPoint(1800, 1392 - 303), window.Position);

            // Only higher (the bottom edge dragged): back to the height of the width.
            CameraHarness.ResizeMiniWithGrip(window, 480, 400);
            Assert.Equal(new Size(480, 303), window.ClientSize);
            Assert.Equal(new PixelPoint(1800, 1089), window.Position);
        });

    [Fact]
    public Task AWindowOfTheRightSizeIsNotSetAgain() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            (CameraWindow window, _) = await harness.OpenStreamingAsync(1280, 720, Red);
            // 190.5 DIP high: whole pixels differ from the DIP value, the window is still right.
            CameraHarness.ResizeMiniWithGrip(window, 280, 213);
            int changes = 0;
            window.PropertyChanged += (_, e) => changes += e.Property == Layoutable.WidthProperty || e.Property == Layoutable.HeightProperty ? 1 : 0;
            window.PositionChanged += (_, _) => changes++;

            // The grip pressed and released without moving.
            Point grip = new(window.ClientSize.Width - 2, window.ClientSize.Height - 2);
            window.MouseDown(grip, MouseButton.Left);
            window.MouseUp(grip, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            window.Settle();

            Assert.Equal(0, changes);
            Assert.Equal(new Size(280, 191), window.ClientSize);
        });

    [Fact]
    public Task ResizedOverTheRightEdgeTheWindowComesBackOntoTheWorkArea() =>
        session.RunAsync(async scope =>
        {
            // Only the primary monitor: the window cannot belong to another one.
            FakeScreenCatalog screens = new(FakeScreenCatalog.Screen("Primary", 0, 0, 2560, 1440, primary: true));
            await using CameraHarness harness = new(screens: screens, window: MiniFirst);
            (CameraWindow window, _) = await harness.OpenStreamingAsync(1280, 720, Red);

            CameraHarness.ResizeMiniWithGrip(window, 480, 213);

            Assert.Equal(new Size(480, 303), window.ClientSize);
            Assert.Equal(new PixelPoint(2560 - 480, 1376 - 303), window.Position);
        });

    [Fact]
    public Task ResizingNearTheTopEdgeGrowsDown() =>
        session.RunAsync(async scope =>
        {
            CameraWindowDefaults topLeft = MiniFirst with { Mini = new CameraWindowLayout(new CameraWindowSize(320, 180), CameraWindowCorner.TopLeft) };
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: topLeft);
            (CameraWindow window, _) = await harness.OpenStreamingAsync(1280, 720, Red);
            Assert.Equal(new PixelPoint(16, 16), window.Position);

            CameraHarness.ResizeMiniWithGrip(window, 400, 260);

            Assert.Equal(new PixelPoint(16, 16), window.Position);
            Assert.Equal(new Size(400, 258), window.ClientSize);
        });

    [Fact]
    public Task NarrowerThan240IsNotPossible() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            (CameraWindow window, _) = await harness.OpenStreamingAsync(1280, 720, Red);

            Assert.Equal(240, window.MinWidth);
            CameraHarness.ResizeMiniWithGrip(window, 100, 90);

            Assert.Equal(new Size(240, 168), window.ClientSize);

            // The normal window keeps its minimum of 160.
            ViewModel(window).ShowNormalCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(160, window.MinWidth);
        });

    [Fact]
    public Task FirstFrameOfA4By3StreamMakesTheWindowHigherAwayFromTheTaskbar() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            CameraWindow window = harness.Open();
            window.Settle();
            // Until the first frame: 16:9.
            Assert.Equal(new Size(320, 213), window.ClientSize);
            Assert.Equal(new PixelPoint(2224, 1163), window.Position);

            FakeCameraFeed feed = await harness.Factory.WaitForFeedAsync(1);
            feed.SendColor(640, 480, Red);
            await Ui.UntilAsync(() => ViewModel(window).HasFrame, "first frame");
            Dispatcher.UIThread.RunJobs();
            window.Settle();

            Assert.Equal(new Size(320, 273), window.ClientSize);
            Assert.Equal(new PixelPoint(2224, 1376 - 273), window.Position);

            // More frames of the same size change nothing.
            int sizeChanges = 0;
            window.PropertyChanged += (_, e) => sizeChanges += e.Property == Window.ClientSizeProperty ? 1 : 0;
            feed.SendColor(640, 480, Red);
            feed.SendColor(640, 480, Red);
            await Task.Delay(50, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, sizeChanges);
        });

    [Fact]
    public Task StripIsOnlyInTheMiniPreviewAndNoCardsAreDrawnOverItsImage() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors());
            (CameraWindow window, _) = await harness.OpenStreamingAsync(1280, 720, Red);
            Show(harness, window, Snapshot(PrinterState.Printing, Job(42.7, TimeSpan.FromMinutes(65))));
            CameraViewModel viewModel = ViewModel(window);

            Assert.False(StripOf(window).IsEffectivelyVisible);
            Assert.Equal(window.ClientSize.Height, BoundsIn(Image(window), window).Height, 0.01);
            Assert.True(window.Find<OverlayAnchorsPanel>("CameraOverlay").IsEffectivelyVisible);

            viewModel.ShowMiniCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            window.Settle();

            Assert.True(StripOf(window).IsEffectivelyVisible);
            Assert.False(window.Find<OverlayAnchorsPanel>("CameraOverlay").IsEffectivelyVisible);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Border>(), static border => border.Classes.Contains("overlayCard") && border.IsEffectivelyVisible);

            viewModel.ShowFullScreenCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            window.Settle();
            Assert.False(StripOf(window).IsEffectivelyVisible);
        });

    [Fact]
    public Task WindowButtonsStayOverTheImage() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            (CameraWindow window, _) = await harness.OpenStreamingAsync(1280, 720, Red);
            CameraHarness.ResizeMiniWithGrip(window, 240, 168);

            Rect strip = BoundsIn(StripOf(window), window);
            foreach (Button button in window.Find<StackPanel>("CameraWindowButtons").Children.OfType<Button>().Where(static button => button.IsVisible))
            {
                Rect bounds = BoundsIn(button, window);
                Assert.True(bounds.Bottom <= strip.Top, $"{AutomationProperties.GetAutomationId(button)} {bounds} over the strip {strip}");
            }
        });

    [Fact]
    public Task PartsHaveTheirAutomationIdsNamesAndTheReservedToggleSpace() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            (CameraWindow window, _) = await harness.OpenStreamingAsync(1280, 720, Red);
            Show(harness, window, Snapshot(PrinterState.Printing, Job(19.9, TimeSpan.FromMinutes(154.2))));

            OverlayProgressBar progress = window.Find<OverlayProgressBar>("CameraMiniProgress");
            Grid summary = window.Find<Grid>("CameraMiniSummary");
            StackPanel toggles = window.Find<StackPanel>("CameraMiniToggles");
            Rect strip = BoundsIn(StripOf(window), window);

            Assert.Equal(19, progress.Value);
            Assert.Equal(new Rect(strip.X, strip.Y, 320, 3), BoundsIn(progress, window));
            Assert.Equal(30, summary.Bounds.Height);
            Assert.Equal("19% · 2 h 35 min", SummaryText(window).Text);
            Assert.Equal("Printing, 19%, 2 h 35 min remaining", AutomationProperties.GetName(summary));
            Assert.Equal("Printing, 19%, 2 h 35 min remaining", ToolTip.GetTip(summary));
            // Room for the two 30 × 30 toggles of T-090, 2 apart, 6 from the right edge (the corner grip stays free).
            Rect reserved = BoundsIn(toggles, window);
            Assert.Equal(62, reserved.Width);
            Assert.Equal(320 - 6, reserved.Right, 0.01);
            Assert.True(BoundsIn(SummaryText(window), window).Right <= reserved.Left - 8 + 0.01);
            // Only the two toggles of T-090 take the keyboard focus.
            Assert.Equal(
                ["CameraMiniToggleName", "CameraMiniToggleChart"],
                StripOf(window).GetVisualDescendants().OfType<InputElement>().Where(static element => element.Focusable && element.IsEffectivelyVisible).Select(static element => AutomationProperties.GetAutomationId(element)));
        });

    [Fact]
    public Task SummaryInEveryStateAndLanguageFitsTheNarrowestWindow() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            (CameraWindow window, _) = await harness.OpenStreamingAsync(1280, 720, Red);
            CameraHarness.ResizeMiniWithGrip(window, 240, 168);
            PrinterSnapshot[] states =
            [
                Snapshot(PrinterState.Printing, Job(19.9, TimeSpan.FromMinutes(154.2))),
                Snapshot(PrinterState.Printing, Job(100, TimeSpan.FromDays(3.5))),
                Snapshot(PrinterState.Paused, Job(19.9)),
                Snapshot(PrinterState.Attention, Job(19.9)),
                Snapshot(PrinterState.Idle),
                Snapshot(PrinterState.Finished),
                Snapshot(PrinterState.Stopped),
                Snapshot(PrinterState.Busy),
                Snapshot(PrinterState.Offline),
                Snapshot(PrinterState.Error),
                Snapshot(PrinterState.Unauthorized),
                Snapshot(PrinterState.Unknown),
            ];

            // Polls 5 s apart, as the cool-down needs them.
            DateTimeOffset time = Now;
            void Poll(PrinterSnapshot snapshot)
            {
                time = time.AddSeconds(5);
                Show(harness, window, snapshot with { Timestamp = time });
            }

            foreach (string language in AppLanguages.Supported)
            {
                Assert.True(new UiLanguage().Apply(language));
                harness.Windows.RefreshTexts();
                foreach (PrinterSnapshot snapshot in states)
                {
                    Poll(snapshot);
                    AssertSummaryFits(window, $"{language}, {snapshot.State}");
                    if (snapshot.State == PrinterState.Printing && snapshot.Job!.ProgressPercent < 100)
                    {
                        // "19 % · 2 h 35 min" is never trimmed (T-076 4: 140 DIP at 240).
                        Assert.False(SummaryText(window).TextLayout.TextLines[0].HasCollapsed, $"{language}: '{SummaryText(window).Text}' trimmed");
                    }
                }

                // After a print: cooling down, then cooled down (T-079).
                Poll(Snapshot(PrinterState.Printing, Job(50)) with { Telemetry = Hot(215, 215) });
                Poll(Snapshot(PrinterState.Finished, Job(100)) with { Telemetry = Hot(200, 0) });
                Assert.True(ViewModel(window).MiniStrip.ShowsCoolingIcon, language);
                AssertSummaryFits(window, $"{language}, cooling");
                foreach (double nozzle in CoolingReadings)
                {
                    Poll(Snapshot(PrinterState.Finished, Job(100)) with { Telemetry = Hot(nozzle, 0) });
                }

                Assert.True(ViewModel(window).MiniStrip.ShowsCooledIcon, language);
                AssertSummaryFits(window, $"{language}, cooled");
                Assert.False(SummaryText(window).TextLayout.TextLines[0].HasCollapsed, $"{language}: '{SummaryText(window).Text}' trimmed");
                Poll(Snapshot(PrinterState.Offline));
            }
        });

    [Fact]
    public Task DoubleClickOnTheStripReturnsToTheNormalWindowLikeTheImage() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            (CameraWindow window, _) = await harness.OpenStreamingAsync(1280, 720, Red);
            Point onStrip = Ui.CentreOf(SummaryText(window), window);

            Ui.Click(window, onStrip);
            Ui.Click(window, onStrip);
            Dispatcher.UIThread.RunJobs();

            Assert.True(ViewModel(window).IsNormal);
        });

    [Fact]
    public Task ClosedMiniPreviewOpensAgainAtTheSamePlaceAndRemembersItsImage() =>
        session.RunAsync(async scope =>
        {
            MemoryCameraWindowStateStore states = new();
            await using CameraHarness harness = new(states, FakeScreenCatalog.ThreeMonitors(), MiniFirst);
            (CameraWindow window, _) = await harness.OpenStreamingAsync(1280, 720, Red);
            CameraHarness.ResizeMiniWithGrip(window, 400, 300);
            window.Position = new PixelPoint(1500, 1100);
            window.Settle();
            Size size = window.ClientSize;

            window.Close();
            Dispatcher.UIThread.RunJobs();

            // The image (below the strip near the taskbar), not the window.
            Assert.Equal(new CameraWindowPlacement(1500, 1100 + 33, 400, 225), states.Get("a").Mini);
            CameraWindow again = harness.Open();
            again.Settle();
            Assert.Equal(new PixelPoint(1500, 1100), again.Position);
            Assert.Equal(size, again.ClientSize);
        });

    [Theory]
    [InlineData(2224, 1196, 2224, 1163)]
    [InlineData(2224, 1212, 2224, 1179)]
    [InlineData(100, 16, 100, 16)]
    public Task MiniPreviewRememberedBeforeTheStripGrowsByItAndStaysOnTheScreen(int x, int y, int expectedX, int expectedY) =>
        session.RunAsync(async scope =>
        {
            // camera-windows.json of an older version: the 320 × 180 window had no strip; it is the image now.
            MemoryCameraWindowStateStore states = new();
            states.Set("a", new CameraWindowState(CameraWindowMode.Mini, null, new CameraWindowPlacement(x, y, 320, 180)));
            await using CameraHarness harness = new(states, FakeScreenCatalog.ThreeMonitors());

            CameraWindow window = harness.Open();
            window.Settle();

            Assert.True(ViewModel(window).IsMini);
            Assert.Equal(new PixelPoint(expectedX, expectedY), window.Position);
            Assert.Equal(new Size(320, 213), window.ClientSize);
        });

    [Fact]
    public Task SnapshotOfThePrintingStrip() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            (CameraWindow window, _) = await harness.OpenStreamingAsync(1280, 720, 0xFF406080);
            Show(harness, window, Snapshot(PrinterState.Printing, Job(19.9, TimeSpan.FromMinutes(154.2))));

            SnapshotAssert.Matches(window, "camera-mini-strip-printing");
        });

    [Fact]
    public Task SnapshotOfTheCooledStripInGerman() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            (CameraWindow window, _) = await harness.OpenStreamingAsync(1280, 720, 0xFF406080);
            Assert.True(new UiLanguage().Apply("de"));
            harness.Windows.RefreshTexts();
            Show(harness, window, Snapshot(PrinterState.Printing, Job(50)) with { Telemetry = Hot(215, 215) });
            foreach ((int seconds, double nozzle) in new[] { (5, 200.0), (10, 120.0), (15, 70.0), (20, 45.0), (25, 40.0) })
            {
                Show(harness, window, Snapshot(PrinterState.Finished, Job(100)) with { Timestamp = Now.AddSeconds(seconds), Telemetry = Hot(nozzle, 0) });
            }

            Assert.True(ViewModel(window).MiniStrip.ShowsCooledIcon);
            SnapshotAssert.Matches(window, "camera-mini-strip-cooled-de");
        });

    [Fact]
    public Task SnapshotOfTheNarrowestStripWithTheLongestStateInPolish() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            (CameraWindow window, _) = await harness.OpenStreamingAsync(1280, 720, 0xFF406080);
            CameraHarness.ResizeMiniWithGrip(window, 240, 168);
            Assert.True(new UiLanguage().Apply("pl"));
            harness.Windows.RefreshTexts();
            Show(harness, window, Snapshot(PrinterState.Unauthorized));

            Assert.True(SummaryText(window).TextLayout.TextLines[0].HasCollapsed);
            // The pointer left the grip over the window: its buttons are shown (the fade-in runs on real time, skipped).
            StackPanel buttons = window.Find<StackPanel>("CameraWindowButtons");
            buttons.Transitions = null;
            window.Settle();
            Assert.Equal(1, buttons.Opacity);
            SnapshotAssert.Matches(window, "camera-mini-strip-240-pl");
        });

    /// <summary>The summary text stays on one line inside its column (left of the reserved toggles) and the strip keeps its height.</summary>
    private static void AssertSummaryFits(CameraWindow window, string context)
    {
        TextBlock text = SummaryText(window);
        Rect bounds = BoundsIn(text, window);
        Rect toggles = BoundsIn(window.Find<StackPanel>("CameraMiniToggles"), window);
        Assert.False(string.IsNullOrEmpty(text.Text), context);
        Assert.True(bounds.Left >= 10 + 8 + 6 - 0.01, $"{context}: '{text.Text}' starts at {bounds.Left}");
        Assert.True(bounds.Right <= toggles.Left - 8 + 0.01, $"{context}: '{text.Text}' {bounds} reaches the toggles {toggles}");
        Assert.True(text.TextLayout.WidthIncludingTrailingWhitespace <= bounds.Width + 0.5, $"{context}: '{text.Text}' overflows {bounds.Width}");
        Assert.Single(text.TextLayout.TextLines);
        Assert.Equal(Strip, StripOf(window).Bounds.Height);
        Assert.Equal(new Size(240, 168), window.ClientSize);
    }

    private static Telemetry Hot(double nozzle, double target) => new() { Nozzle = new Temperature(nozzle, target), Bed = new Temperature(55, 0) };
}
