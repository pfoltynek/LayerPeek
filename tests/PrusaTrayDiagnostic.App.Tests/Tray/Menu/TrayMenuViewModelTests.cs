using System.Collections.Specialized;

using PrusaTrayDiagnostic.App.Tray.Menu;
using PrusaTrayDiagnostic.Core.Printers;

using static PrusaTrayDiagnostic.App.Tests.Tray.TestPrinters;

namespace PrusaTrayDiagnostic.App.Tests.Tray.Menu;

public sealed class TrayMenuViewModelTests : IDisposable
{
    private readonly CultureScope _culture = new("en-US");
    private readonly FakePrinterMonitor _monitor = new();
    private readonly ManualDispatcher _dispatcher = new();
    private readonly FakeUrlLauncher _launcher = new();
    private readonly TrayMenuViewModel _viewModel;
    private readonly List<NotifyCollectionChangedEventArgs> _changes = [];

    public TrayMenuViewModelTests()
    {
        _viewModel = new TrayMenuViewModel(_monitor, _dispatcher, _launcher, TrayMenuOptions.Default);
        ((INotifyCollectionChanged)_viewModel.PrinterItems).CollectionChanged += OnCollectionChanged;
    }

    public void Dispose()
    {
        ((INotifyCollectionChanged)_viewModel.PrinterItems).CollectionChanged -= OnCollectionChanged;
        _viewModel.Dispose();
        _culture.Dispose();
    }

    [Fact]
    public void StartsWithDisabledStartingEntryWithoutDispatching()
    {
        TrayMenuEntry entry = Assert.Single(_viewModel.PrinterItems);

        Assert.Equal("Starting…", entry.Header);
        Assert.False(entry.IsEnabled);
        Assert.Equal(0, _dispatcher.PostCount);
    }

    [Fact]
    public void FixedItemsOfferSettingsDiagnosticsAboutAndQuit()
    {
        Assert.Collection(
            _viewModel.FixedItems,
            item => AssertCommand(item, TrayMenuCommand.Settings, "Settings…", enabled: false),
            item => AssertCommand(item, TrayMenuCommand.Diagnostics, "Diagnostics…", enabled: false),
            item => Assert.True(item.IsSeparator),
            item => AssertCommand(item, TrayMenuCommand.About, "About", enabled: true),
            item => AssertCommand(item, TrayMenuCommand.Quit, "Quit", enabled: true));
    }

    [Fact]
    public void AvailableWindowsAreEnabled()
    {
        using TrayMenuViewModel viewModel = new(_monitor, _dispatcher, _launcher, new TrayMenuOptions(SettingsAvailable: true, DiagnosticsAvailable: true));

        Assert.All(viewModel.FixedItems.Where(item => !item.IsSeparator), item => Assert.True(item.IsEnabled));
    }

    [Fact]
    public void FixedItemsFollowUiCulture()
    {
        using CultureScope czech = new("cs-CZ");
        using TrayMenuViewModel viewModel = new(_monitor, _dispatcher, _launcher, TrayMenuOptions.Default);

        Assert.Equal(
            ["Nastavení…", "Diagnostika…", "O aplikaci", "Ukončit"],
            viewModel.FixedItems.Where(item => !item.IsSeparator).Select(item => item.Header));
    }

    [Fact]
    public void SubscribesToMonitorAndUnsubscribesOnDispose()
    {
        Assert.Equal(1, _monitor.SubscriberCount);

        _viewModel.Dispose();

        Assert.Equal(0, _monitor.SubscriberCount);
    }

    [Fact]
    public void NoPrintersShowsNotConfigured()
    {
        _viewModel.SetPrinters([]);
        _dispatcher.RunAll();

        TrayMenuEntry entry = Assert.Single(_viewModel.PrinterItems);
        Assert.Equal("No printer configured", entry.Header);
        Assert.False(entry.IsEnabled);
    }

    [Fact]
    public void ConfiguredPrintersAreListedInConfigurationOrder()
    {
        _viewModel.SetPrinters([Definition("a", "Alpha"), Definition("b", "Beta")]);
        _dispatcher.RunAll();

        Assert.Equal(["Alpha: Connecting…", "Beta: Connecting…"], _viewModel.PrinterItems.Select(item => item.Header));
        Assert.All(_viewModel.PrinterItems, item => Assert.Equal(TrayMenuCommand.OpenWeb, item.Command));
    }

