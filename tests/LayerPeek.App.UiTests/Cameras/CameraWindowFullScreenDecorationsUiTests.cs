using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

using LayerPeek.App.Cameras;
using LayerPeek.App.Platform.MacOS;
using LayerPeek.App.Tests.Cameras;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;
using LayerPeek.Core.Cameras;

namespace LayerPeek.App.UiTests.Cameras;

/// <summary>
/// T-237 (T-038 question 2): the real camera window after AppKit left full screen by itself (Esc on macOS, the green
/// button; here the window state set from outside, which is what Avalonia reports after the exit animation) gives its
/// native window the decorations of the mode it returns to (a fake), then places it; exits the app asks for leave the
/// decorations to Avalonia. Whether the frame really goes away is checked live by <c>mac.mini-after-fullscreen</c>.
/// </summary>
public sealed class CameraWindowFullScreenDecorationsUiTests(HeadlessSession session)
{
    private static readonly CameraWindowDefaults MiniFirst = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini };

    private static CameraViewModel ViewModel(CameraWindow window) => (CameraViewModel)window.DataContext!;

    private static void Run(CameraWindow window, Action action)
    {
        action();
        Dispatcher.UIThread.RunJobs();
        window.Settle();
    }

    /// <summary>The mini preview somewhere on the second monitor, with the fake native window attached.</summary>
    private static (CameraWindow Window, FakeDecorations Fake) OpenMini(CameraHarness harness)
    {
        CameraWindow window = harness.Open();
        window.Settle();
        Assert.True(ViewModel(window).IsMini);
        window.Position = new PixelPoint(1500, 800);
        window.Settle();
        FakeDecorations fake = new(window);
        window.NativeDecorations = fake;
        return (window, fake);
    }

    /// <summary>Full screen as the app enters it (F11).</summary>
    private static void EnterFullScreen(CameraWindow window)
    {
        Run(window, () => ViewModel(window).ToggleFullScreenCommand.Execute(null));
        Assert.Equal(WindowState.FullScreen, window.WindowState);
    }

    /// <summary>AppKit left full screen by itself; Avalonia reports it when the exit animation has ended.</summary>
    private static void AppKitLeavesFullScreen(CameraWindow window) => Run(window, () => window.WindowState = WindowState.Normal);

    [Fact]
    public Task TheHeadlessWindowHasNoNativeDecorations() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(window: MiniFirst);
            CameraWindow window = harness.Open();
            window.Settle();

            // Only an NSWindow has them; the headless platform (and Windows) has none, so nothing native is touched.
            Assert.Null(window.NativeDecorations);
            Assert.False(window.DecorationsIgnoredByAvalonia);
        });

    [Fact]
    public Task AfterAppKitLeftFullScreenTheMiniPreviewLosesItsFrameAndThenTakesItsPlace() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            (CameraWindow window, FakeDecorations fake) = OpenMini(harness);
            PixelPoint position = window.Position;
            Size size = window.ClientSize;
            // Like AppKit, the new style mask keeps the frame where it was (the restored frame with its title bar is higher).
            fake.OnApply = () => window.Position = new PixelPoint(position.X, position.Y - 28);

            EnterFullScreen(window);
            // The frame of the normal window for full screen: Avalonia applies it (the app entered).
            Assert.Empty(fake.Applied);

            AppKitLeavesFullScreen(window);

            CameraViewModel viewModel = ViewModel(window);
            Assert.True(viewModel.IsMini);
            Assert.True(window.DecorationsIgnoredByAvalonia);
            // Once, without a frame, after the exit (the window was no longer in full screen) and before the placement: the
            // window is at the place of the mini preview again, not where the style change left it.
            FakeDecorations.Call call = Assert.Single(fake.Applied);
            Assert.False(call.Framed);
            Assert.Equal(WindowState.Normal, call.State);
            Assert.Equal(WindowDecorations.None, window.WindowDecorations);
            Assert.True(window.Topmost);
            Assert.Equal(position, window.Position);
            Assert.Equal(size, window.ClientSize);
        });

    [Fact]
    public Task ExitsTheAppAsksForLeaveTheDecorationsToAvalonia() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            (CameraWindow window, FakeDecorations fake) = OpenMini(harness);
            PixelPoint position = window.Position;

            // The mini preview button of full screen, Esc and F11 as the app handles them (Avalonia then clears its flag).
            EnterFullScreen(window);
            Run(window, () => Ui.Click(window, Ui.CentreOf(window.Find<Button>("CameraMiniButton"), window)));
            EnterFullScreen(window);
            Run(window, () => window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null));
            EnterFullScreen(window);
            Run(window, () => window.KeyPress(Key.F11, RawInputModifiers.None, PhysicalKey.F11, null));

            Assert.True(ViewModel(window).IsMini);
            Assert.False(window.DecorationsIgnoredByAvalonia);
            Assert.Empty(fake.Applied);
            Assert.Equal(WindowDecorations.None, window.WindowDecorations);
            Assert.Equal(position, window.Position);
        });

    [Fact]
    public Task EveryLaterModeGetsItsDecorationsUntilTheAppEntersFullScreenAgain() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            (CameraWindow window, FakeDecorations fake) = OpenMini(harness);
            CameraViewModel viewModel = ViewModel(window);
            EnterFullScreen(window);
            AppKitLeavesFullScreen(window);

            Run(window, () => viewModel.ShowNormalCommand.Execute(null));
            Run(window, () => viewModel.ShowMiniCommand.Execute(null));
            Assert.Equal([false, true, false], fake.Applied.Select(call => call.Framed));
            Assert.True(fake.Applied[1].Resizable);

            // Full screen needs the frame of the normal window first; entering it gives Avalonia the decorations back.
            EnterFullScreen(window);
            Assert.Equal(4, fake.Applied.Count);
            Assert.True(fake.Applied[^1].Framed);
            Assert.False(window.DecorationsIgnoredByAvalonia);
            Run(window, () => viewModel.ShowMiniCommand.Execute(null));

            Assert.True(viewModel.IsMini);
            Assert.Equal(4, fake.Applied.Count);
        });

    [Fact]
    public Task TheNormalWindowAfterAppKitLeftFullScreenKeepsTheFrameAppKitRestored() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors());
            CameraWindow window = harness.Open();
            window.Settle();
            FakeDecorations fake = new(window);
            window.NativeDecorations = fake;
            CameraViewModel viewModel = ViewModel(window);

            EnterFullScreen(window);
            AppKitLeavesFullScreen(window);
            Assert.True(viewModel.IsNormal);
            Assert.Empty(fake.Applied);

            // The mini preview switched to later still loses the frame.
            Run(window, () => viewModel.ShowMiniCommand.Execute(null));
            Assert.False(Assert.Single(fake.Applied).Framed);
        });

    [Fact]
    public Task NothingIsAppliedAfterTheWindowClosed() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            (CameraWindow window, FakeDecorations fake) = OpenMini(harness);
            EnterFullScreen(window);
            AppKitLeavesFullScreen(window);
            int applied = fake.Applied.Count;

            window.Close();
            Dispatcher.UIThread.RunJobs();
            window.WindowDecorations = WindowDecorations.Full;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(applied, fake.Applied.Count);
        });

    private sealed class FakeDecorations(Window window) : IWindowDecorations
    {
        public List<Call> Applied { get; } = [];

        public Action? OnApply { get; set; }

        public void Apply(bool framed, bool resizable, bool miniaturizable)
        {
            Applied.Add(new Call(framed, resizable, window.WindowState));
            OnApply?.Invoke();
        }

        public sealed record Call(bool Framed, bool Resizable, WindowState State);
    }
}
