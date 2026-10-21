using System.ComponentModel;

using PrusaTrayDiagnostic.App.Diagnostics;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.Core.History;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;
using PrusaTrayDiagnostic.PrusaLink;

using static PrusaTrayDiagnostic.App.Tests.Tray.TestPrinters;

namespace PrusaTrayDiagnostic.App.Tests.Diagnostics;

public sealed class DiagnosticsViewModelTests : IDisposable
{
    private const string Missing = "–";

    private static readonly PrusaLinkPrinterInfo CoreOneInfo = new(
        Hostname: "prusa-core-one",
        Firmware: "6.9.2+10123",
        ApiVersion: "2.0.0",
        PrinterModel: "7.0.0",
        NozzleDiameter: 0.4,
        HasMmu: true);

    private readonly CultureScope _culture = new("en-US");
    private readonly FakePrinterMonitor _monitor = new();
    private readonly MonitoredPrinters _printers = new();
    private readonly TelemetryHistory _history = new();
    private readonly ManualDispatcher _dispatcher = new();
    private readonly FakePrinterInfoSource _info = new();
    private readonly FixedClock _clock = new(Now);
    private DiagnosticsViewModel? _viewModel;

    public void Dispose()
    {
        _viewModel?.Dispose();
        _info.CancelPending();
        _culture.Dispose();
    }

    [Fact]
    public void BeforeMonitoringStartsAStartingNoticeIsShown()
    {
        DiagnosticsViewModel viewModel = Create();

        Assert.Equal("Starting…", viewModel.Notice);
        Assert.True(viewModel.HasNotice);
        Assert.False(viewModel.HasPrinter);
        Assert.Empty(viewModel.Printers);
        Assert.Null(viewModel.ChartPrinterId);
    }

    [Fact]
    public void WithoutConfiguredPrintersANoticeIsShown()
    {
        Configure();

        DiagnosticsViewModel viewModel = Create();

        Assert.Equal("No printer configured", viewModel.Notice);
        Assert.False(viewModel.HasPrinter);
        Assert.Empty(_info.Requests);
    }

    [Fact]
    public void SinglePrinterIsSelectedWithoutASelector()
    {
        Configure(Definition());

        DiagnosticsViewModel viewModel = Create();

        Assert.Equal(new PrinterChoice("mk4", "MK4"), viewModel.SelectedPrinter);
        Assert.False(viewModel.HasPrinterSelector);
        Assert.True(viewModel.HasPrinter);
        Assert.Null(viewModel.Notice);
        Assert.False(viewModel.HasNotice);
        Assert.Equal("mk4", viewModel.ChartPrinterId);
        Assert.Same(_history, viewModel.History);
        Assert.Same(_clock, viewModel.Clock);
    }

    [Fact]
    public void SeveralPrintersOfferASelectorAndStartWithTheFirst()
    {
        Configure(Definition("a", "Alpha"), Definition("b", "Beta"));

        DiagnosticsViewModel viewModel = Create();

        Assert.Equal([new PrinterChoice("a", "Alpha"), new PrinterChoice("b", "Beta")], viewModel.Printers);
        Assert.True(viewModel.HasPrinterSelector);
        Assert.Equal("a", viewModel.SelectedPrinter?.Id);
    }

    [Fact]
    public void OpeningForAPrinterSelectsItAndLoadsOnlyItsInfo()
    {
        Configure(Definition("a", "Alpha"), Definition("b", "Beta"));
        _monitor.Raise(Printing(nozzle: 120, printerId: "b"));

        DiagnosticsViewModel viewModel = Create(initialPrinterId: "B");

        Assert.Equal("b", viewModel.SelectedPrinter?.Id);
        Assert.Equal("b", viewModel.ChartPrinterId);
        Assert.Equal("120.0\u00A0°C", viewModel.NozzleActualText);
        Assert.Equal("b", Assert.Single(_info.Requests).Printer.Id);
    }

    [Fact]
    public void OpeningForAnUnknownPrinterFallsBackToTheFirst()
    {
        Configure(Definition("a", "Alpha"), Definition("b", "Beta"));

        DiagnosticsViewModel viewModel = Create(initialPrinterId: "gone");

        Assert.Equal("a", viewModel.SelectedPrinter?.Id);
    }

    [Fact]
    public void OpeningForAPrinterBeforeMonitoringStartsSelectsItOnceKnown()
    {
        DiagnosticsViewModel viewModel = Create(initialPrinterId: "b");

        Configure(Definition("a", "Alpha"), Definition("b", "Beta"));
        _dispatcher.RunAll();

        Assert.Equal("b", viewModel.SelectedPrinter?.Id);
        Assert.Equal("b", Assert.Single(_info.Requests).Printer.Id);
    }