    [Fact]
    public void PrusaLinkPrinterOpensItsWebInterface()
    {
        _viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();

        TrayMenuEntry entry = Assert.Single(_viewModel.PrinterItems);
        Assert.True(entry.IsEnabled);
        Assert.Equal(new Uri("http://192.168.1.10/"), entry.WebUri);
        Assert.Equal("Open PrusaLink in the browser", entry.ToolTip);
    }

    [Fact]
    public void PrusaConnectPrinterOpensPrusaConnect()
    {
        _viewModel.SetPrinters([new PrinterDefinition { Id = "c", Name = "XL", ConnectionType = ConnectionType.PrusaConnect, Address = "0b9d8a2c-uuid" }]);
        _dispatcher.RunAll();

        TrayMenuEntry entry = Assert.Single(_viewModel.PrinterItems);
        Assert.True(entry.IsEnabled);
        Assert.Equal(new Uri("https://connect.prusa3d.com/"), entry.WebUri);
        Assert.Equal("Open Prusa Connect in the browser", entry.ToolTip);
    }

    [Fact]
    public void PrinterWithInvalidAddressIsListedButCannotBeOpened()
    {
        _viewModel.SetPrinters([Definition() with { Address = "ftp://printer" }]);
        _dispatcher.RunAll();

        TrayMenuEntry entry = Assert.Single(_viewModel.PrinterItems);
        Assert.Equal("MK4: Connecting…", entry.Header);
        Assert.False(entry.IsEnabled);
        Assert.Null(entry.WebUri);
        Assert.Equal("The printer address is not valid", entry.ToolTip);
    }

    [Fact]
    public void SnapshotUpdatesOnlyItsEntryOnUiThread()
    {
        _viewModel.SetPrinters([Definition("a", "Alpha"), Definition("b", "Beta")]);
        _dispatcher.RunAll();
        _changes.Clear();

        _monitor.Raise(Snapshot(PrinterState.Printing, Job(42), printerId: "b"));

        Assert.Empty(_changes);
        Assert.Equal(1, _dispatcher.Pending);

        _dispatcher.RunAll();

        NotifyCollectionChangedEventArgs change = Assert.Single(_changes);
        Assert.Equal(NotifyCollectionChangedAction.Replace, change.Action);
        Assert.Equal(1, change.NewStartingIndex);
        Assert.Equal(["Alpha: Connecting…", "Beta: Printing – 42%"], _viewModel.PrinterItems.Select(item => item.Header));
    }

    [Fact]
    public void ProgressIsShownForPausedAndAttentionWithJob()
    {
        _viewModel.SetPrinters([Definition("a", "Alpha"), Definition("b", "Beta"), Definition("c", "Gamma")]);
        _monitor.Raise(Snapshot(PrinterState.Paused, Job(30.9), printerId: "a"));
        _monitor.Raise(Snapshot(PrinterState.Attention, Job(99.99), printerId: "b"));
        _monitor.Raise(Snapshot(PrinterState.Finished, Job(100), printerId: "c"));
        _dispatcher.RunAll();

        Assert.Equal(
            ["Alpha: Paused – 30%", "Beta: Needs attention – 99%", "Gamma: Finished"],
            _viewModel.PrinterItems.Select(item => item.Header));
    }

    [Fact]
    public void UnreachablePrinterShowsNoJobData()
    {
        _viewModel.SetPrinters([Definition()]);
        _monitor.Raise(Snapshot(PrinterState.Offline, Job(50)));
        _dispatcher.RunAll();

        Assert.Equal("MK4: Offline", Assert.Single(_viewModel.PrinterItems).Header);
    }

