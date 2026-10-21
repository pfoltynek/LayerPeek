using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.Tray.Menu;
using PrusaTrayDiagnostic.Core.Printers;

using static PrusaTrayDiagnostic.App.Tests.Tray.TestPrinters;

namespace PrusaTrayDiagnostic.App.Tests.Tray.Menu;

public sealed class TrayMenuPresenterTests : IDisposable
{
    private readonly CultureScope _culture = new("en-US");
    private readonly FakePrinterMonitor _monitor = new();
    private readonly ManualDispatcher _dispatcher = new();
    private readonly FakeUrlLauncher _launcher = new();
    private readonly FakeMenuTarget _target = new();
    private readonly FakeAppCommands _commands = new();
    private readonly ListLogger<TrayMenuPresenterTests> _logger = new();
    private readonly TrayMenuViewModel _viewModel;

    public TrayMenuPresenterTests() => _viewModel = new TrayMenuViewModel(_monitor, _dispatcher, TestWebOpener.Create(_launcher), TrayMenuOptions.Default);

    public void Dispose()
    {
        _viewModel.Dispose();
        _culture.Dispose();
    }

    [Fact]
    public void BuildsWholeMenuOnCreation()
    {
        using TrayMenuPresenter presenter = Create();

        Assert.Equal(["fixed", "insert 0"], _target.Operations);
        Assert.Equal(_viewModel.FixedItems, _target.Fixed);
        Assert.Equal(_viewModel.PrinterItems, _target.Printers);
    }

    [Fact]
    public void ChangedPrinterUpdatesOnlyItsItem()
    {
        _viewModel.SetPrinters([Definition("a", "Alpha"), Definition("b", "Beta")]);
        _dispatcher.RunAll();
        using TrayMenuPresenter presenter = Create();
        _target.Operations.Clear();

        _monitor.Raise(Snapshot(PrinterState.Printing, Job(42), printerId: "b"));
        _dispatcher.RunAll();

        Assert.Equal(["update 1"], _target.Operations);
        Assert.Equal("Beta: Printing – 42%", _target.Printers[1].Header);
    }

    [Fact]
    public void ConfigurationChangesAreMirroredItemByItem()
    {
        using TrayMenuPresenter presenter = Create();

        _viewModel.SetPrinters([Definition("a", "Alpha"), Definition("b", "Beta"), Definition("c", "Gamma")]);
        _dispatcher.RunAll();
        Assert.Equal(_viewModel.PrinterItems, _target.Printers);

        _viewModel.SetPrinters([]);
        _dispatcher.RunAll();
        Assert.Equal(_viewModel.PrinterItems, _target.Printers);
        Assert.Equal(["No printer configured", "Add _printer…"], _target.Printers.Select(static item => item.Header));
    }

    [Fact]
    public async Task SinglePrinterOpensItsWebInterfaceFromItsSubmenuOnly()
    {
        _viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();
        using TrayMenuPresenter presenter = Create();

        // T-042: the printer item only opens its submenu (T-008 opened the web interface).
        _target.Click(_target.Printers[0]);
        Assert.Empty(_launcher.Opened);

        _target.Click(Child(0, TrayMenuCommand.OpenWeb));
        await _launcher.WaitForOpenedAsync(1);

        Assert.Equal([new Uri("http://192.168.1.10/")], _launcher.Opened);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public void ClickOnDiagnosticsOpensTheWindowWithoutAPrinter()
    {
        _viewModel.SetPrinters([Definition("a", "Alpha"), Definition("b", "Beta")]);
        _dispatcher.RunAll();
        using TrayMenuPresenter presenter = Create();

        _target.Click(Fixed(TrayMenuCommand.Diagnostics));

        // The window has its own printer combo; an open window keeps its printer.
        Assert.Equal([null], _commands.DiagnosticsRequests);
        Assert.Equal(0, _commands.SettingsCount);
    }

    [Fact]
    public void DiagnosticsFlipUpdatesOnlyItsItem()
    {
        using TrayMenuPresenter presenter = Create();
        Assert.False(Fixed(TrayMenuCommand.Diagnostics).IsEnabled);
        _target.Operations.Clear();

        _viewModel.SetPrinters([Definition("a", "Alpha"), Definition("b", "Beta")]);
        _dispatcher.RunAll();

        // "Starting…" is replaced by the printers and only the "Diagnostics…" item is updated in place: no rebuild, no clear
        // (user decision 2026-10-21; an open menu stays as it is).
        Assert.Equal(["update 0", "insert 1", "update fixed 0"], _target.Operations);
        Assert.Equal(_viewModel.FixedItems, _target.Fixed);
        Assert.Equal(_viewModel.PrinterItems, _target.Printers);
        Assert.True(Fixed(TrayMenuCommand.Diagnostics).IsEnabled);

        // A poll touches its printer only.
        _target.Operations.Clear();
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(42), printerId: "b"));
        _dispatcher.RunAll();
        Assert.Equal(["update 1"], _target.Operations);

