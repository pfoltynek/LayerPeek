using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

using static PrusaTrayDiagnostic.App.Tests.Tray.TestPrinters;

namespace PrusaTrayDiagnostic.App.Tests.Tray;

public sealed class MonitoringSessionTests : IAsyncDisposable
{
    private readonly CultureScope _culture = new("en-US");
    private readonly FakePrinterMonitor _monitor = new();
    private readonly ManualDispatcher _dispatcher = new();
    private readonly ListLogger<MonitoringSession> _logger = new();
    private readonly TrayStatusViewModel _viewModel;
    private MonitoringSession? _session;

    public MonitoringSessionTests() => _viewModel = new TrayStatusViewModel(_monitor, _dispatcher);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            await _session.DisposeAsync();
        }

        _viewModel.Dispose();
        _culture.Dispose();
    }

    [Fact]
    public async Task StartsMonitorWithLoadedSettings()
    {
        AppSettings settings = new() { Printers = [Definition()], PollingInterval = TimeSpan.FromSeconds(7) };
        _session = Create(() => FakeSettingsStore.Returning(settings));

        _session.Start();
        (IReadOnlyList<PrinterDefinition> printers, TimeSpan interval) = await _monitor.Started.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal(settings.Printers, printers);
        Assert.Equal(TimeSpan.FromSeconds(7), interval);
        _dispatcher.RunAll();
        Assert.Equal("MK4\nConnecting…", _viewModel.ToolTipText);
    }

    [Fact]
    public async Task UnavailableSettingsStoreFallsBackToDefaults()
    {
        _session = Create(() => throw new InvalidOperationException("no app data folder"));

        _session.Start();
        (IReadOnlyList<PrinterDefinition> printers, TimeSpan interval) = await _monitor.Started.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Empty(printers);
        Assert.Equal(AppSettings.DefaultPollingInterval, interval);
        _dispatcher.RunAll();
        Assert.Equal("Prusa Tray Diagnostic\nNo printer configured", _viewModel.ToolTipText);
    }

    [Fact]
    public async Task FailingSettingsLoadFallsBackToDefaults()
    {
        _session = Create(() => new FakeSettingsStore(_ => Task.FromException<SettingsLoadResult>(new IOException("disk gone"))));

        _session.Start();
        (IReadOnlyList<PrinterDefinition> printers, _) = await _monitor.Started.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Empty(printers);
    }

    [Fact]
    public async Task MonitorFailureIsShownInTray()
    {
        _monitor.RunBehavior = _ => Task.FromException(new InvalidOperationException("boom"));
        _session = Create(() => FakeSettingsStore.Returning(new AppSettings { Printers = [Definition()] }));

        _session.Start();
        await _session.Completion.WaitAsync(TestTimeouts.Wait, Ct);
        _dispatcher.RunAll();

        Assert.Equal(TrayIconKey.For(TrayStatus.Error, null), _viewModel.IconKey);
    }

    [Fact]
    public async Task EveryObserverReceivesPrintersAndFailure()
    {
        RecordingObserver second = new();
        _monitor.RunBehavior = _ => Task.FromException(new InvalidOperationException("boom"));
        _session = new MonitoringSession(
            () => FakeSettingsStore.Returning(new AppSettings { Printers = [Definition()] }),
            _monitor,
            [_viewModel, second],
            _logger);

        _session.Start();
        await _session.Completion.WaitAsync(TestTimeouts.Wait, Ct);
        _dispatcher.RunAll();

        Assert.Equal(TrayIconKey.For(TrayStatus.Error, null), _viewModel.IconKey);
        Assert.Equal(["printers: mk4", "failed"], second.Calls);
    }

    [Fact]
    public async Task ObserversReceiveLoadedSettingsIncludingNotifications()
    {
        RecordingObserver observer = new();
        AppSettings settings = new() { Printers = [Definition()], Notifications = new NotificationSettings { RemainingTimeMinutes = 7 } };
        _session = new MonitoringSession(() => FakeSettingsStore.Returning(settings), _monitor, [observer], _logger);

        _session.Start();
        await _monitor.Started.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Same(settings, observer.Settings);
    }

    [Fact]
    public async Task StopCancelsMonitorAndCompletes()
    {
        _session = Create(() => FakeSettingsStore.Returning(new AppSettings { Printers = [Definition()] }));
        _session.Start();
        await _monitor.Started.WaitAsync(TestTimeouts.Wait, Ct);

        await _session.StopAsync(TestTimeouts.Wait);

        Assert.True(_session.Completion.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task StopGivesUpOnMonitorThatIgnoresCancellation()
    {
        TaskCompletionSource never = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _monitor.RunBehavior = _ => never.Task;
            _session = Create(() => FakeSettingsStore.Returning(new AppSettings { Printers = [Definition()] }));
            _session.Start();
            await _monitor.Started.WaitAsync(TestTimeouts.Wait, Ct);

            await _session.StopAsync(TimeSpan.FromMilliseconds(50)).WaitAsync(TestTimeouts.Wait, Ct);

            Assert.False(_session.Completion.IsCompleted);
            Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning);
        }
        finally
        {
            never.TrySetResult();
        }
    }

    [Fact]
    public async Task DisposeAfterTimedOutStopReturnsImmediately()
    {
        TaskCompletionSource never = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _monitor.RunBehavior = _ => never.Task;
            _session = Create(() => FakeSettingsStore.Returning(new AppSettings { Printers = [Definition()] }));
            _session.Start();
            await _monitor.Started.WaitAsync(TestTimeouts.Wait, Ct);
            await _session.StopAsync(TimeSpan.FromMilliseconds(50)).WaitAsync(TestTimeouts.Wait, Ct);

            ValueTask dispose = _session.DisposeAsync();

            // Quitting already waited once; disposal must not wait for the stuck monitor again.
            Assert.True(dispose.IsCompleted);
            await dispose;
        }
        finally
        {
            never.TrySetResult();
        }
    }

    [Fact]
    public async Task RequestStopCancelsWithoutWaitingOrLogging()
    {
        TaskCompletionSource never = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            CancellationToken monitorToken = default;
            _monitor.RunBehavior = ct =>
            {
                monitorToken = ct;
                return never.Task;
            };
            _session = Create(() => FakeSettingsStore.Returning(new AppSettings { Printers = [Definition()] }));
            _session.Start();
            await _monitor.Started.WaitAsync(TestTimeouts.Wait, Ct);

            _session.RequestStop();

            await Task.Run(
                async () =>
                {
                    while (!monitorToken.IsCancellationRequested)
                    {
                        await Task.Delay(10, Ct);
                    }
                },
                Ct).WaitAsync(TestTimeouts.Wait, Ct);
            Assert.Empty(_logger.Entries);
        }
        finally
        {
            never.TrySetResult();
        }
    }

    [Fact]
    public async Task StopWithZeroTimeoutDoesNotLog()
    {
        TaskCompletionSource never = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _monitor.RunBehavior = _ => never.Task;
            _session = Create(() => FakeSettingsStore.Returning(new AppSettings { Printers = [Definition()] }));
            _session.Start();
            await _monitor.Started.WaitAsync(TestTimeouts.Wait, Ct);

            await _session.StopAsync(TimeSpan.Zero).WaitAsync(TestTimeouts.Wait, Ct);

            Assert.Empty(_logger.Entries);
        }
        finally
        {
            never.TrySetResult();
        }
    }

    [Fact]
    public async Task StopBeforeStartIsHarmless()
    {
        _session = Create(() => FakeSettingsStore.Returning(AppSettings.Default));

        await _session.StopAsync(TestTimeouts.Wait);

        Assert.True(_session.Completion.IsCompleted);
    }

    [Fact]
    public async Task RepeatedStopIsHarmless()
    {
        _session = Create(() => FakeSettingsStore.Returning(AppSettings.Default));
        _session.Start();

        await _session.StopAsync(TestTimeouts.Wait);
        await _session.StopAsync(TestTimeouts.Wait);

        Assert.True(_session.Completion.IsCompleted);
    }

    [Fact]
    public async Task SynchronousDisposeStopsMonitor()
    {
        _session = Create(() => FakeSettingsStore.Returning(new AppSettings { Printers = [Definition()] }));
        _session.Start();
        await _monitor.Started.WaitAsync(TestTimeouts.Wait, Ct);

        _session.Dispose();

        await _session.Completion.WaitAsync(TestTimeouts.Wait, Ct);
        // Disposing again (asynchronously, from the test cleanup) must be harmless.
    }

    [Fact]
    public void StartTwiceThrows()
    {
        _session = Create(() => FakeSettingsStore.Returning(AppSettings.Default));
        _session.Start();

        Assert.Throws<InvalidOperationException>(_session.Start);
    }

    [Fact]
    public async Task AppliedSettingsRestartTheMonitor()
    {
        RecordingObserver observer = new();
        _session = new MonitoringSession(() => FakeSettingsStore.Returning(new AppSettings { Printers = [Definition("a", "Alpha")] }), _monitor, [observer], _logger);
        _session.Start();
        await _monitor.WaitForRunsAsync(1).WaitAsync(TestTimeouts.Wait, Ct);
        AppSettings saved = new() { Printers = [Definition("a", "Alpha"), Definition("b", "Beta")], PollingInterval = TimeSpan.FromSeconds(9) };

        _ = _session.ApplyAsync(saved, credentialsChanged: false);
        await _monitor.WaitForRunsAsync(2).WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal(["a", "b"], _monitor.Runs[1].Printers.Select(static p => p.Id));
        Assert.Equal(TimeSpan.FromSeconds(9), _monitor.Runs[1].Interval);
        Assert.Equal(["printers: a", "printers: a,b"], observer.Calls);
        Assert.Same(saved, observer.Settings);
    }

    [Fact]
    public async Task OnlyTheLatestOfQuicklyAppliedSettingsIsUsed()
    {
        TaskCompletionSource firstRunCancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseFirstRun = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int runs = 0;
        _monitor.RunBehavior = async ct =>
        {
            if (Interlocked.Increment(ref runs) == 1)
            {
                // The first run ends only when the test lets it, so that two applies queue up meanwhile.
                ct.Register(() => firstRunCancelled.TrySetResult());
                await releaseFirstRun.Task;
                return;
            }

            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        };
        _session = Create(() => FakeSettingsStore.Returning(AppSettings.Default));
        _session.Start();
        await _monitor.WaitForRunsAsync(1).WaitAsync(TestTimeouts.Wait, Ct);

        _ = _session.ApplyAsync(new AppSettings { Printers = [Definition("a", "Alpha")] }, credentialsChanged: false);
        _ = _session.ApplyAsync(new AppSettings { Printers = [Definition("b", "Beta")] }, credentialsChanged: false);
        await firstRunCancelled.Task.WaitAsync(TestTimeouts.Wait, Ct);
        releaseFirstRun.SetResult();
        await _monitor.WaitForRunsAsync(2).WaitAsync(TestTimeouts.Wait, Ct);
        await Task.Delay(50, Ct);

        Assert.Equal(2, _monitor.Runs.Count);
        Assert.Equal("b", Assert.Single(_monitor.Runs[1].Printers).Id);
    }

    [Fact]
    public async Task SettingsAppliedBeforeLoadingFinishedWin()
    {
        TaskCompletionSource<SettingsLoadResult> load = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _session = Create(() => new FakeSettingsStore(_ => load.Task));
        _session.Start();

        _ = _session.ApplyAsync(new AppSettings { Printers = [Definition("new", "New")] }, credentialsChanged: false);
        load.SetResult(new SettingsLoadResult { Settings = new AppSettings { Printers = [Definition("old", "Old")] }, Status = SettingsLoadStatus.Loaded });
        await _monitor.WaitForRunsAsync(1).WaitAsync(TestTimeouts.Wait, Ct);
        await Task.Delay(50, Ct);

        Assert.Equal("new", Assert.Single(_monitor.Runs[^1].Printers).Id);
    }

    [Fact]
    public async Task AppliedSettingsRestartMonitoringAfterAFailure()
    {
        int runs = 0;
        _monitor.RunBehavior = ct => Interlocked.Increment(ref runs) == 1
            ? Task.FromException(new InvalidOperationException("boom"))
            : Task.Delay(Timeout.Infinite, ct).ContinueWith(static _ => { }, TaskScheduler.Default);
        RecordingObserver observer = new();
        _session = new MonitoringSession(() => FakeSettingsStore.Returning(new AppSettings { Printers = [Definition("a", "Alpha")] }), _monitor, [observer], _logger);
        _session.Start();
        await _session.Completion.WaitAsync(TestTimeouts.Wait, Ct);

        _ = _session.ApplyAsync(new AppSettings { Printers = [Definition("a", "Alpha")] }, credentialsChanged: false);
        await _monitor.WaitForRunsAsync(2).WaitAsync(TestTimeouts.Wait, Ct);

        // Applying clears the failure shown in the tray.
        Assert.Equal(["printers: a", "failed", "printers: a"], observer.Calls);
        await _session.StopAsync(TestTimeouts.Wait);
        Assert.True(_session.Completion.IsCompleted);
    }

    [Fact]
    public async Task SettingsAppliedAfterStopAreIgnored()
    {
        _session = Create(() => FakeSettingsStore.Returning(AppSettings.Default));
        _session.Start();
        await _monitor.WaitForRunsAsync(1).WaitAsync(TestTimeouts.Wait, Ct);
        await _session.StopAsync(TestTimeouts.Wait);

        _ = _session.ApplyAsync(new AppSettings { Printers = [Definition()] }, credentialsChanged: false);
        await Task.Delay(50, Ct);

        Assert.Single(_monitor.Runs);
        Assert.True(_session.Completion.IsCompleted);
    }

    [Fact]
    public async Task StopDuringRestartCompletes()
    {
        _session = Create(() => FakeSettingsStore.Returning(AppSettings.Default));
        _session.Start();
        await _monitor.WaitForRunsAsync(1).WaitAsync(TestTimeouts.Wait, Ct);

        _ = _session.ApplyAsync(new AppSettings { Printers = [Definition()] }, credentialsChanged: false);
        await _session.StopAsync(TestTimeouts.Wait);

        Assert.True(_session.Completion.IsCompletedSuccessfully);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task ApplyCompletesOnlyWhenTheOldRunEnded()
    {
        TaskCompletionSource releaseOldRun = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int runs = 0;
        _monitor.RunBehavior = async ct =>
        {
            if (Interlocked.Increment(ref runs) == 1)
            {
                // Like a printer client still finishing its request after the cancellation.
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                await releaseOldRun.Task;
                return;
            }

            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        };
        _session = Create(() => FakeSettingsStore.Returning(new AppSettings { Printers = [Definition("a", "Alpha")] }));
        _session.Start();
        await _monitor.WaitForRunsAsync(1).WaitAsync(TestTimeouts.Wait, Ct);

        Task applied = _session.ApplyAsync(new AppSettings { Printers = [Definition("b", "Beta")] }, credentialsChanged: false);
        await Task.Delay(50, Ct);
        Assert.False(applied.IsCompleted);

        releaseOldRun.SetResult();
        await applied.WaitAsync(TestTimeouts.Wait, Ct);
    }

    [Fact]
    public async Task ApplyNeverFaultsAndIsDoneWhenNothingRuns()
    {
        _session = Create(() => FakeSettingsStore.Returning(AppSettings.Default));

        Assert.True(_session.ApplyAsync(new AppSettings(), credentialsChanged: false).IsCompletedSuccessfully);

        _session.Start();
        await _monitor.WaitForRunsAsync(1).WaitAsync(TestTimeouts.Wait, Ct);
        await _session.StopAsync(TestTimeouts.Wait);
        Assert.True(_session.ApplyAsync(new AppSettings { Printers = [Definition()] }, credentialsChanged: false).IsCompletedSuccessfully);
    }

    [Fact]
    public async Task NotificationOnlyChangeKeepsTheMonitorRunning()
    {
        RecordingObserver observer = new();
        AppSettings loaded = new() { Printers = [Definition("a", "Alpha")], PollingInterval = TimeSpan.FromSeconds(7) };
        _session = new MonitoringSession(() => FakeSettingsStore.Returning(loaded), _monitor, [observer], _logger);
        _session.Start();
        await _monitor.WaitForRunsAsync(1).WaitAsync(TestTimeouts.Wait, Ct);
        AppSettings saved = loaded with { Notifications = new NotificationSettings { Sound = NotificationSound.All, PrintStarted = false } };

        Task applied = _session.ApplyAsync(saved, credentialsChanged: false);
        await Task.Delay(50, Ct);

        // Same printers (id, name, address, type) and interval: no restart, so the tray keeps showing the current state.
        Assert.True(applied.IsCompletedSuccessfully);
        Assert.Single(_monitor.Runs);
        Assert.Equal(["printers: a", "update"], observer.Calls);
        Assert.Same(saved, observer.Settings);

        // The next real change compares against the updated settings and restarts.
        _ = _session.ApplyAsync(saved with { Printers = [Definition("a", "Renamed")] }, credentialsChanged: false);
        await _monitor.WaitForRunsAsync(2).WaitAsync(TestTimeouts.Wait, Ct);
        Assert.Equal(["printers: a", "update", "printers: a"], observer.Calls);
    }

    [Fact]
    public async Task ChangedPasswordRestartsEvenWithUnchangedSettings()
    {
        // A corrected password must be used now: the running client would re-read it only after the next 401, and an
        // unauthorized printer is on backoff for up to minutes.
        AppSettings loaded = new() { Printers = [Definition("a", "Alpha")] };
        _session = Create(() => FakeSettingsStore.Returning(loaded));
        _session.Start();
        await _monitor.WaitForRunsAsync(1).WaitAsync(TestTimeouts.Wait, Ct);

        Task applied = _session.ApplyAsync(loaded, credentialsChanged: true);

        await _monitor.WaitForRunsAsync(2).WaitAsync(TestTimeouts.Wait, Ct);
        await applied.WaitAsync(TestTimeouts.Wait, Ct);
        Assert.Equal(["a"], _monitor.Runs[1].Printers.Select(static p => p.Id));
    }

    [Fact]
    public async Task NotificationUpdateDuringRunStartIsNotOverwrittenByOlderSettings()
    {
        AppSettings loaded = new() { Printers = [Definition("a", "Alpha")] };
        AppSettings updated = loaded with { Notifications = new NotificationSettings { Sound = NotificationSound.All } };
        BlockingObserver observer = new();
        _session = new MonitoringSession(() => FakeSettingsStore.Returning(loaded), _monitor, [observer], _logger);
        try
        {
            _session.Start();
            await observer.FirstApplyStarted.WaitAsync(TestTimeouts.Wait, Ct);

            // The run is starting and its observer is still receiving the loaded settings when a notification-only save
            // arrives. Release the observer only once the update is stored in the session (the race is then certain).
            Task update = Task.Run(() => _session.ApplyAsync(updated, credentialsChanged: false), Ct);
            await WaitUntilAsync(() => ReferenceEquals(_session.CurrentSettings, updated) || update.IsCompleted);
            observer.ReleaseFirstApply();
            await update.WaitAsync(TestTimeouts.Wait, Ct);
            await _monitor.WaitForRunsAsync(1).WaitAsync(TestTimeouts.Wait, Ct);

            // The older settings of the run start come first, the update last; nothing restarted.
            Assert.Equal([("apply", loaded), ("update", updated)], observer.Deliveries);
            Assert.Single(_monitor.Runs);
        }
        finally
        {
            // Never leave the session thread blocked in the observer, even when an assertion failed.
            observer.ReleaseFirstApply();
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TestTimeouts.Wait);
        while (!condition())
        {
            await Task.Delay(1, timeout.Token);
        }
    }

    private MonitoringSession Create(Func<ISettingsStore> settingsStoreFactory) =>
        new(settingsStoreFactory, _monitor, [_viewModel], _logger);

    /// <summary>Its first <see cref="ApplySettings"/> waits until the test releases it.</summary>
    private sealed class BlockingObserver : IMonitoringObserver
    {
        private readonly TaskCompletionSource _firstApplyStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _applies;
        private readonly Lock _lock = new();
        private readonly List<(string Kind, AppSettings Settings)> _deliveries = [];

        public Task FirstApplyStarted => _firstApplyStarted.Task;

        public IReadOnlyList<(string Kind, AppSettings Settings)> Deliveries
        {
            get
            {
                lock (_lock)
                {
                    return [.. _deliveries];
                }
            }
        }

        public void ApplySettings(AppSettings settings)
        {
            if (Interlocked.Increment(ref _applies) == 1)
            {
                _firstApplyStarted.TrySetResult();
                // Blocks on purpose: observers are called synchronously by the session.
                _release.Task.Wait(TestTimeouts.Wait);
            }

            Record("apply", settings);
        }

        public void UpdateSettings(AppSettings settings) => Record("update", settings);

        public void ReportMonitoringFailed()
        {
        }

        public void ReleaseFirstApply() => _release.TrySetResult();

        private void Record(string kind, AppSettings settings)
        {
            lock (_lock)
            {
                _deliveries.Add((kind, settings));
            }
        }
    }

    private sealed class RecordingObserver : IMonitoringObserver
    {
        public List<string> Calls { get; } = [];

        public AppSettings? Settings { get; private set; }

        public void ApplySettings(AppSettings settings)
        {
            Settings = settings;
            Calls.Add("printers: " + string.Join(",", settings.Printers.Select(printer => printer.Id)));
        }

        public void ReportMonitoringFailed() => Calls.Add("failed");

        public void UpdateSettings(AppSettings settings)
        {
            Settings = settings;
            Calls.Add("update");
        }
    }
}
