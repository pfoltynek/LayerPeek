using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

using static PrusaTrayDiagnostic.App.Tests.Tray.TestPrinters;

namespace PrusaTrayDiagnostic.App.Tests.Tray;

/// <summary>
/// The model detected by monitoring (T-039, user decision 2026-10-20): saved in the background only when it changed, without
/// restarting monitoring; image and detected-model changes from the settings window do not restart it either; the load
/// status reaches the orphan sweep.
/// </summary>
public sealed class MonitoringSessionModelTests : IAsyncDisposable
{
    private static readonly PrinterModelCode Mk4SCode = new(1, 4, 1);
    private static readonly PrinterModelCode CoreOneCode = new(7, 1, 0);

    private readonly FakePrinterMonitor _monitor = new();
    private readonly ListLogger<MonitoringSession> _logger = new();
    private readonly RecordingObserver _observer = new();
    private MonitoringSession? _session;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            await _session.DisposeAsync();
        }
    }

    private async Task<(MonitoringSession Session, ModelStore Store)> StartAsync(params PrinterDefinition[] printers)
    {
        ModelStore store = new(AppSettings.Default with { Printers = printers });
        _session = new MonitoringSession(() => store, _monitor, [_observer], _logger);
        _session.Start();
        await _monitor.WaitForRunsAsync(1).WaitAsync(TestTimeouts.Wait, Ct);
        return (_session, store);
    }

    [Fact]
    public async Task NewModelIsSavedInTheBackgroundWithoutARestart()
    {
        (MonitoringSession session, ModelStore store) = await StartAsync(Definition("a", "Alpha"));

        _monitor.RaiseModelDetected("a", Mk4SCode);
        await session.ModelSaves.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal([("a", Mk4SCode)], store.DetectedModelSaves);
        Assert.Single(_monitor.Runs);
        Assert.Equal(["apply", "update"], _observer.Calls);
        Assert.Equal(Mk4SCode, Assert.Single(_observer.Settings!.Printers).DetectedModel);
        Assert.Equal(Mk4SCode, Assert.Single(session.CurrentSettings!.Printers).DetectedModel);
        Assert.Contains(_logger.Entries, static entry => entry.Message.Contains("1.4.1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WriteNamesTheMachineTheCodeWasDetectedOn()
    {
        (MonitoringSession session, ModelStore store) = await StartAsync(Definition("a", "Alpha") with { Address = "printer.local" });

        _monitor.RaiseModelDetected("a", Mk4SCode);
        await session.ModelSaves.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal([("printer.local", ConnectionType.PrusaLink)], store.Machines);
    }

    [Fact]
    public async Task QueuedWriteIsSkippedWhenThePrinterMovedMeanwhile()
    {
        (MonitoringSession session, ModelStore store) = await StartAsync(Definition("a", "Alpha"), Definition("b", "Beta") with { Address = "192.168.1.11" });
        store.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _monitor.RaiseModelDetected("a", Mk4SCode);
        _monitor.RaiseModelDetected("b", CoreOneCode);
        Assert.True(SpinWait.SpinUntil(() => store.Started >= 1, TestTimeouts.Wait));

        // The settings window moves "b" to another address: monitoring restarts, the queued write of "b" no longer applies.
        await session.ApplyAsync(
            AppSettings.Default with { Printers = [Definition("a", "Alpha") with { DetectedModel = Mk4SCode }, Definition("b", "Beta") with { Address = "192.168.1.99" }] },
            credentialsChanged: false).WaitAsync(TestTimeouts.Wait, Ct);
        await _monitor.WaitForRunsAsync(2).WaitAsync(TestTimeouts.Wait, Ct);
        store.Gate.SetResult();
        await session.ModelSaves.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal([("a", Mk4SCode)], store.DetectedModelSaves);
        Assert.Equal(1, store.Started);
    }

    [Fact]
    public async Task UpdateFromTheWindowKeepsAModelDetectedMeanwhile()
    {
        // The window took the running settings before the detection and saves only a new name (no restart).
        (MonitoringSession session, ModelStore store) = await StartAsync(Definition("a", "Alpha"));
        _monitor.RaiseModelDetected("a", Mk4SCode);
        await session.ModelSaves.WaitAsync(TestTimeouts.Wait, Ct);

        await session.ApplyAsync(AppSettings.Default with { Printers = [Definition("a", "Alpha") with { Image = PrinterImageChoice.ForModel(PrinterModel.Mini) }] }, credentialsChanged: false)
            .WaitAsync(TestTimeouts.Wait, Ct);
        await session.ModelSaves.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Single(_monitor.Runs);
        PrinterDefinition current = Assert.Single(session.CurrentSettings!.Printers);
        Assert.Equal(Mk4SCode, current.DetectedModel);
        Assert.Equal(PrinterImageChoice.ForModel(PrinterModel.Mini), current.Image);
        Assert.Equal(Mk4SCode, Assert.Single(_observer.Settings!.Printers).DetectedModel);
        // The window's file lacks the code: it is written again.
        Assert.Equal([("a", Mk4SCode), ("a", Mk4SCode)], store.DetectedModelSaves);
    }

    [Fact]
    public async Task UpdateOfAnotherMachineDoesNotTakeTheModel()
    {
        (MonitoringSession session, _) = await StartAsync(Definition("a", "Alpha"), Definition("b", "Beta") with { Address = "192.168.1.11" });
        _monitor.RaiseModelDetected("a", Mk4SCode);
        await session.ModelSaves.WaitAsync(TestTimeouts.Wait, Ct);

        // Another address restarts monitoring; the new run starts without the old machine's model.
        await session.ApplyAsync(AppSettings.Default with { Printers = [Definition("a", "Alpha") with { Address = "192.168.1.99" }, Definition("b", "Beta") with { Address = "192.168.1.11" }] }, credentialsChanged: false)
            .WaitAsync(TestTimeouts.Wait, Ct);
        await _monitor.WaitForRunsAsync(2).WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Null(session.CurrentSettings!.Printers[0].DetectedModel);
    }

    [Fact]
    public async Task SameModelSavesNothing()
    {
        (MonitoringSession session, ModelStore store) = await StartAsync(Definition("a", "Alpha") with { DetectedModel = Mk4SCode });

        _monitor.RaiseModelDetected("A", Mk4SCode);
        _monitor.RaiseModelDetected("unknown", CoreOneCode);
        await session.ModelSaves.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Empty(store.DetectedModelSaves);
        Assert.Equal(["apply"], _observer.Calls);
    }

    [Fact]
    public async Task ChangedModelReplacesTheStoredOne()
    {
        (MonitoringSession session, ModelStore store) = await StartAsync(Definition("a", "Alpha") with { DetectedModel = Mk4SCode });

        _monitor.RaiseModelDetected("a", CoreOneCode);
        await session.ModelSaves.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal([("a", CoreOneCode)], store.DetectedModelSaves);
    }

    [Fact]
    public async Task SavesRunOneAfterAnother()
    {
        (MonitoringSession session, ModelStore store) = await StartAsync(Definition("a", "Alpha"), Definition("b", "Beta"));
        store.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _monitor.RaiseModelDetected("a", Mk4SCode);
        _monitor.RaiseModelDetected("b", CoreOneCode);
        Assert.True(SpinWait.SpinUntil(() => store.Started >= 1, TestTimeouts.Wait));
        await Task.Delay(50, Ct);
        Assert.Equal(1, store.Started);
        store.Gate.SetResult();
        await session.ModelSaves.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal([("a", Mk4SCode), ("b", CoreOneCode)], store.DetectedModelSaves);
        Assert.Equal(2, session.CurrentSettings!.Printers.Count(static printer => printer.DetectedModel is not null));
    }

    [Fact]
    public async Task FailedSaveIsLoggedAndMonitoringGoesOn()
    {
        (MonitoringSession session, ModelStore store) = await StartAsync(Definition("a", "Alpha"));
        store.Failure = new IOException("disk full");

        _monitor.RaiseModelDetected("a", Mk4SCode);
        await session.ModelSaves.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Contains(_logger.Entries, static entry => entry.Exception is IOException);
        Assert.Single(_monitor.Runs);
        // Shown anyway (the tray knows the model); the next run tries to save it again.
        Assert.Equal(Mk4SCode, session.CurrentSettings!.Printers[0].DetectedModel);
    }

    [Fact]
    public async Task ReportOfARunBeingReplacedIsDropped()
    {
        // The run of the old address ends; until the new one starts, a late report must not reach the new address.
        BlockingRun blocking = new(_monitor);
        (MonitoringSession session, ModelStore store) = await StartAsync(Definition("a", "Alpha"));

        Task applied = session.ApplyAsync(AppSettings.Default with { Printers = [Definition("a", "Alpha") with { Address = "192.168.1.99" }] }, credentialsChanged: false);
        _monitor.RaiseModelDetected("a", Mk4SCode);
        blocking.Release();
        await applied.WaitAsync(TestTimeouts.Wait, Ct);
        await _monitor.WaitForRunsAsync(2).WaitAsync(TestTimeouts.Wait, Ct);
        await session.ModelSaves.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Empty(store.DetectedModelSaves);
        Assert.Null(Assert.Single(session.CurrentSettings!.Printers).DetectedModel);
    }

    [Fact]
    public async Task ImageOrModelChangeFromTheWindowDoesNotRestart()
    {
        (MonitoringSession session, _) = await StartAsync(Definition("a", "Alpha"));
        AppSettings changed = AppSettings.Default with
        {
            Printers = [Definition("a", "Alpha") with { DetectedModel = Mk4SCode, Image = PrinterImageChoice.ForModel(PrinterModel.Mini) }],
        };

        await session.ApplyAsync(changed, credentialsChanged: false).WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Single(_monitor.Runs);
        Assert.Equal(["apply", "update"], _observer.Calls);
        Assert.Same(changed, _observer.Settings);
    }

    [Fact]
    public async Task OtherPrinterChangesStillRestart()
    {
        (MonitoringSession session, _) = await StartAsync(Definition("a", "Alpha"));

        await session.ApplyAsync(AppSettings.Default with { Printers = [Definition("a", "Alpha") with { Address = "192.168.1.99", DetectedModel = Mk4SCode }] }, credentialsChanged: false)
            .WaitAsync(TestTimeouts.Wait, Ct);
        await _monitor.WaitForRunsAsync(2).WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal(2, _monitor.Runs.Count);
    }

    [Fact]
    public async Task DisposedSessionNoLongerListens()
    {
        (MonitoringSession session, ModelStore store) = await StartAsync(Definition("a", "Alpha"));
        Assert.Equal(1, _monitor.ModelSubscriberCount);

        await session.DisposeAsync();
        _session = null;
        _monitor.RaiseModelDetected("a", Mk4SCode);

        Assert.Equal(0, _monitor.ModelSubscriberCount);
        Assert.Empty(store.DetectedModelSaves);
    }

    [Theory]
    [InlineData(SettingsLoadStatus.Loaded)]
    [InlineData(SettingsLoadStatus.NotFound)]
    [InlineData(SettingsLoadStatus.Corrupted)]
    [InlineData(SettingsLoadStatus.Unreadable)]
    public async Task LoadReportsItsStatus(SettingsLoadStatus status)
    {
        AppSettings settings = AppSettings.Default with { Printers = [Definition("a", "Alpha")] };
        FakeSettingsStore store = new(_ => Task.FromResult(new SettingsLoadResult { Settings = settings, Status = status, StoredPrinterIds = ["a", "skipped"] }));

        (AppSettings loaded, SettingsLoadStatus reported, IReadOnlyList<string> storedIds) = await MonitoringSession.LoadSettingsWithStatusAsync(() => store, _logger, Ct);

        Assert.Same(settings, loaded);
        Assert.Equal(status, reported);
        Assert.Equal(["a", "skipped"], storedIds);
    }

    [Fact]
    public async Task UnavailableStoreLoadsAsUnreadable()
    {
        (AppSettings loaded, SettingsLoadStatus status, IReadOnlyList<string> storedIds) = await MonitoringSession.LoadSettingsWithStatusAsync(() => throw new InvalidOperationException("no folder"), _logger, Ct);

        Assert.Same(AppSettings.Default, loaded);
        Assert.Equal(SettingsLoadStatus.Unreadable, status);
        Assert.Empty(storedIds);
    }

    /// <summary>Settings store that records detected-model saves; a save can wait for <see cref="Gate"/> or fail.</summary>
    private sealed class ModelStore(AppSettings settings) : ISettingsStore
    {
        private readonly Lock _lock = new();
        private readonly List<(string, PrinterModelCode)> _saves = [];
        private readonly List<(string, ConnectionType)> _machines = [];
        private int _started;

        public TaskCompletionSource? Gate { get; set; }

        public Exception? Failure { get; set; }

        public int Started => Volatile.Read(ref _started);

        /// <summary>Address and connection type each save expected, in save order.</summary>
        public IReadOnlyList<(string, ConnectionType)> Machines
        {
            get
            {
                lock (_lock)
                {
                    return [.. _machines];
                }
            }
        }

        public IReadOnlyList<(string, PrinterModelCode)> DetectedModelSaves
        {
            get
            {
                lock (_lock)
                {
                    return [.. _saves];
                }
            }
        }

        public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SettingsLoadResult { Settings = settings, Status = SettingsLoadStatus.Loaded });

        public Task SaveAsync(AppSettings settings1, CancellationToken cancellationToken) => throw new NotSupportedException("Only the detected model is saved.");

        public async Task<bool> SetDetectedModelAsync(string printerId, PrinterModelCode code, string expectedAddress, ConnectionType expectedConnectionType, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _started);
            if (Gate is { } gate)
            {
                await gate.Task.WaitAsync(TestTimeouts.Wait, cancellationToken);
            }

            if (Failure is { } failure)
            {
                throw failure;
            }

            lock (_lock)
            {
                _saves.Add((printerId, code));
                _machines.Add((expectedAddress, expectedConnectionType));
            }

            return true;
        }
    }

    /// <summary>Makes the current run wait after its cancellation until released (a run that is still ending).</summary>
    private sealed class BlockingRun
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BlockingRun(FakePrinterMonitor monitor) => monitor.RunBehavior = async token =>
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            catch (OperationCanceledException)
            {
                await _release.Task;
            }
        };

        public void Release() => _release.TrySetResult();
    }

    private sealed class RecordingObserver : IMonitoringObserver
    {
        private readonly Lock _lock = new();
        private readonly List<string> _calls = [];

        public List<string> Calls
        {
            get
            {
                lock (_lock)
                {
                    return [.. _calls];
                }
            }
        }

        public AppSettings? Settings { get; private set; }

        public void ApplySettings(AppSettings settings)
        {
            Settings = settings;
            Add("apply");
        }

        public void ReportMonitoringFailed() => Add("failed");

        public void UpdateSettings(AppSettings settings)
        {
            Settings = settings;
            Add("update");
        }

        private void Add(string call)
        {
            lock (_lock)
            {
                _calls.Add(call);
            }
        }
    }
}
