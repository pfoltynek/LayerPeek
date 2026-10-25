using System.ComponentModel;

using LayerPeek.App.Tray;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Tray;

public sealed class TrayStatusViewModelTests : IDisposable
{
    private readonly CultureScope _culture = new("en-US");
    private readonly FakePrinterMonitor _monitor = new();
    private readonly ManualDispatcher _dispatcher = new();
    private readonly TrayStatusViewModel _viewModel;
    private readonly List<string?> _changes = [];

    public TrayStatusViewModelTests()
    {
        _viewModel = new TrayStatusViewModel(_monitor, _dispatcher);
        _viewModel.PropertyChanged += OnPropertyChanged;
    }

    public void Dispose()
    {
        _viewModel.PropertyChanged -= OnPropertyChanged;
        _viewModel.Dispose();
        _culture.Dispose();
    }

    [Fact]
    public void StartsInStartingStateWithoutDispatching()
    {
        Assert.Equal(TrayIconKey.For(TrayStatus.Offline, null), _viewModel.IconKey);
        Assert.Equal("LayerPeek\nStarting…", _viewModel.ToolTipText);
        Assert.Equal(0, _dispatcher.PostCount);
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

        Assert.Equal(TrayIconKey.For(TrayStatus.Offline, null), _viewModel.IconKey);
        Assert.Equal("LayerPeek\nNo printer configured", _viewModel.ToolTipText);
    }

    [Fact]
    public void ConfiguredPrinterIsConnectingUntilFirstSnapshot()
    {
        _viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();

        Assert.Equal("MK4\nConnecting…", _viewModel.ToolTipText);
    }

    [Fact]
    public void SnapshotUpdatesIconAndToolTipOnUiThreadOnly()
    {
        _viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();
        _changes.Clear();

        _monitor.Raise(Snapshot(PrinterState.Printing, Job(42, TimeSpan.FromMinutes(10))));

        // Nothing changes until the dispatcher runs the posted update.
        Assert.Empty(_changes);
        Assert.Equal(1, _dispatcher.Pending);

        _dispatcher.RunAll();

        Assert.Equal(TrayIconKey.For(TrayStatus.Printing, 42), _viewModel.IconKey);
        Assert.Equal("MK4\nPrinting – 42%\n10\u00A0min remaining\nbenchy.bgcode", _viewModel.ToolTipText);
        Assert.Equal([nameof(TrayStatusViewModel.IconKey), nameof(TrayStatusViewModel.ToolTipText)], _changes);
    }

    [Fact]
    public void UnchangedPresentationDoesNotDispatch()
    {
        _viewModel.SetPrinters([Definition()]);
        _monitor.Raise(Snapshot(PrinterState.Idle));
        _dispatcher.RunAll();
        int posts = _dispatcher.PostCount;
        _changes.Clear();

        // A new poll with a different timestamp but the same visible content.
        _monitor.Raise(Snapshot(PrinterState.Idle) with { Timestamp = Now.AddSeconds(5) });

        Assert.Equal(posts, _dispatcher.PostCount);
        Assert.Empty(_changes);
    }

