using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

using LayerPeek.App.Cameras;
using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Cameras;
using LayerPeek.App.Tests.Settings;
using LayerPeek.App.Tray;
using LayerPeek.App.UiTests.Cameras;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.UiTests.Settings;

/// <summary>
/// T-040 in the real settings window: only the close button in the title bar, "Factory settings" and "Apply" of the Camera
/// window sub-tab, and Apply placing the open camera window (of <see cref="CameraHarness"/>) on three fake monitors.
/// </summary>
public sealed class SettingsWindowCameraWindowActionsUiTests(HeadlessSession session)
{
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");
    private static readonly CameraMonitorId Lg = new("LG HDR 4K", 1920, 1080);

    private static readonly CameraWindowDefaults Custom = new()
    {
        RememberLastPlacement = false,
        StartMode = CameraWindowStartMode.FullScreen,
        Monitor = Lg,
        Normal = new CameraWindowLayout(new CameraWindowSize(1280, 720), CameraWindowCorner.TopRight),
        Mini = new CameraWindowLayout(new CameraWindowSize(400, 225), CameraWindowCorner.TopLeft),
        TopmostNormal = true,
        TopmostMini = false,
    };

    private static PrinterDefinition Printer(string id = "a", string name = "Alpha", bool cameraEnabled = true, CameraWindowDefaults? window = null) =>
        LinkPrinter(id, name) with { Camera = new CameraSettings(cameraEnabled, StreamUri) { Window = window ?? CameraWindowDefaults.Default } };

    private static async Task<SettingsWindowHarness> OpenAsync(
        UiTestScope scope,
        FakeEditorStore store,
        ICameraWindowStateStore? states = null,
        ICameraWindowPlacer? placer = null)
    {
        SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(
            scope,
            store,
            screens: FakeScreenCatalog.ThreeMonitors(),
            cameraWindowStates: states,
            cameraWindowPlacer: placer);
        ui.Click("PrinterTabCameraWindow");
        Assert.Equal(SettingsViewModel.CameraWindowPrinterTab, ui.ViewModel.SelectedPrinterTabIndex);
        return ui;
    }

    private static CameraViewModel ViewModel(CameraWindow window) => (CameraViewModel)window.DataContext!;

    private static CameraScreen ScreenAt(FakeScreenCatalog screens, int x) => screens.GetScreens().Single(screen => screen.Bounds.X == x);

    /// <summary>
    /// Where <paramref name="layout"/> puts the window on <paramref name="screen"/> (chrome assumed before showing); the mini
    /// preview's layout is its image, the 33 DIP info strip is added by the growth rule (T-089).
    /// </summary>
    private static (PixelPoint Position, Size Size) Expected(CameraWindowLayout layout, CameraScreen screen, CameraWindowMode mode)
    {
        CameraWindowPlacement placement = CameraWindowPlanner.Place(layout, screen, CameraWindowPlanner.AssumedChrome(mode));
        if (mode == CameraWindowMode.Mini)
        {
            placement = CameraMiniGeometry.FromImage(placement, screen.Scaling, screen.UsableArea, null, 33);
        }

        return (new PixelPoint(placement.X, placement.Y), new Size(placement.Width, placement.Height));
    }

    private static void AssertPlaced(CameraWindow window, (PixelPoint Position, Size Size) expected)
    {
        Assert.Equal(expected.Position, window.Position);
        Assert.Equal(expected.Size, window.ClientSize);
    }

    private static Rect BoundsIn(Control control, Visual root) =>
        new(control.TranslatePoint(default, root) ?? throw new InvalidOperationException("Not in the window."), control.Bounds.Size);

    // ---- Title bar ----

