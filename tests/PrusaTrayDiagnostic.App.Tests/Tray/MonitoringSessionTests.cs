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

    private MonitoringSession Create(Func<ISettingsStore> settingsStoreFactory) =>
        new(settingsStoreFactory, _monitor, _viewModel, _logger);
}
