using System.Collections.Specialized;

using PrusaTrayDiagnostic.App.Tests.Settings;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.App.Tray.Menu;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

using static PrusaTrayDiagnostic.App.Tests.Tray.TestPrinters;

namespace PrusaTrayDiagnostic.App.Tests.Tray.Menu;

public sealed class TrayMenuViewModelTests : IDisposable
{
    private readonly CultureScope _culture = new("en-US");
    private readonly FakePrinterMonitor _monitor = new();
    private readonly ManualDispatcher _dispatcher = new();
    private readonly FakeUrlLauncher _launcher = new();
    private readonly FakeWebGateway _gateway = new();
    private readonly TrayMenuViewModel _viewModel;
    private readonly List<NotifyCollectionChangedEventArgs> _changes = [];

    public TrayMenuViewModelTests()
    {
        _viewModel = new TrayMenuViewModel(_monitor, _dispatcher, TestWebOpener.Create(_launcher, _gateway), TrayMenuOptions.Default);
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
    public void FixedItemsOfferSettingsAboutAndQuitWithAccessKeys()
    {
        // No general "Diagnostics…" any more (T-042): every printer submenu has its own.
        Assert.Collection(
            _viewModel.FixedItems,
            item => AssertCommand(item, TrayMenuCommand.Settings, "_Settings…", enabled: true),
            item => Assert.True(item.IsSeparator),
            item => AssertCommand(item, TrayMenuCommand.About, "_About", enabled: true),
            item => AssertCommand(item, TrayMenuCommand.Quit, "_Quit", enabled: true));
        Assert.All(_viewModel.FixedItems.Where(static item => !item.IsSeparator), static item => Assert.True(item.HasAccessKey));
    }

    [Fact]
    public void UnavailableWindowsAreDisabled()
    {
        using TrayMenuViewModel viewModel = new(_monitor, _dispatcher, TestWebOpener.Create(_launcher, _gateway), new TrayMenuOptions(SettingsAvailable: false, DiagnosticsAvailable: false));
        viewModel.SetPrinters([]);
        _dispatcher.RunAll();

        Assert.False(viewModel.FixedItems.Single(item => item.Command == TrayMenuCommand.Settings).IsEnabled);
        // "Add printer…" opens the settings window.
        Assert.False(viewModel.PrinterItems.Single(item => item.Command == TrayMenuCommand.AddPrinter).IsEnabled);
        Assert.DoesNotContain(viewModel.FixedItems, static item => item.Command == TrayMenuCommand.Diagnostics);
    }

    [Fact]
    public void FixedItemsFollowUiCulture()
    {
        using CultureScope czech = new("cs-CZ");
        using TrayMenuViewModel viewModel = new(_monitor, _dispatcher, TestWebOpener.Create(_launcher, _gateway), TrayMenuOptions.Default);

        Assert.Equal(
            ["_Nastavení…", "_O aplikaci", "U_končit"],
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
    public void NoPrintersShowsNotConfiguredAndOffersAddPrinter()
    {
        _viewModel.SetPrinters([]);
        _dispatcher.RunAll();

        Assert.Collection(
            _viewModel.PrinterItems,
            message =>
            {
                AssertCommand(message, TrayMenuCommand.None, "No printer configured", enabled: false);
                Assert.False(message.HasAccessKey);
            },
            add =>
            {
                AssertCommand(add, TrayMenuCommand.AddPrinter, "Add _printer…", enabled: true);
                Assert.True(add.HasAccessKey);
                Assert.Null(add.Submenu);
                Assert.Null(add.PrinterId);
            });
    }

    [Fact]
    public void AddPrinterIsOfferedOnlyWithoutPrinters()
    {
        // Starting and a failure show their message only.
        Assert.DoesNotContain(_viewModel.PrinterItems, static item => item.Command == TrayMenuCommand.AddPrinter);

        _viewModel.SetPrinters([]);
        _dispatcher.RunAll();
        Assert.Contains(_viewModel.PrinterItems, static item => item.Command == TrayMenuCommand.AddPrinter);

        _viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();
        Assert.DoesNotContain(_viewModel.PrinterItems, static item => item.Command == TrayMenuCommand.AddPrinter);

        _viewModel.SetPrinters([]);
        _viewModel.ReportMonitoringFailed();
        _dispatcher.RunAll();
        Assert.Equal("Monitoring stopped due to an error", Assert.Single(_viewModel.PrinterItems).Header);
    }

    [Fact]
    public void AddPrinterFollowsTheLanguage()
    {
        _viewModel.SetPrinters([]);
        _dispatcher.RunAll();

        using (new CultureScope("cs-CZ"))
        {
            _viewModel.RefreshTexts();
        }

        Assert.Equal(["Není nastavena žádná tiskárna", "_Přidat tiskárnu…"], _viewModel.PrinterItems.Select(static item => item.Header));
    }

    [Fact]
    public void ConfiguredPrintersAreListedInConfigurationOrder()
    {
        _viewModel.SetPrinters([Definition("a", "Alpha"), Definition("b", "Beta")]);
        _dispatcher.RunAll();

        Assert.Equal(["Alpha: Connecting…", "Beta: Connecting…"], _viewModel.PrinterItems.Select(item => item.Header));
        Assert.All(_viewModel.PrinterItems, item => Assert.NotNull(item.Submenu));
    }

    [Fact]
    public void SinglePrinterIsASubmenuLikeSeveralPrinters()
    {
        _viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();

        // T-042: the printer item only opens its submenu (before, a single printer item opened the web interface).
        TrayMenuEntry entry = Assert.Single(_viewModel.PrinterItems);
        Assert.Equal(TrayMenuCommand.None, entry.Command);
        Assert.True(entry.IsEnabled);
        Assert.Null(entry.WebUri);
        Assert.False(entry.HasAccessKey);
        Assert.Equal("mk4", entry.PrinterId);
        Assert.Equal([TrayMenuCommand.OpenWeb, TrayMenuCommand.Diagnostics], entry.Submenu!.Select(static item => item.Command));
        Assert.All(entry.Submenu!, static item => Assert.Equal(("mk4", true), (item.PrinterId, item.HasAccessKey)));
    }

    [Fact]
    public void SeveralPrintersAreSubmenusWithWebAndDiagnostics()
    {
        _viewModel.SetPrinters([Definition("a", "Alpha"), new PrinterDefinition { Id = "c", Name = "XL", ConnectionType = ConnectionType.PrusaConnect, Address = "uuid" }]);
        _dispatcher.RunAll();

        TrayMenuEntry alpha = _viewModel.PrinterItems[0];
        Assert.Equal(TrayMenuCommand.None, alpha.Command);
        Assert.True(alpha.IsEnabled);
        Assert.Equal("a", alpha.PrinterId);
        Assert.Null(alpha.WebUri);
        Assert.Collection(
            alpha.Submenu!,
            item =>
            {
                AssertCommand(item, TrayMenuCommand.OpenWeb, "_Open PrusaLink in the browser", enabled: true);
                Assert.Equal(new Uri("http://192.168.1.10/"), item.WebUri);
                Assert.Equal("a", item.PrinterId);
            },
            item =>
            {
                AssertCommand(item, TrayMenuCommand.Diagnostics, "_Diagnostics…", enabled: true);
                Assert.Equal("a", item.PrinterId);
            });

        TrayMenuEntry xl = _viewModel.PrinterItems[1];
        Assert.Equal("_Open Prusa Connect in the browser", xl.Submenu![0].Header);
        Assert.Equal(new Uri("https://connect.prusa3d.com/"), xl.Submenu[0].WebUri);
        Assert.Equal("c", xl.Submenu[1].PrinterId);
    }

    [Fact]
    public void SubmenuOfPrinterWithInvalidAddressStillOffersDiagnostics()
    {
        _viewModel.SetPrinters([Definition("a", "Alpha") with { Address = "ftp://printer" }, Definition("b", "Beta")]);
        _dispatcher.RunAll();

        TrayMenuEntry entry = _viewModel.PrinterItems[0];
        Assert.True(entry.IsEnabled);
        TrayMenuEntry web = entry.Submenu![0];
        Assert.False(web.IsEnabled);
        Assert.Null(web.WebUri);
        Assert.Equal("The printer address is not valid", web.ToolTip);
        Assert.True(entry.Submenu[1].IsEnabled);
    }

    [Fact]
    public void SubmenuDiagnosticsFollowsOptions()
    {
        using TrayMenuViewModel viewModel = new(_monitor, _dispatcher, TestWebOpener.Create(_launcher, _gateway), TrayMenuOptions.Default with { DiagnosticsAvailable = false });
        viewModel.SetPrinters([Definition("a", "Alpha"), Definition("b", "Beta")]);
        _dispatcher.RunAll();

        Assert.All(viewModel.PrinterItems, item => Assert.False(item.Submenu![1].IsEnabled));
    }

    [Fact]
    public void SubmenuFollowsUiCulture()
    {
        using CultureScope czech = new("cs-CZ");
        _viewModel.SetPrinters([Definition("a", "Alpha"), Definition("b", "Beta")]);
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(42), printerId: "b"));
        _dispatcher.RunAll();

        Assert.Equal("Beta: Tiskne – 42\u00A0%", _viewModel.PrinterItems[1].Header);
        Assert.Equal(["_Otevřít PrusaLink v prohlížeči", "_Diagnostika…"], _viewModel.PrinterItems[1].Submenu!.Select(item => item.Header));
    }

    [Fact]
    public void RefreshTextsFormatsEveryItemInTheNewLanguage()
    {
        _viewModel.SetPrinters([Definition("a", "Alpha"), Definition("b", "Beta")]);
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(42), printerId: "b"));
        _dispatcher.RunAll();
        int fixedChanges = 0;
        _viewModel.FixedItemsChanged += (_, _) => fixedChanges++;

