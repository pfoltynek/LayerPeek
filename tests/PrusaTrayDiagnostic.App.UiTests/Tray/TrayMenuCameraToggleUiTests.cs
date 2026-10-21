using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

using Microsoft.Extensions.Logging.Abstractions;

using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.App.Tests.Tray.Menu;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.App.Tray.Menu;
using PrusaTrayDiagnostic.App.UiTests.Cameras;
using PrusaTrayDiagnostic.App.UiTests.Infrastructure;
using PrusaTrayDiagnostic.App.Windows;
using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.App.UiTests.Tray;

/// <summary>
/// The "Camera" check item of the tray menu (T-040) on the real Avalonia <see cref="NativeMenu"/>, wired to the real camera
/// windows of <see cref="CameraHarness"/> like the app does it (<c>App.ToggleCamera</c>, <c>CameraWindows.OpenChanged</c>
/// → <see cref="TrayMenuViewModel.SetCameraWindowOpen"/>). How the OS draws the check mark stays a manual check.
/// </summary>
public sealed class TrayMenuCameraToggleUiTests(HeadlessSession session)
{
    /// <summary>The tray menu of printers "a" and "b" (both with cameras, like the harness) on the app's tray icon.</summary>
    private sealed class Wiring : ITrayAppCommands
    {
        private Wiring(UiTestScope scope, CameraHarness cameras, params PrinterDefinition[] printers)
        {
            Cameras = cameras;
            TrayIcon icon = Assert.Single(TrayIcon.GetIcons(Application.Current!)!);
            Menu = icon.Menu!;
            ViewModel = scope.Defer(new TrayMenuViewModel(new FakePrinterMonitor(), new AvaloniaUiDispatcher(), TestWebOpener.Create(new FakeUrlLauncher()), TrayMenuOptions.Default));
            scope.Defer(new TrayMenuPresenter(new TrayMenuTarget(new AvaloniaNativeTrayMenu(icon)), ViewModel, this, NullLogger.Instance));
            cameras.Windows.OpenChanged += (_, e) => ViewModel.SetCameraWindowOpen(e.PrinterId, e.IsOpen);
            ViewModel.SetPrinters(printers);
            Dispatcher.UIThread.RunJobs();
        }

        public CameraHarness Cameras { get; }

        public NativeMenu Menu { get; }

        public TrayMenuViewModel ViewModel { get; }

        public static Wiring Create(UiTestScope scope, CameraHarness cameras, params PrinterDefinition[] printers) => new(scope, cameras, printers);

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

        public void Quit()
        {
        }
    }

    [Fact]
    public Task CameraItemIsACheckItemThatOpensAndClosesTheWindow() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness cameras = new(screens: Tests.Cameras.FakeScreenCatalog.ThreeMonitors());
            Wiring tray = Wiring.Create(scope, cameras, CameraHarness.CameraPrinter("a", "Alpha"));
            NativeMenuItem camera = tray.CameraItem();

            Assert.Equal("_Camera", camera.Header);
            Assert.Equal(MenuItemToggleType.CheckBox, camera.ToggleType);
            Assert.False(camera.IsChecked);

            Wiring.Click(camera);
            Assert.True(cameras.Windows.OpenWindows.ContainsKey("a"));
            Assert.True(tray.CameraItem().IsChecked);

            Wiring.Click(tray.CameraItem());
            Assert.Empty(cameras.Windows.OpenWindows);
            Assert.False(tray.CameraItem().IsChecked);
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
    public Task CheckMarkFollowsTheWindowClosedByItsCloseButton() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness cameras = new(screens: Tests.Cameras.FakeScreenCatalog.ThreeMonitors());
            Wiring tray = Wiring.Create(scope, cameras, CameraHarness.CameraPrinter("a", "Alpha"));
            Wiring.Click(tray.CameraItem());

            cameras.Windows.OpenWindows["a"].Close();
            Dispatcher.UIThread.RunJobs();

            Assert.False(tray.CameraItem().IsChecked);
        });

    [Fact]
    public Task CheckMarkFollowsTheWindowClosedBySettingsThatDisableTheCamera() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness cameras = new(screens: Tests.Cameras.FakeScreenCatalog.ThreeMonitors());
            Wiring tray = Wiring.Create(scope, cameras, CameraHarness.CameraPrinter("a", "Alpha"), CameraHarness.CameraPrinter("b", "Beta"));
            Wiring.Click(tray.CameraItem(0));
            Wiring.Click(tray.CameraItem(1));
            Assert.True(tray.CameraItem(0).IsChecked);
            Assert.True(tray.CameraItem(1).IsChecked);

            // The settings disable the camera of "a" and remove nothing else: its window closes, "b" stays open and checked.
            AppSettings settings = new() { Printers = [TestPrinters.Definition("a", "Alpha"), CameraHarness.CameraPrinter("b", "Beta")] };
            ((IMonitoringObserver)cameras.Printers).ApplySettings(settings);
            tray.ViewModel.SetPrinters(settings.Printers);
            Dispatcher.UIThread.RunJobs();

            Assert.False(cameras.Windows.OpenWindows.ContainsKey("a"));
            Assert.DoesNotContain(Assert.IsType<NativeMenuItem>(tray.Menu.Items[0]).Menu!.Items, item => item is NativeMenuItem { Header: "_Camera" });
            Assert.True(tray.CameraItem(1).IsChecked);

            // Removing "b": its window closes too; when it is added again its item starts unchecked.
            ((IMonitoringObserver)cameras.Printers).ApplySettings(new AppSettings { Printers = [TestPrinters.Definition("a", "Alpha")] });
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(cameras.Windows.OpenWindows);
            tray.ViewModel.SetPrinters([CameraHarness.CameraPrinter("b", "Beta")]);
            Dispatcher.UIThread.RunJobs();
            Assert.False(tray.CameraItem().IsChecked);
        });

    [Fact]
    public Task SeveralPrintersKeepTheirOwnCheckMark() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness cameras = new(screens: Tests.Cameras.FakeScreenCatalog.ThreeMonitors());
            Wiring tray = Wiring.Create(scope, cameras, CameraHarness.CameraPrinter("a", "Alpha"), CameraHarness.CameraPrinter("b", "Beta"));

            Wiring.Click(tray.CameraItem(1));

            Assert.False(tray.CameraItem(0).IsChecked);
            Assert.True(tray.CameraItem(1).IsChecked);
            Assert.Equal(["b"], cameras.Windows.OpenWindows.Keys);
        });

    [Fact]
    public Task QuittingUnchecksEverything() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness cameras = new(screens: Tests.Cameras.FakeScreenCatalog.ThreeMonitors());
            Wiring tray = Wiring.Create(scope, cameras, CameraHarness.CameraPrinter("a", "Alpha"));
            Wiring.Click(tray.CameraItem());

            cameras.Windows.Shutdown();
            Dispatcher.UIThread.RunJobs();

            Assert.False(tray.CameraItem().IsChecked);
            // After quitting started the item opens nothing.
            Wiring.Click(tray.CameraItem());
            Assert.Empty(cameras.Windows.OpenWindows);
        });
}
