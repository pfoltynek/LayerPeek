using PrusaTrayDiagnostic.App.History;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.Core.History;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

using static PrusaTrayDiagnostic.App.Tests.Tray.TestPrinters;

namespace PrusaTrayDiagnostic.App.Tests.History;

public sealed class TelemetryHistoryObserverTests : IDisposable
{
    private readonly FakePrinterMonitor _monitor = new();
    private readonly TelemetryHistory _history = new();
    private readonly TelemetryHistoryObserver _observer;

    public TelemetryHistoryObserverTests() => _observer = new TelemetryHistoryObserver(_monitor, _history);

    public void Dispose() => _observer.Dispose();

    [Fact]
    public void AppliedSettingsConfigurePrintersAndCapacity()
    {
        _observer.ApplySettings(new AppSettings { Printers = [Definition()], PollingInterval = TimeSpan.FromSeconds(10) });

        Assert.Equal(TelemetryHistory.CapacityFor(TimeSpan.FromDays(1), TimeSpan.FromSeconds(10)), _history.GetCapacity("mk4"));
        Assert.Equal(0, _history.GetCapacity("other"));
    }

    [Fact]
    public void PublishedSnapshotsAreRecorded()
    {
        _observer.ApplySettings(new AppSettings { Printers = [Definition()] });

        _monitor.Raise(Snapshot(PrinterState.Idle) with { Telemetry = new Telemetry { Nozzle = new Temperature(24, 0) } });
        _monitor.Raise(Snapshot(PrinterState.Offline) with { Timestamp = Now.AddSeconds(5) });

        TelemetrySample[] samples = new TelemetrySample[4];
        int copied = _history.CopySamples("mk4", DateTimeOffset.MinValue, DateTimeOffset.MaxValue, samples, out _);
        Assert.Equal(2, copied);
        Assert.Equal(24f, samples[0].NozzleActual);
        Assert.Equal(PrinterState.Offline, samples[1].State);
        Assert.False(samples[1].HasTelemetry);
    }

    [Fact]
    public void MonitoringFailureKeepsHistory()
    {
        _observer.ApplySettings(new AppSettings { Printers = [Definition()] });
        _monitor.Raise(Snapshot(PrinterState.Idle));

        _observer.ReportMonitoringFailed();

        Assert.Equal(1, _history.CountSamples("mk4", DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
    }

    [Fact]
    public void DisposeUnsubscribesFromMonitor()
    {
        Assert.Equal(1, _monitor.SubscriberCount);

        _observer.Dispose();
        _observer.Dispose();

        Assert.Equal(0, _monitor.SubscriberCount);
    }

    [Fact]
    public void NullArgumentsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new TelemetryHistoryObserver(null!, _history));
        Assert.Throws<ArgumentNullException>(() => new TelemetryHistoryObserver(_monitor, null!));
        Assert.Throws<ArgumentNullException>(() => _observer.ApplySettings(null!));
    }
}