    [Fact]
    public void ShowPrinterSwitchesTheOpenWindow()
    {
        Configure(Definition("a", "Alpha"), Definition("b", "Beta"));
        _monitor.Raise(Printing(nozzle: 130, printerId: "b"));
        DiagnosticsViewModel viewModel = Create();

        viewModel.ShowPrinter("b");

        Assert.Equal("b", viewModel.SelectedPrinter?.Id);
        Assert.Equal("130.0\u00A0°C", viewModel.NozzleActualText);
        Assert.Equal(["a", "b"], _info.Requests.Select(request => request.Printer.Id));
    }

    [Fact]
    public void ShowPrinterOfTheShownPrinterChangesNothing()
    {
        Configure(Definition("a", "Alpha"), Definition("b", "Beta"));
        DiagnosticsViewModel viewModel = Create();
        List<string?> changes = Track(viewModel);

        viewModel.ShowPrinter("A");

        Assert.Empty(changes);
        Assert.Single(_info.Requests);
    }

    [Fact]
    public void ShowPrinterOfAnUnknownPrinterKeepsTheSelection()
    {
        Configure(Definition("a", "Alpha"), Definition("b", "Beta"));
        DiagnosticsViewModel viewModel = Create();
        viewModel.ShowPrinter("b");

        viewModel.ShowPrinter("gone");

        Assert.Equal("b", viewModel.SelectedPrinter?.Id);
    }

    [Fact]
    public void ManualSelectionWinsOverAPendingMenuRequest()
    {
        Configure(Definition("a", "Alpha"), Definition("b", "Beta"));
        DiagnosticsViewModel viewModel = Create();
        // Requested from the menu of a printer this window does not know yet (its printer list is older).
        viewModel.ShowPrinter("c");

        viewModel.SelectedPrinter = viewModel.Printers[1];
        Configure(Definition("a", "Alpha"), Definition("b", "Beta"), Definition("c", "Gamma"));
        _dispatcher.RunAll();

        Assert.Equal("b", viewModel.SelectedPrinter?.Id);
    }

    [Fact]
    public void PendingMenuRequestIsAppliedWhenThePrinterArrives()
    {
        Configure(Definition("a", "Alpha"), Definition("b", "Beta"));
        DiagnosticsViewModel viewModel = Create();
        viewModel.ShowPrinter("c");

        Configure(Definition("a", "Alpha"), Definition("b", "Beta"), Definition("c", "Gamma"));
        _dispatcher.RunAll();

        Assert.Equal("c", viewModel.SelectedPrinter?.Id);
    }

    [Fact]
    public void ShowPrinterAfterDisposeIsIgnored()
    {
        Configure(Definition("a", "Alpha"), Definition("b", "Beta"));
        DiagnosticsViewModel viewModel = Create();
        viewModel.Dispose();

        viewModel.ShowPrinter("b");

        Assert.Equal("a", viewModel.SelectedPrinter?.Id);
        Assert.Single(_info.Requests);
    }

    [Fact]
    public void BeforeTheFirstPollValuesAreMissing()
    {
        Configure(Definition());

        DiagnosticsViewModel viewModel = Create();

        Assert.Equal("Connecting…", viewModel.StateText);
        Assert.All(ValueTexts(viewModel), text => Assert.Equal(Missing, text));
        Assert.Equal(Missing, viewModel.LastUpdateText);
        Assert.False(viewModel.HasDetail);
    }

    [Fact]
    public void LatestSnapshotIsShownImmediatelyOnOpen()
    {
        Configure(Definition());
        _monitor.Raise(Printing());

        DiagnosticsViewModel viewModel = Create();

        Assert.Equal("Printing", viewModel.StateText);
        Assert.Equal("215.3\u00A0°C", viewModel.NozzleActualText);
        Assert.Equal(0, _dispatcher.PostCount);
    }

    [Fact]
    public void LiveSnapshotUpdatesEveryValueOnTheUiThread()
    {
        Configure(Definition());
        DiagnosticsViewModel viewModel = Create();

        _monitor.Raise(Printing());
        Assert.Equal(Missing, viewModel.NozzleActualText);

        _dispatcher.RunAll();

        Assert.Equal("Printing", viewModel.StateText);
        Assert.Equal("215.3\u00A0°C", viewModel.NozzleActualText);
        Assert.Equal("215\u00A0°C", viewModel.NozzleTargetText);
        Assert.Equal("60.0\u00A0°C", viewModel.BedActualText);
        Assert.Equal("60\u00A0°C", viewModel.BedTargetText);
        Assert.Equal("7,200 RPM", viewModel.HotendFanText);
        Assert.Equal("3,000 RPM", viewModel.PrintFanText);
        Assert.Equal("100%", viewModel.SpeedText);
        Assert.Equal("95%", viewModel.FlowText);
        Assert.Equal("14.00 mm", viewModel.AxisZText);
        Assert.Equal(DiagnosticsFormatter.Timestamp(Now, Now, FixedClock.Zone), viewModel.LastUpdateText);
        Assert.False(viewModel.HasDetail);
    }

