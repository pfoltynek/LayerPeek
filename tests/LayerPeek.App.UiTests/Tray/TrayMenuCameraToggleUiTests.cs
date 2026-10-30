using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Cameras;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tests.Tray.Menu;
using LayerPeek.App.Tray;
using LayerPeek.App.Tray.Menu;
using LayerPeek.App.UiTests.Cameras;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.UiTests.Tray;

/// <summary>
/// The "Show camera" / "Hide camera" item of the tray menu (T-040; a plain item instead of a check item since 2026-10-22)
/// on the real Avalonia <see cref="NativeMenu"/>, wired to the real camera windows of <see cref="CameraHarness"/> like the
/// app does it (<c>App.ToggleCamera</c>, <c>CameraWindows.OpenChanged</c> → <see cref="TrayMenuViewModel.SetCameraWindowOpen"/>).
/// How the OS menu shows the changed text stays a manual check.
/// </summary>
public sealed class TrayMenuCameraToggleUiTests(HeadlessSession session)
{
    // The session runs in English ("system" language on en-US).
    private const string ShowCamera = "Show _camera";
    private const string HideCamera = "Hide _camera";

    /// <summary>The tray menu of printers "a" and "b" (both with cameras, like the harness) on the app's tray icon.</summary>
    private sealed class Wiring : ITrayAppCommands
    {
        private Wiring(UiTestScope scope, CameraHarness cameras, TrayMenuOptions options, params PrinterDefinition[] printers)
        {
            Cameras = cameras;
            TrayIcon icon = Assert.Single(TrayIcon.GetIcons(Application.Current!)!);
            Menu = icon.Menu!;
            ViewModel = scope.Defer(new TrayMenuViewModel(new FakePrinterMonitor(), new AvaloniaUiDispatcher(), TestWebOpener.Create(new FakeUrlLauncher()), options));
            scope.Defer(new TrayMenuPresenter(new TrayMenuTarget(new AvaloniaNativeTrayMenu(icon)), ViewModel, this, NullLogger.Instance));
            cameras.Windows.OpenChanged += (_, e) => ViewModel.SetCameraWindowOpen(e.PrinterId, e.IsOpen);
            ViewModel.SetPrinters(printers);
            Dispatcher.UIThread.RunJobs();
        }

        public CameraHarness Cameras { get; }

        public NativeMenu Menu { get; }

        public TrayMenuViewModel ViewModel { get; }

        public static Wiring Create(UiTestScope scope, CameraHarness cameras, params PrinterDefinition[] printers) => new(scope, cameras, TrayMenuOptions.Default, printers);

        /// <summary>The menu of the app (T-102: "Show camera" for an open window, macOS and Windows).</summary>
        public static Wiring CreateWithCameraToFront(UiTestScope scope, CameraHarness cameras, params PrinterDefinition[] printers) =>
            new(scope, cameras, TrayMenuOptions.Default with { CameraToFront = TrayMenuOptions.ForCurrentPlatform.CameraToFront }, printers);

        /// <summary>The item at <paramref name="index"/> in the submenu of <paramref name="printerIndex"/>.</summary>
        public NativeMenuItem SubmenuItem(int printerIndex, int index) =>
            Assert.IsType<NativeMenuItem>(Assert.IsType<NativeMenuItem>(Menu.Items[printerIndex]).Menu!.Items[index]);

        /// <summary>The camera item in the submenu of <paramref name="printerIndex"/> (also of a single printer, T-042).</summary>
        public NativeMenuItem CameraItem(int printerIndex = 0) =>
            Assert.IsType<NativeMenuItem>(Assert.IsType<NativeMenuItem>(Menu.Items[printerIndex]).Menu!.Items[^1]);

        public static void Click(NativeMenuItem item)
        {
            ((INativeMenuItemExporterEventsImplBridge)item).RaiseClicked();
            Dispatcher.UIThread.RunJobs();
        }

        public void ShowAbout()
        {
        }

        public void ShowDiagnostics(string? printerId)
        {
        }

        public void ShowSettings()
        {
        }

        public void AddPrinter()
        {
        }

        public void ToggleCamera(string printerId) => Cameras.Windows.Toggle(printerId);

        public void BringCameraToFront(string printerId) => Cameras.Windows.BringToFront(printerId);

        public void InstallUpdate()
        {
        }

        public void Quit()
        {
        }
    }

    [Fact]
    public Task CameraItemIsAPlainItemThatOpensAndClosesTheWindowAndSwitchesItsText() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness cameras = new(screens: Tests.Cameras.FakeScreenCatalog.ThreeMonitors());
            Wiring tray = Wiring.Create(scope, cameras, CameraHarness.CameraPrinter("a", "Alpha"));
            NativeMenuItem camera = tray.CameraItem();

