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
    private readonly FakeSecretStore _secrets = new();
    private readonly TrayMenuViewModel _viewModel;
    private readonly List<NotifyCollectionChangedEventArgs> _changes = [];

    public TrayMenuViewModelTests()
    {
        _viewModel = new TrayMenuViewModel(_monitor, _dispatcher, TestWebOpener.Create(_launcher, _secrets), TrayMenuOptions.Default);
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
            item => AssertCommand(item, TrayMenuCommand.Settings, "Settings…", enabled: true),
            item => AssertCommand(item, TrayMenuCommand.Diagnostics, "Diagnostics…", enabled: true),
            item => Assert.True(item.IsSeparator),
            item => AssertCommand(item, TrayMenuCommand.About, "About", enabled: true),
            item => AssertCommand(item, TrayMenuCommand.Quit, "Quit", enabled: true));
    }

    [Fact]
    public void UnavailableWindowsAreDisabled()
    {
        using TrayMenuViewModel viewModel = new(_monitor, _dispatcher, TestWebOpener.Create(_launcher, _secrets), new TrayMenuOptions(SettingsAvailable: false, DiagnosticsAvailable: false));

        Assert.False(viewModel.FixedItems.Single(item => item.Command == TrayMenuCommand.Settings).IsEnabled);
        Assert.False(viewModel.FixedItems.Single(item => item.Command == TrayMenuCommand.Diagnostics).IsEnabled);
    }

    [Fact]
    public void FixedItemsFollowUiCulture()
    {
        using CultureScope czech = new("cs-CZ");
        using TrayMenuViewModel viewModel = new(_monitor, _dispatcher, TestWebOpener.Create(_launcher, _secrets), TrayMenuOptions.Default);

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
        Assert.All(_viewModel.PrinterItems, item => Assert.NotNull(item.Submenu));
    }

    [Fact]
    public void SinglePrinterIsAFlatItemThatOpensTheWeb()
    {
        _viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();

        TrayMenuEntry entry = Assert.Single(_viewModel.PrinterItems);
        Assert.Equal(TrayMenuCommand.OpenWeb, entry.Command);
        Assert.Null(entry.Submenu);
        Assert.Equal("mk4", entry.PrinterId);
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
                AssertCommand(item, TrayMenuCommand.OpenWeb, "Open PrusaLink in the browser", enabled: true);
                Assert.Equal(new Uri("http://192.168.1.10/"), item.WebUri);
                Assert.Equal("a", item.PrinterId);
            },
            item =>
            {
                AssertCommand(item, TrayMenuCommand.Diagnostics, "Diagnostics…", enabled: true);
                Assert.Equal("a", item.PrinterId);
            });

        TrayMenuEntry xl = _viewModel.PrinterItems[1];
        Assert.Equal("Open Prusa Connect in the browser", xl.Submenu![0].Header);
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
        using TrayMenuViewModel viewModel = new(_monitor, _dispatcher, TestWebOpener.Create(_launcher, _secrets), TrayMenuOptions.Default with { DiagnosticsAvailable = false });
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

        Assert.Equal("Beta: Tiskne – 42 %", _viewModel.PrinterItems[1].Header);
        Assert.Equal(["Otevřít PrusaLink v prohlížeči", "Diagnostika…"], _viewModel.PrinterItems[1].Submenu!.Select(item => item.Header));
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
    public void GoingFromOneToSeveralPrintersSwitchesToSubmenusAndBack()
    {
        _viewModel.SetPrinters([Definition("a", "Alpha")]);
        _dispatcher.RunAll();
        Assert.Null(_viewModel.PrinterItems[0].Submenu);

        _viewModel.SetPrinters([Definition("a", "Alpha"), Definition("b", "Beta")]);
        _dispatcher.RunAll();
        Assert.All(_viewModel.PrinterItems, item => Assert.NotNull(item.Submenu));

        _viewModel.SetPrinters([Definition("b", "Beta")]);
        _dispatcher.RunAll();
        TrayMenuEntry single = Assert.Single(_viewModel.PrinterItems);
        Assert.Null(single.Submenu);
        Assert.Equal(TrayMenuCommand.OpenWeb, single.Command);
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
    public async Task OpenWebLaunchesTheEntryAddress()
    {
        _viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();

        _viewModel.OpenWeb(_viewModel.PrinterItems[0]);
        await _launcher.WaitForOpenedAsync(1);

        Assert.Equal([new Uri("http://192.168.1.10/")], _launcher.Opened);
    }

    [Fact]
    public async Task OpenWebLogsInWithTheStoredPasswordAndUserName()
    {
        _secrets.Seed("mk4", "pw");
        _viewModel.SetPrinters([Definition() with { UserName = "admin" }]);
        _dispatcher.RunAll();

        _viewModel.OpenWeb(_viewModel.PrinterItems[0]);
        await _launcher.WaitForOpenedAsync(1);

        Assert.Equal([(new Uri("http://192.168.1.10/"), "admin", "pw")], _launcher.OpenedWithCredentials);
        Assert.Empty(_launcher.Opened);
    }

    [Fact]
    public async Task OpenWebFromSubmenuLogsInAsThatPrinter()
    {
        _secrets.Seed("b", "pw-b");
        _viewModel.SetPrinters([Definition("a", "Alpha"), Definition("b", "Beta") with { Address = "192.168.1.11" }]);
        _dispatcher.RunAll();

        _viewModel.OpenWeb(_viewModel.PrinterItems[1].Submenu![0]);
        _viewModel.OpenWeb(_viewModel.PrinterItems[0].Submenu![0]);
        await _launcher.WaitForOpenedAsync(2);

        Assert.Equal([(new Uri("http://192.168.1.11/"), "maker", "pw-b")], _launcher.OpenedWithCredentials);
        Assert.Equal([new Uri("http://192.168.1.10/")], _launcher.Opened);
    }

    [Fact]
    public async Task OpenWebFromAStaleEntryNeverSendsCredentials()
    {
        _secrets.Seed("mk4", "pw");
        _viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();
        TrayMenuEntry stale = _viewModel.PrinterItems[0];
        // The printer moved: the old entry must not send the password to the old address.
        _viewModel.SetPrinters([Definition() with { Address = "192.168.1.99" }]);

        _viewModel.OpenWeb(stale);
        await _launcher.WaitForOpenedAsync(1);

        Assert.Empty(_launcher.OpenedWithCredentials);
        Assert.Equal([new Uri("http://192.168.1.10/")], _launcher.Opened);
    }

    [Fact]
    public async Task OpenWebWithFailingSecretStoreOpensPlainAddress()
    {
        _secrets.FailGet.Add(Core.Secrets.SecretKeys.ForPrinter("mk4"));
        _viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();

        _viewModel.OpenWeb(_viewModel.PrinterItems[0]);
        await _launcher.WaitForOpenedAsync(1);

        Assert.Equal([new Uri("http://192.168.1.10/")], _launcher.Opened);
        Assert.Empty(_launcher.OpenedWithCredentials);
    }

    [Fact]
    public async Task DisposeCancelsAnOpenWaitingForThePassword()
    {
        BlockingSecretStore secrets = new();
        using TrayMenuViewModel viewModel = new(_monitor, _dispatcher, TestWebOpener.Create(_launcher, secrets), TrayMenuOptions.Default);
        viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();

        viewModel.OpenWeb(viewModel.PrinterItems[0]);
        CancellationToken readToken = await secrets.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.False(readToken.IsCancellationRequested);
        viewModel.Dispose();

        Assert.True(readToken.IsCancellationRequested);
        secrets.Release.SetResult();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Empty(_launcher.Opened);
        Assert.Empty(_launcher.OpenedWithCredentials);
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

    /// <summary>Holds every password read until <see cref="Release"/> is set; reports the token of the read.</summary>
    private sealed class BlockingSecretStore : Core.Secrets.ISecretStore
    {
        public TaskCompletionSource<CancellationToken> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<string?> GetAsync(string key, CancellationToken cancellationToken)
        {
            Entered.TrySetResult(cancellationToken);
            await Release.Task.ConfigureAwait(false);
            return "pw";
        }

        public Task SetAsync(string key, string secret, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
