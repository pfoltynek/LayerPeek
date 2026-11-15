using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Cameras;
using LayerPeek.App.Tests.Cameras;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tests.Tray.Menu;
using LayerPeek.App.Tray;
using LayerPeek.App.Tray.Menu;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.UiTests.Cameras;

/// <summary>
/// T-151 on the real camera windows (<see cref="CameraWindows"/>) and the real tray menu: a switched-off printer's window
/// closes like its close button (placement remembered, stream stopped) and opens again where it was without taking the
/// focus; the tray menu follows; the layout mode and the context menu make the close wait; a window the user closed stays
/// closed. The real timing, focus and Dock behaviour of the OS are checked by hand (T-125 Windows, T-127 macOS).
/// </summary>
public sealed class CameraWindowAutoCloseUiTests(HeadlessSession session)
{
    // The session runs in English ("system" language on en-US).
    private const string ShowCamera = "Show _camera";
    private const string HideCamera = "Hide _camera";

    private static readonly TimeSpan CloseAfter = CameraWindowAutoVisibilityOptions.Default.CloseAfterOffline;
    private static readonly TimeSpan OpenAfter = CameraWindowAutoVisibilityOptions.Default.OpenAfterOnline;

    [Fact]
    public Task OfflineClosesTheWindowAndOnlineOpensItAgainWhereItWasWithoutTheFocus() =>
        session.RunAsync(static async scope =>
        {
            List<string> activations = [];
            await using CameraHarness cameras = new(screens: FakeScreenCatalog.ThreeMonitors(), bringAppToFront: () => activations.Add("app"), activator: new RecordingActivator(activations));
            Wiring tray = Wiring.Create(scope, cameras);
            Online(tray);

            tray.Click(tray.CameraItem());
            CameraWindow window = cameras.Windows.OpenWindows["a"];
            window.Settle();
            window.Position = new PixelPoint(321, 234);
            window.Settle();
            Assert.Equal(HideCamera, tray.CameraItem().Header);

            tray.Raise(PrinterState.Offline);
            tray.Advance(CloseAfter - TimeSpan.FromSeconds(1));
            Assert.True(cameras.Windows.OpenWindows.ContainsKey("a"));
            tray.Advance(TimeSpan.FromSeconds(1));

            // Closed like the close button: the stream stopped and the placement is remembered, the tray offers the camera.
            Assert.Empty(cameras.Windows.OpenWindows);
            Assert.False(Assert.Single(cameras.Created).IsStreaming);
            Assert.Equal(321, cameras.States.Get("a").Normal!.Value.X);
            Assert.Equal(234, cameras.States.Get("a").Normal!.Value.Y);
            Assert.Equal(["a"], cameras.States.GetAutoClosedPrinters());
            Assert.Equal(ShowCamera, tray.CameraItem().Header);
            Assert.Equal([("a", false, true)], tray.Changes.Where(static c => !c.IsOpen));

            activations.Clear();
            tray.Raise(PrinterState.Idle);
            tray.Advance(OpenAfter);

            CameraWindow reopened = scope.Track(cameras.Windows.OpenWindows["a"]);
            reopened.Settle();
            Assert.NotSame(window, reopened);
            Assert.Equal(new PixelPoint(321, 234), reopened.Position);
            // Silently (user decision 2026-11-03 #1): neither the window nor the app is activated.
            Assert.False(reopened.ShowActivated);
            Assert.False(reopened.IsActive);
            Assert.Empty(activations);
            Assert.True(((CameraViewModel)reopened.DataContext!).IsStreaming);
            Assert.Empty(cameras.States.GetAutoClosedPrinters());
            Assert.Equal(HideCamera, tray.CameraItem().Header);
        });

    [Fact]
    public Task AWindowClosedFromTheTrayMenuNeverOpensByItself() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness cameras = new(screens: FakeScreenCatalog.ThreeMonitors());
            Wiring tray = Wiring.Create(scope, cameras);
            Online(tray);
            tray.Click(tray.CameraItem());
            tray.Raise(PrinterState.Offline);
            tray.Advance(CloseAfter);
            Assert.Empty(cameras.Windows.OpenWindows);

