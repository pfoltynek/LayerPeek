using LayerPeek.App.Cooldown;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tray;
using LayerPeek.Core.Cooldown;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Cooldown;

public sealed class CooldownObserverTests : IDisposable
{
    private readonly FakePrinterMonitor _monitor = new();
    private readonly CooldownMonitor _cooldown;
    private readonly CooldownObserver _observer;

    public CooldownObserverTests()
    {
        _cooldown = new CooldownMonitor(_monitor, TimeProvider.System, new ListLogger<CooldownMonitor>());
        _observer = new CooldownObserver(_cooldown);
    }

    public void Dispose() => _cooldown.Dispose();

    [Fact]
    public void AppliedSettingsConfigureThePrintersWithTheirThresholds()
    {
        _observer.ApplySettings(new AppSettings { Printers = [Definition() with { CooldownThresholdCelsius = 80 }] });

        RaiseEndedPrint(nozzle: 120);

        CooldownState state = _cooldown.GetState("mk4");
        Assert.Equal((CooldownPhase.Cooling, 80), (state.Phase, state.ThresholdCelsius));
    }

    [Fact]
    public void AppliedSettingsPassThePollingInterval()
    {
        // At a 5 minute polling interval, 5 minutes between two snapshots is no gap: the print end was observed.
        _observer.ApplySettings(new AppSettings { Printers = [Definition()], PollingInterval = TimeSpan.FromMinutes(5) });
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(50)) with { Telemetry = Hot(215, 215) });

        _monitor.Raise(Snapshot(PrinterState.Finished, Job(100)) with { Timestamp = Now.AddMinutes(5), Telemetry = Hot(200, 0) });

        Assert.Equal(Now.AddMinutes(5), _cooldown.GetState("mk4").PrintEndedAt);
    }

    [Fact]
    public void UpdatedThresholdApplies()
    {
        _observer.ApplySettings(new AppSettings { Printers = [Definition()] });
        RaiseEndedPrint(nozzle: 70);
        Assert.Equal(CooldownPhase.Cooling, _cooldown.GetState("mk4").Phase);

        ((IMonitoringObserver)_observer).UpdateSettings(new AppSettings { Printers = [Definition() with { CooldownThresholdCelsius = 75 }] });

        // The printer kept its state and the last two readings (72, 70 °C) meet the new threshold.
        Assert.Equal((CooldownPhase.CooledDown, 75), (_cooldown.GetState("mk4").Phase, _cooldown.GetState("mk4").ThresholdCelsius));
    }

    [Fact]
    public void MonitoringFailureKeepsTheStates()
    {
        _observer.ApplySettings(new AppSettings { Printers = [Definition()] });
        RaiseEndedPrint(nozzle: 120);

        _observer.ReportMonitoringFailed();

        Assert.Equal(CooldownPhase.Cooling, _cooldown.GetState("mk4").Phase);
    }

    [Fact]
    public void NullArgumentsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new CooldownObserver(null!));
        Assert.Throws<ArgumentNullException>(() => _observer.ApplySettings(null!));
    }

    /// <summary>A print, then two "finished" readings: 2 °C above <paramref name="nozzle"/> and <paramref name="nozzle"/>.</summary>
    private void RaiseEndedPrint(double nozzle)
    {
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(50)) with { Telemetry = Hot(215, 215) });
        _monitor.Raise(Snapshot(PrinterState.Finished, Job(100)) with { Timestamp = Now.AddSeconds(5), Telemetry = Hot(200, 0) });
        _monitor.Raise(Snapshot(PrinterState.Finished, Job(100)) with { Timestamp = Now.AddSeconds(10), Telemetry = Hot(nozzle + 2, 0) });
        _monitor.Raise(Snapshot(PrinterState.Finished, Job(100)) with { Timestamp = Now.AddSeconds(15), Telemetry = Hot(nozzle, 0) });
    }

    private static Telemetry Hot(double nozzle, double target) => new() { Nozzle = new Temperature(nozzle, target), Bed = new Temperature(55, 0) };
}
