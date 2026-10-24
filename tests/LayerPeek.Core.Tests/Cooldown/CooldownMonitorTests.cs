using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.Core.Cooldown;
using LayerPeek.Core.Monitoring;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Tests.Monitoring;

using static LayerPeek.Core.Tests.Cooldown.CooldownTestData;

namespace LayerPeek.Core.Tests.Cooldown;

public sealed class CooldownMonitorTests : IDisposable
{
    private const double End = 5;

    private static readonly PrinterDefinition Mk4 = new() { Id = "mk4", Name = "Workshop MK4", ConnectionType = ConnectionType.PrusaLink, Address = "192.168.1.20" };

    private readonly EventMonitor _monitor = new();
    private readonly FakeTimeProvider _time = new(Start);
    private readonly ListLogger<CooldownMonitor> _logger = new();
    private readonly CooldownMonitor _cooldown;
    private readonly ConcurrentQueue<string> _changed = new();
    private readonly ConcurrentQueue<CooldownEvent> _cooledDown = new();

    public CooldownMonitorTests()
    {
        _cooldown = new CooldownMonitor(_monitor, _time, _logger);
        _cooldown.Changed += (_, printerId) => _changed.Enqueue(printerId);
        _cooldown.CooledDown += (_, cooled) => _cooledDown.Enqueue(cooled);
    }

    public void Dispose() => _cooldown.Dispose();

    [Fact]
    public void CoolingIsPublishedWithChangedAndGetState()
    {
        _cooldown.Configure([Mk4]);
        Raise(Printing(0));
        Assert.Empty(_changed);

        Raise(Cooling(PrinterState.Finished, End, End));

        Assert.Equal(["mk4"], _changed);
        CooldownState state = _cooldown.GetState("mk4");
        Assert.Equal(CooldownPhase.Cooling, state.Phase);
        Assert.Equal(NozzlePrintTemperature, state.NozzleCelsius);
        Assert.Equal(CooldownThreshold.DefaultCelsius, state.ThresholdCelsius);
    }

    [Fact]
    public void ChangedIsRaisedOnlyWhenTheStateChanges()
    {
        _cooldown.Configure([Mk4]);
        Raise(Snap(PrinterState.Idle, 0, 25, 0));
        Raise(Snap(PrinterState.Idle, 5, 25, 0));
        Raise(Printing(10));
        Raise(Printing(15));

        Assert.Empty(_changed);
    }

    [Fact]
    public void CooledDownIsRaisedOncePerJob()
    {
        _cooldown.Configure([Mk4]);
        Raise(Printing(0));

        RunCurve("mk4", End, End + 400);

        CooldownEvent cooled = Assert.Single(_cooledDown);
        Assert.Equal("mk4", cooled.PrinterId);
        Assert.Equal("benchy.bgcode", cooled.FileName);
        Assert.Equal(CooldownPhase.CooledDown, _cooldown.GetState("mk4").Phase);
    }

    [Fact]
    public void PrintEndedBelowTheThresholdRaisesNoCooledDown()
    {
        _cooldown.Configure([Mk4]);
        Raise(Printing(0));
        Raise(Snap(PrinterState.Finished, End, 45, 0, job: Job()));
        Raise(Snap(PrinterState.Finished, 10, 44, 0, job: Job()));

        Assert.Equal(CooldownPhase.CooledDown, _cooldown.GetState("mk4").Phase);
        Assert.Empty(_cooledDown);
        Assert.Equal(["mk4"], _changed);
    }

    [Fact]
    public void StateDisappearsWithChangedWhenTheNextPrintStarts()
    {
        _cooldown.Configure([Mk4]);
        Raise(Printing(0));
        RunCurve("mk4", End, End + 400);
        _changed.Clear();

        Raise(Printing(500, job: Job(8)));

        Assert.Equal(["mk4"], _changed);
        Assert.Equal(CooldownPhase.None, _cooldown.GetState("mk4").Phase);
        Assert.Single(_cooledDown);
    }

