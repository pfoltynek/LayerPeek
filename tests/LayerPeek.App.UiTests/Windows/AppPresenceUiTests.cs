using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

using LayerPeek.App.Cameras;
using LayerPeek.App.Platform;
using LayerPeek.App.Tests.Cameras;
using LayerPeek.App.Tests.Platform;
using LayerPeek.App.UiTests.Cameras;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;
using LayerPeek.Core.Cameras;

namespace LayerPeek.App.UiTests.Windows;

/// <summary>
/// The Dock icon of macOS (T-102, user decision 2026-10-29) on the real windows through <see cref="AuxiliaryWindowTracker"/>:
/// every open window that is not always on top counts, also across mode switches of a camera window; a fake platform
/// presence records what AppKit would be told. The Dock icon itself, ⌘Tab and the Dock menu are a manual check on a Mac.
/// </summary>
public sealed class AppPresenceUiTests(HeadlessSession session)
{
    private static CameraViewModel ViewModel(CameraWindow window) => (CameraViewModel)window.DataContext!;

    private static void Run(CameraWindow window, Action action)
    {
        action();
        Dispatcher.UIThread.RunJobs();
        window.Settle();
    }

    [Fact]
    public Task CameraWindowShowsTheAppBeforeItIsActivatedAndFollowsEveryModeSwitch() =>
        session.RunAsync(static async scope =>
        {
            RecordingAppPresence platform = new();
            AppPresence presence = new(platform);
            AuxiliaryWindowTracker tracker = new(NoWindowShell.Instance, presence: presence);
            // The activation of the app goes to the same list: the Dock icon first, then the app comes to the front.
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), track: tracker.Track, bringAppToFront: () => platform.Calls.Add("activate"));
            CameraWindow window = scope.Track(harness.Open());
            window.Settle();
            CameraViewModel viewModel = ViewModel(window);
            Assert.Equal(["show", "activate"], platform.Calls);
            platform.Calls.Clear();