    [Fact]
    public void ValuesFollowTheUiCulture()
    {
        using CultureScope czech = new("cs-CZ");
        Configure(Definition());
        _monitor.Raise(Printing());

        DiagnosticsViewModel viewModel = Create();

        Assert.Equal("Tiskne", viewModel.StateText);
        Assert.Equal("215,3\u00A0°C", viewModel.NozzleActualText);
        Assert.Equal("14:00:00", viewModel.LastUpdateText);
    }

    [Fact]
    public void RefreshTextsSwitchesTheLanguageButNotTheNumberFormat()
    {
        Configure(Definition());
        _monitor.Raise(Printing());
        DiagnosticsViewModel viewModel = Create();
        _info.Requests[0].Completion.SetResult(new PrusaLinkInfoResult(PrinterState.Idle, null, CoreOneInfo));
        _dispatcher.RunAll();
        ChartWindowOption window = viewModel.SelectedTimeWindow;
        int requests = _info.Requests.Count;

        using (new UiCultureScope("cs-CZ"))
        {
            viewModel.RefreshTexts();

            Assert.Equal("Tiskne", viewModel.StateText);
            Assert.Equal("Ano", viewModel.MmuText);
        }

        // Only the texts: numbers keep the (en-US) format of the system, no new information request.
        Assert.Equal("215.3\u00A0°C", viewModel.NozzleActualText);
        Assert.Equal("0.40 mm", viewModel.NozzleDiameterText);
        Assert.Same(window, viewModel.SelectedTimeWindow);
        Assert.Equal(requests, _info.Requests.Count);
    }

    [Fact]
    public void RefreshTextsTranslatesNoticeInfoStatusAndChartRanges()
    {
        DiagnosticsViewModel viewModel = Create();
        List<string?> windowChanges = [];
        ((INotifyPropertyChanged)viewModel.TimeWindows[0]).PropertyChanged += (_, e) => windowChanges.Add(e.PropertyName);

        using (new UiCultureScope("cs-CZ"))
        {
            viewModel.RefreshTexts();

            Assert.Equal("Spouštění…", viewModel.Notice);
        }

        Assert.Equal([nameof(ChartWindowOption.Text)], windowChanges);

        Configure(Definition() with { ConnectionType = ConnectionType.PrusaConnect, Address = "printer-uuid" });
        _dispatcher.RunAll();
        _info.Requests[0].Completion.SetResult(null);
        _dispatcher.RunAll();
        using (new UiCultureScope("cs-CZ"))
        {
            viewModel.RefreshTexts();

            Assert.Equal("Pro Prusa Connect není k dispozici", viewModel.InfoStatusText);
            Assert.Null(viewModel.Notice);
        }
    }

    [Fact]
    public void RefreshTextsKeepsTheReasonOfFailedInfo()
    {
        Configure(Definition());
        DiagnosticsViewModel viewModel = Create();
        _info.Requests[0].Completion.SetResult(new PrusaLinkInfoResult(PrinterState.Offline, "The printer is not reachable.", null));
        _dispatcher.RunAll();

        using (new UiCultureScope("cs-CZ"))
        {
            viewModel.RefreshTexts();

            Assert.Equal("Nelze načíst: Nedostupná", viewModel.InfoStatusText);
        }

        Assert.Single(_info.Requests);
    }

    [Fact]
    public void RefreshTextsAfterDisposeDoesNothing()
    {
        DiagnosticsViewModel viewModel = Create();
        viewModel.Dispose();

        using (new UiCultureScope("cs-CZ"))
        {
            viewModel.RefreshTexts();
        }

        Assert.Equal("Starting…", viewModel.Notice);
    }

    [Fact]
    public void BurstOfSnapshotsIsCoalescedIntoOneUiUpdate()
    {
        Configure(Definition());
        DiagnosticsViewModel viewModel = Create();

        for (int i = 1; i <= 5; i++)
        {
            _monitor.Raise(Printing(nozzle: 200 + i, second: i));
        }

        Assert.Equal(1, _dispatcher.PostCount);
        _dispatcher.RunAll();
        Assert.Equal("205.0\u00A0°C", viewModel.NozzleActualText);
    }

    [Fact]
    public void InvisibleChangesDoNotReachTheUiThread()
    {
        Configure(Definition());
        DiagnosticsViewModel viewModel = Create();
        _monitor.Raise(Printing(nozzle: 215.30));
        _dispatcher.RunAll();
        List<string?> changed = Track(viewModel);

        // Same second, same rounded values.
        _monitor.Raise(Printing(nozzle: 215.31) with { Timestamp = Now.AddMilliseconds(300) });

        Assert.Equal(1, _dispatcher.PostCount);
        Assert.Empty(changed);
    }

