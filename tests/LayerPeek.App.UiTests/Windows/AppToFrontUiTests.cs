using Avalonia.Threading;

using LayerPeek.App.Tests.Tray.Menu;
using LayerPeek.App.UiTests.Cameras;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;

namespace LayerPeek.App.UiTests.Windows;

/// <summary>
/// macOS (T-019): LayerPeek is an agent app without a Dock icon, so a window opened from the menu bar comes to the front
/// only when the app is activated first. Every window the menu activates (settings, diagnostics, About and, user decision
/// 2026-10-29, the camera windows) brings the app to the front right before the window itself; a camera window Apply shows
/// without the focus does not. A recording action stands in for <c>MacAppActivation.Activate</c>.
/// </summary>
public sealed class AppToFrontUiTests(HeadlessSession session)
{
    [Fact]
    public Task CameraWindowFromTheMenuBringsTheAppToTheFrontBeforeItIsActivated() =>
        session.RunAsync(static async scope =>
        {
            List<string> calls = [];
            await using CameraHarness harness = new(bringAppToFront: () => calls.Add("app"));

            CameraWindow window = scope.Track(harness.Open());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["app"], calls);

            // Already open: the menu activates it again, with the app.
            window.Activated += (_, _) => calls.Add("window");
            Assert.True(harness.Windows.Show("a"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("app", calls[1]);
            Assert.All(calls.Skip(2), static call => Assert.Equal("window", call));
        });

    [Fact]
    public Task TheTrayMenuToggleBringsTheAppToTheFront() =>
        session.RunAsync(static async scope =>
        {
            int calls = 0;
            await using CameraHarness harness = new(bringAppToFront: () => calls++);

            Assert.True(harness.Windows.Toggle("b"));
            scope.Track(harness.Windows.OpenWindows["b"]);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, calls);
        });

    /// <summary>T-040: Apply shows a closed camera window without taking the focus from the settings: no activation either.</summary>
    [Fact]
    public Task CameraWindowShownByApplyDoesNotActivateTheApp() =>
        session.RunAsync(static async scope =>
        {
            int calls = 0;
            await using CameraHarness harness = new(bringAppToFront: () => calls++);

            Assert.True(harness.Windows.ApplySettings(CameraHarness.CameraPrinter("a", "Alpha")));
            scope.Track(harness.Windows.OpenWindows["a"]);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0, calls);
        });

    [Fact]
    public Task CameraWindowsWithoutTheActionStillOpen() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness harness = new();

            scope.Track(harness.Open());
            Dispatcher.UIThread.RunJobs();

            Assert.Single(harness.Windows.OpenWindows);
        });

    [Fact]
    public Task AboutAndDiagnosticsBringTheAppToTheFrontBeforeTheyAreActivated() =>
        session.RunAsync(static scope =>
        {
            List<string> calls = [];
            AuxiliaryWindowFactory factory = new(null, bringAppToFront: () => calls.Add("app"));

            AuxiliaryWindowSlot about = new();
            about.ShowOrActivate(() => (factory.About(new AboutViewModel(new FakeUrlLauncher(), typeof(AboutViewModel).Assembly)), null));
            AuxiliaryWindowSlot diagnostics = new();
            diagnostics.ShowOrActivate(() => (factory.Diagnostics(null), null));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(["app", "app"], calls);
            about.Shutdown();
            diagnostics.Shutdown();
            return Task.CompletedTask;
        });
}
