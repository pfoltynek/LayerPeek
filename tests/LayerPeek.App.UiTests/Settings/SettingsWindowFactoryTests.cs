using LayerPeek.App.Cameras;
using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Settings;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;

namespace LayerPeek.App.UiTests.Settings;

/// <summary>How <c>App.ShowSettings</c> creates the settings window and its view model (T-035).</summary>
public sealed class SettingsWindowFactoryTests(HeadlessSession session)
{
    private static SettingsViewModel NewViewModel(IScreenCatalog screens) =>
        new(SettingsWindowHarness.StoreWith(), new FakeConnectionTester(), SettingsWindowHarness.Supported, screens: screens);

    [Fact]
    public Task ViewModelGetsTheMonitorsOfItsWindow() =>
        session.RunAsync(static scope =>
        {
            SettingsWindow? created = null;

            (SettingsWindow window, SettingsViewModel viewModel) = SettingsWindowFactory.Create(() => created = new SettingsWindow(), NewViewModel);
            scope.Defer(viewModel);
            scope.Track(window);

            Assert.Same(created, window);
            Assert.Same(viewModel, window.DataContext);
            // The headless platform has one screen; the catalog reads it through the (not yet shown) window.
            Assert.Single(viewModel.ScreenList);
            return Task.CompletedTask;
        });

    [Fact]
    public Task FailingViewModelLeavesNoWindowBehind() =>
        session.RunAsync(static scope =>
        {
            SettingsWindow? created = null;
            int closed = 0;

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => SettingsWindowFactory.Create(
                () =>
                {
                    created = new SettingsWindow();
                    created.Closed += (_, _) => closed++;
                    return created;
                },
                static _ => throw new InvalidOperationException("broken")));

            Assert.Equal("broken", error.Message);
            Assert.NotNull(created);
            Assert.False(created.IsVisible);
            Assert.Null(created.PlatformImpl);
            Assert.Equal(1, closed);
            return Task.CompletedTask;
        });
}