    [Fact]
    public void UiUpdateRaisesChangesOnlyForValuesThatChanged()
    {
        Configure(Definition());
        DiagnosticsViewModel viewModel = Create();
        _monitor.Raise(Printing());
        _dispatcher.RunAll();
        List<string?> changed = Track(viewModel);

        _monitor.Raise(Printing(nozzle: 220, second: 5));
        _dispatcher.RunAll();

        Assert.Equal([nameof(DiagnosticsViewModel.LastUpdateText), nameof(DiagnosticsViewModel.NozzleActualText)], changed.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void SnapshotsOfOtherPrintersAreIgnored()
    {
        Configure(Definition("a", "Alpha"), Definition("b", "Beta"));
        DiagnosticsViewModel viewModel = Create();

        _monitor.Raise(Printing(printerId: "b"));
        _monitor.Raise(Printing(printerId: "unknown"));

        Assert.Equal(0, _dispatcher.PostCount);
        Assert.Equal(Missing, viewModel.NozzleActualText);
    }

    [Fact]
    public void SnapshotIdMatchesRegardlessOfCase()
    {
        Configure(Definition("mk4"));
        DiagnosticsViewModel viewModel = Create();

        _monitor.Raise(Printing(printerId: "MK4"));
        _dispatcher.RunAll();

        Assert.Equal("215.3\u00A0°C", viewModel.NozzleActualText);
    }

    [Fact]
    public void UnavailablePrinterShowsMissingValuesAndTheReason()
    {
        Configure(Definition());
        DiagnosticsViewModel viewModel = Create();
        _monitor.Raise(Printing());
        _dispatcher.RunAll();

        _monitor.Raise(PrinterSnapshot.Unavailable("mk4", PrinterState.Offline, Now.AddSeconds(5), "The printer is not reachable."));
        _dispatcher.RunAll();

        Assert.Equal("Offline", viewModel.StateText);
        Assert.All(ValueTexts(viewModel), text => Assert.Equal(Missing, text));
        Assert.Equal("The printer is not reachable.", viewModel.DetailText);
        Assert.True(viewModel.HasDetail);

        _monitor.Raise(Printing(second: 10));
        _dispatcher.RunAll();
        Assert.False(viewModel.HasDetail);
    }

    [Fact]
    public void PartialTelemetryShowsWhatIsKnown()
    {
        Configure(Definition());
        DiagnosticsViewModel viewModel = Create();

        _monitor.Raise(Snapshot(PrinterState.Idle) with { Telemetry = new Telemetry { Nozzle = new Temperature(double.NaN, null), Bed = new Temperature(24.5, 0) } });
        _dispatcher.RunAll();

        Assert.Equal(Missing, viewModel.NozzleActualText);
        Assert.Equal(Missing, viewModel.NozzleTargetText);
        Assert.Equal("24.5\u00A0°C", viewModel.BedActualText);
        Assert.Equal("Off", viewModel.BedTargetText);
        Assert.Equal(Missing, viewModel.HotendFanText);
    }

    [Fact]
    public void SwitchingPrintersShowsTheOtherPrinterAndItsChart()
    {
        Configure(Definition("a", "Alpha"), Definition("b", "Beta"));
        _monitor.Raise(Printing(nozzle: 100, printerId: "b"));
        DiagnosticsViewModel viewModel = Create();

        viewModel.SelectedPrinter = viewModel.Printers[1];

        Assert.Equal("100.0\u00A0°C", viewModel.NozzleActualText);
        Assert.Equal("b", viewModel.ChartPrinterId);

        _monitor.Raise(Printing(nozzle: 300, printerId: "a", second: 1));
        Assert.Equal(0, _dispatcher.PostCount);
        _monitor.Raise(Printing(nozzle: 110, printerId: "b", second: 1));
        _dispatcher.RunAll();
        Assert.Equal("110.0\u00A0°C", viewModel.NozzleActualText);
    }

    [Fact]
    public void SwitchingToAPrinterWithoutDataClearsTheValues()
    {
        Configure(Definition("a", "Alpha"), Definition("b", "Beta"));
        _monitor.Raise(Printing(printerId: "a"));
        DiagnosticsViewModel viewModel = Create();

        viewModel.SelectedPrinter = viewModel.Printers[1];

        Assert.Equal("Connecting…", viewModel.StateText);
        Assert.Equal(Missing, viewModel.NozzleActualText);
    }

    [Fact]
    public void RemovedPrinterFallsBackToTheFirstRemaining()
    {
        Configure(Definition("a", "Alpha"), Definition("b", "Beta"));
        DiagnosticsViewModel viewModel = Create();
        viewModel.SelectedPrinter = viewModel.Printers[1];

        Configure(Definition("a", "Alpha"));
        Assert.Equal("b", viewModel.SelectedPrinter?.Id);
        _dispatcher.RunAll();

        Assert.Equal("a", viewModel.SelectedPrinter?.Id);
        Assert.Equal("a", viewModel.ChartPrinterId);
        Assert.Single(viewModel.Printers);
        Assert.False(viewModel.HasPrinterSelector);
    }

    [Fact]
    public void RemovingEveryPrinterShowsTheNotice()
    {
        Configure(Definition());
        DiagnosticsViewModel viewModel = Create();

        Configure();
        _dispatcher.RunAll();

        Assert.Null(viewModel.SelectedPrinter);
        Assert.False(viewModel.HasPrinter);
        Assert.Null(viewModel.ChartPrinterId);
        Assert.Equal("No printer configured", viewModel.Notice);
    }

    [Fact]
    public void RenamedPrinterStaysSelectedWithoutReloadingInfo()
    {
        Configure(Definition("a", "Alpha"), Definition("b", "Beta"));
        DiagnosticsViewModel viewModel = Create();
        viewModel.SelectedPrinter = viewModel.Printers[1];
        int requests = _info.Requests.Count;

        Configure(Definition("a", "Alpha"), Definition("b", "Beta 2"));
        _dispatcher.RunAll();

        Assert.Equal(new PrinterChoice("b", "Beta 2"), viewModel.SelectedPrinter);
        Assert.Equal(requests, _info.Requests.Count);
    }

    [Fact]
    public void ChangedAddressReloadsInfo()
    {
        Configure(Definition());
        DiagnosticsViewModel viewModel = Create();
        _info.Requests[0].Completion.SetResult(new PrusaLinkInfoResult(PrinterState.Idle, null, CoreOneInfo));
        _dispatcher.RunAll();

        Configure(Definition() with { Address = "192.168.1.99" });
        _dispatcher.RunAll();

        Assert.Equal(2, _info.Requests.Count);
        Assert.Equal("192.168.1.99", _info.Requests[1].Printer.Address);
        Assert.Equal("Loading…", viewModel.InfoStatusText);
    }

    [Fact]
    public void MonitoringFailureShowsTheNoticeAndKeepsTheLastValues()
    {
        Configure(Definition());
        _monitor.Raise(Printing());
        DiagnosticsViewModel viewModel = Create();

        _printers.ReportMonitoringFailed();
        _dispatcher.RunAll();

        Assert.Equal("Monitoring stopped due to an error", viewModel.Notice);
        Assert.True(viewModel.HasPrinter);
        Assert.Equal("215.3\u00A0°C", viewModel.NozzleActualText);

        Configure(Definition());
        _dispatcher.RunAll();
        Assert.Null(viewModel.Notice);
    }

    [Fact]
    public void ConfigurationBurstIsAppliedInOneUiUpdate()
    {
        DiagnosticsViewModel viewModel = Create();

        Configure(Definition("a", "Alpha"));
        Configure(Definition("a", "Alpha"), Definition("b", "Beta"));
        _printers.ReportMonitoringFailed();

        Assert.Equal(1, _dispatcher.PostCount);
        _dispatcher.RunAll();
        Assert.Equal(2, viewModel.Printers.Count);
    }

    [Fact]
    public void DisposeUnsubscribesAndIgnoresLateWork()
    {
        Configure(Definition());
        DiagnosticsViewModel viewModel = Create();
        Assert.Equal(1, _monitor.SubscriberCount);
        _monitor.Raise(Printing());

        viewModel.Dispose();
        _dispatcher.RunAll();
        _monitor.Raise(Printing(second: 5));
        Configure();

        Assert.Equal(0, _monitor.SubscriberCount);
        Assert.Equal(1, _dispatcher.PostCount);
        Assert.Equal(Missing, viewModel.NozzleActualText);
        Assert.True(_info.Requests[0].Token.IsCancellationRequested);

        _info.Requests[0].Completion.SetResult(new PrusaLinkInfoResult(PrinterState.Idle, null, CoreOneInfo));
        _dispatcher.RunAll();
        Assert.Equal(Missing, viewModel.FirmwareText);

        viewModel.Dispose();
    }

    [Fact]
    public void InfoOfTheSelectedPrinterIsLoaded()
    {
        Configure(Definition());
        DiagnosticsViewModel viewModel = Create();

        InfoRequest request = Assert.Single(_info.Requests);
        Assert.Equal("mk4", request.Printer.Id);
        Assert.Equal("Loading…", viewModel.InfoStatusText);
        Assert.True(viewModel.HasInfoStatus);

        request.Completion.SetResult(new PrusaLinkInfoResult(PrinterState.Idle, null, CoreOneInfo));
        _dispatcher.RunAll();

        Assert.False(viewModel.HasInfoStatus);
        Assert.Equal("6.9.2+10123", viewModel.FirmwareText);
        Assert.Equal("7.0.0", viewModel.ModelText);
        Assert.Equal("prusa-core-one", viewModel.HostnameText);
        Assert.Equal("0.40 mm", viewModel.NozzleDiameterText);
        Assert.Equal("Yes", viewModel.MmuText);
    }

    [Fact]
    public void MissingInfoFieldsAreDashes()
    {
        Configure(Definition());
        DiagnosticsViewModel viewModel = Create();

        _info.Requests[0].Completion.SetResult(new PrusaLinkInfoResult(PrinterState.Idle, null, new PrusaLinkPrinterInfo("prusa-mk4", null, "2.0.0", null, null, false)));
        _dispatcher.RunAll();

        Assert.Equal(Missing, viewModel.FirmwareText);
        Assert.Equal(Missing, viewModel.ModelText);
        Assert.Equal(Missing, viewModel.NozzleDiameterText);
        Assert.Equal("No", viewModel.MmuText);
    }

    [Fact]
    public void InfoValuesReplaceTheStatusTextOnlyOnceLoaded()
    {
        // T-041: the footer shows either the status text or the label/value pairs, never both.
        Configure(Definition("a", "Alpha"), Definition("b", "Beta"));
        DiagnosticsViewModel viewModel = Create();
        List<string?> changes = Track(viewModel);
        Assert.True(viewModel.HasInfoStatus);
        Assert.False(viewModel.HasInfoValues);

        _info.Requests[0].Completion.SetResult(new PrusaLinkInfoResult(PrinterState.Idle, null, CoreOneInfo));
        _dispatcher.RunAll();

        Assert.False(viewModel.HasInfoStatus);
        Assert.True(viewModel.HasInfoValues);
        Assert.Contains(nameof(DiagnosticsViewModel.HasInfoValues), changes);

        // Another printer: its information is loading, so only the status is shown again.
        changes.Clear();
        viewModel.SelectedPrinter = viewModel.Printers[1];

        Assert.Equal("Loading…", viewModel.InfoStatusText);
        Assert.False(viewModel.HasInfoValues);
        Assert.Contains(nameof(DiagnosticsViewModel.HasInfoValues), changes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedOrUnsupportedInfoShowsOnlyTheStatusText(bool prusaConnect)
    {
        Configure(prusaConnect ? Definition() with { ConnectionType = ConnectionType.PrusaConnect, Address = "printer-uuid" } : Definition());
        DiagnosticsViewModel viewModel = Create();

        _info.Requests[0].Completion.SetResult(prusaConnect ? null : Failed(PrinterState.Offline));
        _dispatcher.RunAll();

        Assert.True(viewModel.HasInfoStatus);
        Assert.False(viewModel.HasInfoValues);
    }

    [Fact]
    public void WithoutAPrinterNeitherInfoValuesNorStatusAreShown()
    {
        Configure(Definition());
        DiagnosticsViewModel viewModel = Create();
        _info.Requests[0].Completion.SetResult(new PrusaLinkInfoResult(PrinterState.Idle, null, CoreOneInfo));
        _dispatcher.RunAll();
        Assert.True(viewModel.HasInfoValues);
        List<string?> changes = Track(viewModel);

        Configure();
        _dispatcher.RunAll();

        Assert.False(viewModel.HasPrinter);
        Assert.False(viewModel.HasInfoStatus);
        Assert.False(viewModel.HasInfoValues);
        Assert.Contains(nameof(DiagnosticsViewModel.HasInfoValues), changes);
    }

    [Fact]
    public void InfoIsNotAvailableForPrusaConnect()
    {
        Configure(Definition() with { ConnectionType = ConnectionType.PrusaConnect, Address = "printer-uuid" });
        DiagnosticsViewModel viewModel = Create();

        _info.Requests[0].Completion.SetResult(null);
        _dispatcher.RunAll();

        Assert.Equal("Not available for Prusa Connect", viewModel.InfoStatusText);
        Assert.Equal(Missing, viewModel.FirmwareText);
    }

    [Fact]
    public void OfflinePrinterIsNotAskedForItsInformationUntilItIsReachable()
    {
        // T-044: a refused connection can take seconds on Windows; an offline printer shows the failure at once.
        Configure(Definition());
        _monitor.Raise(PrinterSnapshot.Unavailable("mk4", PrinterState.Offline, Now, "The printer is not reachable.", PrinterProblem.NotReachable));

        DiagnosticsViewModel viewModel = Create();

        Assert.Empty(_info.Requests);
        Assert.Equal("Could not be read: Offline", viewModel.InfoStatusText);

        _monitor.Raise(Printing(second: 10));
        _dispatcher.RunAll();
        Assert.Single(_info.Requests);
        Assert.Equal("Loading…", viewModel.InfoStatusText);
    }

    [Fact]
    public void KnownProblemIsShownTranslatedInsteadOfTheTechnicalMessage()
    {
        Configure(Definition());
        DiagnosticsViewModel viewModel = Create();

        _monitor.Raise(PrinterSnapshot.Unavailable("mk4", PrinterState.Unauthorized, Now, "technical text", PrinterProblem.PasswordMissing));
        _dispatcher.RunAll();

        Assert.Equal("The printer requires a password (Settings).", viewModel.DetailText);
    }

    [Fact]
    public void FailedInfoShowsTheReasonAndIsRetriedWhenThePrinterBecomesReachable()
    {
        Configure(Definition());
        DiagnosticsViewModel viewModel = Create();
        _monitor.Raise(PrinterSnapshot.Unavailable("mk4", PrinterState.Offline, Now));
        _dispatcher.RunAll();

        _info.Requests[0].Completion.SetResult(new PrusaLinkInfoResult(PrinterState.Offline, "The printer is not reachable.", null));
        _dispatcher.RunAll();
        Assert.Equal("Could not be read: Offline", viewModel.InfoStatusText);

        _monitor.Raise(PrinterSnapshot.Unavailable("mk4", PrinterState.Offline, Now.AddSeconds(5), "again"));
        _dispatcher.RunAll();
        Assert.Single(_info.Requests);

        _monitor.Raise(Printing(second: 10));
        _dispatcher.RunAll();
        Assert.Equal(2, _info.Requests.Count);
        Assert.Equal("Loading…", viewModel.InfoStatusText);

        // Still reachable: no request storm when the retry fails too.
        _info.Requests[1].Completion.SetResult(new PrusaLinkInfoResult(PrinterState.Unknown, "odd", null));
        _dispatcher.RunAll();
        _monitor.Raise(Printing(nozzle: 100, second: 15));
        _dispatcher.RunAll();
        Assert.Equal(2, _info.Requests.Count);
        Assert.Equal("Could not be read: Unknown state", viewModel.InfoStatusText);
    }

    [Fact]
    public void InfoFailingAfterTheFirstPollIsRetriedOnTheNextPoll()
    {
        // The request is still in flight while the first (reachable) snapshot is shown, e.g. right after start.
        Configure(Definition());
        DiagnosticsViewModel viewModel = Create();
        _monitor.Raise(Printing());
        _dispatcher.RunAll();

        _info.Requests[0].Completion.SetResult(Failed(PrinterState.Offline));
        _dispatcher.RunAll();
        Assert.Single(_info.Requests);

        _monitor.Raise(Printing(second: 5));
        _dispatcher.RunAll();

        Assert.Equal(2, _info.Requests.Count);
        _info.Requests[1].Completion.SetResult(new PrusaLinkInfoResult(PrinterState.Idle, null, CoreOneInfo));
        _dispatcher.RunAll();
        Assert.Equal("6.9.2+10123", viewModel.FirmwareText);
    }

    [Fact]
    public void TransientInfoFailureWhileReachableIsRetriedOnce()
    {
        Configure(Definition());
        _monitor.Raise(Printing());
        DiagnosticsViewModel viewModel = Create();
        for (int second = 5; second <= 15; second += 5)
        {
            _monitor.Raise(Printing(second: second));
            _dispatcher.RunAll();
        }

        _info.Requests[0].Completion.SetResult(Failed(PrinterState.Unknown));
        _dispatcher.RunAll();
        _monitor.Raise(Printing(second: 20));
        _dispatcher.RunAll();

        Assert.Equal(2, _info.Requests.Count);
        Assert.Equal("Loading…", viewModel.InfoStatusText);
    }

    [Fact]
    public void FailedInfoAfterAnAddressChangeIsRetried()
    {
        Configure(Definition());
        _monitor.Raise(Printing());
        DiagnosticsViewModel viewModel = Create();
        _info.Requests[0].Completion.SetResult(new PrusaLinkInfoResult(PrinterState.Idle, null, CoreOneInfo));
        _dispatcher.RunAll();
        _monitor.Raise(Printing(second: 5));
        _dispatcher.RunAll();

        Configure(Definition() with { Address = "192.168.1.99" });
        _dispatcher.RunAll();
        _info.Requests[1].Completion.SetResult(Failed(PrinterState.Offline));
        _dispatcher.RunAll();
        _monitor.Raise(Printing(second: 10));
        _dispatcher.RunAll();

        Assert.Equal(3, _info.Requests.Count);
        Assert.Equal("192.168.1.99", _info.Requests[2].Printer.Address);
        Assert.Equal("Loading…", viewModel.InfoStatusText);
    }

    [Fact]
    public void RepeatedInfoFailuresWhileReachableDoNotRetryOnEveryPoll()
    {
        Configure(Definition());
        _monitor.Raise(Printing());
        DiagnosticsViewModel viewModel = Create();
        _info.Requests[0].Completion.SetResult(Failed(PrinterState.Unknown));
        _dispatcher.RunAll();

        // One retry while reachable; when it fails too, polls of the reachable printer ask no more.
        for (int second = 5; second <= 60; second += 5)
        {
            _monitor.Raise(Printing(second: second));
            _dispatcher.RunAll();
            if (_info.Requests.Count == 2 && !_info.Requests[1].Completion.Task.IsCompleted)
            {
                _info.Requests[1].Completion.SetResult(Failed(PrinterState.Unknown));
                _dispatcher.RunAll();
            }
        }

        Assert.Equal(2, _info.Requests.Count);
        Assert.Equal("Could not be read: Unknown state", viewModel.InfoStatusText);

        // After an outage the printer gets one more chance.
        _monitor.Raise(PrinterSnapshot.Unavailable("mk4", PrinterState.Offline, Now.AddSeconds(65)));
        _dispatcher.RunAll();
        _monitor.Raise(Printing(second: 70));
        _dispatcher.RunAll();
        Assert.Equal(3, _info.Requests.Count);
    }

    [Fact]
    public void InfoOfAPreviouslySelectedPrinterIsIgnored()
    {
        Configure(Definition("a", "Alpha"), Definition("b", "Beta"));
        DiagnosticsViewModel viewModel = Create();

        viewModel.SelectedPrinter = viewModel.Printers[1];
        Assert.True(_info.Requests[0].Token.IsCancellationRequested);

        _info.Requests[0].Completion.SetResult(new PrusaLinkInfoResult(PrinterState.Idle, null, CoreOneInfo));
        _dispatcher.RunAll();

        Assert.Equal(Missing, viewModel.FirmwareText);
        Assert.Equal("Loading…", viewModel.InfoStatusText);
        Assert.Equal("b", _info.Requests[1].Printer.Id);
    }

    [Fact]
    public void FailingInfoSourceIsShownAsUnknown()
    {
        Configure(Definition());
        _info.ThrowOnRequest = new InvalidOperationException("broken");

        DiagnosticsViewModel viewModel = Create();
        _dispatcher.RunAll();

        Assert.Equal("Could not be read: Unknown state", viewModel.InfoStatusText);
    }

    [Fact]
    public void FaultedInfoRequestIsShownAsUnknown()
    {
        Configure(Definition());
        DiagnosticsViewModel viewModel = Create();

        _info.Requests[0].Completion.SetException(new HttpRequestException("boom"));
        _dispatcher.RunAll();

        Assert.Equal("Could not be read: Unknown state", viewModel.InfoStatusText);
    }

    [Fact]
    public void ChartOffersThreeTimeWindowsAndStartsWithTwoHours()
    {
        DiagnosticsViewModel viewModel = Create();

        Assert.Equal([TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(24)], viewModel.TimeWindows.Select(static option => option.Window));
        Assert.Equal(["30 min", "2 h", "24 h"], viewModel.TimeWindows.Select(static option => option.Text));
        Assert.Equal(TimeSpan.FromHours(2), viewModel.SelectedTimeWindow.Window);

        viewModel.SelectedTimeWindow = viewModel.TimeWindows[0];
        Assert.Equal(TimeSpan.FromMinutes(30), viewModel.SelectedTimeWindow.Window);

        // A selector without a selection (e.g. while its items are rebuilt) does not clear the window.
        viewModel.SelectedTimeWindow = null!;
        Assert.Equal(TimeSpan.FromMinutes(30), viewModel.SelectedTimeWindow.Window);
    }

    [Fact]
    public void DeselectingThroughTheSelectorKeepsThePrinterWhilePrintersExist()
    {
        Configure(Definition("a", "Alpha"), Definition("b", "Beta"));
        DiagnosticsViewModel viewModel = Create();

        viewModel.SelectedPrinter = null;

        Assert.Equal("a", viewModel.SelectedPrinter?.Id);
        Assert.True(viewModel.HasPrinter);
    }

    private static PrinterSnapshot Printing(double nozzle = 215.34, string printerId = "mk4", int second = 0) => new()
    {
        PrinterId = printerId,
        State = PrinterState.Printing,
        Timestamp = Now.AddSeconds(second),
        Telemetry = new Telemetry
        {
            Nozzle = new Temperature(nozzle, 215),
            Bed = new Temperature(60.04, 60),
            HotendFanRpm = 7200,
            PrintFanRpm = 3000,
            AxisZ = 14,
            SpeedPercent = 100,
            FlowPercent = 95,
        },
    };

    private static PrusaLinkInfoResult Failed(PrinterState state) => new(state, "failed", null);

    private static string[] ValueTexts(DiagnosticsViewModel viewModel) =>
    [
        viewModel.NozzleActualText,
        viewModel.NozzleTargetText,
        viewModel.BedActualText,
        viewModel.BedTargetText,
        viewModel.HotendFanText,
        viewModel.PrintFanText,
        viewModel.SpeedText,
        viewModel.FlowText,
        viewModel.AxisZText,
    ];

    private static List<string?> Track(INotifyPropertyChanged source)
    {
        List<string?> names = [];
        source.PropertyChanged += (_, e) => names.Add(e.PropertyName);
        return names;
    }

    private void Configure(params PrinterDefinition[] printers) =>
        ((IMonitoringObserver)_printers).ApplySettings(new AppSettings { Printers = printers });

    private DiagnosticsViewModel Create(string? initialPrinterId = null) =>
        _viewModel = new DiagnosticsViewModel(_monitor, _printers, _history, _dispatcher, _info, _clock, initialPrinterId);
}