    [Fact]
    public void CzechHeaderUsesCzechStateAndProgress()
    {
        using CultureScope czech = new("cs-CZ");
        _viewModel.SetPrinters([Definition()]);
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(42)));
        _dispatcher.RunAll();

        Assert.Equal("MK4: Tiskne – 42 %", Assert.Single(_viewModel.PrinterItems).Header);
    }

    [Fact]
    public void UnchangedVisibleContentDoesNotDispatch()
    {
        _viewModel.SetPrinters([Definition()]);
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(42.1, TimeSpan.FromMinutes(30), "a.bgcode")));
        _dispatcher.RunAll();
        int posts = _dispatcher.PostCount;
        _changes.Clear();

        // Different timestamp, fraction of a percent, remaining time and file name: none of it is shown in the menu.
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(42.9, TimeSpan.FromMinutes(29), "b.bgcode")) with { Timestamp = Now.AddSeconds(5) });

        Assert.Equal(posts, _dispatcher.PostCount);
        Assert.Empty(_changes);
    }

    [Fact]
    public void BurstOfSnapshotsIsCoalescedIntoOneUiUpdateWithLatestState()
    {
        _viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();
        int posts = _dispatcher.PostCount;
        _changes.Clear();

        _monitor.Raise(Snapshot(PrinterState.Printing, Job(10)));
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(20)));
        _monitor.Raise(Snapshot(PrinterState.Paused, Job(30)));

        Assert.Equal(posts + 1, _dispatcher.PostCount);
        _dispatcher.RunAll();
        Assert.Single(_changes);
        Assert.Equal("MK4: Paused – 30%", Assert.Single(_viewModel.PrinterItems).Header);
    }

    [Fact]
    public void ChangeThatEndsWhereItStartedTouchesNothing()
    {
        _viewModel.SetPrinters([Definition()]);
        _monitor.Raise(Snapshot(PrinterState.Idle));
        _dispatcher.RunAll();
        _changes.Clear();

        _monitor.Raise(Snapshot(PrinterState.Busy));
        _monitor.Raise(Snapshot(PrinterState.Idle));
        _dispatcher.RunAll();

        Assert.Empty(_changes);
    }

    [Fact]
    public void SnapshotOfUnknownPrinterIsIgnored()
    {
        _viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();
        int posts = _dispatcher.PostCount;

        _monitor.Raise(Snapshot(PrinterState.Error, printerId: "other"));

        Assert.Equal(posts, _dispatcher.PostCount);
    }

    [Fact]
    public void SnapshotBeforePrintersAreKnownIsIgnored()
    {
        _monitor.Raise(Snapshot(PrinterState.Error));

        Assert.Equal(0, _dispatcher.PostCount);
    }

    [Fact]
    public void PrinterIdMatchIsCaseInsensitive()
    {
        _viewModel.SetPrinters([Definition(id: "MK4")]);
        _monitor.Raise(Snapshot(PrinterState.Finished, printerId: "mk4"));
        _dispatcher.RunAll();

        Assert.Equal("MK4: Finished", Assert.Single(_viewModel.PrinterItems).Header);
    }

    [Fact]
    public void NewConfigurationReplacesOnlyChangedEntries()
    {
        _viewModel.SetPrinters([Definition("a", "Alpha"), Definition("b", "Beta"), Definition("c", "Gamma")]);
        _dispatcher.RunAll();
        _changes.Clear();

        _viewModel.SetPrinters([Definition("a", "Alpha"), Definition("d", "Delta")]);
        _dispatcher.RunAll();

        Assert.Equal(["Alpha: Connecting…", "Delta: Connecting…"], _viewModel.PrinterItems.Select(item => item.Header));
        Assert.Collection(
            _changes,
            change => Assert.Equal((NotifyCollectionChangedAction.Replace, 1), (change.Action, change.NewStartingIndex)),
            change => Assert.Equal((NotifyCollectionChangedAction.Remove, 2), (change.Action, change.OldStartingIndex)));
    }

    [Fact]
    public void MonitoringFailureReplacesPrintersUntilPrintersAreSetAgain()
    {
        _viewModel.SetPrinters([Definition()]);
        _monitor.Raise(Snapshot(PrinterState.Idle));
        _viewModel.ReportMonitoringFailed();
        _dispatcher.RunAll();

        TrayMenuEntry entry = Assert.Single(_viewModel.PrinterItems);
        Assert.Equal("Monitoring stopped due to an error", entry.Header);
        Assert.False(entry.IsEnabled);

        // Late snapshots of the failed run must not bring stale printer entries back.
        int posts = _dispatcher.PostCount;
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(5)));
        Assert.Equal(posts, _dispatcher.PostCount);

        _viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();

        Assert.Equal("MK4: Connecting…", Assert.Single(_viewModel.PrinterItems).Header);
    }

    [Fact]
    public void LongPrinterNameIsShortened()
    {
        string name = new('x', 300);
        _viewModel.SetPrinters([Definition(name: name)]);
        _dispatcher.RunAll();

        string header = Assert.Single(_viewModel.PrinterItems).Header;
        Assert.True(header.Length <= TrayMenuFormatter.MaxHeaderLength, $"Header has {header.Length} characters.");
        Assert.EndsWith(": Connecting…", header, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenWebLaunchesTheEntryAddress()
    {
        _viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();

        _viewModel.OpenWeb(_viewModel.PrinterItems[0]);

        Assert.Equal([new Uri("http://192.168.1.10/")], _launcher.Opened);
    }

    [Fact]
    public void OpenWebIgnoresEntryWithoutAddress()
    {
        _viewModel.OpenWeb(_viewModel.PrinterItems[0]);

        Assert.Empty(_launcher.Opened);
    }

    [Fact]
    public void OpenWebAfterDisposeIsIgnored()
    {
        _viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();
        TrayMenuEntry entry = _viewModel.PrinterItems[0];
        _viewModel.Dispose();

        _viewModel.OpenWeb(entry);

        Assert.Empty(_launcher.Opened);
    }

    [Fact]
    public void UpdatesAfterDisposeAreIgnored()
    {
        _viewModel.SetPrinters([Definition()]);
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(10)));
        _viewModel.Dispose();
        _changes.Clear();

        // An update that was already queued runs after disposal (e.g. while quitting).
        _dispatcher.RunAll();
        _viewModel.SetPrinters([Definition("x", "X")]);
        _dispatcher.RunAll();

        Assert.Empty(_changes);
    }

    [Fact]
    public async Task ConcurrentSnapshotsWhileUiDrainsLoseNoUpdate()
    {
        PrinterDefinition[] printers = [.. Enumerable.Range(0, 8).Select(i => Definition($"p{i}", $"P{i}"))];
        _viewModel.SetPrinters(printers);
        _dispatcher.RunAll();
        _changes.Clear();

        Task producers = Task.WhenAll(printers.Select(printer => Task.Run(
            () =>
            {
                for (int progress = 0; progress <= 100; progress++)
                {
                    _monitor.Raise(Snapshot(PrinterState.Printing, Job(progress), printer.Id));
                }
            },
            TestContext.Current.CancellationToken)));

        // This (test) thread plays the UI thread and applies updates while the snapshots keep coming.
        while (!producers.IsCompleted)
        {
            _dispatcher.RunAll();
            await Task.Yield();
        }

        await producers;
        _dispatcher.RunAll();

        Assert.Equal(
            printers.Select(printer => $"{printer.Name}: Printing – 100%"),
            _viewModel.PrinterItems.Select(item => item.Header));
        Assert.Equal(0, _dispatcher.Pending);

        // Every printer only ever moved forward: no update was applied out of order or overwritten by a stale one.
        Assert.All(_changes, change => Assert.Equal(NotifyCollectionChangedAction.Replace, change.Action));
        foreach (IGrouping<int, NotifyCollectionChangedEventArgs> perPrinter in _changes.GroupBy(change => change.NewStartingIndex))
        {
            int[] progress = [.. perPrinter.Select(change => ProgressOf(((TrayMenuEntry)change.NewItems![0]!).Header))];
            Assert.Equal(progress.Order(), progress);
            Assert.Equal(100, progress[^1]);
        }
    }

    private static int ProgressOf(string header)
    {
        int dash = header.LastIndexOf('–');
        return int.Parse(header.AsSpan(dash + 2, header.Length - dash - 3), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void AssertCommand(TrayMenuEntry item, TrayMenuCommand command, string header, bool enabled)
    {
        Assert.Equal(command, item.Command);
        Assert.Equal(header, item.Header);
        Assert.Equal(enabled, item.IsEnabled);
        Assert.False(item.IsSeparator);
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => _changes.Add(e);
}
