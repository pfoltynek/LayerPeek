using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Resources;
using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Settings;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tests.Tray.Menu;
using LayerPeek.App.Tray;
using LayerPeek.App.Tray.Menu;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.UiTests.Settings;
using LayerPeek.App.Windows;

namespace LayerPeek.App.UiTests.Tray;

/// <summary>
/// "Add printer…" (T-042) from the real tray menu through <see cref="ITrayAppCommands.AddPrinter"/> to the real settings
/// window, wired like <c>App</c> does it (<see cref="SettingsWindowCommand"/> on one <see cref="AuxiliaryWindowSlot"/>):
/// the first click opens a new window, the next ones reach the window that is already open.
/// </summary>
public sealed class TrayMenuAddPrinterUiTests(HeadlessSession session)
{
    [Fact]
    public Task AddPrinterOpensTheSettingsWithANewPrinterAndReusesTheOpenWindow() =>
        session.RunAsync(static scope =>
        {
            Commands commands = new(scope, SettingsWindowHarness.StoreWith());
            NativeMenu menu = CreateMenu(scope, commands);
            NativeMenuItem add = Assert.Single(menu.Items.OfType<NativeMenuItem>(), static item => item.Header == Strings.MenuAddPrinter);

            // New window: the printer is added once the settings are loaded.
            Click(add);
            SettingsWindow window = Assert.Single(commands.Windows);
            SettingsViewModel viewModel = Assert.IsType<SettingsViewModel>(window.DataContext);
            Assert.True(window.IsVisible);
            Assert.Equal("p1", Assert.Single(viewModel.Printers).Id);
            Assert.Same(viewModel.Printers[0], viewModel.SelectedPrinter);
            Assert.Equal(SettingsViewModel.PrintersTab, viewModel.SelectedTabIndex);

            // Already open (the printer is not saved yet, so the menu still offers the item): the same window shows the
            // unsaved printer again, no second one is added.
            viewModel.SelectedTabIndex = SettingsViewModel.GeneralTab;
            viewModel.SelectedPrinterTabIndex = SettingsViewModel.CameraPrinterTab;
            window.Settle();
            Click(add);

            Assert.Single(commands.Windows);
            Assert.Single(viewModel.Printers);
            Assert.Equal(SettingsViewModel.PrintersTab, viewModel.SelectedTabIndex);
            Assert.Equal(SettingsViewModel.ConnectionPrinterTab, viewModel.SelectedPrinterTabIndex);
            Assert.True(window.IsVisible);
            return Task.CompletedTask;
        });

    [Fact]
    public Task SettingsAndAddPrinterOnAnOpenWindowAddOnlyOnRequest() =>
        session.RunAsync(static scope =>
        {
            Commands commands = new(scope, SettingsWindowHarness.StoreWith());
            ITrayAppCommands tray = commands;

            // "Settings…" opens the window without adding anything; "Add printer…" then adds to that open window.
            tray.ShowSettings();
            Dispatcher.UIThread.RunJobs();
            SettingsViewModel viewModel = Assert.IsType<SettingsViewModel>(Assert.Single(commands.Windows).DataContext);
            Assert.Empty(viewModel.Printers);

            tray.AddPrinter();
            Dispatcher.UIThread.RunJobs();

            Assert.Single(commands.Windows);
            Assert.Equal("p1", Assert.Single(viewModel.Printers).Id);
            return Task.CompletedTask;
        });

    private static NativeMenu CreateMenu(UiTestScope scope, ITrayAppCommands commands)
    {
        TrayIcon icon = Assert.Single(TrayIcon.GetIcons(Application.Current!)!);
        TrayMenuViewModel viewModel = scope.Defer(new TrayMenuViewModel(new FakePrinterMonitor(), new AvaloniaUiDispatcher(), TestWebOpener.Create(new FakeUrlLauncher()), TrayMenuOptions.Default));
        scope.Defer(new TrayMenuPresenter(new TrayMenuTarget(new AvaloniaNativeTrayMenu(icon)), viewModel, commands, NullLogger.Instance));
        viewModel.SetPrinters([]);
        Dispatcher.UIThread.RunJobs();
        return icon.Menu!;
    }

    private static void Click(NativeMenuItem item)
    {
        ((INativeMenuItemExporterEventsImplBridge)item).RaiseClicked();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The settings part of <c>App</c>: one settings window slot, windows from <see cref="AuxiliaryWindowFactory"/>.</summary>
    private sealed class Commands(UiTestScope scope, FakeEditorStore store) : ITrayAppCommands
    {
        private readonly AuxiliaryWindowSlot _slot = new();
        private readonly AuxiliaryWindowFactory _windows = new(track: null);
        private readonly Queue<string> _ids = new(["p1", "p2", "p3"]);

        /// <summary>Every settings window created, in order.</summary>
        public List<SettingsWindow> Windows { get; } = [];

        public void AddPrinter() => Show(addPrinter: true);

        public void ShowSettings() => Show(addPrinter: false);

        public void ShowAbout()
        {
        }

        public void ShowDiagnostics(string? printerId)
        {
        }

        public void ToggleCamera(string printerId)
        {
        }

        public void BringCameraToFront(string printerId)
        {
        }

        public void Quit()
        {
        }

        private void Show(bool addPrinter) =>
            SettingsWindowCommand.Show(
                _slot,
                () => _windows.Settings(
                    screens => new SettingsViewModel(store, new FakeConnectionTester(), SettingsWindowHarness.Supported, _ids.Dequeue, new FakeCameraTester(), screens: screens),
                    () =>
                    {
                        // Closed after the test; closing releases the view model through the slot.
                        SettingsWindow window = scope.Track(new SettingsWindow());
                        Windows.Add(window);
                        return window;
                    }),
                addPrinter);
    }
}
