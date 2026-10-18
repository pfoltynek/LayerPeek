using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

using static PrusaTrayDiagnostic.App.Tests.Tray.TestPrinters;

namespace PrusaTrayDiagnostic.App.Tests.Tray;

public sealed class MonitoredPrintersTests
{
    private readonly MonitoredPrinters _printers = new();

    [Fact]
    public void BeforeTheFirstRunNothingIsKnown()
    {
        Assert.Null(_printers.Current.Printers);
        Assert.False(_printers.Current.MonitoringFailed);
    }

    [Fact]
    public void AppliedSettingsPublishACopyOfThePrinters()
    {
        List<PrinterDefinition> configured = [Definition("a", "Alpha"), Definition("b", "Beta")];
        int changes = 0;
        _printers.Changed += (_, _) => changes++;

        ((IMonitoringObserver)_printers).ApplySettings(new AppSettings { Printers = configured });
        configured.Clear();

        Assert.Equal(1, changes);
        Assert.Equal(["a", "b"], _printers.Current.Printers!.Select(static p => p.Id));
    }

    [Fact]
    public void FailureIsKeptUntilTheNextRun()
    {
        IMonitoringObserver observer = _printers;
        observer.ApplySettings(new AppSettings { Printers = [Definition()] });
        int changes = 0;
        _printers.Changed += (_, _) => changes++;

        observer.ReportMonitoringFailed();
        Assert.True(_printers.Current.MonitoringFailed);
        Assert.Single(_printers.Current.Printers!);

        observer.ApplySettings(new AppSettings { Printers = [Definition()] });
        Assert.False(_printers.Current.MonitoringFailed);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void FailureBeforeTheFirstRunIsReported()
    {
        ((IMonitoringObserver)_printers).ReportMonitoringFailed();

        Assert.True(_printers.Current.MonitoringFailed);
        Assert.Null(_printers.Current.Printers);
    }
}