    [Fact]
    public Task TitleBarHasCloseAndMaximizeButtonsAndTheWindowStaysResizable() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(Printer()));

            Assert.False(ui.Window.CanMinimize);
            Assert.True(ui.Window.CanMaximize);
            Assert.True(ui.Window.CanResize);
        });

    // ---- Buttons ----

    [Fact]
    public Task ButtonsSitRightAlignedBelowTheTableOfTheSubTab() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope, SettingsWindowHarness.StoreWith(Printer()));
            Button reset = ui.Find<Button>("PrinterCameraWindowReset");
            Button apply = ui.Find<Button>("PrinterCameraWindowApply");

            Assert.Equal("Restore defaults", reset.Content);
            Assert.Equal("Apply", apply.Content);
            Assert.True(reset.IsEffectivelyEnabled);
            Assert.True(apply.IsEffectivelyEnabled);

            Rect resetBounds = BoundsIn(reset, ui.Window);
            Rect applyBounds = BoundsIn(apply, ui.Window);
            Rect miniTopmost = BoundsIn(ui.Find<CheckBox>("PrinterCameraWindowMiniTopmost"), ui.Window);
            Rect monitor = BoundsIn(ui.Find<ComboBox>("PrinterCameraWindowMonitor"), ui.Window);
            Rect save = BoundsIn(ui.Find<Button>("SettingsSave"), ui.Window);

            // One row under the table, Apply last at the right edge of the fields, both above Save / Cancel.
            Assert.Equal(resetBounds.Top, applyBounds.Top);
            Assert.True(resetBounds.Top >= miniTopmost.Bottom);
            Assert.True(resetBounds.Right <= applyBounds.Left);
            Assert.Equal(monitor.Right, applyBounds.Right, 0.5);
            Assert.True(applyBounds.Bottom < save.Top);
        });

    [Fact]
    public Task ButtonsAreDisabledWhileTheCameraIsOff() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope, SettingsWindowHarness.StoreWith(Printer(cameraEnabled: false)));

            Assert.False(ui.Find<Button>("PrinterCameraWindowReset").IsEffectivelyEnabled);
            Assert.False(ui.Find<Button>("PrinterCameraWindowApply").IsEffectivelyEnabled);

            ui.ViewModel.SelectedPrinter!.CameraEnabled = true;
            ui.Window.Settle();

            Assert.True(ui.Find<Button>("PrinterCameraWindowReset").IsEffectivelyEnabled);
            Assert.True(ui.Find<Button>("PrinterCameraWindowApply").IsEffectivelyEnabled);
        });

    [Fact]
    public Task FactorySettingsShowTheDefaultsAndSaveForgetsTheRememberedWindow() =>
        session.RunAsync(static async scope =>
        {
            FakeEditorStore store = SettingsWindowHarness.StoreWith(Printer(window: Custom));
            MemoryCameraWindowStateStore states = new();
            states.Set("a", new CameraWindowState(CameraWindowMode.Mini, null, new CameraWindowPlacement(1, 2, 400, 225)));
            SettingsWindowHarness ui = await OpenAsync(scope, store, states);

            ui.Click("PrinterCameraWindowReset");
            ui.Window.Settle();

            Assert.True(ui.Find<CheckBox>("PrinterCameraWindowRemember").IsChecked);
            Assert.True(Ui.ShowsText(ui.Window, "Same as last time"));
            Assert.True(Ui.ShowsText(ui.Window, "Primary monitor"));
            Assert.Equal(CameraWindowSizeText.Format(new CameraWindowSize(960, 540)), ui.Find<ComboBox>("PrinterCameraWindowNormalSize").Text);
            Assert.Equal(CameraWindowSizeText.Format(new CameraWindowSize(320, 180)), ui.Find<ComboBox>("PrinterCameraWindowMiniSize").Text);
            Assert.True(Ui.ShowsText(ui.Window, "Center"));
            Assert.True(Ui.ShowsText(ui.Window, "Bottom right"));
            Assert.False(ui.Find<CheckBox>("PrinterCameraWindowNormalTopmost").IsChecked);
            Assert.True(ui.Find<CheckBox>("PrinterCameraWindowMiniTopmost").IsChecked);
            Assert.Equal(0, states.GetRemoveCount("a"));

            await ui.SaveAsync();

            Assert.Equal(CameraWindowDefaults.Default, Assert.Single(store.Saves).Updated.Printers[0].Camera.Window);
            Assert.Same(CameraWindowState.Default, states.Get("a"));
            Assert.False(ui.IsOpen);
        });

    // ---- Apply ----

    [Fact]
    public Task ApplyPlacesTheOpenCameraWindowOfThatPrinterAndKeepsTheSettingsOpen() =>
        session.RunAsync(static async scope =>
        {
            FakeScreenCatalog screens = FakeScreenCatalog.ThreeMonitors();
            await using CameraHarness cameras = new(screens: screens);
            CameraWindow alpha = cameras.Open("a");
            CameraWindow beta = cameras.Open("b");
            alpha.Settle();
            beta.Settle();
            alpha.Position = new PixelPoint(300, 300);
            beta.Position = new PixelPoint(400, 400);
            alpha.Settle();

            FakeEditorStore store = SettingsWindowHarness.StoreWith(Printer(), Printer("b", "Beta"));
            SettingsWindowHarness ui = await OpenAsync(scope, store, cameras.States, cameras.Windows);
            PrinterEditorViewModel printer = ui.ViewModel.SelectedPrinter!;
            printer.SelectedStartMode = printer.StartModeOptions.Single(option => option.Value == CameraWindowStartMode.Mini);
            printer.SelectedMonitor = printer.MonitorOptions.Single(option => option.Value?.DisplayName == Lg.DisplayName);
            printer.MiniSizeText = "400 x 225";
            printer.SelectedMiniCorner = printer.CornerOptions.Single(option => option.Value == CameraWindowCorner.TopLeft);
            printer.TopmostMini = false;
            ui.Window.Settle();

            await ui.ApplyCameraWindowAsync();
            Dispatcher.UIThread.RunJobs();
            alpha.Settle();

            Assert.True(ui.IsOpen);
            Assert.Equal("Saved", ui.ShownText("SettingsStatus"));
            Assert.Single(store.Saves);
            Assert.True(ViewModel(alpha).IsMini);
            Assert.Equal(WindowDecorations.None, alpha.WindowDecorations);
            Assert.False(alpha.Topmost);
            // T-102 (2026-10-29): a mini preview that is not on top has a taskbar button like any other window.
            Assert.True(alpha.ShowInTaskbar);
            AssertPlaced(alpha, Expected(new CameraWindowLayout(new CameraWindowSize(400, 225), CameraWindowCorner.TopLeft), ScreenAt(screens, -1920), CameraWindowMode.Mini));

            // The other printer's window stays where it was.
            Assert.Equal(new PixelPoint(400, 400), beta.Position);
            Assert.True(ViewModel(beta).IsNormal);
        });

    [Fact]
    public Task ApplyPlacesTheNormalWindowInItsCornerOnTheChosenMonitor() =>
        session.RunAsync(static async scope =>
        {
            FakeScreenCatalog screens = FakeScreenCatalog.ThreeMonitors();
            await using CameraHarness cameras = new(screens: screens, window: CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini });
            CameraWindow window = cameras.Open("a");
            window.Settle();
            Assert.True(ViewModel(window).IsMini);

            CameraWindowDefaults wanted = CameraWindowDefaults.Default with
            {
                StartMode = CameraWindowStartMode.Normal,
                Monitor = Lg,
                Normal = new CameraWindowLayout(new CameraWindowSize(640, 360), CameraWindowCorner.BottomLeft),
                TopmostNormal = true,
            };
            FakeEditorStore store = SettingsWindowHarness.StoreWith(Printer(window: wanted));
            SettingsWindowHarness ui = await OpenAsync(scope, store, cameras.States, cameras.Windows);

            // Nothing to save (the settings are already these): Apply still places the window.
            await ui.ApplyCameraWindowAsync();
            Dispatcher.UIThread.RunJobs();
            window.Settle();

            Assert.Empty(store.Saves);
            Assert.True(ViewModel(window).IsNormal);
            Assert.Equal(WindowDecorations.Full, window.WindowDecorations);
            Assert.True(window.Topmost);
            // T-102 (2026-10-29): a window on top has no taskbar button, also the normal window.
            Assert.False(window.ShowInTaskbar);
            AssertPlaced(window, Expected(wanted.Normal, ScreenAt(screens, -1920), CameraWindowMode.Normal));
        });

    [Fact]
    public Task ApplyFullScreenGoesFullScreenOnTheChosenMonitor() =>
        session.RunAsync(static async scope =>
        {
            FakeScreenCatalog screens = FakeScreenCatalog.ThreeMonitors();
            await using CameraHarness cameras = new(screens: screens);
            CameraWindow window = cameras.Open("a");
            window.Settle();

            CameraWindowDefaults wanted = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.FullScreen, Monitor = Lg };
            SettingsWindowHarness ui = await OpenAsync(scope, SettingsWindowHarness.StoreWith(Printer(window: wanted)), cameras.States, cameras.Windows);

            await ui.ApplyCameraWindowAsync();
            Dispatcher.UIThread.RunJobs();
            window.Settle();

            Assert.True(ViewModel(window).IsFullScreen);
            Assert.Equal(WindowState.FullScreen, window.WindowState);
            Assert.False(window.Topmost);
            Assert.True(window.ShowInTaskbar);

            // Esc (the command: the settings window has the keyboard here): the normal window, placed on the chosen monitor first.
            ViewModel(window).EscapeCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            window.Settle();
            Assert.Equal(WindowState.Normal, window.WindowState);
            AssertPlaced(window, Expected(wanted.Normal, ScreenAt(screens, -1920), CameraWindowMode.Normal));
        });

    [Fact]
    public Task ApplyRestoresAMinimizedCameraWindowAndItsStream() =>
        session.RunAsync(static async scope =>
        {
            FakeScreenCatalog screens = FakeScreenCatalog.ThreeMonitors();
            await using CameraHarness cameras = new(screens: screens);
            CameraWindow window = cameras.Open("a");
            window.Settle();
            Assert.True(ViewModel(window).IsStreaming);
            window.WindowState = WindowState.Minimized;
            window.Settle();
            Assert.False(ViewModel(window).IsStreaming);

            SettingsWindowHarness ui = await OpenAsync(scope, SettingsWindowHarness.StoreWith(Printer()), cameras.States, cameras.Windows);
            await ui.ApplyCameraWindowAsync();
            Dispatcher.UIThread.RunJobs();
            window.Settle();

            Assert.Equal(WindowState.Normal, window.WindowState);
            Assert.True(ViewModel(window).IsStreaming);
            AssertPlaced(window, Expected(CameraWindowDefaults.DefaultNormal, ScreenAt(screens, 0), CameraWindowMode.Normal));
        });

    [Fact]
    public Task ApplyFromTheMiniPreviewCorrectsTheNormalWindowWithTheRealFrame() =>
        session.RunAsync(static async scope =>
        {
            // A frame unlike the assumed one (larger title bar and borders), known only while the window has a frame.
            WindowChrome real = new(new Size(20, 50), new Thickness(9, 0, 9, 9));
            FakeScreenCatalog screens = FakeScreenCatalog.ThreeMonitors();
            await using CameraHarness cameras = new(
                screens: screens,
                window: CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini },
                measureChrome: window => window.WindowDecorations == WindowDecorations.Full ? real : null);
            CameraWindow window = cameras.Open("a");
            window.Settle();
            Assert.True(ViewModel(window).IsMini);

            CameraWindowDefaults wanted = CameraWindowDefaults.Default with
            {
                StartMode = CameraWindowStartMode.Normal,
                Monitor = Lg,
                Normal = new CameraWindowLayout(new CameraWindowSize(640, 360), CameraWindowCorner.TopLeft),
            };
            SettingsWindowHarness ui = await OpenAsync(scope, SettingsWindowHarness.StoreWith(Printer(window: wanted)), cameras.States, cameras.Windows);
            await ui.ApplyCameraWindowAsync();
            Dispatcher.UIThread.RunJobs();
            window.Settle();

            // Placed with the assumed frame first, then once more with the real one.
            CameraWindowPlacement corrected = CameraWindowPlanner.Place(wanted.Normal, ScreenAt(screens, -1920), real);
            Assert.NotEqual(Expected(wanted.Normal, ScreenAt(screens, -1920), CameraWindowMode.Normal).Position, new PixelPoint(corrected.X, corrected.Y));
            Assert.Equal(new PixelPoint(corrected.X, corrected.Y), window.Position);
            Assert.Equal(new Size(corrected.Width, corrected.Height), window.ClientSize);
        });

    [Fact]
    public Task ApplyOnAFramedWindowUsesItsMeasuredFrameWhereNoneCanBePredicted() =>
        session.RunAsync(static async scope =>
        {
            // Where the frame cannot be predicted (macOS, or Windows refusing): the frame measured on the shown window, not
            // the assumed one followed by a correction (the window would move twice).
            WindowChrome real = new(new Size(20, 50), new Thickness(9, 0, 9, 9));
            FakeScreenCatalog screens = FakeScreenCatalog.ThreeMonitors();
            await using CameraHarness cameras = new(
                screens: screens,
                window: CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal },
                measureChrome: window => window.WindowDecorations == WindowDecorations.Full ? real : null,
                predictChrome: static (_, _) => null);
            CameraWindow window = cameras.Open("a");
            window.Settle();
            Assert.Equal(CameraWindowMode.Normal, ViewModel(window).Mode);

            CameraWindowDefaults wanted = CameraWindowDefaults.Default with
            {
                StartMode = CameraWindowStartMode.Normal,
                Monitor = Lg,
                Normal = new CameraWindowLayout(new CameraWindowSize(640, 360), CameraWindowCorner.TopLeft),
            };
            SettingsWindowHarness ui = await OpenAsync(scope, SettingsWindowHarness.StoreWith(Printer(window: wanted)), cameras.States, cameras.Windows);
            List<PixelPoint> positions = [];
            window.PositionChanged += (_, e) => positions.Add(e.Point);
            await ui.ApplyCameraWindowAsync();
            Dispatcher.UIThread.RunJobs();
            window.Settle();

            CameraWindowPlacement exact = CameraWindowPlanner.Place(wanted.Normal, ScreenAt(screens, -1920), real);
            Assert.Equal(new PixelPoint(exact.X, exact.Y), window.Position);
            Assert.Equal([new PixelPoint(exact.X, exact.Y)], positions.Distinct());
        });

    [Fact]
    public Task SaveDoesNotMoveTheOpenCameraWindow() =>
        session.RunAsync(static async scope =>
        {
            FakeScreenCatalog screens = FakeScreenCatalog.ThreeMonitors();
            await using CameraHarness cameras = new(screens: screens);
            CameraWindow window = cameras.Open("a");
            window.Settle();
            window.Position = new PixelPoint(300, 300);
            window.Settle();
            FakeEditorStore store = SettingsWindowHarness.StoreWith(Printer());
            SettingsWindowHarness ui = await OpenAsync(scope, store, cameras.States, cameras.Windows);
            PrinterEditorViewModel printer = ui.ViewModel.SelectedPrinter!;
            printer.SelectedStartMode = printer.StartModeOptions.Single(option => option.Value == CameraWindowStartMode.Mini);

            await ui.SaveAsync();
            Dispatcher.UIThread.RunJobs();
            window.Settle();

            Assert.Single(store.Saves);
            Assert.False(ui.IsOpen);
            Assert.True(ViewModel(window).IsNormal);
            Assert.Equal(new PixelPoint(300, 300), window.Position);
        });

    [Fact]
    public Task ApplyOpensTheClosedCameraWindowOfThatPrinterBySavedSettings() =>
        session.RunAsync(static async scope =>
        {
            // User report 2026-10-21: Apply with the camera window closed showed nothing (user decision: it opens).
            FakeScreenCatalog screens = FakeScreenCatalog.ThreeMonitors();
            await using CameraHarness cameras = new(screens: screens);
            // Remembered normal window: Apply places by the defaults anyway (like an open window).
            cameras.States.Set("a", new CameraWindowState(CameraWindowMode.Normal, new CameraWindowPlacement(300, 300, 700, 400), null));
            List<(string PrinterId, bool IsOpen)> changes = [];
            cameras.Windows.OpenChanged += (_, e) => changes.Add((e.PrinterId, e.IsOpen));
            FakeEditorStore store = SettingsWindowHarness.StoreWith(Printer(), Printer("b", "Beta"));
            SettingsWindowHarness ui = await OpenAsync(scope, store, cameras.States, cameras.Windows);
            PrinterEditorViewModel printer = ui.ViewModel.SelectedPrinter!;
            printer.SelectedStartMode = printer.StartModeOptions.Single(option => option.Value == CameraWindowStartMode.Mini);
            printer.SelectedMonitor = printer.MonitorOptions.Single(option => option.Value?.DisplayName == Lg.DisplayName);
            printer.SelectedMiniCorner = printer.CornerOptions.Single(option => option.Value == CameraWindowCorner.BottomRight);
            ui.Window.Settle();

            await ui.ApplyCameraWindowAsync();
            Dispatcher.UIThread.RunJobs();

            Assert.True(ui.IsOpen);
            Assert.Single(store.Saves);
            CameraWindow window = Assert.Single(cameras.Windows.OpenWindows).Value;
            Assert.Equal([("a", true)], changes);
            window.Settle();
            Assert.True(window.IsVisible);
            Assert.True(ViewModel(window).IsMini);
            Assert.Equal(WindowDecorations.None, window.WindowDecorations);
            Assert.True(window.Topmost);
            Assert.False(window.ShowInTaskbar);
            AssertPlaced(window, Expected(CameraWindowDefaults.DefaultMini, ScreenAt(screens, -1920), CameraWindowMode.Mini));
            Assert.True(ViewModel(window).IsStreaming);
            // The settings window keeps the focus (user decision 2026-10-21).
            Assert.False(window.ShowActivated);
            Assert.False(window.IsActive);
            Assert.True(ui.Window.IsActive);

            // A second Apply places the now open window again (and opens no second one).
            window.Position = new PixelPoint(10, 10);
            window.Settle();
            await ui.ApplyCameraWindowAsync();
            Dispatcher.UIThread.RunJobs();
            window.Settle();
            Assert.Same(window, Assert.Single(cameras.Windows.OpenWindows).Value);
            AssertPlaced(window, Expected(CameraWindowDefaults.DefaultMini, ScreenAt(screens, -1920), CameraWindowMode.Mini));
        });

    [Fact]
    public Task ApplyOpensTheClosedCameraWindowInFullScreenOnTheChosenMonitor() =>
        session.RunAsync(static async scope =>
        {
            FakeScreenCatalog screens = FakeScreenCatalog.ThreeMonitors();
            await using CameraHarness cameras = new(screens: screens);
            CameraWindowDefaults wanted = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.FullScreen, Monitor = Lg };
            SettingsWindowHarness ui = await OpenAsync(scope, SettingsWindowHarness.StoreWith(Printer(window: wanted)), cameras.States, cameras.Windows);

            // Nothing to save: Apply still opens the window.
            await ui.ApplyCameraWindowAsync();
            Dispatcher.UIThread.RunJobs();
            CameraWindow window = cameras.Windows.OpenWindows["a"];
            window.Settle();

            Assert.True(ViewModel(window).IsFullScreen);
            Assert.Equal(WindowState.FullScreen, window.WindowState);
            Assert.False(window.Topmost);
            Assert.True(window.ShowInTaskbar);

            // Esc: the normal window on the chosen monitor (placed there before going full screen).
            ViewModel(window).EscapeCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            window.Settle();
            Assert.Equal(WindowState.Normal, window.WindowState);
            AssertPlaced(window, Expected(wanted.Normal, ScreenAt(screens, -1920), CameraWindowMode.Normal));
        });

    [Fact]
    public Task ApplyOpensTheCameraWindowFromTheSavedPrinterBeforeMonitoringHasIt() =>
        session.RunAsync(static async scope =>
        {
            // Monitoring restarts after a save and hands the new settings to the app only later: printer "c" has no camera
            // there yet, and its camera, name and address come from the saved settings.
            await using CameraHarness cameras = new(screens: FakeScreenCatalog.ThreeMonitors());
            Uri newCamera = new("rtsp://192.168.0.228/live");
            PrinterDefinition saved = LinkPrinter("c", "Gamma") with { Camera = new CameraSettings(enabled: true, newCamera) };
            SettingsWindowHarness ui = await OpenAsync(scope, SettingsWindowHarness.StoreWith(saved), cameras.States, cameras.Windows);

            await ui.ApplyCameraWindowAsync();
            Dispatcher.UIThread.RunJobs();

            CameraWindow window = cameras.Windows.OpenWindows["c"];
            window.Settle();
            Assert.Equal("Gamma – Camera", window.Title);
            await cameras.Factory.WaitForFeedAsync(1);
            Assert.Equal([newCamera], cameras.Factory.Uris);

            // Monitoring catches up with the saved settings: the window stays open on the same stream.
            ((IMonitoringObserver)cameras.Printers).ApplySettings(new AppSettings { Printers = [saved] });
            Dispatcher.UIThread.RunJobs();
            window.Settle();
            Assert.Same(window, cameras.Windows.OpenWindows["c"]);
            Assert.True(window.IsVisible);
            Assert.Equal([newCamera], cameras.Factory.Uris);
        });

    [Fact]
    public Task SaveDoesNotOpenAClosedCameraWindow() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness cameras = new(screens: FakeScreenCatalog.ThreeMonitors());
            FakeEditorStore store = SettingsWindowHarness.StoreWith(Printer());
            SettingsWindowHarness ui = await OpenAsync(scope, store, cameras.States, cameras.Windows);
            ui.ViewModel.SelectedPrinter!.MiniSizeText = "400 x 225";

            await ui.SaveAsync();
            Dispatcher.UIThread.RunJobs();

            Assert.Single(store.Saves);
            Assert.Empty(cameras.Windows.OpenWindows);
        });

    [Fact]
    public Task CameraWindowsOpenNothingForAPrinterWithoutCameraOrAfterShutdown() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness cameras = new(screens: FakeScreenCatalog.ThreeMonitors());

            Assert.False(cameras.Windows.ApplySettings(Printer("a", cameraEnabled: false)));
            Assert.Empty(cameras.Windows.OpenWindows);

            cameras.Windows.Shutdown();
            Assert.False(cameras.Windows.ApplySettings(Printer("a")));
            Assert.Empty(cameras.Windows.OpenWindows);
            Assert.Empty(cameras.Created);
        });

    [Fact]
    public Task TheTrayMenuStillActivatesTheCameraWindow() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness cameras = new(screens: FakeScreenCatalog.ThreeMonitors());

            CameraWindow window = cameras.Open("a");
            window.Settle();

            Assert.True(window.ShowActivated);
            Assert.True(window.IsActive);
        });
}
