using Avalonia.Controls;
using Avalonia.Threading;

using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.App.Diagnostics;
using PrusaTrayDiagnostic.App.Platform;
using PrusaTrayDiagnostic.App.Settings;
using PrusaTrayDiagnostic.App.Tests.Settings;
using PrusaTrayDiagnostic.App.Tests.Tray.Menu;
using PrusaTrayDiagnostic.App.UiTests.Cameras;
using PrusaTrayDiagnostic.App.UiTests.Infrastructure;
using PrusaTrayDiagnostic.App.UiTests.Settings;
using PrusaTrayDiagnostic.App.Windows;

namespace PrusaTrayDiagnostic.App.UiTests.Windows;

/// <summary>
/// The real windows through <see cref="AuxiliaryWindowTracker"/> (T-043): what a second start of the app brings to the
/// front, and what the OS shell is told (a fake shell; the headless windows have no native handle, so handles are fake
/// too).
/// </summary>
public sealed class AuxiliaryWindowTrackingUiTests(HeadlessSession session)
{
    [Fact]
    public Task MinimizedFullScreenCameraWindowComesBackInFullScreen() =>
        session.RunAsync(static async scope =>
        {
            RecordingShell shell = new();
            AuxiliaryWindowTracker tracker = new(shell, FakeHandles());
            await using CameraHarness harness = new(track: tracker.Track);
            CameraWindow window = scope.Track(harness.Open());
            Dispatcher.UIThread.RunJobs();
            ((CameraViewModel)window.DataContext!).ToggleFullScreenCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(WindowState.FullScreen, window.WindowState);

            window.WindowState = WindowState.Minimized;
            Dispatcher.UIThread.RunJobs();
            new InstanceActivation(tracker.Recent, static () => Assert.Fail("A window is open.")).Activate();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(WindowState.FullScreen, window.WindowState);
            Assert.Equal(["prepare 101", "attention 101"], shell.Calls);
        });

    [Fact]
    public Task ClosedWindowIsReleasedWithTheHandleOfItsCreationAndDropsOut() =>
        session.RunAsync(static scope =>
        {
            RecordingShell shell = new();
            // The handle a window reports changes (it is gone once the window closed): only the first one counts.
            AuxiliaryWindowTracker tracker = new(shell, FakeHandles());
            AuxiliaryWindowSlot slot = new();
            Window window = scope.Track(new Window());
            slot.ShowOrActivate(() => (new AvaloniaAuxiliaryWindow(window, tracker.Track), null));
            Assert.Equal(1, tracker.Recent.Count);

            window.Close();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(["prepare 101", "release 101"], shell.Calls);
            Assert.Equal(0, tracker.Recent.Count);
            Assert.False(slot.IsOpen);
            return Task.CompletedTask;
        });

    [Fact]
    public Task WindowThatFailsToShowIsUntracked() =>
        session.RunAsync(static scope =>
        {
            RecordingShell shell = new();
            AuxiliaryWindowTracker tracker = new(shell, FakeHandles());
            AuxiliaryWindowSlot slot = new();
            // A closed window cannot be shown again: Show throws after the window was tracked.
            Window window = new();
            window.Close();

            Assert.Throws<InvalidOperationException>(() => slot.ShowOrActivate(() => (new AvaloniaAuxiliaryWindow(window, tracker.Track), null)));

            Assert.Equal(0, tracker.Recent.Count);
            Assert.Equal(["prepare 101", "release 101"], shell.Calls);
            Assert.False(slot.IsOpen);
            return Task.CompletedTask;
        });

    [Fact]
    public Task LicencesOpenedFromAboutAreTrackedAndBringTheirOwnerBack() =>
        session.RunAsync(static scope =>
        {
            RecordingShell shell = new();
            AuxiliaryWindowTracker tracker = new(shell, FakeHandles());
            AuxiliaryWindowFactory windows = new(tracker.Track);
            AuxiliaryWindowSlot slot = new();
            slot.ShowOrActivate(() => (windows.About(new AboutViewModel(new FakeUrlLauncher(), typeof(AboutViewModel).Assembly)), null));
            AboutWindow about = scope.Track(Assert.IsType<AboutWindow>(Assert.IsType<AvaloniaRecentWindow>(tracker.Recent.MostRecent).Window));
            Dispatcher.UIThread.RunJobs();

            HyperlinkButton open = Ui.Find<HyperlinkButton>(about, static button => button.Content as string == "Third-party licences…");
            Ui.Click(about, Ui.CentreOf(open, about));
            Dispatcher.UIThread.RunJobs();

            LicensesWindow licences = Assert.IsType<LicensesWindow>(Assert.Single(about.OwnedWindows));
            Assert.Equal(2, tracker.Recent.Count);
            Assert.Same(licences, Assert.IsType<AvaloniaRecentWindow>(tracker.Recent.MostRecent).Window);

            // Minimized with its owner: the owner is restored first, then the licences are activated.
            about.WindowState = WindowState.Minimized;
            Dispatcher.UIThread.RunJobs();
            tracker.Recent.MostRecent!.BringToFront();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(WindowState.Normal, about.WindowState);
            Assert.Equal(WindowState.Normal, licences.WindowState);
            Assert.Equal("attention 102", shell.Calls[^1]);
            return Task.CompletedTask;
        });

    [Fact]
    public Task EveryAuxiliaryWindowOfTheTrayCommandsIsTracked() =>
        session.RunAsync(static async scope =>
        {
            List<Window> tracked = [];
            Func<Window, IDisposable> track = window =>
            {
                tracked.Add(window);
                return scope.Defer(new Untrack());
            };
            AuxiliaryWindowFactory windows = new(track);

            IAuxiliaryWindow diagnostics = windows.Diagnostics(viewModel: null);
            (IAuxiliaryWindow settings, SettingsViewModel viewModel) = windows.Settings(static screens =>
                new SettingsViewModel(SettingsWindowHarness.StoreWith(), new FakeConnectionTester(), SettingsWindowHarness.Supported, screens: screens));
            scope.Defer(viewModel);
            IAuxiliaryWindow about = windows.About(new AboutViewModel(new FakeUrlLauncher(), typeof(AboutViewModel).Assembly));
            await using CameraHarness harness = new(track: track);
            scope.Track(harness.Open());

            Assert.Collection(
                tracked,
                static window => Assert.IsType<DiagnosticsWindow>(window),
                static window => Assert.IsType<SettingsWindow>(window),
                static window => Assert.IsType<AboutWindow>(window),
                static window => Assert.IsType<CameraWindow>(window));
            foreach (Window window in tracked)
            {
                scope.Track(window);
            }

            diagnostics.Close();
            settings.Close();
            about.Close();
        });

    /// <summary>Handles 101, 102... in order of the windows asked, then 0 (like a closed window).</summary>
    private static Func<Window, nint> FakeHandles()
    {
        Dictionary<Window, nint> handles = [];
        return window =>
        {
            if (handles.ContainsKey(window))
            {
                return 0;
            }

            handles[window] = 101 + handles.Count;
            return handles[window];
        };
    }

    private sealed class Untrack : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private sealed class RecordingShell : IWindowShell
    {
        public List<string> Calls { get; } = [];

        public void Prepare(nint handle) => Calls.Add($"prepare {handle}");

        public void Release(nint handle) => Calls.Add($"release {handle}");

        public void RequestAttention(nint handle) => Calls.Add($"attention {handle}");
    }
}