            Assert.Equal(ShowCamera, camera.Header);
            Assert.Equal(MenuItemToggleType.None, camera.ToggleType);
            Assert.False(camera.IsChecked);

            Wiring.Click(camera);
            Assert.True(cameras.Windows.OpenWindows.ContainsKey("a"));
            // The same native item, only its text changed; never a check mark.
            Assert.Same(camera, tray.CameraItem());
            Assert.Equal(HideCamera, camera.Header);
            Assert.Equal(MenuItemToggleType.None, camera.ToggleType);
            Assert.False(camera.IsChecked);

            Wiring.Click(camera);
            Assert.Empty(cameras.Windows.OpenWindows);
            Assert.Same(camera, tray.CameraItem());
            Assert.Equal(ShowCamera, camera.Header);
            Assert.Equal(MenuItemToggleType.None, camera.ToggleType);
            Assert.False(camera.IsChecked);
        });

    [Fact]
    public Task ClosingFromTheMenuStopsTheStreamAndRemembersThePlacement() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness cameras = new(screens: Tests.Cameras.FakeScreenCatalog.ThreeMonitors());
            Wiring tray = Wiring.Create(scope, cameras, CameraHarness.CameraPrinter("a", "Alpha"));
            Wiring.Click(tray.CameraItem());
            CameraWindow window = cameras.Windows.OpenWindows["a"];
            window.Settle();
            window.Position = new PixelPoint(321, 234);
            window.Settle();

            Wiring.Click(tray.CameraItem());

            CameraViewModel viewModel = Assert.Single(cameras.Created);
            Assert.False(viewModel.IsStreaming);
            CameraWindowState state = cameras.States.Get("a");
            Assert.Equal(CameraWindowMode.Normal, state.Mode);
            Assert.Equal(321, state.Normal!.Value.X);
            Assert.Equal(234, state.Normal!.Value.Y);
        });

    [Fact]
    public Task TextFollowsTheWindowClosedByItsCloseButton() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness cameras = new(screens: Tests.Cameras.FakeScreenCatalog.ThreeMonitors());
            Wiring tray = Wiring.Create(scope, cameras, CameraHarness.CameraPrinter("a", "Alpha"));
            Wiring.Click(tray.CameraItem());

            cameras.Windows.OpenWindows["a"].Close();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(ShowCamera, tray.CameraItem().Header);
        });

    [Fact]
    public Task TextFollowsTheWindowClosedBySettingsThatDisableTheCamera() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness cameras = new(screens: Tests.Cameras.FakeScreenCatalog.ThreeMonitors());
            Wiring tray = Wiring.Create(scope, cameras, CameraHarness.CameraPrinter("a", "Alpha"), CameraHarness.CameraPrinter("b", "Beta"));
            Wiring.Click(tray.CameraItem(0));
            Wiring.Click(tray.CameraItem(1));
            Assert.Equal(HideCamera, tray.CameraItem(0).Header);
            Assert.Equal(HideCamera, tray.CameraItem(1).Header);

            // The settings disable the camera of "a" and remove nothing else: its window closes, "b" stays open with "Hide camera".
            AppSettings settings = new() { Printers = [TestPrinters.Definition("a", "Alpha"), CameraHarness.CameraPrinter("b", "Beta")] };
            ((IMonitoringObserver)cameras.Printers).ApplySettings(settings);
            tray.ViewModel.SetPrinters(settings.Printers);
            Dispatcher.UIThread.RunJobs();

            Assert.False(cameras.Windows.OpenWindows.ContainsKey("a"));
            Assert.DoesNotContain(Assert.IsType<NativeMenuItem>(tray.Menu.Items[0]).Menu!.Items, item => item is NativeMenuItem { Header: ShowCamera or HideCamera });
            Assert.Equal(HideCamera, tray.CameraItem(1).Header);

            // Removing "b": its window closes too; when it is added again its item offers "Show camera".
            ((IMonitoringObserver)cameras.Printers).ApplySettings(new AppSettings { Printers = [TestPrinters.Definition("a", "Alpha")] });
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(cameras.Windows.OpenWindows);
            tray.ViewModel.SetPrinters([CameraHarness.CameraPrinter("b", "Beta")]);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ShowCamera, tray.CameraItem().Header);
        });

    [Fact]
    public Task SeveralPrintersKeepTheirOwnText() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness cameras = new(screens: Tests.Cameras.FakeScreenCatalog.ThreeMonitors());
            Wiring tray = Wiring.Create(scope, cameras, CameraHarness.CameraPrinter("a", "Alpha"), CameraHarness.CameraPrinter("b", "Beta"));

            Wiring.Click(tray.CameraItem(1));

            Assert.Equal(ShowCamera, tray.CameraItem(0).Header);
            Assert.Equal(HideCamera, tray.CameraItem(1).Header);
            Assert.Equal(["b"], cameras.Windows.OpenWindows.Keys);
        });

    [Fact]
    public Task QuittingSwitchesTheItemBackToShowCamera() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness cameras = new(screens: Tests.Cameras.FakeScreenCatalog.ThreeMonitors());
            Wiring tray = Wiring.Create(scope, cameras, CameraHarness.CameraPrinter("a", "Alpha"));
            Wiring.Click(tray.CameraItem());

            cameras.Windows.Shutdown();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(ShowCamera, tray.CameraItem().Header);
            // After quitting started the item opens nothing.
            Wiring.Click(tray.CameraItem());
            Assert.Empty(cameras.Windows.OpenWindows);
        });

    [Fact]
    public Task ShowCameraBringsTheOpenWindowToTheFrontAndRestoresItsFullScreen() =>
        session.RunAsync(static async scope =>
        {
            List<string> activations = [];
            await using CameraHarness cameras = new(screens: Tests.Cameras.FakeScreenCatalog.ThreeMonitors(), bringAppToFront: () => activations.Add("app"));
            Wiring tray = Wiring.CreateWithCameraToFront(scope, cameras, CameraHarness.CameraPrinter("a", "Alpha"));
            Assert.Equal(2, Assert.IsType<NativeMenuItem>(tray.Menu.Items[0]).Menu!.Items.Count);

            Wiring.Click(tray.CameraItem());
            CameraWindow window = scope.Track(cameras.Windows.OpenWindows["a"]);
            window.Settle();
            // "Show camera" right above "Hide camera" (shown by the native menu without the access key marker).
            Assert.Equal("_Show camera", tray.SubmenuItem(0, 1).Header);
            Assert.Equal(HideCamera, tray.CameraItem().Header);

            ((CameraViewModel)window.DataContext!).ToggleFullScreenCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            window.WindowState = WindowState.Minimized;
            Dispatcher.UIThread.RunJobs();
            activations.Clear();

            Wiring.Click(tray.SubmenuItem(0, 1));
            window.Settle();

            Assert.Equal(WindowState.FullScreen, window.WindowState);
            Assert.True(window.IsActive);
            Assert.Equal(["app"], activations);
            // Still the same single window, and the item stays while it is open.
            Assert.Same(window, Assert.Single(cameras.Windows.OpenWindows).Value);
            Assert.Equal("_Show camera", tray.SubmenuItem(0, 1).Header);

            Wiring.Click(tray.CameraItem());
            Assert.Empty(cameras.Windows.OpenWindows);
            Assert.Equal(2, Assert.IsType<NativeMenuItem>(tray.Menu.Items[0]).Menu!.Items.Count);
            Assert.Equal(ShowCamera, tray.CameraItem().Header);
        });

    [Fact]
    public Task ShowCameraOfAWindowClosedMeanwhileOpensIt() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness cameras = new(screens: Tests.Cameras.FakeScreenCatalog.ThreeMonitors());
            Wiring tray = Wiring.CreateWithCameraToFront(scope, cameras, CameraHarness.CameraPrinter("a", "Alpha"));
            Wiring.Click(tray.CameraItem());
            NativeMenuItem front = tray.SubmenuItem(0, 1);
            CameraWindow window = cameras.Windows.OpenWindows["a"];
            window.Close();
            Dispatcher.UIThread.RunJobs();

            // A click from a menu that was open while the window closed: the item is detached and does nothing.
            Wiring.Click(front);
            Assert.Empty(cameras.Windows.OpenWindows);

            // The command itself (a stale entry) opens the window like "Show camera".
            Assert.True(cameras.Windows.BringToFront("a"));
            scope.Track(cameras.Windows.OpenWindows["a"]);
        });

    [Fact]
    public Task ShowCameraActivatesAMiniPreviewOnTop() =>
        session.RunAsync(static async scope =>
        {
            // A window on top has no Dock icon or taskbar button (T-102): the menu is the way back to its focus.
            await using CameraHarness cameras = new(screens: Tests.Cameras.FakeScreenCatalog.ThreeMonitors(), window: CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini });
            Wiring tray = Wiring.CreateWithCameraToFront(scope, cameras, CameraHarness.CameraPrinter("a", "Alpha", CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini }));
            Wiring.Click(tray.CameraItem());
            CameraWindow window = scope.Track(cameras.Windows.OpenWindows["a"]);
            window.Settle();
            Assert.True(window.Topmost);
            Assert.False(window.ShowInTaskbar);
            // Headless windows do not take the activation from each other: the activation itself is counted.
            int activations = 0;
            window.Activated += (_, _) => activations++;

            Wiring.Click(tray.SubmenuItem(0, 1));
            window.Settle();

            Assert.True(activations > 0);
            Assert.True(window.IsActive);
            Assert.True(((CameraViewModel)window.DataContext!).IsMini);
        });
}
