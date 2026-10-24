using LayerPeek.App.Tray;
using LayerPeek.Core.Printers;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Tray;

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
    public void SeveralPrintersAreIdentifiedBySummaryVersion()
    {
        TrayPrinter error = Printer(PrinterState.Error, id: "a", name: "A");
        TrayPrinter[] printers = [error, Printer(PrinterState.Printing, Job(40), id: "b", name: "B")];

        TrayPresentationKey first = TrayPresentationKey.Create(printers, monitoringFailed: false, summaryVersion: 1);

        Assert.Equal(TrayMessage.Summary, first.Message);
        Assert.Equal(default, first.Printer);
        Assert.Equal(first, TrayPresentationKey.Create(printers, monitoringFailed: false, summaryVersion: 1));
        Assert.NotEqual(first, TrayPresentationKey.Create(printers, monitoringFailed: false, summaryVersion: 2));
    }

    [Fact]
    public void SinglePrinterKeyIgnoresSummaryVersion()
    {
        TrayPrinter[] printers = [Printer(PrinterState.Idle)];

        Assert.Equal(
            TrayPresentationKey.Create(printers, monitoringFailed: false, summaryVersion: 1),
            TrayPresentationKey.Create(printers, monitoringFailed: false, summaryVersion: 2));
    }

    [Fact]
    public void FormattingSummaryKeyMatchesSummaryFormatter()
    {
        using CultureScope culture = new("en-US");
        TrayPrinter[] printers = [Printer(PrinterState.Idle, id: "a", name: "A"), Printer(PrinterState.Paused, Job(10), id: "b", name: "B")];
        ToolTipContent[] contents = [.. printers.Select(static printer => ToolTipContent.From(printer, otherPrinterCount: 0))];

        TrayPresentation presentation = TrayPresentation.Format(TrayPresentationKey.Create(printers, monitoringFailed: false, summaryVersion: 7), contents);

        Assert.Equal(TrayIconKey.For(TrayStatus.Paused, 10), presentation.IconKey);
        Assert.Equal("B: Paused – 10%\nA: Ready", presentation.ToolTipText);
        Assert.Equal(TrayToolTipFormatter.FormatSummary(contents), presentation.ToolTipText);
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