        using (new CultureScope("cs-CZ"))
        {
            _viewModel.RefreshTexts();
        }

        // Applied right away (UI thread), not posted.
        Assert.Equal(1, fixedChanges);
        Assert.Equal(
            ["_Nastavení…", "_O aplikaci", "U_končit"],
            _viewModel.FixedItems.Where(item => !item.IsSeparator).Select(item => item.Header));
        Assert.Equal("Beta: Tiskne – 42\u00A0%", _viewModel.PrinterItems[1].Header);
        // The cached submenu is formatted again too.
        Assert.Equal(["_Otevřít PrusaLink v prohlížeči", "_Diagnostika…"], _viewModel.PrinterItems[1].Submenu!.Select(item => item.Header));
        Assert.Equal(["_Otevřít PrusaLink v prohlížeči", "_Diagnostika…"], _viewModel.PrinterItems[0].Submenu!.Select(item => item.Header));
    }

    [Fact]
    public void RefreshTextsTranslatesTheStatusMessage()
    {
        using (new CultureScope("cs-CZ"))
        {
            _viewModel.RefreshTexts();
        }

        Assert.Equal("Spouštění…", Assert.Single(_viewModel.PrinterItems).Header);
    }

    [Fact]
    public void RefreshTextsAfterDisposeDoesNothing()
    {
        int fixedChanges = 0;
        _viewModel.FixedItemsChanged += (_, _) => fixedChanges++;
        _viewModel.Dispose();

        _viewModel.RefreshTexts();

        Assert.Equal(0, fixedChanges);
    }

    [Fact]
    public void SubmenuIsReusedWhenOnlyTheStateChanges()
    {
        _viewModel.SetPrinters([Definition("a", "Alpha"), Definition("b", "Beta")]);
        _dispatcher.RunAll();
        TrayMenuEntryList before = _viewModel.PrinterItems[1].Submenu!;
        _changes.Clear();

        _monitor.Raise(Snapshot(PrinterState.Printing, Job(1), printerId: "b"));
        _dispatcher.RunAll();
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(2), printerId: "b"));
        _dispatcher.RunAll();

        Assert.Equal(2, _changes.Count);
        Assert.All(_changes, change => Assert.Equal((NotifyCollectionChangedAction.Replace, 1), (change.Action, change.NewStartingIndex)));
        Assert.Same(before, _viewModel.PrinterItems[1].Submenu);
    }

    [Fact]
    public void AddingASecondPrinterKeepsTheFirstOneAsItIs()
    {
        _viewModel.SetPrinters([Definition("a", "Alpha")]);
        _dispatcher.RunAll();
        TrayMenuEntry alpha = _viewModel.PrinterItems[0];
        _changes.Clear();

        _viewModel.SetPrinters([Definition("a", "Alpha"), Definition("b", "Beta")]);
        _dispatcher.RunAll();

        // The same structure for one and several printers (T-042): only the new printer is added.
        NotifyCollectionChangedEventArgs change = Assert.Single(_changes);
        Assert.Equal((NotifyCollectionChangedAction.Add, 1), (change.Action, change.NewStartingIndex));
        Assert.Equal(alpha, _viewModel.PrinterItems[0]);

        _changes.Clear();
        _viewModel.SetPrinters([Definition("a", "Alpha")]);
        _dispatcher.RunAll();
        change = Assert.Single(_changes);
        Assert.Equal((NotifyCollectionChangedAction.Remove, 1), (change.Action, change.OldStartingIndex));
    }

    [Fact]
    public void SubmenuEntriesCompareByValue()
    {
        _viewModel.SetPrinters([Definition("a", "Alpha"), Definition("b", "Beta")]);
        _dispatcher.RunAll();
        TrayMenuEntry entry = _viewModel.PrinterItems[0];

        TrayMenuEntry copy = entry with { Submenu = new TrayMenuEntryList([.. entry.Submenu!]) };
        TrayMenuEntry differentChild = entry with { Submenu = new TrayMenuEntryList([entry.Submenu![0], entry.Submenu[1] with { IsEnabled = false }]) };

        Assert.Equal(entry, copy);
        Assert.Equal(entry.GetHashCode(), copy.GetHashCode());
        Assert.NotEqual(entry, differentChild);
        Assert.NotEqual(entry, entry with { Submenu = null });
    }

    [Fact]
    public void PrusaLinkPrinterOpensItsWebInterface()
    {
        _viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();

        TrayMenuEntry web = Assert.Single(_viewModel.PrinterItems).Submenu![0];
        AssertCommand(web, TrayMenuCommand.OpenWeb, "_Open PrusaLink in the browser", enabled: true);
        Assert.Equal(new Uri("http://192.168.1.10/"), web.WebUri);
        Assert.Null(web.ToolTip);
    }

    [Fact]
    public void PrusaConnectPrinterOpensPrusaConnect()
    {
        _viewModel.SetPrinters([new PrinterDefinition { Id = "c", Name = "XL", ConnectionType = ConnectionType.PrusaConnect, Address = "0b9d8a2c-uuid" }]);
        _dispatcher.RunAll();

        TrayMenuEntry web = Assert.Single(_viewModel.PrinterItems).Submenu![0];
        AssertCommand(web, TrayMenuCommand.OpenWeb, "_Open Prusa Connect in the browser", enabled: true);
        Assert.Equal(new Uri("https://connect.prusa3d.com/"), web.WebUri);
    }

    [Fact]
    public void PrinterWithInvalidAddressIsListedButCannotBeOpened()
    {
        _viewModel.SetPrinters([Definition() with { Address = "ftp://printer" }]);
        _dispatcher.RunAll();

        TrayMenuEntry entry = Assert.Single(_viewModel.PrinterItems);
        Assert.Equal("MK4: Connecting…", entry.Header);
        // The submenu still offers diagnostics; only its web item is disabled, with the reason.
        Assert.True(entry.IsEnabled);
        Assert.Null(entry.ToolTip);
        TrayMenuEntry web = entry.Submenu![0];
        Assert.False(web.IsEnabled);
        Assert.Null(web.WebUri);
        Assert.Equal("The printer address is not valid", web.ToolTip);
        Assert.True(entry.Submenu[1].IsEnabled);
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

        Assert.Equal("MK4: Tiskne – 42\u00A0%", Assert.Single(_viewModel.PrinterItems).Header);
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
    public void AppliedSettingsShowPrintersAndClearFailure()
    {
        IMonitoringObserver observer = _viewModel;
        _viewModel.SetPrinters([Definition()]);
        _viewModel.ReportMonitoringFailed();
        _dispatcher.RunAll();

        observer.ApplySettings(new AppSettings { Printers = [Definition("a", "Alpha"), Definition("b", "Beta")] });
        _dispatcher.RunAll();

        Assert.Equal(["Alpha: Connecting…", "Beta: Connecting…"], _viewModel.PrinterItems.Select(static item => item.Header));
        Assert.Throws<ArgumentNullException>(() => observer.ApplySettings(null!));
    }

    [Fact]
    public void LongPrinterNameIsShortenedAndShownInFullInTheToolTip()
    {
        string name = new string('x', 40) + "_tail";
        _viewModel.SetPrinters([Definition(name: name)]);
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(42)));
        _dispatcher.RunAll();

        TrayMenuEntry entry = Assert.Single(_viewModel.PrinterItems);
        // 31 characters of the name and "…" (T-042: about 32 characters); the state is never shortened.
        Assert.Equal(new string('x', 31) + "…: Printing – 42%", entry.Header);
        Assert.Equal(name, entry.ToolTip);
    }

    [Theory]
    [InlineData(31)]
    [InlineData(32)]
    public void NameThatFitsIsShownWholeWithoutToolTip(int length)
    {
        string name = new('n', length);
        _viewModel.SetPrinters([Definition(name: name)]);
        _dispatcher.RunAll();

        TrayMenuEntry entry = Assert.Single(_viewModel.PrinterItems);
        Assert.Equal(name + ": Connecting…", entry.Header);
        Assert.Null(entry.ToolTip);
    }

    [Fact]
    public void NameOfThirtyThreeCharactersIsShortenedToThirtyTwo()
    {
        string name = new('n', 33);
        _viewModel.SetPrinters([Definition(name: name)]);
        _dispatcher.RunAll();

        TrayMenuEntry entry = Assert.Single(_viewModel.PrinterItems);
        Assert.Equal(new string('n', 31) + "…: Connecting…", entry.Header);
        Assert.Equal(name, entry.ToolTip);
    }

    [Fact]
    public async Task OpenWebOpensTheProxyAddressOfThePrinter()
    {
        _viewModel.SetPrinters([Definition() with { UserName = "admin" }]);
        _dispatcher.RunAll();

        _viewModel.OpenWeb(_viewModel.PrinterItems[0].Submenu![0]);
        await _launcher.WaitForOpenedAsync(1);

        Assert.Equal([FakeWebGateway.ProxyAddress], _launcher.OpenedSecret);
        Assert.Empty(_launcher.Opened);
        PrinterDefinition asked = Assert.Single(_gateway.Opened);
        Assert.Equal("mk4", asked.Id);
        Assert.Equal("admin", asked.UserName);
    }

    [Fact]
    public async Task OpenWebFallbackOpensThePlainAddress()
    {
        _gateway.Open = static (_, _) => Task.FromResult(Core.Web.PrinterWebAccess.Plain(new Uri("http://192.168.1.10/"), Core.Web.PrinterWebFallback.PasswordRejected));
        _viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();

        _viewModel.OpenWeb(_viewModel.PrinterItems[0].Submenu![0]);
        await _launcher.WaitForOpenedAsync(1);

        Assert.Equal([new Uri("http://192.168.1.10/")], _launcher.Opened);
        Assert.Empty(_launcher.OpenedSecret);
    }

    [Fact]
    public async Task OpenWebFromSubmenuAsksForThatPrinter()
    {
        _viewModel.SetPrinters([Definition("a", "Alpha"), Definition("b", "Beta") with { Address = "192.168.1.11" }]);
        _dispatcher.RunAll();

        _viewModel.OpenWeb(_viewModel.PrinterItems[1].Submenu![0]);
        await _launcher.WaitForOpenedAsync(1);
        _viewModel.OpenWeb(_viewModel.PrinterItems[0].Submenu![0]);
        await _launcher.WaitForOpenedAsync(2);

        Assert.Equal(["b", "a"], _gateway.Opened.Select(static printer => printer.Id));
        Assert.Equal("192.168.1.11", _gateway.Opened[0].Address);
    }

    [Fact]
    public async Task OpenWebFromAStaleEntryNeverUsesTheGateway()
    {
        _viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();
        TrayMenuEntry stale = _viewModel.PrinterItems[0].Submenu![0];
        // The printer moved: the old entry must not open a logged-in proxy for the printer's new definition.
        _viewModel.SetPrinters([Definition() with { Address = "192.168.1.99" }]);

        _viewModel.OpenWeb(stale);
        await _launcher.WaitForOpenedAsync(1);

        Assert.Empty(_gateway.Opened);
        Assert.Empty(_launcher.OpenedSecret);
        Assert.Equal([new Uri("http://192.168.1.10/")], _launcher.Opened);
    }

    [Fact]
    public async Task OpenWebWithFailingGatewayOpensPlainAddress()
    {
        _gateway.Open = static (_, _) => throw new InvalidOperationException("broken");
        _viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();

        _viewModel.OpenWeb(_viewModel.PrinterItems[0].Submenu![0]);
        await _launcher.WaitForOpenedAsync(1);

        Assert.Equal([new Uri("http://192.168.1.10/")], _launcher.Opened);
        Assert.Empty(_launcher.OpenedSecret);
    }

    [Fact]
    public async Task DisposeCancelsAnOpenWaitingForTheGateway()
    {
        TaskCompletionSource<CancellationToken> entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeWebGateway gateway = new()
        {
            Open = async (_, token) =>
            {
                entered.TrySetResult(token);
                await release.Task.ConfigureAwait(false);
                return Core.Web.PrinterWebAccess.Proxied(FakeWebGateway.ProxyAddress);
            },
        };
        using TrayMenuViewModel viewModel = new(_monitor, _dispatcher, TestWebOpener.Create(_launcher, gateway), TrayMenuOptions.Default);
        viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();

        viewModel.OpenWeb(viewModel.PrinterItems[0].Submenu![0]);
        CancellationToken openToken = await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.False(openToken.IsCancellationRequested);
        viewModel.Dispose();

        Assert.True(openToken.IsCancellationRequested);
        release.SetResult();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Empty(_launcher.Opened);
        Assert.Empty(_launcher.OpenedSecret);
    }

    [Fact]
    public void OpenWebIgnoresEntryWithoutAddress()
    {
        _viewModel.OpenWeb(_viewModel.PrinterItems[0]);
        _viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();
        // The printer item itself only opens its submenu.
        _viewModel.OpenWeb(_viewModel.PrinterItems[0]);

        Assert.Empty(_launcher.Opened);
        Assert.Empty(_launcher.OpenedSecret);
    }

    [Fact]
    public void OpenWebAfterDisposeIsIgnored()
    {
        _viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();
        TrayMenuEntry entry = _viewModel.PrinterItems[0].Submenu![0];
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
