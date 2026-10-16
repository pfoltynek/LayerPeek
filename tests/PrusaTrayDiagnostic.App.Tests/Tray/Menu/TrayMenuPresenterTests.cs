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

    public TrayMenuPresenterTests() => _viewModel = new TrayMenuViewModel(_monitor, _dispatcher, _launcher, TrayMenuOptions.Default);

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
        Assert.Equal("No printer configured", Assert.Single(_target.Printers).Header);
    }

    [Fact]
    public void ClickOnPrinterOpensItsWebInterface()
    {
        _viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();
        using TrayMenuPresenter presenter = Create();

        _target.Click(_target.Printers[0]);

        Assert.Equal([new Uri("http://192.168.1.10/")], _launcher.Opened);
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

        _target.Click(_target.Printers[0]);
        _target.Click(Fixed(TrayMenuCommand.Settings));
        _target.Click(Fixed(TrayMenuCommand.Diagnostics));

        Assert.Empty(_launcher.Opened);
        Assert.Equal(0, _commands.AboutCount);
        Assert.Equal(0, _commands.QuitCount);
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

    private TrayMenuEntry Fixed(TrayMenuCommand command) => _target.Fixed.Single(item => item.Command == command);

    private TrayMenuPresenter Create() => new(_target, _viewModel, _commands, _logger);
}
