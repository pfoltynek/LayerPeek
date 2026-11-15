using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Threading;

using LayerPeek.App.Cameras;
using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Cameras.Overlay.Widgets;
using LayerPeek.App.Composition;
using LayerPeek.App.Tests.Cameras;
using LayerPeek.App.UiTests.Cameras;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Printers;

using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Settings;
using LayerPeek.App.UiTests.Diagnostics;
using LayerPeek.App.UiTests.Settings;
using LayerPeek.PrusaLink;

using static LayerPeek.App.Tests.Settings.SettingsTestData;
using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.UiTests.Readme;

/// <summary>Whether the README screenshots are generated in this run (opt-in, see <see cref="ReadmeScreenshots"/>).</summary>
public static class ReadmeScreenshotSwitch
{
    public const string Variable = "LAYERPEEK_README_SCREENSHOTS";

    public const string SkipReason = "Generates the README screenshots into docs/images; run with " + Variable + "=1.";

    public static bool IsOn => Environment.GetEnvironmentVariable(Variable) == "1";
}

/// <summary>
/// Generates the screenshots of README.md (English), README.cz.md (Czech) and the user manuals (manuals/, every
/// language of the app) into <c>docs/images/&lt;language&gt;</c> (docs/readme-screenshots.md). Not a test: skipped unless <c>LAYERPEEK_README_SCREENSHOTS=1</c>.
/// Everything is the real app rendered headless at 2× by Skia: the real windows, widgets and view models on the fakes
/// of the UI tests, a real frame of the recorded Buddy3D clip as the camera picture and the simulator's G-code preview.
/// </summary>
public sealed class ReadmeScreenshots(HeadlessSession session)
{
    public static TheoryData<string> Languages => [.. ReadmeScenario.Cultures.Keys];

    private const double MiniWidth = 400;

    /// <summary>The picture around a cropped widget card, in DIP.</summary>
    private const double CropMargin = 16;

    /// <summary>White space between the crops of the widget sizes image, in DIP.</summary>
    private const double SizesGap = 8;

    /// <summary>The height of the top band of the camera window in the recording image, in DIP (below the top cards).</summary>
    private const double RecordingBandHeight = 156;

