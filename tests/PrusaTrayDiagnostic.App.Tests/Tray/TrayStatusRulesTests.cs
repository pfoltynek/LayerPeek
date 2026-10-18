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

    /// <summary>
    /// Agreed aggregation order, written out independently of the implementation: error &gt; attention (incl. rejected
    /// credentials) &gt; offline (incl. unknown and not yet polled) &gt; paused &gt; printing &gt; busy &gt; finished &gt; idle (incl. stopped).
    /// </summary>
    private static readonly Dictionary<string, int> AgreedRank = new()
    {
        ["NotPolled"] = 5,
        [nameof(PrinterState.Unknown)] = 5,
        [nameof(PrinterState.Idle)] = 0,
        [nameof(PrinterState.Stopped)] = 0,
        [nameof(PrinterState.Finished)] = 1,
        [nameof(PrinterState.Busy)] = 2,
        [nameof(PrinterState.Printing)] = 3,
        [nameof(PrinterState.Paused)] = 4,
        [nameof(PrinterState.Offline)] = 5,
        [nameof(PrinterState.Attention)] = 6,
        [nameof(PrinterState.Unauthorized)] = 6,
        [nameof(PrinterState.Error)] = 7,
    };

    public static TheoryData<string, string> AllStatePairs()
    {
        TheoryData<string, string> pairs = [];
        foreach (string first in AgreedRank.Keys)
        {
            foreach (string second in AgreedRank.Keys)
            {
                pairs.Add(first, second);
            }
        }

        return pairs;
    }

    [Fact]
    public void AgreedOrderCoversEveryPrinterState()
    {
        foreach (PrinterState state in Enum.GetValues<PrinterState>())
        {
            Assert.True(AgreedRank.ContainsKey(state.ToString()), $"{state} is missing in the aggregation table.");
        }
    }

    [Theory]
    [MemberData(nameof(AllStatePairs))]
    public void MoreSevereOfTwoPrintersWinsAndFirstConfiguredWinsTie(string first, string second)
    {
        TrayPrinter[] printers = [Printer(Parse(first), id: "a"), Printer(Parse(second), id: "b")];

        int expected = AgreedRank[second] > AgreedRank[first] ? 1 : 0;

        Assert.Equal(expected, TrayStatusRules.SelectPrimary(printers));
    }

    [Theory]
    [MemberData(nameof(AllStatePairs))]
    public void AggregatedIconShowsStatusOfMoreSeverePrinter(string first, string second)
    {
        TrayPrinter a = Printer(Parse(first), Job(42), id: "a", name: "A");
        TrayPrinter b = Printer(Parse(second), Job(77), id: "b", name: "B");
        TrayPrinter winner = AgreedRank[second] > AgreedRank[first] ? b : a;

        TrayPresentationKey key = TrayPresentationKey.Create([a, b], monitoringFailed: false);

        Assert.Equal(TrayIconKey.For(winner.Status, winner.Snapshot?.Job?.ProgressPercent), key.Icon);
    }

    [Fact]
    public void MostSeverePrinterWinsRegardlessOfPosition()
    {
        PrinterState?[] states = [PrinterState.Idle, PrinterState.Finished, PrinterState.Busy, PrinterState.Printing, PrinterState.Paused, null, PrinterState.Attention, PrinterState.Error];
        for (int shift = 0; shift < states.Length; shift++)
        {
            // Rotate so that the error printer is at every position once.
            TrayPrinter[] printers = new TrayPrinter[states.Length];
            for (int i = 0; i < states.Length; i++)
            {
                printers[(i + shift) % states.Length] = Printer(states[i], id: "p" + i);
            }

            int primary = TrayStatusRules.SelectPrimary(printers);

            Assert.Equal(TrayStatus.Error, printers[primary].Status);
        }
    }

    [Fact]
    public void OfflineUnknownAndNotYetPolledPrintersTieSoFirstConfiguredWins()
    {
        TrayPrinter[] printers =
        [
            Printer(PrinterState.Paused, id: "a"),
            Printer(PrinterState.Unknown, id: "b"),
            Printer(PrinterState.Offline, id: "c"),
            Printer(state: null, id: "d"),
        ];

        Assert.Equal(1, TrayStatusRules.SelectPrimary(printers));
        Assert.Equal(0, TrayStatusRules.SelectPrimary(printers[2..]));
        Assert.Equal(0, TrayStatusRules.SelectPrimary([printers[3], printers[1]]));
    }

    [Fact]
    public void TieBetweenPrintingPrintersShowsProgressOfFirstConfigured()
    {
        TrayPrinter[] printers =
        [
            Printer(PrinterState.Printing, Job(90), id: "a"),
            Printer(PrinterState.Printing, Job(10), id: "b"),
        ];

        Assert.Equal(TrayIconKey.For(TrayStatus.Printing, 90), TrayPresentationKey.Create(printers, monitoringFailed: false).Icon);
    }

    [Fact]
    public void EmptyPrinterSetShowsNotConfiguredWithOfflineIcon()
    {
        TrayPresentationKey key = TrayPresentationKey.Create([], monitoringFailed: false);

        Assert.Equal(TrayMessage.NotConfigured, key.Message);
        Assert.Equal(TrayIconKey.For(TrayStatus.Offline, null), key.Icon);
    }

    [Theory]
    [InlineData(null, TrayStatus.Offline)]
    [InlineData(PrinterState.Unauthorized, TrayStatus.Attention)]
    [InlineData(PrinterState.Stopped, TrayStatus.Idle)]
    public void ToolTipContentStatusMatchesPrinterStatus(PrinterState? state, TrayStatus expected)
    {
        Assert.Equal(expected, ToolTipContent.From(Printer(state), otherPrinterCount: 0).Status);
    }

    private static PrinterState? Parse(string state) => state == "NotPolled" ? null : Enum.Parse<PrinterState>(state);
}
