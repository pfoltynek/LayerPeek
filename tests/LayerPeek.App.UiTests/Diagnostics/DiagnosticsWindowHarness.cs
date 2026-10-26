using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

using LayerPeek.App.Diagnostics;
using LayerPeek.App.Printers;
using LayerPeek.App.Resources;
using LayerPeek.App.Tests.Diagnostics;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tray;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;
using LayerPeek.Core.History;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;
using LayerPeek.PrusaLink;

namespace LayerPeek.App.UiTests.Diagnostics;

/// <summary>
/// The diagnostics window opened the way the app opens it (<c>App.ShowDiagnostics</c>): a <see cref="DiagnosticsWindow"/>
/// over a <see cref="DiagnosticsViewModel"/>, owned by an <see cref="AuxiliaryWindowSlot"/> that disposes the view model
/// when the window closes. Only in-memory fakes: no monitor thread, no network; time is fixed
/// (<see cref="TestPrinters.Now"/> in UTC+2). UI thread only.
/// </summary>
internal sealed class DiagnosticsWindowHarness
{
    /// <summary>Polling interval of the recorded chart history.</summary>
    private static readonly TimeSpan HistoryInterval = TimeSpan.FromSeconds(20);

    private DiagnosticsWindowHarness(DiagnosticsWindow window, DiagnosticsViewModel viewModel, AuxiliaryWindowSlot slot, FakePrinterMonitor monitor, MonitoredPrinters printers, FakePrinterInfoSource info, TelemetryHistory history)
    {
        Window = window;
        ViewModel = viewModel;
        Slot = slot;
        Monitor = monitor;
        Printers = printers;
        Info = info;
        History = history;
    }

    public DiagnosticsWindow Window { get; }

    public DiagnosticsViewModel ViewModel { get; }

    public AuxiliaryWindowSlot Slot { get; }

    public FakePrinterMonitor Monitor { get; }

    public MonitoredPrinters Printers { get; }

    public FakePrinterInfoSource Info { get; }

    public TelemetryHistory History { get; }

    /// <summary>
    /// Opens the window for <paramref name="printers"/> (<see langword="null"/>: monitoring is still starting). The
    /// latest snapshots in <paramref name="snapshots"/> are known to the monitor before the window opens, like after a
    /// few polls; each of them also leaves 2.5 h of chart history behind.
    /// </summary>
    public static DiagnosticsWindowHarness Open(UiTestScope scope, PrinterDefinition[]? printers, params PrinterSnapshot[] snapshots) =>
        OpenRemembering(scope, windowStates: null, printers, snapshots);

    /// <summary>Like <see cref="Open(UiTestScope, PrinterDefinition[], PrinterSnapshot[])"/>, remembering the size in <paramref name="windowStates"/> (T-094).</summary>
    public static DiagnosticsWindowHarness OpenRemembering(UiTestScope scope, IAuxiliaryWindowStateStore? windowStates, PrinterDefinition[]? printers, params PrinterSnapshot[] snapshots)
    {
        ArgumentNullException.ThrowIfNull(scope);
        FakePrinterMonitor monitor = new();
        MonitoredPrinters monitored = new();
        TelemetryHistory history = new();
        if (printers is not null)
        {
            ((IMonitoringObserver)monitored).ApplySettings(AppSettings.Default with { Printers = printers });
            history.Configure(printers, HistoryInterval);
        }

        foreach (PrinterSnapshot snapshot in snapshots)
        {
            RecordHistory(history, snapshot.PrinterId);
            monitor.Raise(snapshot);
        }

        FakePrinterInfoSource info = new();
        scope.Defer(new CancelPendingInfo(info));
        DiagnosticsViewModel viewModel = scope.Defer(new DiagnosticsViewModel(
            monitor,
            monitored,
            history,
            new AvaloniaUiDispatcher(),
            info,
            new FixedClock(TestPrinters.Now),
            images: new CustomPrinterImages()));
        DiagnosticsWindow window = scope.Track(new DiagnosticsWindow { DataContext = viewModel });
        if (windowStates is not null)
        {
            window.RememberSizeIn(windowStates);
        }

        AuxiliaryWindowSlot slot = new();
        slot.ShowOrActivate(() => (new AvaloniaAuxiliaryWindow(window), viewModel));
        window.SetRenderScaling(1.0);
        window.Settle();
        return new DiagnosticsWindowHarness(window, viewModel, slot, monitor, monitored, info, history);
    }

    public T Find<T>(string automationId)
        where T : Control => Window.Find<T>(automationId);

    /// <summary>Answers every pending information request with <paramref name="result"/>.</summary>
    public void CompleteInfo(PrusaLinkInfoResult? result)
    {
        foreach (InfoRequest request in Info.Requests)
        {
            request.Completion.TrySetResult(result);
        }

        // The view model resumes on the thread pool (the headless UI thread does not run on the default task
        // scheduler) and posts the result to the UI thread: wait for it, with a real-time guard.
        DateTime deadline = DateTime.UtcNow + TestTimeouts.Wait;
        while (ViewModel.InfoStatusText == Strings.DiagnosticsInfoLoading)
        {
            Dispatcher.UIThread.RunJobs();
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("The information result did not reach the window.");
            }

            Thread.Sleep(1);
        }

        Window.Settle();
    }

    /// <summary>Monitoring failed (the notice "Monitoring stopped due to an error"); the last values stay.</summary>
    public void FailMonitoring()
    {
        Printers.ReportMonitoringFailed();
        Window.Settle();
    }

    /// <summary>A sample every 20 s over the last 2.5 h: the nozzle heats up, prints, then cools down; the bed stays at 60 °C.</summary>
    private static void RecordHistory(TelemetryHistory history, string printerId)
    {
        DateTimeOffset start = TestPrinters.Now - TimeSpan.FromMinutes(150);
        for (DateTimeOffset time = start; time <= TestPrinters.Now; time += HistoryInterval)
        {
            double minutes = (time - start).TotalMinutes;
            double nozzle = minutes < 10 ? 25 + (19 * minutes) : minutes < 140 ? 215 + (2 * Math.Sin(minutes / 3)) : 215 - (15 * (minutes - 140));
            double bed = minutes < 5 ? 25 + (7 * minutes) : 60;
            history.Record(new PrinterSnapshot
            {
                PrinterId = printerId,
                State = PrinterState.Printing,
                Timestamp = time,
                Telemetry = new Telemetry { Nozzle = new Temperature(nozzle, minutes < 140 ? 215 : 0), Bed = new Temperature(bed, 60) },
            });
        }
    }

    private sealed class CancelPendingInfo(FakePrinterInfoSource info) : IDisposable
    {
        public void Dispose() => info.CancelPending();
    }
}
