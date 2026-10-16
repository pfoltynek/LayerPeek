using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.Core.Printers;

using static PrusaTrayDiagnostic.App.Tests.Tray.TestPrinters;

namespace PrusaTrayDiagnostic.App.Tests.Tray;

public sealed class TrayPresentationKeyTests
{
    [Fact]
    public void TimestampAndTelemetryDoNotChangeKey()
    {
        TrayPrinter first = Printer(PrinterState.Printing, Job(45.2, TimeSpan.FromMinutes(10)));
        TrayPrinter second = first with
        {
            Snapshot = first.Snapshot! with { Timestamp = Now.AddSeconds(5), Telemetry = new Telemetry { AxisZ = 1.2 } },
        };

        Assert.Equal(Key(first), Key(second));
    }

    [Fact]
    public void ProgressChangesKeyOnlyWhenShownPercentChanges()
    {
        Assert.Equal(Key(Printer(PrinterState.Printing, Job(45.2))), Key(Printer(PrinterState.Printing, Job(45.8))));
        Assert.NotEqual(Key(Printer(PrinterState.Printing, Job(45.8))), Key(Printer(PrinterState.Printing, Job(46.1))));
    }

    [Fact]
    public void RemainingChangesKeyOnlyWhenShownMinuteChanges()
    {
        Assert.Equal(
            Key(Printer(PrinterState.Printing, Job(50, TimeSpan.FromSeconds(570)))),
            Key(Printer(PrinterState.Printing, Job(50, TimeSpan.FromSeconds(600)))));
        Assert.NotEqual(
            Key(Printer(PrinterState.Printing, Job(50, TimeSpan.FromSeconds(600)))),
            Key(Printer(PrinterState.Printing, Job(50, TimeSpan.FromSeconds(601)))));
    }

    [Fact]
    public void HiddenJobDataDoesNotChangeKey()
    {
        // The tooltip of an offline printer shows no job, so a different stale job must not trigger formatting.
        Assert.Equal(Key(Printer(PrinterState.Offline, Job(10))), Key(Printer(PrinterState.Offline, Job(90))));
    }

    [Fact]
    public void NonPrimaryPrinterChangeDoesNotChangeKey()
    {
        TrayPrinter error = Printer(PrinterState.Error, id: "a", name: "A");

        TrayPresentationKey before = TrayPresentationKey.Create([error, Printer(PrinterState.Printing, Job(40), id: "b", name: "B")], monitoringFailed: false);
        TrayPresentationKey after = TrayPresentationKey.Create([error, Printer(PrinterState.Printing, Job(41), id: "b", name: "B")], monitoringFailed: false);

        Assert.Equal(before, after);
    }

    [Fact]
    public void StateNameAndCountChangeKey()
    {
        Assert.NotEqual(Key(Printer(PrinterState.Idle)), Key(Printer(PrinterState.Stopped)));
        Assert.NotEqual(Key(Printer(PrinterState.Idle)), Key(Printer(PrinterState.Idle, name: "Other")));
        Assert.NotEqual(Key(Printer(PrinterState.Idle)), Key(Printer(state: null)));
        Assert.NotEqual(
            TrayPresentationKey.Create([Printer(PrinterState.Idle)], monitoringFailed: false),
            TrayPresentationKey.Create([Printer(PrinterState.Idle), Printer(PrinterState.Idle, id: "b")], monitoringFailed: false));
    }

    [Fact]
    public void FormattingTheKeyMatchesFormatter()
    {
        using CultureScope culture = new("en-US");
        TrayPrinter printer = Printer(PrinterState.Paused, Job(10, TimeSpan.FromMinutes(5)));

        TrayPresentation presentation = TrayPresentation.Format(TrayPresentationKey.Create([printer], monitoringFailed: false));

        Assert.Equal(TrayIconKey.For(TrayStatus.Paused, 10), presentation.IconKey);
        Assert.Equal(TrayToolTipFormatter.Format(printer, otherPrinterCount: 0), presentation.ToolTipText);
    }

    [Fact]
    public void ApplicationStatesHaveOwnKeys()
    {
        using CultureScope culture = new("en-US");

        Assert.Equal(TrayToolTipFormatter.Starting(), TrayPresentation.Format(TrayPresentationKey.Create(null, monitoringFailed: false)).ToolTipText);
        Assert.Equal(TrayToolTipFormatter.NotConfigured(), TrayPresentation.Format(TrayPresentationKey.Create([], monitoringFailed: false)).ToolTipText);
        TrayPresentation failed = TrayPresentation.Format(TrayPresentationKey.Create([Printer(PrinterState.Idle)], monitoringFailed: true));
        Assert.Equal(TrayToolTipFormatter.MonitoringFailed(), failed.ToolTipText);
        Assert.Equal(TrayIconKey.For(TrayStatus.Error, null), failed.IconKey);
    }

    private static TrayPresentationKey Key(TrayPrinter printer) => TrayPresentationKey.Create([printer], monitoringFailed: false);
}