    private static readonly CameraWindowDefaults Normal = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal };

    /// <summary>The layout of the hero image: the default one with the temperatures in L (with the chart).</summary>
    private static readonly CameraOverlayLayout HeroLayout = new(
    [
        new OverlayWidgetPlacement(OverlayWidgetIds.Status, OverlayAnchor.TopLeft, OverlayWidgetSize.Medium, emphasized: true),
        new OverlayWidgetPlacement(OverlayWidgetIds.PrintName, OverlayAnchor.TopCenter, OverlayWidgetSize.Medium),
        new OverlayWidgetPlacement(OverlayWidgetIds.Times, OverlayAnchor.BottomLeft, OverlayWidgetSize.Medium),
        new OverlayWidgetPlacement(OverlayWidgetIds.Cooldown, OverlayAnchor.BottomLeft),
        new OverlayWidgetPlacement(OverlayWidgetIds.Temperatures, OverlayAnchor.BottomRight, OverlayWidgetSize.Large),
    ]);

    private static CameraViewModel ViewModel(CameraWindow window) => (CameraViewModel)window.DataContext!;

    /// <summary>The normal window in its default size 960 × 540 once the widgets of the print are shown.</summary>
    private static async Task<CameraWindow> OpenNormalAsync(CameraHarness harness, params string[] widgets)
    {
        string[] shown = widgets.Length > 0 ? widgets : [OverlayWidgetIds.Status, OverlayWidgetIds.PrintName, OverlayWidgetIds.Times, OverlayWidgetIds.Temperatures];
        CameraWindow window = await OpenAsync(harness, w => WidgetsShown(w, shown));
        window.Width = 960;
        window.Height = 540;
        window.Settle();
        Assert.True(ViewModel(window).IsNormal);
        return window;
    }

    /// <summary>Camera windows of the scenario's printer with the real widgets, the real picture and the preview.</summary>
    private static async Task<CameraHarness> CameraHarnessAsync(CameraWindowDefaults window, CameraOverlayLayout overlay, IScreenCatalog? screens = null, LayerPeek.App.Cameras.Recording.ICameraRecordings? recordings = null)
    {
        CameraImage picture = await ReadmeCamera.FrameAsync();
        CameraHarness harness = new(
            screens: screens,
            window: window,
            overlay: overlay,
            widgets: OverlayWidgets.CreateRegistry(),
            thumbnails: ReadmeScenario.Thumbnails(),
            feeds: new StillCameraFeedFactory(picture),
            recordings: recordings,
            first: static printer => printer with { Name = ReadmeScenario.PrinterName, DetectedModel = ReadmeScenario.CoreOne });
        // The clock is at "now" before the stream starts: moving it later would make the frame stale ("No signal").
        harness.Time.SetUtcNow(Now);
        harness.Time.SetLocalTimeZone(ReadmeScenario.TimeZone);
        harness.History.Configure(harness.Printers.Current.Printers!, ReadmeScenario.HistoryInterval);
        ReadmeScenario.RecordHistory(harness.History, "a");
        return harness;
    }

    /// <summary>Opens the window of the printer, waits for the picture, raises the print and waits for the widgets.</summary>
    private static async Task<CameraWindow> OpenAsync(CameraHarness harness, Func<CameraWindow, bool> ready)
    {
        CameraWindow window = harness.Open();
        await Ui.UntilAsync(() => ViewModel(window).HasFrame && !ViewModel(window).IsStatusVisible, "picture shown");
        harness.Monitor.Raise(ReadmeScenario.Printing("a"));
        await Ui.UntilAsync(() => { window.Settle(); return ready(window); }, "widgets shown");
        return window;
    }

    /// <summary>The window buttons appear only under the mouse: the mouse is outside, without the fade.</summary>
    private static void MouseAway(CameraWindow window)
    {
        CameraHarness.SkipWindowButtonFade(window);
        window.MouseMove(new Point(-10, -10));
        window.Settle();
    }

    private static bool WidgetsShown(CameraWindow window, params string[] ids) =>
        ViewModel(window).Overlay.Widgets.Where(widget => ids.Contains(widget.Id)).All(static widget => widget.IsShown)
        && (!ids.Contains(OverlayWidgetIds.PrintName) || ViewModel(window).Overlay.Widgets.OfType<PrintNameWidgetViewModel>().All(static widget => widget.ShowsThumbnail));

    /// <summary>The hero image: the normal camera window with the status, print name, times and temperatures.</summary>
    [Theory(Skip = ReadmeScreenshotSwitch.SkipReason, SkipType = typeof(ReadmeScreenshotSwitch), SkipUnless = nameof(ReadmeScreenshotSwitch.IsOn))]
    [MemberData(nameof(Languages))]
    public Task CameraWindow(string language) =>
        session.RunAsync(async scope =>
        {
            ReadmeScenario.UseLanguage(language);
            await using CameraHarness harness = await CameraHarnessAsync(Normal, HeroLayout);
            CameraWindow window = await OpenNormalAsync(harness);
            MouseAway(window);

            using ReadmeImage image = ReadmeImage.Capture(window);
            image.SaveJpeg(language, "camera-window");
        });

    /// <summary>The mini preview: always on top, with its info strip and both rows (print name, temperature chart) open.</summary>
    [Theory(Skip = ReadmeScreenshotSwitch.SkipReason, SkipType = typeof(ReadmeScreenshotSwitch), SkipUnless = nameof(ReadmeScreenshotSwitch.IsOn))]
    [MemberData(nameof(Languages))]
    public Task MiniPreview(string language) =>
        session.RunAsync(async scope =>
        {
            ReadmeScenario.UseLanguage(language);
            await using CameraHarness harness = await CameraHarnessAsync(
                CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini },
                HeroLayout,
                FakeScreenCatalog.ThreeMonitors());
            CameraWindow window = await OpenAsync(harness, static w => ViewModel(w).IsMini);
            window.Click(window.Find<ToggleButton>("CameraMiniToggleName"));
            window.Click(window.Find<ToggleButton>("CameraMiniToggleChart"));
            CameraHarness.ResizeMiniWithGrip(window, MiniWidth, 600);
            await Ui.UntilAsync(() => { window.Settle(); return !window.Find<LayerPeek.App.Diagnostics.Chart.TemperatureChart>("CameraMiniChart").ShowsNoData; }, "history drawn");
            MouseAway(window);

            using ReadmeImage image = ReadmeImage.Capture(window);
            image.SaveJpeg(language, "camera-mini");
        });

    /// <summary>The layout mode of the camera window: the widgets in their zones, the size popover of the temperatures open.</summary>
    [Theory(Skip = ReadmeScreenshotSwitch.SkipReason, SkipType = typeof(ReadmeScreenshotSwitch), SkipUnless = nameof(ReadmeScreenshotSwitch.IsOn))]
    [MemberData(nameof(Languages))]
    public Task LayoutMode(string language) =>
        session.RunAsync(async scope =>
        {
            ReadmeScenario.UseLanguage(language);
            // Without the Cool-down widget: it has nothing to show while printing (an empty placeholder card in the mode).
            await using CameraHarness harness = await CameraHarnessAsync(Normal, HeroLayout.WithVisible(OverlayWidgetIds.Cooldown, visible: false));
            CameraWindow window = await OpenNormalAsync(harness);
            window.Click(window.Find<Button>("CameraLayoutButton"));
            Dispatcher.UIThread.RunJobs();
            window.Find<Border>("CameraLayoutDim").Transitions = null;
            LayerPeek.App.Cameras.Layout.CameraLayoutModeViewModel mode = ViewModel(window).LayoutMode;
            Assert.True(mode.IsActive);
            LayerPeek.App.Cameras.Layout.CameraLayoutCardViewModel temperatures = mode.Card(OverlayWidgetIds.Temperatures)!;
            mode.Select(temperatures);
            Assert.True(mode.OpenPopover(temperatures));
            window.Settle();
            MouseAway(window);
            Assert.True(window.LayoutModeView.PopoverFlyout.IsOpen);

            // The popover is a popup of its own (not in the window's frame): drawn over the window at its place.
            using ReadmeImage image = ReadmeImage.Capture(window, window.LayoutModeView.Popover);
            image.SaveJpeg(language, "camera-layout-mode");
        });

    /// <summary>One widget (temperatures) in its three sizes S, M and L over the camera picture, side by side.</summary>
    [Theory(Skip = ReadmeScreenshotSwitch.SkipReason, SkipType = typeof(ReadmeScreenshotSwitch), SkipUnless = nameof(ReadmeScreenshotSwitch.IsOn))]
    [MemberData(nameof(Languages))]
    public Task WidgetSizes(string language) =>
        session.RunAsync(async scope =>
        {
            ReadmeScenario.UseLanguage(language);
            // The card in each size anchored bottom right; the crops share the height of the largest card, so that the
            // three slices of the picture line up at the bottom.
            List<(ReadmeImage Frame, Rect Card)> renders = [];
            try
            {
                foreach (OverlayWidgetSize size in new[] { OverlayWidgetSize.Small, OverlayWidgetSize.Medium, OverlayWidgetSize.Large })
                {
                    CameraOverlayLayout layout = new([new OverlayWidgetPlacement(OverlayWidgetIds.Temperatures, OverlayAnchor.BottomRight, size)]);
                    await using CameraHarness harness = await CameraHarnessAsync(Normal, layout);
                    CameraWindow window = await OpenNormalAsync(harness, OverlayWidgetIds.Temperatures);
                    MouseAway(window);
                    Border card = window.Find<Border>(OverlayWidgetViewModel.AutomationIdPrefix + OverlayWidgetIds.Temperatures);
                    renders.Add((ReadmeImage.Capture(window), new Rect(card.TranslatePoint(default, window)!.Value, card.Bounds.Size)));
                }

                double height = renders.Max(static render => render.Card.Height) + (2 * CropMargin);
                List<ReadmeImage> parts = [.. renders.Select(render => render.Frame.Crop(render.Card.X - CropMargin, render.Card.Bottom + CropMargin - height, render.Card.Width + (2 * CropMargin), height))];
                try
                {
                    using ReadmeImage row = ReadmeImage.Row(parts, gap: SizesGap, SkiaSharp.SKColors.White);
                    row.SaveJpeg(language, "widget-sizes");
                }
                finally
                {
                    parts.ForEach(static part => part.Dispose());
                }
            }
            finally
            {
                renders.ForEach(static render => render.Frame.Dispose());
            }
        });

    /// <summary>The Widgets tab of the settings: the widgets of the CORE One, the cool-down thresholds and the card opacity.</summary>
    [Theory(Skip = ReadmeScreenshotSwitch.SkipReason, SkipType = typeof(ReadmeScreenshotSwitch), SkipUnless = nameof(ReadmeScreenshotSwitch.IsOn))]
    [MemberData(nameof(Languages))]
    public Task SettingsWidgets(string language) =>
        session.RunAsync(async scope =>
        {
            ReadmeScenario.UseLanguage(language);
            SettingsWindowHarness ui = await OpenSettingsAsync(scope);
            ui.ViewModel.SelectedTabIndex = SettingsViewModel.WidgetsTab;
            ui.Window.Settle();
            ui.ViewModel.SelectedWidget = ui.ViewModel.Widgets.Single(static widget => widget.Id == OverlayWidgetIds.Cooldown);
            ui.Window.Settle();

            using ReadmeImage image = ReadmeImage.Capture(ui.Window);
            image.SavePng(language, "settings-widgets");
        });

    /// <summary>The Printers tab of the settings with three printers and their pictures.</summary>
    [Theory(Skip = ReadmeScreenshotSwitch.SkipReason, SkipType = typeof(ReadmeScreenshotSwitch), SkipUnless = nameof(ReadmeScreenshotSwitch.IsOn))]
    [MemberData(nameof(Languages))]
    public Task SettingsPrinters(string language) =>
        session.RunAsync(async scope =>
        {
            ReadmeScenario.UseLanguage(language);
            SettingsWindowHarness ui = await OpenSettingsAsync(scope);
            ui.ViewModel.SelectedPrinter = ui.ViewModel.Printers[0];
            ui.Window.Settle();

            using ReadmeImage image = ReadmeImage.Capture(ui.Window);
            image.SavePng(language, "settings-printers");
        });

    /// <summary>The diagnostics window of the CORE One with the temperature history of the print.</summary>
    [Theory(Skip = ReadmeScreenshotSwitch.SkipReason, SkipType = typeof(ReadmeScreenshotSwitch), SkipUnless = nameof(ReadmeScreenshotSwitch.IsOn))]
    [MemberData(nameof(Languages))]
    public Task Diagnostics(string language) =>
        session.RunAsync(scope =>
        {
            ReadmeScenario.UseLanguage(language);
            DiagnosticsWindowHarness ui = DiagnosticsWindowHarness.OpenWithHistory(
                scope,
                ReadmeScenario.RecordHistory,
                [Printers()[0]],
                ReadmeScenario.Printing("a", Now.AddSeconds(-2)));
            ui.CompleteInfo(new PrusaLinkInfoResult(PrinterState.Printing, null, ReadmeScenario.Info));

            using ReadmeImage image = ReadmeImage.Capture(ui.Window);
            image.SavePng(language, "diagnostics");
            return Task.CompletedTask;
        });

    /// <summary>
    /// The camera window while the print is recorded and a timelapse is taken: the red REC and the amber TL indicators
    /// stay in the top right corner without the pointer. Only the top band of the window (the status and print name cards
    /// and the indicators).
    /// </summary>
    [Theory(Skip = ReadmeScreenshotSwitch.SkipReason, SkipType = typeof(ReadmeScreenshotSwitch), SkipUnless = nameof(ReadmeScreenshotSwitch.IsOn))]
    [MemberData(nameof(Languages))]
    public Task CameraRecording(string language) =>
        session.RunAsync(async scope =>
        {
            ReadmeScenario.UseLanguage(language);
            LayerPeek.App.Tests.Cameras.Recording.FakeCameraRecordings recordings = new();
            recordings.SetActivity("a", LayerPeek.Core.Cameras.Recording.CameraRecordingActivity.Recording, raise: false);
            recordings.SetTimelapse("a", LayerPeek.Core.Cameras.Recording.CameraRecordingActivity.Recording, raise: false);
            await using CameraHarness harness = await CameraHarnessAsync(Normal, HeroLayout, recordings: recordings);
            CameraWindow window = await OpenNormalAsync(harness);
            await Ui.UntilAsync(() => { window.Settle(); return window.IsShown<TextBlock>("CameraRecordingIndicator") && window.IsShown<TextBlock>("CameraTimelapseIndicator"); }, "indicators shown");
            MouseAway(window);

            using ReadmeImage frame = ReadmeImage.Capture(window);
            using ReadmeImage image = frame.Crop(0, 0, window.Bounds.Width, RecordingBandHeight);
            image.SaveJpeg(language, "camera-recording");
        });

    /// <summary>
    /// The Recording tab of the settings: the folder and size limit, the dashcam, the timelapse and the print failure
    /// detection (switched on), the last two with their Experimental badge.
    /// </summary>
    [Theory(Skip = ReadmeScreenshotSwitch.SkipReason, SkipType = typeof(ReadmeScreenshotSwitch), SkipUnless = nameof(ReadmeScreenshotSwitch.IsOn))]
    [MemberData(nameof(Languages))]
    public Task SettingsRecording(string language) =>
        session.RunAsync(async scope =>
        {
            ReadmeScenario.UseLanguage(language);
            SettingsWindowHarness ui = await OpenSettingsAsync(scope, detection: true);
            ui.ViewModel.SelectedTabIndex = SettingsViewModel.RecordingTab;
            ui.Window.Settle();

            using ReadmeImage image = ReadmeImage.Capture(ui.Window);
            image.SavePng(language, "settings-recording");
        });

    /// <summary>
    /// The Camera sub-tab of the CORE One: automatic recording, the dashcam and the timelapse of prints switched on.
    /// </summary>
    [Theory(Skip = ReadmeScreenshotSwitch.SkipReason, SkipType = typeof(ReadmeScreenshotSwitch), SkipUnless = nameof(ReadmeScreenshotSwitch.IsOn))]
    [MemberData(nameof(Languages))]
    public Task SettingsCamera(string language) =>
        session.RunAsync(async scope =>
        {
            ReadmeScenario.UseLanguage(language);
            SettingsWindowHarness ui = await OpenSettingsAsync(scope, detection: true);
            ui.ViewModel.SelectedPrinter = ui.ViewModel.Printers[0];
            ui.ViewModel.SelectedPrinterTabIndex = SettingsViewModel.CameraPrinterTab;
            ui.Window.Settle();

            using ReadmeImage image = ReadmeImage.Capture(ui.Window);
            image.SavePng(language, "settings-camera");
        });

    /// <summary>
    /// The Detection sub-tab of the CORE One: detection on, the bed region (the Core One preset) over a frame of the real
    /// camera recording loaded with "Load frame", the "Change on the bed" detector and the dark filament mode. JPEG like the
    /// images with the camera picture (as PNG the photo in the region editor alone would weigh about 250 kB).
    /// </summary>
    [Theory(Skip = ReadmeScreenshotSwitch.SkipReason, SkipType = typeof(ReadmeScreenshotSwitch), SkipUnless = nameof(ReadmeScreenshotSwitch.IsOn))]
    [MemberData(nameof(Languages))]
    public Task SettingsDetection(string language) =>
        session.RunAsync(async scope =>
        {
            ReadmeScenario.UseLanguage(language);
            CameraImage picture = await ReadmeCamera.FrameAsync();
            FakeCameraTester camera = new() { Immediate = new CameraTestResult(CameraTestOutcome.Success, picture.Width, picture.Height, Image: picture) };
            SettingsWindowHarness ui = await OpenSettingsAsync(scope, detection: true, camera);
            ui.ViewModel.SelectedPrinter = ui.ViewModel.Printers[0];
            ui.ViewModel.SelectedPrinterTabIndex = SettingsViewModel.DetectionPrinterTab;
            ui.Window.Settle();
            ui.Click("PrinterDetectionLoadFrame");
            await Ui.UntilAsync(() => { ui.Window.Settle(); return ui.ViewModel.SelectedPrinter!.HasRegionEditorImage; }, "the frame is shown");
            Assert.False(ui.Window.IsShown<TextBlock>("PrinterDetectionLastFrame"));
            Assert.False(ui.Window.IsShown<TextBlock>("PrinterDetectionGloballyOff"));

            using ReadmeImage image = ReadmeImage.Capture(ui.Window);
            image.SaveJpeg(language, "settings-detection");
        });

    /// <summary>The printers of the settings and diagnostics images: the scenario's CORE One with its camera, an MK4S and a MINI+.</summary>
    private static PrinterDefinition[] Printers(bool detection = false) =>
    [
        LinkPrinter("a", ReadmeScenario.PrinterName, ReadmeScenario.PrinterAddress) with
        {
            DetectedModel = ReadmeScenario.CoreOne,
            Camera = new CameraSettings(enabled: true, detection ? ReadmeScenario.CameraAddress : CameraHarness.StreamUri)
            {
                Overlay = HeroLayout,
                RecordPrints = detection,
                Dashcam = detection,
                TimelapsePrints = detection,
            },
            Detection = new LayerPeek.Core.Detection.PrinterDetectionSettings { Enabled = detection },
        },
        LinkPrinter("b", ReadmeScenario.SecondPrinterName, ReadmeScenario.SecondPrinterAddress) with { DetectedModel = new PrinterModelCode(1, 4, 1) },
        LinkPrinter("c", ReadmeScenario.ThirdPrinterName, ReadmeScenario.ThirdPrinterAddress) with { DetectedModel = new PrinterModelCode(2, 1, 0) },
    ];

    /// <summary>
    /// The settings window with <see cref="Printers"/>; with <paramref name="detection"/> the recording features of the
    /// CORE One (automatic recording, dashcam, timelapse of prints) and the print failure detection (for all printers and
    /// for the CORE One) are switched on.
    /// </summary>
    private static async Task<SettingsWindowHarness> OpenSettingsAsync(UiTestScope scope, bool detection = false, ICameraTester? cameraTester = null)
    {
        FakeEditorStore store = SettingsWindowHarness.StoreWith(Printers(detection));
        // The passwords are saved (shown as such, never as text).
        store.Data = store.Data with
        {
            PrintersWithPassword = Ids("a", "b", "c"),
            Settings = store.Data.Settings with { Detection = LayerPeek.Core.Detection.PrintFailureDetectionSettings.Default with { Enabled = detection } },
        };
        return await SettingsWindowHarness.OpenAsync(scope, store, cameraTester: cameraTester, macTerms: OperatingSystem.IsMacOS());
    }
}