            // Opened and closed again by the user while the printer is off.
            tray.Click(tray.CameraItem());
            Assert.Equal(HideCamera, tray.CameraItem().Header);
            tray.Click(tray.CameraItem());
            tray.Raise(PrinterState.Idle);
            tray.Advance(OpenAfter * 3);

            Assert.Empty(cameras.Windows.OpenWindows);
            Assert.Empty(cameras.States.GetAutoClosedPrinters());
            Assert.Equal(ShowCamera, tray.CameraItem().Header);
        });

    [Fact]
    public Task TheOpenContextMenuMakesTheCloseWaitUntilItCloses() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness cameras = new(screens: FakeScreenCatalog.ThreeMonitors());
            Wiring tray = Wiring.Create(scope, cameras);
            Online(tray);
            tray.Click(tray.CameraItem());
            CameraWindow window = cameras.Windows.OpenWindows["a"];
            window.Width = 960;
            window.Height = 540;
            window.Settle();

            window.MouseDown(new Point(100, 100), MouseButton.Right);
            window.MouseUp(new Point(100, 100), MouseButton.Right);
            Settle(window);
            Assert.True(window.ContextMenu!.IsOpen);
            Assert.True(cameras.Windows.IsCloseDeferred("a"));

            tray.Raise(PrinterState.Offline);
            tray.Advance(CloseAfter * 2);
            Assert.True(cameras.Windows.OpenWindows.ContainsKey("a"));

            window.ContextMenu.Close();
            Settle(window);

            Assert.Empty(cameras.Windows.OpenWindows);
            Assert.Equal(["a"], cameras.States.GetAutoClosedPrinters());
            Assert.Equal(ShowCamera, tray.CameraItem().Header);
        });

    [Fact]
    public Task TheLayoutModeMakesTheCloseWaitAndItsEndClosesTheWindow() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness cameras = new(screens: FakeScreenCatalog.ThreeMonitors());
            Wiring tray = Wiring.Create(scope, cameras);
            List<(string PrinterId, bool Deferred)> deferrals = [];
            cameras.Windows.CloseDeferralChanged += (_, e) => deferrals.Add((e.PrinterId, e.Deferred));
            Online(tray);
            tray.Click(tray.CameraItem());
            CameraWindow window = cameras.Windows.OpenWindows["a"];
            window.Width = 960;
            window.Height = 540;
            window.Settle();
            CameraViewModel viewModel = (CameraViewModel)window.DataContext!;

            viewModel.EditLayoutCommand.Execute(null);
            Settle(window);
            Assert.True(viewModel.IsLayoutModeActive);
            Assert.True(cameras.Windows.IsCloseDeferred("a"));

            tray.Raise(PrinterState.Offline);
            tray.Advance(CloseAfter * 2);
            Assert.True(cameras.Windows.OpenWindows.ContainsKey("a"));

            viewModel.LayoutMode.Cancel();
            Settle(window);

            Assert.Equal([("a", true), ("a", false)], deferrals.Distinct());
            Assert.Empty(cameras.Windows.OpenWindows);
            Assert.Equal(ShowCamera, tray.CameraItem().Header);
        });

    [Fact]
    public Task WithTheOptionOffTheWindowStaysOpen() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness cameras = new(screens: FakeScreenCatalog.ThreeMonitors());
            Wiring tray = Wiring.Create(scope, cameras, autoClose: false);
            Online(tray);
            tray.Click(tray.CameraItem());

            tray.Raise(PrinterState.Offline);
            tray.Advance(CloseAfter * 2);

            Assert.True(cameras.Windows.OpenWindows.ContainsKey("a"));
            Assert.Equal(HideCamera, tray.CameraItem().Header);
        });

    [Fact]
    public Task QuittingClosesTheWindowsWithoutForgettingTheFlags() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness cameras = new(screens: FakeScreenCatalog.ThreeMonitors());
            Wiring tray = Wiring.Create(scope, cameras);
            Online(tray);
            Online(tray, "b");
            tray.Click(tray.CameraItem(0));
            tray.Click(tray.CameraItem(1));
            tray.Raise(PrinterState.Offline);
            tray.Advance(CloseAfter);
            Assert.Equal(["a"], cameras.States.GetAutoClosedPrinters());

            // As App.StopHostAsync: the observer lets the windows go first.
            tray.Observer.Detach();
            cameras.Windows.Shutdown();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(["a"], cameras.States.GetAutoClosedPrinters());
        });

    private static void Online(Wiring tray, string printerId = "a") => tray.Raise(PrinterState.Idle, printerId);

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.Settle();
    }

    private sealed class RecordingActivator(List<string> activations) : IWindowActivator
    {
        public void Activate(Window window)
        {
            activations.Add("window");
            window.Activate();
        }
    }

    /// <summary>
    /// The tray menu of printers "a" and "b" on the app's tray icon and the observer of T-151 over the camera windows of
    /// <see cref="CameraHarness"/>, wired like the app (<c>App.StartHost</c>, <c>TrayHost.AttachCameraWindows</c>), with a
    /// clock of its own.
    /// </summary>
    private sealed class Wiring : ITrayAppCommands
    {
        private Wiring(UiTestScope scope, CameraHarness cameras, bool autoClose)
        {
            Cameras = cameras;
            TrayIcon icon = Assert.Single(TrayIcon.GetIcons(Application.Current!)!);
            Menu = icon.Menu!;
            ViewModel = scope.Defer(new TrayMenuViewModel(new FakePrinterMonitor(), new AvaloniaUiDispatcher(), TestWebOpener.Create(new FakeUrlLauncher()), TrayMenuOptions.Default));
            scope.Defer(new TrayMenuPresenter(new TrayMenuTarget(new AvaloniaNativeTrayMenu(icon)), ViewModel, this, NullLogger.Instance));
            cameras.Windows.OpenChanged += (_, e) =>
            {
                Changes.Add((e.PrinterId, e.IsOpen, e.Automatic));
                ViewModel.SetCameraWindowOpen(e.PrinterId, e.IsOpen);
            };
            PrinterDefinition[] printers = [CameraHarness.CameraPrinter("a", "Alpha"), CameraHarness.CameraPrinter("b", "Beta")];
            ViewModel.SetPrinters(printers);
            Observer = scope.Defer(new CameraWindowAutoVisibilityObserver(cameras.Monitor, cameras.States, new AvaloniaUiDispatcher(), Time, NullLogger<CameraWindowAutoVisibilityObserver>.Instance));
            Observer.ApplySettings(new AppSettings { Printers = printers, CameraWindowAutoClose = autoClose });
            Observer.Attach(cameras.Windows);
            Dispatcher.UIThread.RunJobs();
            Assert.True(Observer.IsRunning);
        }

        public CameraHarness Cameras { get; }

        public NativeMenu Menu { get; }

        public TrayMenuViewModel ViewModel { get; }

        public CameraWindowAutoVisibilityObserver Observer { get; }

        public FakeTimeProvider Time { get; } = new(TestPrinters.Now);

        public List<(string PrinterId, bool IsOpen, bool Automatic)> Changes { get; } = [];

        public static Wiring Create(UiTestScope scope, CameraHarness cameras, bool autoClose = true) => new(scope, cameras, autoClose);

        /// <summary>The camera item in the submenu of <paramref name="printerIndex"/>.</summary>
        public NativeMenuItem CameraItem(int printerIndex = 0) =>
            Assert.IsType<NativeMenuItem>(Assert.IsType<NativeMenuItem>(Menu.Items[printerIndex]).Menu!.Items[^1]);

        public void Click(NativeMenuItem item)
        {
            ((INativeMenuItemExporterEventsImplBridge)item).RaiseClicked();
            Dispatcher.UIThread.RunJobs();
            foreach (CameraWindow window in Cameras.Windows.OpenWindows.Values)
            {
                window.Settle();
            }
        }

        public void Raise(PrinterState state, string printerId = "a")
        {
            Cameras.Monitor.Raise(TestPrinters.Snapshot(state, printerId: printerId) with { Timestamp = Time.GetUtcNow() });
            Dispatcher.UIThread.RunJobs();
        }

        public void Advance(TimeSpan by)
        {
            Time.Advance(by);
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

        public void ShowUpdateSettings()
        {
        }

        public void OpenManual()
        {
        }

        public void Quit()
        {
        }
    }
}
