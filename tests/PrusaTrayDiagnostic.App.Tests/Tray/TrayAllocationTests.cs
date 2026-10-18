using PrusaTrayDiagnostic.App.Tests.Tray.Menu;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.App.Tray.Menu;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Tests.Allocation;

using static PrusaTrayDiagnostic.App.Tests.Tray.TestPrinters;

namespace PrusaTrayDiagnostic.App.Tests.Tray;

/// <summary>
/// Polling many printers without visible changes allocates nothing (tray icon and menu); isolated, see
/// <see cref="AllocationSensitive"/>.
/// </summary>
[Collection(nameof(AllocationSensitive))]
public sealed class TrayAllocationTests : IDisposable
{
    private const int PrinterCount = 20;

    private readonly CultureScope _culture = new("en-US");
    private readonly FakePrinterMonitor _monitor = new();
    private readonly ManualDispatcher _dispatcher = new();

    public void Dispose() => _culture.Dispose();

    [Fact]
    public void SteadyPollingOfManyPrintersAllocatesNothingForTheIcon()
    {
        using TrayStatusViewModel viewModel = new(_monitor, _dispatcher);
        PrinterDefinition[] printers = Printers();
        viewModel.SetPrinters(printers);

        AssertSteadyPollingAllocatesNothing(printers, Job(50, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void SteadyPollingOfManyPrintersAllocatesNothingForTheMenu()
    {
        using TrayMenuViewModel viewModel = new(_monitor, _dispatcher, TestWebOpener.Create(new FakeUrlLauncher()), TrayMenuOptions.Default);
        PrinterDefinition[] printers = Printers();
        viewModel.SetPrinters(printers);

        AssertSteadyPollingAllocatesNothing(printers, Job(50));
    }

    private static PrinterDefinition[] Printers() => [.. Enumerable.Range(0, PrinterCount).Select(i => Definition($"p{i}", $"P{i}"))];

    /// <summary>Every pass polls all printers 10× with new timestamps (fresh, invisible changes).</summary>
    private void AssertSteadyPollingAllocatesNothing(PrinterDefinition[] printers, PrintJob job)
    {
        const int Polls = 10;
        const int Passes = 3;
        PrinterSnapshot[][] snapshots = new PrinterSnapshot[1 + (Passes * Polls)][];
        for (int poll = 0; poll < snapshots.Length; poll++)
        {
            snapshots[poll] = [.. printers.Select(printer => Snapshot(PrinterState.Printing, job, printer.Id) with { Timestamp = Now.AddSeconds(5 * poll) })];
        }

        // The first poll makes the state visible; afterwards only timestamps change.
        foreach (PrinterSnapshot snapshot in snapshots[0])
        {
            _monitor.Raise(snapshot);
        }

        _dispatcher.RunAll();
        int posts = _dispatcher.PostCount;

        int next = 1;
        long allocated = AllocationProbe.MinAllocated(() =>
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int end = next + Polls; next < end; next++)
            {
                foreach (PrinterSnapshot snapshot in snapshots[next])
                {
                    _monitor.Raise(snapshot);
                }
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        });

        Assert.Equal(0, allocated);
        Assert.Equal(posts, _dispatcher.PostCount);
    }
}
