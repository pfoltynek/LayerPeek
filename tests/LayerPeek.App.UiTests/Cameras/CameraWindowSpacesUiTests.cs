using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

using LayerPeek.App.Cameras;
using LayerPeek.App.Platform.MacOS;
using LayerPeek.App.Tray;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.UiTests.Cameras;

/// <summary>
/// T-038 question 1 (user decision 2026-10-30, option b): the real camera window puts its mini preview that is always on
/// top on every Space (macOS <c>collectionBehavior</c>, here a fake), and restores the flags when it leaves the mini
/// preview or "always on top" is switched off; full screen is entered with the flags of a normal window. Whether macOS
/// really shows it on other Spaces and above an app in full screen is a manual check (T-115, next to J4).
/// </summary>
public sealed class CameraWindowSpacesUiTests(HeadlessSession session)
{
    private const nuint Avalonia = MacWindowSpaces.FullScreenPrimary;
    private const nuint AllSpaces = MacWindowSpaces.CanJoinAllSpaces | MacWindowSpaces.FullScreenAuxiliary;

    private static CameraViewModel ViewModel(CameraWindow window) => (CameraViewModel)window.DataContext!;

    private static void Run(CameraWindow window, Action action)
    {
        action();
        Dispatcher.UIThread.RunJobs();
        window.Settle();
    }

    private static void Save(CameraHarness harness, CameraWindowDefaults defaults)
    {
        ((IMonitoringObserver)harness.Printers).ApplySettings(new AppSettings
        {
            Printers = [CameraHarness.CameraPrinter("a", "Alpha", defaults), CameraHarness.CameraPrinter("b", "Beta")],
        });
        Dispatcher.UIThread.RunJobs();
    }

    private static FakeWindow Attach(CameraWindow window)
    {
        FakeWindow fake = new(window);
        window.CollectionBehavior = fake;
        Dispatcher.UIThread.RunJobs();
        return fake;
    }

