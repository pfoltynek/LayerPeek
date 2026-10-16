using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.Core.Printers;

using static PrusaTrayDiagnostic.App.Tests.Tray.TestPrinters;

namespace PrusaTrayDiagnostic.App.Tests.Tray;

public sealed class TrayStatusRulesTests
{
    [Theory]
    [InlineData(PrinterState.Idle, TrayStatus.Idle)]
    [InlineData(PrinterState.Busy, TrayStatus.Busy)]
    [InlineData(PrinterState.Printing, TrayStatus.Printing)]
    [InlineData(PrinterState.Paused, TrayStatus.Paused)]
    [InlineData(PrinterState.Finished, TrayStatus.Finished)]
    [InlineData(PrinterState.Stopped, TrayStatus.Idle)]
    [InlineData(PrinterState.Error, TrayStatus.Error)]
    [InlineData(PrinterState.Attention, TrayStatus.Attention)]
    [InlineData(PrinterState.Unauthorized, TrayStatus.Attention)]
    [InlineData(PrinterState.Offline, TrayStatus.Offline)]
    [InlineData(PrinterState.Unknown, TrayStatus.Offline)]
    public void MapsPrinterStateToTrayStatus(PrinterState state, TrayStatus expected)
    {
        Assert.Equal(expected, TrayStatusRules.FromPrinterState(state));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(999)]
    public void UndefinedPrinterStateBehavesLikeUnknown(int value)
    {
        Assert.Equal(TrayStatus.Offline, TrayStatusRules.FromPrinterState((PrinterState)value));
    }

    [Fact]
    public void EveryPrinterStateHasMapping()
    {
        foreach (PrinterState state in Enum.GetValues<PrinterState>())
        {
            Assert.True(Enum.IsDefined(TrayStatusRules.FromPrinterState(state)), $"{state} maps to an undefined status.");
        }
    }

    [Fact]
    public void SeverityFollowsAgreedOrder()
    {
        // error > attention > offline > paused > printing > (busy) > finished > idle
        TrayStatus[] ascending =
        [
            TrayStatus.Idle,
            TrayStatus.Finished,
            TrayStatus.Busy,
            TrayStatus.Printing,
            TrayStatus.Paused,
            TrayStatus.Offline,
            TrayStatus.Attention,
            TrayStatus.Error,
        ];

        Assert.Equal(Enum.GetValues<TrayStatus>().Length, ascending.Length);
        for (int i = 1; i < ascending.Length; i++)
        {
            Assert.True(
                TrayStatusRules.Severity(ascending[i]) > TrayStatusRules.Severity(ascending[i - 1]),
                $"{ascending[i]} must be more severe than {ascending[i - 1]}.");
        }
    }

    [Fact]
    public void SelectPrimaryOfEmptyListReturnsMinusOne()
    {
        Assert.Equal(-1, TrayStatusRules.SelectPrimary([]));
    }

    [Fact]
    public void SelectPrimaryPicksMostSevereStatus()
    {
        TrayPrinter[] printers =
        [
            Printer(PrinterState.Printing, id: "a"),
            Printer(PrinterState.Error, id: "b"),
            Printer(PrinterState.Offline, id: "c"),
            Printer(PrinterState.Finished, id: "d"),
        ];

        Assert.Equal(1, TrayStatusRules.SelectPrimary(printers));
    }

    [Fact]
    public void SelectPrimaryPrefersFirstConfiguredPrinterOnTie()
    {
        TrayPrinter[] printers =
        [
            Printer(PrinterState.Idle, id: "a"),
            Printer(PrinterState.Printing, id: "b"),
            Printer(PrinterState.Printing, id: "c"),
        ];

        Assert.Equal(1, TrayStatusRules.SelectPrimary(printers));
    }

    [Fact]
    public void PrinterWithoutSnapshotCountsAsOffline()
    {
        TrayPrinter waiting = Printer(state: null);

        Assert.Equal(TrayStatus.Offline, waiting.Status);
        // Offline outranks paused.
        Assert.Equal(1, TrayStatusRules.SelectPrimary([Printer(PrinterState.Paused, id: "a"), waiting]));
    }
}