    [Fact]
    public void ThresholdIsPerPrinter()
    {
        PrinterDefinition low = Mk4 with { Id = "low", CooldownThresholdCelsius = 30 };
        PrinterDefinition high = Mk4 with { Id = "high", CooldownThresholdCelsius = 100 };
        _cooldown.Configure([low, high]);
        foreach (string id in new[] { "low", "high" })
        {
            Raise(Printing(0, id));
            RunCurve(id, End, End + 100);
        }

        Assert.Equal((CooldownPhase.Cooling, 30), Summary(_cooldown.GetState("low")));
        Assert.Equal((CooldownPhase.CooledDown, 100), Summary(_cooldown.GetState("high")));
        Assert.Equal("high", Assert.Single(_cooledDown).PrinterId);
    }

    [Fact]
    public void ThresholdChangeAppliesImmediately()
    {
        _cooldown.Configure([Mk4]);
        Raise(Printing(0));
        RunCurve("mk4", End, End + 90);
        Assert.Equal(CooldownPhase.Cooling, _cooldown.GetState("mk4").Phase);
        _changed.Clear();

        // The nozzle is at about 70 °C; the new threshold is above it.
        _cooldown.Configure([Mk4 with { CooldownThresholdCelsius = 80 }]);

        Assert.Equal((CooldownPhase.CooledDown, 80), Summary(_cooldown.GetState("mk4")));
        Assert.Equal(["mk4"], _changed);
        CooldownEvent cooled = Assert.Single(_cooledDown);
        Assert.Equal(Start.AddSeconds(End + 90), cooled.Timestamp);
    }

    [Fact]
    public void ThresholdChangeWhileNothingIsTrackedRaisesNothing()
    {
        _cooldown.Configure([Mk4]);
        Raise(Snap(PrinterState.Idle, 0, 25, 0));

        _cooldown.Configure([Mk4 with { CooldownThresholdCelsius = 70 }]);

        Assert.Equal(CooldownState.None, _cooldown.GetState("mk4"));
        Assert.Empty(_changed);

        // The new threshold applies to the next cool-down.
        Raise(Printing(5));
        Raise(Cooling(PrinterState.Finished, 10, 10));
        Assert.Equal((CooldownPhase.Cooling, 70), Summary(_cooldown.GetState("mk4")));
    }

    [Fact]
    public void RenamedPrinterKeepsItsState()
    {
        _cooldown.Configure([Mk4]);
        Raise(Printing(0));
        RunCurve("mk4", End, End + 30);
        CooldownState before = _cooldown.GetState("mk4");
        _changed.Clear();

        _cooldown.Configure([Mk4 with { Name = "Renamed" }]);

        Assert.Equal(before, _cooldown.GetState("mk4"));
        Assert.Empty(_changed);
        RunCurve("mk4", End, End + 400, from: End + 35);
        Assert.Single(_cooledDown);
    }

    [Theory]
    [InlineData("address")]
    [InlineData("connection")]
    [InlineData("removed")]
    public void AnotherMachineOrRemovedPrinterIsForgotten(string change)
    {
        _cooldown.Configure([Mk4]);
        Raise(Printing(0));
        RunCurve("mk4", End, End + 30);
        _changed.Clear();

        _cooldown.Configure(change switch
        {
            "address" => [Mk4 with { Address = "192.168.1.99" }],
            "connection" => [Mk4 with { ConnectionType = ConnectionType.PrusaConnect }],
            _ => [],
        });

        Assert.Equal(CooldownPhase.None, _cooldown.GetState("mk4").Phase);
        Assert.Equal(["mk4"], _changed);
        Assert.Empty(_cooledDown);
    }

    [Fact]
    public void ForgottenPrinterInNoStateRaisesNothing()
    {
        _cooldown.Configure([Mk4]);
        Raise(Snap(PrinterState.Idle, 0, 25, 0));

        _cooldown.Configure([]);

        Assert.Empty(_changed);
    }

    [Fact]
    public void SnapshotsBeforeConfigureAndOfUnconfiguredPrintersAreIgnored()
    {
        Raise(Printing(0));
        Raise(Cooling(PrinterState.Finished, End, End));
        _cooldown.Configure([Mk4]);
        Raise(Printing(0, "xl"));
        Raise(Cooling(PrinterState.Finished, End, End, "xl"));

        Assert.Empty(_changed);
        Assert.Equal(CooldownPhase.None, _cooldown.GetState("mk4").Phase);
        Assert.Equal(default, _cooldown.GetState("xl"));
    }

