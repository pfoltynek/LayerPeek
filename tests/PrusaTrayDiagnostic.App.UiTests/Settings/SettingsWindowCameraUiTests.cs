using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using PrusaTrayDiagnostic.App.Localization;
using PrusaTrayDiagnostic.App.Settings;
using PrusaTrayDiagnostic.App.Tests.Cameras;
using PrusaTrayDiagnostic.App.Tests.Settings;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.App.UiTests.Cameras;
using PrusaTrayDiagnostic.App.UiTests.Infrastructure;
using PrusaTrayDiagnostic.App.Windows;
using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.App.UiTests.Settings;

/// <summary>The Camera section of the real settings window (T-014), driven by mouse and keyboard, on fakes.</summary>
public sealed class SettingsWindowCameraUiTests(HeadlessSession session)
{
    private sealed class Harness : IDisposable
    {
        private bool _disposed;

        public Harness(PrinterDefinition printer, IReadOnlySet<string>? removedCredentials = null)
        {
            Store.Data = new SettingsEditorData(new AppSettings { Printers = [printer] }, SettingsLoadStatus.Loaded, [], new HashSet<string>(StringComparer.OrdinalIgnoreCase), removedCredentials);
            ViewModel = new SettingsViewModel(
                Store,
                new FakeConnectionTester(),
                new PasswordPolicy(IsSupported: true, MaxBytes: 2560),
                cameraTester: new CameraSnapshotTester(Factory, Time, NullLogger<CameraSnapshotTester>.Instance));
            // The window keeps its designed size (T-033): the camera fields are on their own sub-tab.
            Window = new SettingsWindow { DataContext = ViewModel };
        }

        public FakeEditorStore Store { get; } = new();

        public FakeCameraFeedFactory Factory { get; } = new();

        public FakeTimeProvider Time { get; } = new();

        public SettingsViewModel ViewModel { get; }

        public SettingsWindow Window { get; }

        /// <summary>Shows the window and opens the Camera sub-tab with a click on its header, like the user.</summary>
        public async Task ShowAsync()
        {
            Window.Show();
            await ViewModel.LoadAsync();
            Window.Settle();
            Click(Window.Find<TabItem>("PrinterTabCamera"));
            Window.Settle();
            Assert.Equal(SettingsViewModel.CameraPrinterTab, ViewModel.SelectedPrinterTabIndex);
        }

        public void Click(Control control)
        {
            Ui.Click(Window, Ui.CentreOf(control, Window));
            Dispatcher.UIThread.RunJobs();
        }

        public Button Button(string text) => Ui.Find<Button>(Window, button => button.Content as string == text && button.IsEffectivelyVisible);

        public TextBox UrlField => Window.Find<TextBox>("PrinterCameraUrl");

        public string? UrlFieldText => UrlField.Text;

        public void TypeUrl(string text)
        {
            Click(UrlField);
            Window.KeyTextInput(text);
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Window.Close();
            ViewModel.Dispose();
        }
    }

    private static PrinterDefinition Printer(CameraSettings? camera = null) =>
        TestPrinters.Definition("a", "Alpha") with { Camera = camera ?? CameraSettings.Disabled };

    [Fact]
    public Task InvalidAddressShowsItsMessage() =>
        session.RunAsync(async scope =>
        {
            using Harness harness = new(Printer());
            await harness.ShowAsync();
            Assert.True(Ui.ShowsText(harness.Window, "Camera"));

            harness.TypeUrl("http://192.168.0.227/live");

            Assert.True(Ui.ShowsText(harness.Window, "Only rtsp:// addresses are supported."));
        });

    [Fact]
    public Task RemovedCredentialsShowTheNotice() =>
        session.RunAsync(async scope =>
        {
            using Harness harness = new(Printer(new CameraSettings(enabled: false, CameraHarness.StreamUri)), new HashSet<string>(["a"], StringComparer.OrdinalIgnoreCase));
            await harness.ShowAsync();

            Assert.Contains(Ui.VisibleTexts(harness.Window), static text => text.StartsWith("The user name and password were removed", StringComparison.Ordinal));

            // The notice follows a language switch.
            Assert.True(new UiLanguage().Apply("sk"));
            harness.ViewModel.RefreshTexts();
            harness.Window.Settle();
            Assert.Contains(Ui.VisibleTexts(harness.Window), static text => text.StartsWith("Z uloženej adresy kamery bolo odstránené", StringComparison.Ordinal));
        });

