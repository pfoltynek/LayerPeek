using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

using LayerPeek.App.Cameras;
using LayerPeek.App.Tests.Cameras;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;
using LayerPeek.Core.Cameras;

namespace LayerPeek.App.UiTests.Cameras;

/// <summary>The mini preview has no taskbar button; the normal window and full screen have one (T-040).</summary>
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
}