    [Fact]
    public void PrinterIdIsMatchedCaseInsensitively()
    {
        _cooldown.Configure([Mk4]);
        Raise(Printing(0, "MK4"));
        Raise(Cooling(PrinterState.Finished, End, End, "MK4"));

        Assert.Equal(CooldownPhase.Cooling, _cooldown.GetState("Mk4").Phase);
    }

    [Fact]
    public void GetStateCountsTheEstimateDownBetweenPolls()
    {
        _cooldown.Configure([Mk4]);
        Raise(Printing(0));
        RunCurve("mk4", End, End + 60);
        CooldownState polled = _cooldown.GetState("mk4");
        TimeSpan estimate = Assert.IsType<TimeSpan>(polled.EstimatedRemaining);

        // A clock behind the snapshot does not lengthen the estimate.
        Assert.True(_time.GetUtcNow() < polled.UpdatedAt);
        Assert.Equal(estimate, _cooldown.GetState("mk4").EstimatedRemaining);

        _time.SetUtcNow(polled.UpdatedAt!.Value + TimeSpan.FromSeconds(3));
        Assert.Equal(estimate - TimeSpan.FromSeconds(3), _cooldown.GetState("mk4").EstimatedRemaining);

        _time.SetUtcNow(polled.UpdatedAt.Value + TimeSpan.FromHours(1));
        Assert.Equal(TimeSpan.Zero, _cooldown.GetState("mk4").EstimatedRemaining);
        Assert.Equal(polled with { EstimatedRemaining = TimeSpan.Zero }, _cooldown.GetState("mk4"));
    }

    [Fact]
    public void FailingSubscribersAreLoggedAndDoNotStopOthers()
    {
        List<string> later = [];
        _cooldown.Changed += (_, _) => throw new InvalidOperationException("changed failed");
        _cooldown.Changed += (_, id) => later.Add(id);
        _cooldown.CooledDown += (_, _) => throw new InvalidOperationException("cooled failed");
        _cooldown.Configure([Mk4]);
        Raise(Printing(0));

        RunCurve("mk4", End, End + 400);

        // Every change while cooling (temperatures, progress, estimate), then "cooled down".
        Assert.True(later.Count > 10);
        Assert.Equal(_changed, later);
        Assert.Single(_cooledDown);
        Assert.Equal(later.Count + 1, _logger.Entries.Count);
        Assert.All(_logger.Entries, entry => Assert.Equal(LogLevel.Error, entry.Level));
        Assert.All(_logger.Entries, entry => Assert.Contains("mk4", entry.Message, StringComparison.Ordinal));
    }