    [Fact]
    public Task RemovedCredentialsNoticeDisappearsWhenTheCameraIsTurnedOn() =>
        session.RunAsync(async scope =>
        {
            using Harness harness = new(Printer(new CameraSettings(enabled: false, CameraHarness.StreamUri)), new HashSet<string>(["a"], StringComparer.OrdinalIgnoreCase));
            await harness.ShowAsync();
            Assert.True(harness.Window.IsShown<TextBlock>("PrinterCameraCredentialsRemoved"));

            harness.Click(harness.Window.Find<CheckBox>("PrinterCameraEnabled"));

            Assert.True(harness.ViewModel.SelectedPrinter!.CameraEnabled);
            Assert.False(harness.Window.IsShown<TextBlock>("PrinterCameraCredentialsRemoved"));
        });

    [Fact]
    public Task TestButtonReportsTheResolution() =>
        session.RunAsync(async scope =>
        {
            using Harness harness = new(Printer(new CameraSettings(enabled: true, CameraHarness.StreamUri)));
            await harness.ShowAsync();

            harness.Click(harness.Button("Test camera"));
            Assert.True(Ui.ShowsText(harness.Window, "Waiting for an image from the camera…"));
            FakeCameraFeed feed = await harness.Factory.WaitForFeedAsync(1);
            feed.Send(width: 1280, height: 720);

            await Ui.UntilAsync(() => Ui.ShowsText(harness.Window, "The camera works (1280 × 720 px)."), "success shown");
        });

    [Fact]
    public Task TestButtonReportsATimeoutAfterFifteenSeconds() =>
        session.RunAsync(async scope =>
        {
            using Harness harness = new(Printer(new CameraSettings(enabled: true, CameraHarness.StreamUri)));
            await harness.ShowAsync();

            harness.Click(harness.Button("Test camera"));
            await harness.Factory.WaitForFeedAsync(1);
            harness.Time.Advance(TimeSpan.FromSeconds(15));

            await Ui.UntilAsync(() => Ui.ShowsText(harness.Window, "No image arrived from the camera within 15 s."), "timeout shown");
        });

    // ---- Snapshot preview (T-034) ----

    [Fact]
    public Task NoPreviewIsShownOrReservedBeforeATest() =>
        session.RunAsync(static async scope =>
        {
            using Harness harness = new(Printer(new CameraSettings(enabled: true, CameraHarness.StreamUri)));
            await harness.ShowAsync();

            Assert.False(harness.Window.IsShown<Border>("PrinterCameraPreviewFrame"));
            Assert.Empty(harness.Factory.Feeds);
            // Nothing reserved: the button starts at the left edge of the sub-tab, under the address field.
            Assert.Equal(PositionOf(harness.UrlField, harness.Window).X, PositionOf(harness.Button("Test camera"), harness.Window).X, 1);
        });

    [Fact]
    public Task RedSnapshotFillsThePreviewAfterClickingTest() =>
        session.RunAsync(static async scope =>
        {
            using Harness harness = new(Printer(new CameraSettings(enabled: true, CameraHarness.StreamUri)));
            await harness.ShowAsync();

            await TestWithFrameAsync(harness, 1, 160, 90, Red);

            Border frame = harness.Window.Find<Border>("PrinterCameraPreviewFrame");
            Assert.Equal(new Size(320, 180), frame.Bounds.Size);
            Assert.Equal(new Size(320, 180), harness.Window.Find<Image>("PrinterCameraPreview").Bounds.Size);
            Point origin = PositionOf(frame, harness.Window);
            // Left of the button and its result, aligned with the address field above.
            Assert.Equal(PositionOf(harness.UrlField, harness.Window).X, origin.X, 1);
            Assert.True(PositionOf(harness.Button("Test camera"), harness.Window).X >= origin.X + 320);
            using WriteableBitmap rendered = harness.Window.CaptureRenderedFrame()!;
            // 16:9 in the 16:9 frame: red everywhere, no bands.
            Assert.Equal((255, 0, 0), PixelAt(rendered, origin, 160, 90));
            Assert.Equal((255, 0, 0), PixelAt(rendered, origin, 4, 90));
            Assert.Equal((255, 0, 0), PixelAt(rendered, origin, 316, 90));
            Assert.Equal((255, 0, 0), PixelAt(rendered, origin, 160, 4));
            Assert.Equal((255, 0, 0), PixelAt(rendered, origin, 160, 176));
        });