    [Fact]
    public void ProgressWithinStepChangesOnlyToolTip()
    {
        _viewModel.SetPrinters([Definition()]);
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(41)));
        _dispatcher.RunAll();
        _changes.Clear();

        _monitor.Raise(Snapshot(PrinterState.Printing, Job(43)));
        _dispatcher.RunAll();

        Assert.Equal([nameof(TrayStatusViewModel.ToolTipText)], _changes);
    }

    [Fact]
    public void BurstOfSnapshotsIsCoalescedIntoOneUiUpdateWithLatestState()
    {
        _viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();
        int posts = _dispatcher.PostCount;

        _monitor.Raise(Snapshot(PrinterState.Printing, Job(10)));
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(20)));
        _monitor.Raise(Snapshot(PrinterState.Paused, Job(30)));

        Assert.Equal(posts + 1, _dispatcher.PostCount);
        _dispatcher.RunAll();
        Assert.Equal(TrayIconKey.For(TrayStatus.Paused, 30), _viewModel.IconKey);
        Assert.StartsWith("MK4\nPaused – 30%", _viewModel.ToolTipText, StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotOfUnknownPrinterIsIgnored()
    {
        _viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();
        int posts = _dispatcher.PostCount;

        _monitor.Raise(Snapshot(PrinterState.Error, printerId: "other"));

        Assert.Equal(posts, _dispatcher.PostCount);
        Assert.Equal("MK4\nConnecting…", _viewModel.ToolTipText);
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

        Assert.Equal(TrayIconKey.For(TrayStatus.Finished, null), _viewModel.IconKey);
    }

    [Fact]
    public void MostSeverePrinterDrivesIcon()
    {
        _viewModel.SetPrinters([Definition("a", "Alpha"), Definition("b", "Beta")]);
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(50), printerId: "a"));
        _monitor.Raise(Snapshot(PrinterState.Error, printerId: "b"));
        _dispatcher.RunAll();

        Assert.Equal(TrayIconKey.For(TrayStatus.Error, null), _viewModel.IconKey);
        Assert.Equal("Beta: Error\nAlpha: Printing – 50%", _viewModel.ToolTipText);
    }

    [Fact]
    public void SetPrintersDropsSnapshotsOfPreviousConfiguration()
    {
        _viewModel.SetPrinters([Definition()]);
        _monitor.Raise(Snapshot(PrinterState.Error));
        _dispatcher.RunAll();

        _viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();

        Assert.Equal("MK4\nConnecting…", _viewModel.ToolTipText);
    }

    [Fact]
    public void MonitoringFailureShowsErrorUntilPrintersAreSetAgain()
    {
        _viewModel.SetPrinters([Definition()]);
        _monitor.Raise(Snapshot(PrinterState.Idle));
        _viewModel.ReportMonitoringFailed();
        _dispatcher.RunAll();

        Assert.Equal(TrayIconKey.For(TrayStatus.Error, null), _viewModel.IconKey);
        Assert.Equal("LayerPeek\nMonitoring stopped due to an error", _viewModel.ToolTipText);

        _viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();

        Assert.Equal("MK4\nConnecting…", _viewModel.ToolTipText);
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

        Assert.Equal("Alpha: Connecting…\nBeta: Connecting…", _viewModel.ToolTipText);
        Assert.Throws<ArgumentNullException>(() => observer.ApplySettings(null!));
    }

    [Fact]
    public void SnapshotAfterDisposeIsIgnored()
    {
        _viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();
        int posts = _dispatcher.PostCount;
        _viewModel.Dispose();

        _viewModel.SetPrinters([Definition("x", "X")]);
        _monitor.Raise(Snapshot(PrinterState.Error));

        Assert.Equal(posts, _dispatcher.PostCount);
    }

    [Fact]
    public async Task ConcurrentSnapshotsEndInConsistentState()
    {
        PrinterDefinition[] printers = [.. Enumerable.Range(0, 8).Select(i => Definition($"p{i}", $"P{i}"))];
        _viewModel.SetPrinters(printers);

        await Task.WhenAll(printers.Select(printer => Task.Run(
            () =>
            {
                for (int progress = 0; progress <= 100; progress++)
                {
                    _monitor.Raise(Snapshot(PrinterState.Printing, Job(progress), printer.Id));
                }
            },
            TestContext.Current.CancellationToken)));
        _dispatcher.RunAll();

        Assert.Equal(TrayIconKey.For(TrayStatus.Printing, 100), _viewModel.IconKey);
        Assert.StartsWith("P0: Printing – 100%\nP1: Printing – 100%\n", _viewModel.ToolTipText, StringComparison.Ordinal);
    }

    [Fact]
    public void RefreshTextsFormatsTheToolTipInTheNewLanguage()
    {
        _viewModel.SetPrinters([Definition()]);
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(42, TimeSpan.FromMinutes(10))));
        _dispatcher.RunAll();
        _changes.Clear();

        using (new CultureScope("cs-CZ"))
        {
            _viewModel.RefreshTexts();
            _dispatcher.RunAll();
        }

        Assert.Equal("MK4\nTiskne – 42\u00A0%\nZbývá 10\u00A0min\nbenchy.bgcode", _viewModel.ToolTipText);
        Assert.Equal([nameof(TrayStatusViewModel.ToolTipText)], _changes);
    }

    [Fact]
    public void RefreshTextsCoversStatusMessagesAndTheSummary()
    {
        using (new CultureScope("cs-CZ"))
        {
            _viewModel.RefreshTexts();
            _dispatcher.RunAll();
        }

        Assert.Equal("LayerPeek\nSpouštění…", _viewModel.ToolTipText);

        _viewModel.SetPrinters([Definition("a", "Alpha"), Definition("b", "Beta")]);
        _monitor.Raise(Snapshot(PrinterState.Error, printerId: "a"));
        _monitor.Raise(Snapshot(PrinterState.Idle, printerId: "b"));
        _dispatcher.RunAll();
        using (new CultureScope("cs-CZ"))
        {
            _viewModel.RefreshTexts();
            _dispatcher.RunAll();
        }

        Assert.Equal("Alpha: Chyba\nBeta: Připravena", _viewModel.ToolTipText);
    }

    [Fact]
    public void RefreshTextsAfterDisposeDoesNothing()
    {
        _viewModel.Dispose();

        _viewModel.RefreshTexts();

        Assert.Equal(0, _dispatcher.Pending);
    }

    [Fact]
    public void VisibleChangeOfNonPrimaryPrinterUpdatesOnlyToolTip()
    {
        _viewModel.SetPrinters([Definition("a", "Alpha"), Definition("b", "Beta")]);
        _monitor.Raise(Snapshot(PrinterState.Error, printerId: "a"));
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(10), printerId: "b"));
        _dispatcher.RunAll();
        _changes.Clear();

        _monitor.Raise(Snapshot(PrinterState.Printing, Job(11), printerId: "b"));
        _dispatcher.RunAll();

        Assert.Equal([nameof(TrayStatusViewModel.ToolTipText)], _changes);
        Assert.Equal(TrayIconKey.For(TrayStatus.Error, null), _viewModel.IconKey);
        Assert.Equal("Alpha: Error\nBeta: Printing – 11%", _viewModel.ToolTipText);
    }

    [Fact]
    public void InvisibleChangeOfAnyPrinterDoesNotDispatch()
    {
        _viewModel.SetPrinters([Definition("a", "Alpha"), Definition("b", "Beta")]);
        _monitor.Raise(Snapshot(PrinterState.Idle, printerId: "a"));
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(10.2, TimeSpan.FromSeconds(570)), printerId: "b"));
        _dispatcher.RunAll();
        int posts = _dispatcher.PostCount;

        _monitor.Raise(Snapshot(PrinterState.Idle, printerId: "a") with { Timestamp = Now.AddSeconds(5) });
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(10.7, TimeSpan.FromSeconds(600)), printerId: "b") with { Timestamp = Now.AddSeconds(5) });

        Assert.Equal(posts, _dispatcher.PostCount);
    }

    [Fact]
    public void RemainingTimeOfAnyPrinterIsShownInSummary()
    {
        _viewModel.SetPrinters([Definition("a", "Alpha"), Definition("b", "Beta")]);
        _monitor.Raise(Snapshot(PrinterState.Idle, printerId: "a"));
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(10, TimeSpan.FromMinutes(10)), printerId: "b"));
        _dispatcher.RunAll();

        _monitor.Raise(Snapshot(PrinterState.Printing, Job(10, TimeSpan.FromMinutes(9)), printerId: "b"));
        _dispatcher.RunAll();

        Assert.Equal("Beta: Printing – 10%, 9\u00A0min left\nAlpha: Ready", _viewModel.ToolTipText);
    }

    [Fact]
    public void ChangeBackAndForthWithinOneBurstEndsWithTheSameText()
    {
        _viewModel.SetPrinters([Definition("a", "Alpha"), Definition("b", "Beta")]);
        _monitor.Raise(Snapshot(PrinterState.Idle, printerId: "a"));
        _monitor.Raise(Snapshot(PrinterState.Idle, printerId: "b"));
        _dispatcher.RunAll();
        _changes.Clear();

        _monitor.Raise(Snapshot(PrinterState.Busy, printerId: "b"));
        _monitor.Raise(Snapshot(PrinterState.Idle, printerId: "b"));
        _dispatcher.RunAll();

        // A burst may post one update, but nothing visible changes.
        Assert.Empty(_changes);
        Assert.Equal("Alpha: Ready\nBeta: Ready", _viewModel.ToolTipText);
    }

    [Fact]
    public void ToolTipOfTwentyPrintersFitsWindowsLimit()
    {
        PrinterDefinition[] printers = [.. Enumerable.Range(0, 20).Select(i => Definition($"p{i}", $"Printer {i}"))];
        _viewModel.SetPrinters(printers);
        foreach (PrinterDefinition printer in printers)
        {
            _monitor.Raise(Snapshot(PrinterState.Printing, Job(50, TimeSpan.FromHours(2)), printer.Id));
        }

        _dispatcher.RunAll();

        Assert.True(_viewModel.ToolTipText.Length <= TrayToolTipFormatter.MaxLength);
        Assert.StartsWith("Printer 0: Printing – 50%\n", _viewModel.ToolTipText, StringComparison.Ordinal);
        Assert.Matches(@"\nOther printers: \d+$", _viewModel.ToolTipText);
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e) => _changes.Add(e.PropertyName);
}