    [Fact]
    public void NullSnapshotIsIgnored()
    {
        _cooldown.Configure([Mk4]);

        _monitor.Raise(null!);

        Assert.Empty(_changed);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public void ConfigureRejectsInvalidArguments()
    {
        Assert.Throws<ArgumentNullException>(() => _cooldown.Configure(null!));
        Assert.Throws<ArgumentException>(() => _cooldown.Configure([Mk4, null!]));
        Assert.Throws<ArgumentNullException>(() => _cooldown.GetState(null!));
    }

    [Fact]
    public void ConstructorRejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new CooldownMonitor(null!, _time, _logger));
        Assert.Throws<ArgumentNullException>(() => new CooldownMonitor(_monitor, null!, _logger));
        Assert.Throws<ArgumentNullException>(() => new CooldownMonitor(_monitor, _time, null!));
    }

    [Fact]
    public void DisposeUnsubscribesAndForgetsEverything()
    {
        _cooldown.Configure([Mk4]);
        Raise(Printing(0));
        Raise(Cooling(PrinterState.Finished, End, End));
        Assert.Equal(1, _monitor.SubscriberCount);
        _changed.Clear();

        _cooldown.Dispose();
        _cooldown.Dispose();

        Assert.Equal(0, _monitor.SubscriberCount);
        Assert.Equal(default, _cooldown.GetState("mk4"));
        _cooldown.Configure([Mk4]);
        _monitor.RaiseIgnoringSubscription(_cooldown, Cooling(PrinterState.Finished, End, 10));
        Assert.Equal(default, _cooldown.GetState("mk4"));
        Assert.Empty(_changed);
    }

    [Fact]
    public async Task ConcurrentPrintersAreEachCooledDownExactlyOnce()
    {
        const int PrinterCount = 16;
        PrinterDefinition[] printers = new PrinterDefinition[PrinterCount];
        for (int i = 0; i < PrinterCount; i++)
        {
            printers[i] = Mk4 with { Id = "p" + i, CooldownThresholdCelsius = 30 + (i * 4) };
        }

        _cooldown.Configure(printers);
        using CancellationTokenSource stop = new();

        // Like the monitor: one loop per printer, loops run concurrently, each printer's snapshots in order. Meanwhile the
        // settings are applied again (rename only) and the UI reads the states.
        Task[] loops = new Task[PrinterCount];
        for (int i = 0; i < PrinterCount; i++)
        {
            string id = printers[i].Id;
            loops[i] = Task.Run(
                () =>
                {
                    for (int job = 1; job <= 3; job++)
                    {
                        double offset = job * 1000;
                        Raise(Printing(offset, id, Job(job)));
                        for (double t = offset + End; t < offset + 400; t += PollSeconds)
                        {
                            Raise(Cooling(PrinterState.Finished, offset + End, t, id, Job(job)));
                        }
                    }
                },
                TestContext.Current.CancellationToken);
        }

        Task configure = Task.Run(
            () =>
            {
                int round = 0;
                while (!stop.IsCancellationRequested)
                {
                    _cooldown.Configure([.. printers.Select(p => p with { Name = "n" + round })]);
                    round++;
                }
            },
            TestContext.Current.CancellationToken);
        Task read = Task.Run(
            () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    foreach (PrinterDefinition printer in printers)
                    {
                        CooldownState state = _cooldown.GetState(printer.Id);
                        Assert.InRange(state.Progress, 0, 1);
                    }
                }
            },
            TestContext.Current.CancellationToken);

        await Task.WhenAll(loops).WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        await stop.CancelAsync();
        await Task.WhenAll(configure, read).WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        foreach (PrinterDefinition printer in printers)
        {
            Assert.Equal(3, _cooledDown.Count(e => e.PrinterId == printer.Id));
            Assert.Equal(CooldownPhase.CooledDown, _cooldown.GetState(printer.Id).Phase);
        }

        Assert.Empty(_logger.Entries);
    }

    private static (CooldownPhase Phase, int Threshold) Summary(CooldownState state) => (state.Phase, state.ThresholdCelsius);

    private void Raise(PrinterSnapshot snapshot) => _monitor.Raise(snapshot);

    /// <summary>Simulated snapshots of a print that ended at <paramref name="end"/>, every poll up to <paramref name="until"/>.</summary>
    private void RunCurve(string printerId, double end, double until, double? from = null)
    {
        for (double t = from ?? end; t <= until; t += PollSeconds)
        {
            Raise(Cooling(PrinterState.Finished, end, t, printerId));
        }
    }

    /// <summary>Monitor that raises snapshots on demand, synchronously on the calling thread.</summary>
    private sealed class EventMonitor : IPrinterMonitor
    {
        public event EventHandler<PrinterSnapshot>? SnapshotUpdated;

        public int SubscriberCount => SnapshotUpdated?.GetInvocationList().Length ?? 0;

        public IReadOnlyList<PrinterSnapshot> Snapshots => [];

        public bool TryGetSnapshot(string printerId, [NotNullWhen(true)] out PrinterSnapshot? snapshot)
        {
            snapshot = null;
            return false;
        }

        public Task RunAsync(IReadOnlyList<PrinterDefinition> printers, TimeSpan pollingInterval, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void Raise(PrinterSnapshot snapshot) => SnapshotUpdated?.Invoke(this, snapshot);

        /// <summary>Simulates a publish that started before the handler was removed (the monitor copies its handler list).</summary>
        public void RaiseIgnoringSubscription(CooldownMonitor cooldown, PrinterSnapshot snapshot) =>
            cooldown.OnSnapshotUpdated(this, snapshot);
    }
}