    [Fact]
    public Task FourByThreeSnapshotKeepsItsAspectRatioWithSideBands() =>
        session.RunAsync(static async scope =>
        {
            using Harness harness = new(Printer(new CameraSettings(enabled: true, CameraHarness.StreamUri)));
            await harness.ShowAsync();

            await TestWithFrameAsync(harness, 1, 160, 120, Red);

            Point origin = PositionOf(harness.Window.Find<Border>("PrinterCameraPreviewFrame"), harness.Window);
            using WriteableBitmap rendered = harness.Window.CaptureRenderedFrame()!;
            // 4:3 scaled to the full height of 180 is 240 wide: black bands of 40 DIP left and right.
            Assert.Equal((255, 0, 0), PixelAt(rendered, origin, 160, 90));
            Assert.Equal((255, 0, 0), PixelAt(rendered, origin, 44, 90));
            Assert.Equal((255, 0, 0), PixelAt(rendered, origin, 276, 90));
            Assert.Equal((255, 0, 0), PixelAt(rendered, origin, 160, 4));
            Assert.Equal((0, 0, 0), PixelAt(rendered, origin, 20, 90));
            Assert.Equal((0, 0, 0), PixelAt(rendered, origin, 36, 90));
            Assert.Equal((0, 0, 0), PixelAt(rendered, origin, 284, 90));
            Assert.Equal((0, 0, 0), PixelAt(rendered, origin, 300, 90));
        });

    [Fact]
    public Task WideSnapshotGetsBandsAboveAndBelow() =>
        session.RunAsync(static async scope =>
        {
            using Harness harness = new(Printer(new CameraSettings(enabled: true, CameraHarness.StreamUri)));
            await harness.ShowAsync();

            // 32:9 (two 16:9 next to each other): 320 wide, 90 high, centred.
            await TestWithFrameAsync(harness, 1, 320, 90, Red);

            Point origin = PositionOf(harness.Window.Find<Border>("PrinterCameraPreviewFrame"), harness.Window);
            using WriteableBitmap rendered = harness.Window.CaptureRenderedFrame()!;
            Assert.Equal((255, 0, 0), PixelAt(rendered, origin, 160, 90));
            Assert.Equal((255, 0, 0), PixelAt(rendered, origin, 160, 50));
            Assert.Equal((255, 0, 0), PixelAt(rendered, origin, 160, 130));
            Assert.Equal((0, 0, 0), PixelAt(rendered, origin, 160, 20));
            Assert.Equal((0, 0, 0), PixelAt(rendered, origin, 160, 160));
        });

    [Fact]
    public Task ChangingTheAddressRemovesThePreview() =>
        session.RunAsync(static async scope =>
        {
            using Harness harness = new(Printer(new CameraSettings(enabled: true, CameraHarness.StreamUri)));
            await harness.ShowAsync();
            await TestWithFrameAsync(harness, 1, 160, 90, Red);
            Point origin = PositionOf(harness.Window.Find<Border>("PrinterCameraPreviewFrame"), harness.Window);

            harness.TypeUrl("1");

            Assert.Equal("rtsp://192.168.0.227/live1", harness.UrlFieldText);
            Assert.False(harness.Window.IsShown<Border>("PrinterCameraPreviewFrame"));
            Assert.Null(harness.Window.Find<Image>("PrinterCameraPreview").Source);
            using WriteableBitmap rendered = harness.Window.CaptureRenderedFrame()!;
            Assert.NotEqual((255, 0, 0), PixelAt(rendered, origin, 160, 90));
        });

    [Fact]
    public Task FailedTestRemovesThePreview() =>
        session.RunAsync(static async scope =>
        {
            using Harness harness = new(Printer(new CameraSettings(enabled: true, CameraHarness.StreamUri)));
            await harness.ShowAsync();
            await TestWithFrameAsync(harness, 1, 160, 90, Red);

            harness.Click(harness.Button("Test camera"));
            Assert.False(harness.Window.IsShown<Border>("PrinterCameraPreviewFrame"));
            (await harness.Factory.WaitForFeedAsync(2)).Fail(new CameraFeedException("Connection refused."));
            await Ui.UntilAsync(() => Ui.ShowsText(harness.Window, "The camera stream could not be opened: Connection refused."), "failure shown");
            harness.Window.Settle();

            Assert.False(harness.Window.IsShown<Border>("PrinterCameraPreviewFrame"));
        });

    [Fact]
    public Task PreviewHasALocalizedNameAndSurvivesALanguageSwitch() =>
        session.RunAsync(static async scope =>
        {
            using Harness harness = new(Printer(new CameraSettings(enabled: true, CameraHarness.StreamUri)));
            await harness.ShowAsync();
            await TestWithFrameAsync(harness, 1, 160, 90, Red);
            Image preview = harness.Window.Find<Image>("PrinterCameraPreview");
            object? source = preview.Source;
            Assert.Equal("Camera snapshot from the test", AutomationProperties.GetName(preview));

            Assert.True(new UiLanguage().Apply("cs"));
            harness.ViewModel.RefreshTexts();
            harness.Window.Settle();

            Assert.Equal("Snímek z testu kamery", AutomationProperties.GetName(preview));
            Assert.True(Ui.ShowsText(harness.Window, "Kamera funguje (160 × 90 px)."));
            Assert.Same(source, preview.Source);
            Point origin = PositionOf(harness.Window.Find<Border>("PrinterCameraPreviewFrame"), harness.Window);
            using WriteableBitmap rendered = harness.Window.CaptureRenderedFrame()!;
            Assert.Equal((255, 0, 0), PixelAt(rendered, origin, 160, 90));
        });