            // Defaults: the mini preview is on top (no Dock icon), the normal window and full screen are not.
            Run(window, () => viewModel.ShowMiniCommand.Execute(null));
            Assert.False(presence.IsVisible);
            Run(window, () => viewModel.ToggleFullScreenCommand.Execute(null));
            Assert.True(presence.IsVisible);
            Run(window, () => window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null));
            Assert.True(viewModel.IsMini);
            Run(window, () => viewModel.ShowNormalCommand.Execute(null));
            Run(window, () => viewModel.ToggleFullScreenCommand.Execute(null));
            Run(window, () => viewModel.ToggleFullScreenCommand.Execute(null));
            Assert.True(viewModel.IsNormal);
            Run(window, window.Close);

            Assert.Equal(["hide keep-active", "show", "hide keep-active", "show", "hide"], platform.Calls);
            Assert.Equal(0, presence.Count);
        });

    [Theory]
    [InlineData(CameraWindowStartMode.Mini, false, false, true)]
    [InlineData(CameraWindowStartMode.Mini, false, true, false)]
    [InlineData(CameraWindowStartMode.Normal, true, true, false)]
    [InlineData(CameraWindowStartMode.Normal, false, true, true)]
    [InlineData(CameraWindowStartMode.FullScreen, true, true, true)]
    public Task OnlyAWindowThatIsNotOnTopCounts(CameraWindowStartMode startMode, bool normal, bool mini, bool counted) =>
        session.RunAsync(async scope =>
        {
            RecordingAppPresence platform = new();
            AppPresence presence = new(platform);
            AuxiliaryWindowTracker tracker = new(NoWindowShell.Instance, presence: presence);
            await using CameraHarness harness = new(
                screens: FakeScreenCatalog.ThreeMonitors(),
                window: CameraWindowDefaults.Default with { StartMode = startMode, TopmostNormal = normal, TopmostMini = mini },
                track: tracker.Track);

            CameraWindow window = scope.Track(harness.Open());
            window.Settle();

            Assert.Equal(counted, presence.IsVisible);
            Assert.Equal(counted ? ["show"] : [], platform.Calls);
            Run(window, window.Close);
            Assert.Equal(counted ? ["show", "hide"] : [], platform.Calls);
        });

    [Fact]
    public Task SavedTopmostOptionOfTheOpenWindowIsCountedAgain() =>
        session.RunAsync(static async scope =>
        {
            RecordingAppPresence platform = new();
            AppPresence presence = new(platform);
            AuxiliaryWindowTracker tracker = new(NoWindowShell.Instance, presence: presence);
            CameraWindowDefaults defaults = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini };
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: defaults, track: tracker.Track);
            CameraWindow window = scope.Track(harness.Open());
            window.Settle();
            Assert.False(presence.IsVisible);

            // Apply in the settings (T-040) with "always on top" off for the mini preview: the preview stays and counts now.
            Run(window, () => ViewModel(window).ApplyWindowSettings(defaults with { TopmostMini = false }));
            Assert.True(ViewModel(window).IsMini);
            Assert.True(presence.IsVisible);

            Run(window, () => ViewModel(window).ApplyWindowSettings(defaults));
            Assert.False(presence.IsVisible);
            Assert.Equal(["show", "hide keep-active"], platform.Calls);
        });

    [Fact]
    public Task PinOfTheOpenWindowIsCountedAtOnceInBothModes() =>
        session.RunAsync(static async scope =>
        {
            // T-117: the pin switches Window.Topmost, which the tracker follows like a saved option.
            RecordingAppPresence platform = new();
            AppPresence presence = new(platform);
            AuxiliaryWindowTracker tracker = new(NoWindowShell.Instance, presence: presence);
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal }, track: tracker.Track);
            CameraWindow window = scope.Track(harness.Open());
            window.Settle();
            Avalonia.Controls.Primitives.ToggleButton pin = window.Find<Avalonia.Controls.Primitives.ToggleButton>("CameraTopmostButton");
            Assert.True(presence.IsVisible);

            Run(window, () => Ui.Click(window, Ui.CentreOf(pin, window)));
            Assert.True(window.Topmost);
            Assert.False(presence.IsVisible);
            Run(window, () => Ui.Click(window, Ui.CentreOf(pin, window)));
            Assert.True(presence.IsVisible);

            // The mini preview (on top by default): off with the pin counts it, on again does not.
            Run(window, () => ViewModel(window).ShowMiniCommand.Execute(null));
            Assert.False(presence.IsVisible);
            Run(window, () => Ui.Click(window, Ui.CentreOf(pin, window)));
            Assert.True(presence.IsVisible);
            Run(window, () => Ui.Click(window, Ui.CentreOf(pin, window)));
            Assert.False(presence.IsVisible);

            Assert.Equal(["show", "hide keep-active", "show", "hide keep-active", "show", "hide keep-active"], platform.Calls);
        });

    [Fact]
    public Task OtherWindowsCountAndTheLastClosedHidesTheApp() =>
        session.RunAsync(static async scope =>
        {
            RecordingAppPresence platform = new();
            AppPresence presence = new(platform);
            AuxiliaryWindowTracker tracker = new(NoWindowShell.Instance, presence: presence);
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), track: tracker.Track);
            AuxiliaryWindowSlot settingsSlot = new();
            AuxiliaryWindowSlot aboutSlot = new();
            Window settings = scope.Track(new Window { Title = "Settings" });
            Window about = scope.Track(new Window { Title = "About LayerPeek" });
            settingsSlot.ShowOrActivate(() => (new AvaloniaAuxiliaryWindow(settings, tracker.Track), null));
            aboutSlot.ShowOrActivate(() => (new AvaloniaAuxiliaryWindow(about, tracker.Track), null));
            CameraWindow camera = scope.Track(harness.Open());
            camera.Settle();
            Run(camera, () => ViewModel(camera).ShowMiniCommand.Execute(null));
            Assert.Equal(2, presence.Count);

            settings.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.True(presence.IsVisible);
            about.Close();
            Dispatcher.UIThread.RunJobs();

            // The mini preview (on top) is still open: the app leaves the Dock but stays active for it.
            Assert.Equal(["show", "hide keep-active"], platform.Calls);
            Run(camera, camera.Close);
            Assert.Equal(["show", "hide keep-active"], platform.Calls);
        });

    [Fact]
    public Task WindowShownWithoutActivationCountsToo() =>
        session.RunAsync(static scope =>
        {
            // T-040: Apply opens the camera window without the focus; it is still a window in the Dock menu.
            RecordingAppPresence platform = new();
            AppPresence presence = new(platform);
            AuxiliaryWindowTracker tracker = new(NoWindowShell.Instance, presence: presence);
            AuxiliaryWindowSlot slot = new();
            Window window = scope.Track(new Window { ShowActivated = false });

            slot.ShowOrActivate(() => (new AvaloniaAuxiliaryWindow(window, tracker.Track), null), activate: false);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(["show"], platform.Calls);
            return Task.CompletedTask;
        });

    [Fact]
    public Task WindowThatFailedToShowIsNotCounted() =>
        session.RunAsync(static scope =>
        {
            RecordingAppPresence platform = new();
            AppPresence presence = new(platform);
            AuxiliaryWindowTracker tracker = new(NoWindowShell.Instance, presence: presence);
            AuxiliaryWindowSlot slot = new();
            // A closed window cannot be shown again: Show throws after the window was tracked.
            Window window = new();
            window.Close();

            Assert.ThrowsAny<Exception>(() => slot.ShowOrActivate(() => (new AvaloniaAuxiliaryWindow(window, tracker.Track), null)));

            Assert.False(presence.IsVisible);
            Assert.Equal(["show", "hide"], platform.Calls);
            return Task.CompletedTask;
        });
}
