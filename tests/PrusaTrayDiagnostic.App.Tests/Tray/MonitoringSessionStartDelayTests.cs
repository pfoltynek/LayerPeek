using System.Diagnostics;

using Microsoft.Extensions.Time.Testing;

using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

using static PrusaTrayDiagnostic.App.Tests.Tray.TestPrinters;

namespace PrusaTrayDiagnostic.App.Tests.Tray;

/// <summary>
/// The delayed monitoring start of the Debug test mode (T-067): the tray exists at once and stays "Starting…" until the
/// delay is over; a stop ends the wait, settings applied meanwhile are used for the first run. The delay runs on a fake
/// clock, so a slow machine can never make monitoring start early.
/// </summary>
public sealed class MonitoringSessionStartDelayTests : IAsyncDisposable
{
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(5);

    private readonly CultureScope _culture = new("en-US");
    private readonly FakePrinterMonitor _monitor = new();
    private readonly ManualDispatcher _dispatcher = new();
    private readonly ListLogger<MonitoringSession> _logger = new();
    private readonly FakeTimeProvider _time = new();
    private readonly TrayStatusViewModel _viewModel;
    private readonly MonitoringSession _session;

    public MonitoringSessionStartDelayTests()
    {
        _viewModel = new TrayStatusViewModel(_monitor, _dispatcher);
        _session = new MonitoringSession(() => FakeSettingsStore.Returning(AppSettings.Default), _monitor, [_viewModel], _logger, timeProvider: _time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _session.DisposeAsync();
        _viewModel.Dispose();
        _culture.Dispose();
    }

    [Fact]
    public async Task TheTrayStaysStartingUntilTheDelayIsOver()
    {
        AppSettings settings = new() { Printers = [Definition()] };

        _session.Start(settings, Delay);
        // Real time passes, the fake clock almost reaches the delay: still nothing runs.
        await Task.Delay(100, Ct);
        _time.Advance(Delay - TimeSpan.FromMilliseconds(1));
        await Task.Delay(100, Ct);
        _dispatcher.RunAll();

        Assert.Empty(_monitor.Runs);
        Assert.Null(_session.CurrentSettings);
        Assert.Contains("Starting…", _viewModel.ToolTipText, StringComparison.Ordinal);

        (IReadOnlyList<PrinterDefinition> printers, _) = await AdvanceUntilStartedAsync();
        Assert.Equal(settings.Printers, printers);
        _dispatcher.RunAll();
        Assert.Equal("MK4\nConnecting…", _viewModel.ToolTipText);
    }

    [Fact]
    public async Task WithoutADelayMonitoringStartsAtOnce()
    {
        _session.Start(new AppSettings { Printers = [Definition()] }, TimeSpan.Zero);

        await _monitor.Started.WaitAsync(TestTimeouts.Wait, Ct);
    }

    [Fact]
    public async Task AStopDuringTheDelayStartsNothing()
    {
        _session.Start(new AppSettings { Printers = [Definition()] }, TimeSpan.FromSeconds(60));

        await _session.StopAsync(TimeSpan.FromSeconds(3)).WaitAsync(TestTimeouts.Wait, Ct);

        Assert.True(_session.Completion.IsCompleted);
        Assert.Empty(_monitor.Runs);
    }

    [Fact]
    public async Task SettingsAppliedDuringTheDelayAreTheFirstRun()
    {
        AppSettings saved = new() { Printers = [Definition("xl", "XL")], PollingInterval = TimeSpan.FromSeconds(11) };
        _session.Start(new AppSettings { Printers = [Definition()] }, Delay);

        Task applied = _session.ApplyAsync(saved, credentialsChanged: false);
        (IReadOnlyList<PrinterDefinition> printers, TimeSpan interval) = await AdvanceUntilStartedAsync();

        Assert.Equal(saved.Printers, printers);
        Assert.Equal(TimeSpan.FromSeconds(11), interval);
        await applied.WaitAsync(TestTimeouts.Wait, Ct);
        Assert.Single(_monitor.Runs);
    }

    [Fact]
    public void ANegativeDelayIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _session.Start(AppSettings.Default, TimeSpan.FromMilliseconds(-1)));
    }

    /// <summary>
    /// Moves the fake clock on until monitoring starts (the delay's timer is created on a background thread, possibly only
    /// after the first move).
    /// </summary>
    private async Task<(IReadOnlyList<PrinterDefinition> Printers, TimeSpan Interval)> AdvanceUntilStartedAsync()
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        while (!_monitor.Started.IsCompleted)
        {
            Assert.True(elapsed.Elapsed < TestTimeouts.Wait, "Monitoring did not start after the delay.");
            _time.Advance(Delay);
            await Task.Delay(10, Ct);
        }

        return await _monitor.Started;
    }
}