    [Fact]
    public Task TheHeadlessWindowHasNoNativeFlags() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(window: CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini });
            CameraWindow window = harness.Open();
            window.Settle();

            // Only an NSWindow has them; the headless platform (and Windows) has none, so nothing native is touched.
            Assert.Null(window.CollectionBehavior);
            Assert.False(window.IsOnAllSpaces);
        });

    [Fact]
    public Task TheMiniPreviewOnTopJoinsEverySpaceAndTheNormalWindowRestoresTheFlags() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(window: CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal, TopmostNormal = true });
            CameraWindow window = harness.Open();
            window.Settle();
            FakeWindow fake = Attach(window);
            CameraViewModel viewModel = ViewModel(window);

            // The normal window stays as Avalonia made it, also when it is always on top.
            Assert.True(window.Topmost);
            Assert.Empty(fake.Writes);

            Run(window, () => viewModel.ShowMiniCommand.Execute(null));
            Assert.True(window.IsOnAllSpaces);
            Assert.Equal(AllSpaces, fake.Behavior);

            Run(window, () => viewModel.ShowNormalCommand.Execute(null));
            Assert.False(window.IsOnAllSpaces);
            Assert.Equal(Avalonia, fake.Behavior);
            Assert.Equal(2, fake.Writes.Count);
        });

    [Fact]
    public Task AMiniPreviewOpenedOnTopJoinsEverySpace() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(window: CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini });
            CameraWindow window = harness.Open();
            window.Settle();

            FakeWindow fake = Attach(window);

            Assert.True(window.IsOnAllSpaces);
            Assert.Equal(AllSpaces, fake.Behavior);
        });

    [Fact]
    public Task FullScreenIsEnteredWithTheFlagsOfANormalWindowAndItsExitToTheMiniPreviewJoinsAgain() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(window: CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini });
            CameraWindow window = harness.Open();
            window.Settle();
            FakeWindow fake = Attach(window);
            CameraViewModel viewModel = ViewModel(window);

            Run(window, () => viewModel.ToggleFullScreenCommand.Execute(null));
            Assert.Equal(WindowState.FullScreen, window.WindowState);
            Assert.False(window.IsOnAllSpaces);
            Assert.Equal(Avalonia, fake.Behavior);
            // Restored while the window was still not in full screen (toggleFullScreen: needs FullScreenPrimary).
            Assert.Equal(WindowState.Normal, fake.Writes[^1].State);

            // Esc returns to the mini preview it was entered from.
            Run(window, () => window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null));
            Assert.True(viewModel.IsMini);
            Assert.True(window.IsOnAllSpaces);
            Assert.Equal(AllSpaces, fake.Behavior);
        });

    [Fact]
    public Task SwitchingAlwaysOnTopOffAndOnInTheOpenMiniPreviewFollows() =>
        session.RunAsync(async scope =>
        {
            CameraWindowDefaults defaults = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini };
            await using CameraHarness harness = new(window: defaults);
            CameraWindow window = harness.Open();
            window.Settle();
            FakeWindow fake = Attach(window);
            Assert.True(window.IsOnAllSpaces);

            Save(harness, defaults with { TopmostMini = false });
            window.Settle();
            Assert.False(window.Topmost);
            Assert.False(window.IsOnAllSpaces);
            Assert.Equal(Avalonia, fake.Behavior);

            Save(harness, defaults);
            window.Settle();
            Assert.True(window.IsOnAllSpaces);
            Assert.Equal(AllSpaces, fake.Behavior);
        });

    [Fact]
    public Task ThePinOfTheMiniPreviewSwitchesItsSpacesAtOnce() =>
        session.RunAsync(async scope =>
        {
            // T-117: the pin switches Window.Topmost, which the flags follow like a saved option.
            await using CameraHarness harness = new(window: CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini });
            CameraWindow window = harness.Open();
            window.Settle();
            FakeWindow fake = Attach(window);
            global::Avalonia.Controls.Primitives.ToggleButton pin = window.Find<global::Avalonia.Controls.Primitives.ToggleButton>("CameraTopmostButton");
            Assert.True(window.IsOnAllSpaces);

            Run(window, () => Ui.Click(window, Ui.CentreOf(pin, window)));
            Assert.False(window.Topmost);
            Assert.False(window.IsOnAllSpaces);
            Assert.Equal(Avalonia, fake.Behavior);

            Run(window, () => Ui.Click(window, Ui.CentreOf(pin, window)));
            Assert.True(window.Topmost);
            Assert.True(window.IsOnAllSpaces);
            Assert.Equal(AllSpaces, fake.Behavior);
        });

    [Fact]
    public Task AMiniPreviewThatIsNotOnTopStaysOnItsSpace() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(window: CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal, TopmostMini = false });
            CameraWindow window = harness.Open();
            window.Settle();
            FakeWindow fake = Attach(window);

            Run(window, () => ViewModel(window).ShowMiniCommand.Execute(null));

            Assert.True(ViewModel(window).IsMini);
            Assert.False(window.IsOnAllSpaces);
            Assert.Empty(fake.Writes);
        });

    [Fact]
    public Task NothingIsWrittenAfterTheWindowClosed() =>
        session.RunAsync(async scope =>
        {
            CameraWindowDefaults defaults = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini };
            await using CameraHarness harness = new(window: defaults);
            CameraWindow window = harness.Open();
            window.Settle();
            FakeWindow fake = Attach(window);
            int writes = fake.Writes.Count;

            window.Close();
            Dispatcher.UIThread.RunJobs();
            window.Topmost = false;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(writes, fake.Writes.Count);
        });

    private sealed class FakeWindow(Window window) : IWindowCollectionBehavior
    {
        public nuint Behavior { get; private set; } = Avalonia;

        public List<(nuint Behavior, WindowState State)> Writes { get; } = [];

        public nuint? Read() => Behavior;

        public void Write(nuint behavior)
        {
            Writes.Add((behavior, window.WindowState));
            Behavior = behavior;
        }
    }
}