        // Removing every printer disables it again, the printer part shows "Add printer…".
        _target.Operations.Clear();
        _viewModel.SetPrinters([]);
        _dispatcher.RunAll();
        Assert.Equal(["update 0", "update 1", "update fixed 0"], _target.Operations);
        Assert.False(Fixed(TrayMenuCommand.Diagnostics).IsEnabled);
        Assert.Equal(_viewModel.FixedItems, _target.Fixed);
        Assert.Equal(_viewModel.PrinterItems, _target.Printers);
        Assert.Equal(["No printer configured", "Add _printer…"], _target.Printers.Select(static item => item.Header));
    }

    [Fact]
    public void ClickOnAddPrinterRunsTheAppCommand()
    {
        _viewModel.SetPrinters([]);
        _dispatcher.RunAll();
        using TrayMenuPresenter presenter = Create();

        _target.Click(_target.Printers.Single(static item => item.Command == TrayMenuCommand.AddPrinter));
        // The message next to it does nothing.
        _target.Click(_target.Printers[0]);

        Assert.Equal(1, _commands.AddPrinterCount);
        Assert.Equal(0, _commands.SettingsCount);
    }

    [Fact]
    public void DisabledAddPrinterDoesNothing()
    {
        using TrayMenuViewModel viewModel = new(_monitor, _dispatcher, TestWebOpener.Create(_launcher), TrayMenuOptions.Default with { SettingsAvailable = false });
        viewModel.SetPrinters([]);
        _dispatcher.RunAll();
        using TrayMenuPresenter presenter = new(_target, viewModel, _commands, _logger);

        _target.Click(_target.Printers.Single(static item => item.Command == TrayMenuCommand.AddPrinter));

        Assert.Equal(0, _commands.AddPrinterCount);
    }

    [Fact]
    public void ClickOnAboutAndQuitRunsAppCommands()
    {
        using TrayMenuPresenter presenter = Create();

        _target.Click(Fixed(TrayMenuCommand.About));
        _target.Click(Fixed(TrayMenuCommand.Quit));

        Assert.Equal(1, _commands.AboutCount);
        Assert.Equal(1, _commands.QuitCount);
    }

    [Fact]
    public void ClickOnDisabledItemDoesNothing()
    {
        _viewModel.SetPrinters([Definition() with { Address = "ftp://nope" }]);
        _dispatcher.RunAll();
        using TrayMenuPresenter presenter = Create();

        _target.Click(Child(0, TrayMenuCommand.OpenWeb));
        _target.Click(Fixed(TrayMenuCommand.Settings) with { IsEnabled = false });

        Assert.Empty(_launcher.Opened);
        Assert.Equal(0, _commands.AboutCount);
        Assert.Equal(0, _commands.QuitCount);
        Assert.Equal(0, _commands.SettingsCount);
    }

    [Fact]
    public void ClickOnSettingsOpensTheSettingsWindow()
    {
        using TrayMenuPresenter presenter = Create();

        _target.Click(Fixed(TrayMenuCommand.Settings));

        Assert.Equal(1, _commands.SettingsCount);
        Assert.Equal(0, _commands.DiagnosticsCount);
        Assert.Equal(0, _commands.AboutCount);
    }

    [Fact]
    public void MainMenuOffersDiagnosticsAboveSettings()
    {
        _viewModel.SetPrinters([Definition("a", "Alpha")]);
        _dispatcher.RunAll();
        using TrayMenuPresenter presenter = Create();

        // 2026-10-21: one global "Diagnostics…" in the main menu, none in the printer submenus.
        Assert.Equal(
            [TrayMenuCommand.Diagnostics, TrayMenuCommand.Settings, TrayMenuCommand.None, TrayMenuCommand.About, TrayMenuCommand.Quit],
            _target.Fixed.Select(static item => item.Command));
        Assert.True(_target.Fixed[2].IsSeparator);
        Assert.DoesNotContain(_target.Printers[0].Submenu!, static item => item.Command == TrayMenuCommand.Diagnostics);
    }

    [Fact]
    public async Task SubmenuItemsAreRoutedToTheirPrinter()
    {
        _viewModel.SetPrinters([Definition("a", "Alpha"), Definition("b", "Beta") with { Address = "192.168.1.11" }]);
        _dispatcher.RunAll();
        using TrayMenuPresenter presenter = Create();

        _target.Click(Child(1, TrayMenuCommand.OpenWeb));
        await _launcher.WaitForOpenedAsync(1);

        Assert.Equal([new Uri("http://192.168.1.11/")], _launcher.Opened);
    }

    [Fact]
    public void ClickOnSubmenuHeaderDoesNothing()
    {
        _viewModel.SetPrinters([Definition("a", "Alpha"), Definition("b", "Beta")]);
        _dispatcher.RunAll();
        using TrayMenuPresenter presenter = Create();

        _target.Click(_target.Printers[0]);

        Assert.Empty(_launcher.Opened);
        Assert.Equal(0, _commands.DiagnosticsCount);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public void DisabledOpenWebSubmenuItemDoesNothing()
    {
        _viewModel.SetPrinters([Definition("a", "Alpha") with { Address = "ftp://nope" }, Definition("b", "Beta")]);
        _dispatcher.RunAll();
        using TrayMenuPresenter presenter = Create();

        _target.Click(Child(0, TrayMenuCommand.OpenWeb));

        Assert.Empty(_launcher.Opened);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public void PrinterUpdateInSubmenuModeUpdatesOnlyItsItem()
    {
        _viewModel.SetPrinters([Definition("a", "Alpha"), Definition("b", "Beta"), Definition("c", "Gamma")]);
        _dispatcher.RunAll();
        using TrayMenuPresenter presenter = Create();
        _target.Operations.Clear();

        _monitor.Raise(Snapshot(PrinterState.Paused, Job(12), printerId: "c"));
        _dispatcher.RunAll();

        Assert.Equal(["update 2"], _target.Operations);
        Assert.Equal("Gamma: Paused – 12%", _target.Printers[2].Header);
        Assert.NotNull(_target.Printers[2].Submenu);
    }

    [Fact]
    public void DisabledDiagnosticsItemDoesNothing()
    {
        using TrayMenuViewModel viewModel = new(_monitor, _dispatcher, TestWebOpener.Create(_launcher), TrayMenuOptions.Default with { DiagnosticsAvailable = false });
        viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();
        using TrayMenuPresenter presenter = new(_target, viewModel, _commands, _logger);

        _target.Click(Fixed(TrayMenuCommand.Diagnostics));

        Assert.False(Fixed(TrayMenuCommand.Diagnostics).IsEnabled);
        Assert.Equal(0, _commands.DiagnosticsCount);
    }

    [Fact]
    public void DiagnosticsWithoutPrintersDoesNothing()
    {
        using TrayMenuPresenter presenter = Create();

        // Starting, then no printer configured: disabled in both.
        _target.Click(Fixed(TrayMenuCommand.Diagnostics));
        _viewModel.SetPrinters([]);
        _dispatcher.RunAll();
        _target.Click(Fixed(TrayMenuCommand.Diagnostics));

        Assert.False(Fixed(TrayMenuCommand.Diagnostics).IsEnabled);
        Assert.Equal(0, _commands.DiagnosticsCount);
    }

    [Fact]
    public void FailingCommandIsLoggedAndDoesNotEscape()
    {
        _commands.ThrowOnAbout = new InvalidOperationException("window failed");
        using TrayMenuPresenter presenter = Create();

        _target.Click(Fixed(TrayMenuCommand.About));

        (LogLevel level, _, Exception? exception) = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Error, level);
        Assert.Same(_commands.ThrowOnAbout, exception);
    }

    [Fact]
    public void DisposeStopsUpdatesAndClicks()
    {
        TrayMenuPresenter presenter = Create();
        presenter.Dispose();
        _target.Operations.Clear();

        _viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();
        _target.Click(Fixed(TrayMenuCommand.Quit));

        Assert.Empty(_target.Operations);
        Assert.Equal(0, _commands.QuitCount);
        Assert.Equal(0, _target.ClickSubscriberCount);
    }

    [Fact]
    public void LanguageSwitchRebuildsTheMenuOnce()
    {
        _viewModel.SetPrinters([Definition("a", "Alpha"), Definition("b", "Beta")]);
        _dispatcher.RunAll();
        using TrayMenuPresenter presenter = Create();
        _target.Operations.Clear();

        using (new CultureScope("cs-CZ"))
        {
            _viewModel.RefreshTexts();
        }

        // The printer items are replaced first (still in place), then the whole menu is built again.
        Assert.Equal(["update 0", "update 1", "fixed", "insert 0", "insert 1"], _target.Operations);
        Assert.Equal(_viewModel.FixedItems, _target.Fixed);
        Assert.Equal(_viewModel.PrinterItems, _target.Printers);
        Assert.Equal("U_končit", Fixed(TrayMenuCommand.Quit).Header);
        Assert.Equal("Alpha: Připojování…", _target.Printers[0].Header);

        // Clicks act on the rebuilt items.
        _target.Click(Fixed(TrayMenuCommand.Quit));
        Assert.Equal(1, _commands.QuitCount);
    }

    [Fact]
    public void LanguageSwitchWithPendingDiagnosticsFlipRebuildsTheMenuOnce()
    {
        using TrayMenuPresenter presenter = Create();
        _viewModel.SetPrinters([Definition("a", "Alpha"), Definition("b", "Beta")]);
        _target.Operations.Clear();

        // The posted UI update has not run yet: the language switch applies the printers (and the flip) itself.
        using (new CultureScope("cs-CZ"))
        {
            _viewModel.RefreshTexts();
            _dispatcher.RunAll();
        }

        Assert.Single(_target.Operations, static operation => operation == "fixed");
        // The rebuild already shows the enabled item: no separate in-place update.
        Assert.DoesNotContain(_target.Operations, static operation => operation.StartsWith("update fixed", StringComparison.Ordinal));
        Assert.Equal(_viewModel.FixedItems, _target.Fixed);
        Assert.Equal(_viewModel.PrinterItems, _target.Printers);
        TrayMenuEntry diagnostics = Fixed(TrayMenuCommand.Diagnostics);
        Assert.True(diagnostics.IsEnabled);
        Assert.Equal("_Diagnostika…", diagnostics.Header);
    }

    [Fact]
    public void PendingDiagnosticsFlipAfterViewModelDisposeTouchesNothing()
    {
        using TrayMenuPresenter presenter = Create();
        _viewModel.SetPrinters([Definition("a", "Alpha")]);
        // Quitting: the view model goes away before the posted UI update runs.
        _viewModel.Dispose();
        _target.Operations.Clear();

        _dispatcher.RunAll();

        Assert.Empty(_target.Operations);
        Assert.False(Fixed(TrayMenuCommand.Diagnostics).IsEnabled);
        Assert.False(_viewModel.FixedItems.Single(static item => item.Command == TrayMenuCommand.Diagnostics).IsEnabled);
    }

    [Fact]
    public void FailedDiagnosticsUpdateRebuildsTheMenuAndLogs()
    {
        using TrayMenuPresenter presenter = Create();
        _dispatcher.RunAll();
        _target.Operations.Clear();
        _target.ThrowOnUpdateFixedItem = new ArgumentException("Structure changed.");

        _viewModel.SetPrinters([Definition("a", "Alpha")]);
        _dispatcher.RunAll();

        // The in-place update failed: the whole menu is built again instead of staying stale or crashing the UI thread.
        Assert.Equal(["update 0", "fixed", "insert 0"], _target.Operations);
        Assert.Equal(_viewModel.FixedItems, _target.Fixed);
        Assert.Equal(_viewModel.PrinterItems, _target.Printers);
        Assert.True(Fixed(TrayMenuCommand.Diagnostics).IsEnabled);
        (LogLevel level, _, Exception? exception) = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Warning, level);
        Assert.IsType<ArgumentException>(exception);
    }

    [Fact]
    public void LanguageSwitchAfterDisposeTouchesNothing()
    {
        TrayMenuPresenter presenter = Create();
        presenter.Dispose();
        _target.Operations.Clear();

        _viewModel.RefreshTexts();

        Assert.Empty(_target.Operations);
    }

    private TrayMenuEntry Fixed(TrayMenuCommand command) => _target.Fixed.Single(item => item.Command == command);

    private TrayMenuEntry Child(int printer, TrayMenuCommand command) => _target.Printers[printer].Submenu!.Single(item => item.Command == command);

    private TrayMenuPresenter Create() => new(_target, _viewModel, _commands, _logger);
}
