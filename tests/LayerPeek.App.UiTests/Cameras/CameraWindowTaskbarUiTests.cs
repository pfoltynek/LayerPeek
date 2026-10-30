using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

using LayerPeek.App.Cameras;
using LayerPeek.App.Tests.Cameras;
using LayerPeek.App.Tray;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.UiTests.Cameras;

/// <summary>
/// A camera window has a taskbar button unless it is always on top (T-102, user decision 2026-10-29; T-040 before: none for
/// the mini preview only). With the default options that is the same as before: the mini preview is on top, the normal
/// window and full screen are not.
/// </summary>
public sealed class CameraWindowTaskbarUiTests(HeadlessSession session)
{
    private static CameraViewModel ViewModel(CameraWindow window) => (CameraViewModel)window.DataContext!;

    [Theory]
    [InlineData(CameraWindowStartMode.Normal, true)]
    [InlineData(CameraWindowStartMode.Mini, false)]
    [InlineData(CameraWindowStartMode.FullScreen, true)]
    [InlineData(CameraWindowStartMode.Last, true)]
    public Task OpeningModeDecidesTheTaskbarButton(CameraWindowStartMode startMode, bool expected) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: CameraWindowDefaults.Default with { StartMode = startMode });

            CameraWindow window = harness.Open();
            window.Settle();

            Assert.Equal(expected, window.ShowInTaskbar);
        });

    [Fact]
    public Task EveryModeSwitchUpdatesTheTaskbarButton() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors());
            CameraWindow window = harness.Open();
            window.Settle();
            CameraViewModel viewModel = ViewModel(window);
            Assert.True(window.ShowInTaskbar);

            void Step(Action action, bool expected, CameraWindowMode mode)
            {
                action();
                Dispatcher.UIThread.RunJobs();
                window.Settle();
                Assert.Equal(mode, viewModel.Mode);
                Assert.Equal(expected, window.ShowInTaskbar);
            }

            Step(() => viewModel.ShowMiniCommand.Execute(null), false, CameraWindowMode.Mini);
            Step(() => viewModel.ShowNormalCommand.Execute(null), true, CameraWindowMode.Normal);
            Step(() => viewModel.ShowMiniCommand.Execute(null), false, CameraWindowMode.Mini);
            Step(() => viewModel.ToggleFullScreenCommand.Execute(null), true, CameraWindowMode.FullScreen);
            Step(() => window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null), false, CameraWindowMode.Mini);
            Step(() => window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null), true, CameraWindowMode.Normal);
            Step(() => viewModel.ToggleFullScreenCommand.Execute(null), true, CameraWindowMode.FullScreen);
            Step(() => viewModel.ToggleFullScreenCommand.Execute(null), true, CameraWindowMode.Normal);
        });

    [Fact]
    public Task MiniPreviewKeepsNoTaskbarButtonAfterReopening() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors());
            CameraWindow window = harness.Open();
            window.Settle();
            ViewModel(window).ShowMiniCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            window.Close();

            // "Same as last time": the mini preview again, still without a taskbar button.
            CameraWindow reopened = harness.Open();
            reopened.Settle();

            Assert.True(ViewModel(reopened).IsMini);
            Assert.False(reopened.ShowInTaskbar);
        });

    [Fact]
    public Task WindowsOfOtherPrintersKeepTheirOwnTaskbarButton() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors());
            CameraWindow alpha = harness.Open("a");
            CameraWindow beta = harness.Open("b");
            alpha.Settle();

            ViewModel(alpha).ShowMiniCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.False(alpha.ShowInTaskbar);
            Assert.True(beta.ShowInTaskbar);
        });

    public static TheoryData<bool, bool> TopmostOptions => new() { { false, false }, { false, true }, { true, false }, { true, true } };

    [Theory]
    [MemberData(nameof(TopmostOptions))]
    public Task TaskbarButtonFollowsTopmostInEveryMode(bool normal, bool mini) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: CameraWindowDefaults.Default with { TopmostNormal = normal, TopmostMini = mini });
            CameraWindow window = harness.Open();
            window.Settle();
            CameraViewModel viewModel = ViewModel(window);
            Assert.Equal(!normal, window.ShowInTaskbar);

            void Step(Action action, CameraWindowMode mode, bool topmost)
            {
                action();
                Dispatcher.UIThread.RunJobs();
                window.Settle();
                Assert.Equal(mode, viewModel.Mode);
                Assert.Equal(topmost, window.Topmost);
                Assert.Equal(!topmost, window.ShowInTaskbar);
            }

            Step(() => viewModel.ShowMiniCommand.Execute(null), CameraWindowMode.Mini, mini);
            Step(() => viewModel.ToggleFullScreenCommand.Execute(null), CameraWindowMode.FullScreen, false);
            Step(() => window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null), CameraWindowMode.Mini, mini);
            Step(() => viewModel.ShowNormalCommand.Execute(null), CameraWindowMode.Normal, normal);
            Step(() => viewModel.ToggleFullScreenCommand.Execute(null), CameraWindowMode.FullScreen, false);
            Step(() => viewModel.ToggleFullScreenCommand.Execute(null), CameraWindowMode.Normal, normal);
        });

    [Theory]
    [InlineData(CameraWindowStartMode.Normal, true, true, false)]
    [InlineData(CameraWindowStartMode.Mini, false, false, true)]
    [InlineData(CameraWindowStartMode.FullScreen, true, true, true)]
    public Task OpeningWithTopmostOptionsDecidesTheTaskbarButton(CameraWindowStartMode startMode, bool normal, bool mini, bool expected) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: CameraWindowDefaults.Default with { StartMode = startMode, TopmostNormal = normal, TopmostMini = mini });

            CameraWindow window = harness.Open();
            window.Settle();

            Assert.Equal(expected, window.ShowInTaskbar);
        });

    [Theory]
    [InlineData(CameraWindowStartMode.Normal)]
    [InlineData(CameraWindowStartMode.Mini)]
    public Task SavedTopmostOptionUpdatesTheTaskbarButtonOfTheOpenWindow(CameraWindowStartMode startMode) =>
        session.RunAsync(async scope =>
        {
            bool mini = startMode == CameraWindowStartMode.Mini;
            CameraWindowDefaults defaults = CameraWindowDefaults.Default with { StartMode = startMode };
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: defaults);
            CameraWindow window = harness.Open();
            window.Settle();
            // Defaults: the mini preview on top, the normal window not.
            Assert.Equal(!mini, window.ShowInTaskbar);

            // Saving flips the option of the current mode (T-037: applied to the open window right away).
            Save(harness, mini ? defaults with { TopmostMini = false } : defaults with { TopmostNormal = true });
            window.Settle();
            Assert.Equal(mini, window.ShowInTaskbar);

            Save(harness, defaults);
            window.Settle();
            Assert.Equal(!mini, window.ShowInTaskbar);
        });

    private static void Save(CameraHarness harness, CameraWindowDefaults defaults)
    {
        // What saving the settings does for an open window: the monitoring session applies the new printer definitions.
        ((IMonitoringObserver)harness.Printers).ApplySettings(new AppSettings
        {
            Printers = [CameraHarness.CameraPrinter("a", "Alpha", defaults), CameraHarness.CameraPrinter("b", "Beta")],
        });
        Dispatcher.UIThread.RunJobs();
    }
}