    [Fact]
    public Task ClosingTheWindowReleasesThePreview() =>
        session.RunAsync(static async scope =>
        {
            using Harness harness = new(Printer(new CameraSettings(enabled: true, CameraHarness.StreamUri)));
            await harness.ShowAsync();
            await TestWithFrameAsync(harness, 1, 160, 90, Red);
            Assert.IsType<WriteableBitmap>(harness.ViewModel.SelectedPrinter!.CameraPreview);
            PrinterEditorViewModel printer = harness.ViewModel.SelectedPrinter;

            harness.Dispose();

            Assert.Null(printer.CameraPreview);
        });

    private const uint Red = 0xFFFF0000;

    /// <summary>Clicks Test, answers with one frame of one colour and waits until the snapshot is shown.</summary>
    private static async Task TestWithFrameAsync(Harness harness, int feedNumber, int width, int height, uint argb)
    {
        harness.Click(harness.Button("Test camera"));
        FakeCameraFeed feed = await harness.Factory.WaitForFeedAsync(feedNumber);
        feed.SendColor(width, height, argb);
        await Ui.UntilAsync(() => harness.ViewModel.SelectedPrinter!.HasCameraPreview, "snapshot shown");
        harness.Window.Settle();
        Assert.True(harness.Window.IsShown<Border>("PrinterCameraPreviewFrame"));
    }

    private static Point PositionOf(Control control, Visual window) =>
        control.TranslatePoint(default, window) ?? throw new InvalidOperationException("The control is not in the window.");

    private static (byte R, byte G, byte B) PixelAt(WriteableBitmap rendered, Point origin, double x, double y) =>
        Ui.Pixel(rendered, (int)(origin.X + x), (int)(origin.Y + y));

    [Fact]
    public Task SaveCarriesTheEnteredCamera() =>
        session.RunAsync(async scope =>
        {
            using Harness harness = new(Printer());
            await harness.ShowAsync();

            harness.TypeUrl("rtsp://192.168.0.227/live");
            harness.Click(Ui.Find<CheckBox>(harness.Window, box => box.Content as string == "Use the camera (Camera… item in the tray menu)"));
            harness.Click(harness.Button("Save"));
            await Ui.UntilAsync(() => harness.Store.Saves.Count == 1, "saved");

            Assert.Equal(new CameraSettings(enabled: true, CameraHarness.StreamUri), harness.Store.Saves[0].Updated.Printers[0].Camera);
        });

    [Fact]
    public Task LanguageSwitchTranslatesTheCameraSectionAndItsMessages() =>
        session.RunAsync(static async scope =>
        {
            using Harness harness = new(Printer(new CameraSettings(enabled: true, CameraHarness.StreamUri)), new HashSet<string>(["a"], StringComparer.OrdinalIgnoreCase));
            await harness.ShowAsync();
            harness.Window.TypeOver(harness.UrlField, "x");
            Assert.True(Ui.ShowsText(harness.Window, "This is not a valid address. Use the form rtsp://address/path."));
            // Editing the address answers the notice about the removed credentials (T-033, Q3).
            Assert.False(harness.Window.IsShown<TextBlock>("PrinterCameraCredentialsRemoved"));

            // What the app does after a switch: resources first, then the open window's view model.
            Assert.True(new UiLanguage().Apply("sk"));
            harness.ViewModel.RefreshTexts();
            harness.Window.Settle();

            Assert.True(Ui.ShowsText(harness.Window, "Kamera"));
            Assert.True(Ui.ShowsText(harness.Window, "Adresa RTSP streamu"));
            Assert.True(Ui.ShowsText(harness.Window, "Adresa nie je platná. Použite tvar rtsp://adresa/cesta."));
            Assert.NotNull(harness.Button("Otestovať kameru"));
            Assert.NotNull(Ui.Find<CheckBox>(harness.Window, static box => box.Content as string == "Používať kameru (položka Kamera… v ponuke oznamovacej oblasti)"));
            // The typed address survives the switch.
            Assert.Equal("x", harness.UrlFieldText);
        });
}
