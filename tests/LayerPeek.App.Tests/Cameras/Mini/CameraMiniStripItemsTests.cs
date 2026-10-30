using LayerPeek.App.Cameras;
using LayerPeek.App.Cameras.Mini;
using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cooldown;
using LayerPeek.Core.Printers;
using LayerPeek.Tests.Shared;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Cameras.Mini;

/// <summary>
/// What the mini preview strip shows by the printer's settings (T-110, design T-104 1, 3–5): switched-off items are in no
/// variant of the summary row and not in the tooltip; the state name is the minimum; the progress bar off is the faint
/// track; rows switched off have no toggle and no height; applying new settings to an open strip.
/// <see cref="TestPrinters.Now"/> is Friday 2026-10-16 12:00 UTC.
/// </summary>
public sealed class CameraMiniStripItemsTests : IDisposable
{
    private const string Nbsp = " ";

    private static readonly CameraMiniStripSettings All = CameraMiniStripSettings.Default;
    private static readonly CameraMiniStripSettings NoPercent = new() { Percent = false };
    private static readonly CameraMiniStripSettings NoRemaining = new() { Remaining = false };
    private static readonly CameraMiniStripSettings NoFinish = new() { Finish = false };
    private static readonly CameraMiniStripSettings NoCooldown = new() { Cooldown = false };
    private static readonly CameraMiniStripSettings NoBar = new() { ProgressBar = false };
    private static readonly CameraMiniStripSettings OnlyBar = new() { Percent = false, Remaining = false, Finish = false, Cooldown = false };
    private static readonly CameraMiniStripSettings NoRows = new() { NameRow = false, ChartRow = false };

    private static readonly CooldownState Cooling = new(CooldownPhase.Cooling, 87.4, 45, 50, 40, 0.42, TimeSpan.FromMinutes(5.5), Now, Now, CooldownPrintOutcome.Finished, Now, null);
    private static readonly CooldownState Cooled = new(CooldownPhase.CooledDown, 33, 39.6, 40, 40, 1, null, Now, Now, CooldownPrintOutcome.Finished, Now, Now);

    private readonly ManualDispatcher _dispatcher = new();
    private readonly CultureScope _culture = new("en-US");

    public void Dispose() => _culture.Dispose();

    private static PrinterSnapshot Printing(PrinterState state = PrinterState.Printing) =>
        Snapshot(state, Job(19.9, TimeSpan.FromMinutes(154.2), "benchy.bgcode")) with
        {
            Telemetry = new Telemetry { Nozzle = new Temperature(214.6, 215), Bed = new Temperature(59.8, 60) },
        };

    private static MiniStripContent Of(PrinterSnapshot? snapshot, CameraMiniStripSettings items, CooldownState? cooldown = null) =>
        MiniStripContent.Of(snapshot, cooldown ?? CooldownState.None, Now, TimeZoneInfo.Utc, items);

    private static MiniStripTexts TextsOf(PrinterSnapshot? snapshot, CameraMiniStripSettings items, CooldownState? cooldown = null) =>
        CameraMiniStripViewModel.Texts(Of(snapshot, items, cooldown));

    private static IEnumerable<MiniStripVariant> Variants(MiniStripTexts texts) =>
        new[] { texts.First, texts.Second, texts.Third }.OfType<MiniStripVariant>();

    private CameraMiniStripViewModel Create(CameraMiniStripSettings items, CameraMiniRows rows = default) =>
        new("mk4", _dispatcher, cooldown: null, rows: rows, items: items);

    private void Raise(CameraMiniStripViewModel strip, PrinterSnapshot snapshot)
    {
        strip.OnSnapshot(snapshot);
        _dispatcher.RunAll();
    }

    // ---- Content and texts ----

    [Fact]
    public void DefaultIsTheStripOfT103()
    {
        Assert.Equal(MiniStripContent.Of(Printing(), CooldownState.None, Now, TimeZoneInfo.Utc), Of(Printing(), All));
        Assert.Equal(MiniStripContent.Of(Snapshot(PrinterState.Finished), Cooling, Now, TimeZoneInfo.Utc), Of(Snapshot(PrinterState.Finished), All, Cooling));
        Assert.Equal(MiniStripContent.Of(Snapshot(PrinterState.Finished), Cooled, Now, TimeZoneInfo.Utc), Of(Snapshot(PrinterState.Finished), All, Cooled));
        Assert.False(Of(Printing(), All).HidesPercent);
    }

    [Fact]
    public void PercentOffLeavesTheRemainingTimeAndTheFinishTime()
    {
        MiniStripTexts texts = TextsOf(Printing(), NoPercent);

        Assert.Equal(new MiniStripVariant($"2{Nbsp}h 35{Nbsp}min", CultureTime.EnUs("Done at 2:35 PM")), texts.First);
        Assert.Equal(new MiniStripVariant($"2{Nbsp}h 35{Nbsp}min", CultureTime.EnUs("2:35 PM")), texts.Second);
        Assert.Equal(new MiniStripVariant($"2{Nbsp}h 35{Nbsp}min"), texts.Third);
        Assert.Equal($"Printing, 2{Nbsp}h 35{Nbsp}min remaining, " + CultureTime.EnUs("Done at 2:35 PM"), texts.Accessible);
        Assert.All(Variants(texts), static variant => Assert.DoesNotContain("%", variant.Summary, StringComparison.Ordinal));
    }

    [Fact]
    public void RemainingOffShowsThePercentWithTheFinishTime()
    {
        MiniStripTexts texts = TextsOf(Printing(), NoRemaining);

        Assert.Equal(new MiniStripVariant("19%", CultureTime.EnUs("Done at 2:35 PM")), texts.First);
        Assert.Equal(new MiniStripVariant("19%", CultureTime.EnUs("2:35 PM")), texts.Second);
        Assert.Equal(new MiniStripVariant("19%"), texts.Third);
        Assert.Equal("Printing, 19%, " + CultureTime.EnUs("Done at 2:35 PM"), texts.Accessible);
        Assert.DoesNotContain("min", texts.Accessible, StringComparison.Ordinal);
    }

    [Fact]
    public void FinishOffHasNoFlagGroupAnywhere()
    {
        MiniStripContent content = Of(Printing(), NoFinish);
        MiniStripTexts texts = CameraMiniStripViewModel.Texts(content);

        Assert.Null(content.Finish);
        Assert.Equal(new MiniStripTexts(new MiniStripVariant($"19% · 2{Nbsp}h 35{Nbsp}min"), null, null, $"Printing, 19%, 2{Nbsp}h 35{Nbsp}min remaining"), texts);
    }

    [Theory]
    [MemberData(nameof(MinimumCases))]
    public void NothingToShowLeavesTheStateName(CameraMiniStripSettings items)
    {
        MiniStripTexts texts = TextsOf(Printing(), items);

        Assert.Equal(new MiniStripTexts(new MiniStripVariant("Printing"), null, null, "Printing"), texts);
    }

    public static TheoryData<CameraMiniStripSettings> MinimumCases =>
    [
        OnlyBar,
        OnlyBar with { ProgressBar = false },
        OnlyBar with { NameRow = false, ChartRow = false },
        new CameraMiniStripSettings { Percent = false, Remaining = false, Finish = false },
    ];

    [Fact]
    public void OnlyTheFinishTimeStandsAfterTheStateName()
    {
        MiniStripTexts texts = TextsOf(Printing(), new CameraMiniStripSettings { Percent = false, Remaining = false });

        Assert.Equal(new MiniStripVariant("Printing", CultureTime.EnUs("Done at 2:35 PM")), texts.First);
        Assert.Equal(new MiniStripVariant("Printing"), texts.Third);
        Assert.Equal("Printing, " + CultureTime.EnUs("Done at 2:35 PM"), texts.Accessible);
    }

    [Theory]
    [InlineData(PrinterState.Paused)]
    [InlineData(PrinterState.Attention)]
    public void PausedWithoutThePercentIsTheStateName(PrinterState state)
    {
        MiniStripTexts texts = TextsOf(Printing(state), NoPercent);

        string name = OverlayFormat.StateName(state);
        Assert.Equal(new MiniStripTexts(new MiniStripVariant(name), null, null, name), texts);
        Assert.Equal($"19% · {name}", TextsOf(Printing(state), All).First.Summary);
    }

    [Theory]
    [InlineData(PrinterState.Finished, "Finished")]
    [InlineData(PrinterState.Idle, "Ready")]
    public void CooldownOffAfterThePrintIsTheStateNameWithoutIconsAndWithoutBar(PrinterState state, string name)
    {
        foreach (CooldownState cooldown in new[] { Cooling, Cooled })
        {
            MiniStripContent content = Of(Snapshot(state), NoCooldown, cooldown);
            MiniStripTexts texts = CameraMiniStripViewModel.Texts(content);

            Assert.Equal(MiniStripKind.State, content.Kind);
            Assert.Equal(new MiniStripTexts(new MiniStripVariant(name), null, null, name), texts);
            Assert.Equal(MiniStripKind.Cooling, Of(Snapshot(state), All, Cooling).Kind);
        }
    }

    [Fact]
    public void HiddenValuesDoNotMakeTheContentDiffer()
    {
        // What nothing shows is left out, so that a poll that changes only hidden values posts nothing.
        PrinterSnapshot first = Snapshot(PrinterState.Printing, Job(19.9, TimeSpan.FromMinutes(154.2)));
        PrinterSnapshot second = Snapshot(PrinterState.Printing, Job(64.2, TimeSpan.FromMinutes(31)));
        CameraMiniStripSettings nothing = OnlyBar with { ProgressBar = false };

        Assert.Equal(Of(first, nothing), Of(second, nothing));
        Assert.NotEqual(Of(first, OnlyBar), Of(second, OnlyBar));
        Assert.Equal(0, Of(Snapshot(PrinterState.Finished), NoBar, Cooling).CoolingPercent);
        Assert.Equal(42, Of(Snapshot(PrinterState.Finished), All, Cooling).CoolingPercent);
    }

    // ---- The view model ----

    [Fact]
    public void ProgressBarOffIsAlwaysTheFaintTrack()
    {
        using CameraMiniStripViewModel strip = Create(NoBar);
        Raise(strip, Printing());

        Assert.False(strip.HasProgress);
        Assert.Equal(0, strip.Progress);
        Assert.Equal($"19% · 2{Nbsp}h 35{Nbsp}min", strip.Summary);
        Assert.Equal(CameraMiniStripViewModel.CollapsedHeight, strip.StripHeight);
    }

    [Theory]
    [InlineData(true, true, true, true, 131)]
    [InlineData(false, true, false, true, 97)]
    [InlineData(true, false, true, false, 67)]
    [InlineData(false, false, false, false, 33)]
    public void RowsSwitchedOffHaveNoToggleAndNoHeight(bool nameOn, bool chartOn, bool togglesName, bool togglesChart, double height)
    {
        // Both rows remembered as expanded.
        using CameraMiniStripViewModel strip = Create(new CameraMiniStripSettings { NameRow = nameOn, ChartRow = chartOn }, new CameraMiniRows(Name: true, Chart: true));
        Raise(strip, Printing());

        Assert.Equal(togglesName, strip.IsNameRowAvailable);
        Assert.Equal(togglesChart, strip.IsChartRowAvailable);
        Assert.Equal(nameOn || chartOn, strip.ShowsToggles);
        Assert.Equal(nameOn, strip.ShowsNameRow);
        Assert.Equal(chartOn, strip.ShowsChartRow);
        Assert.Equal(height, strip.StripHeight);
        // The remembered expansion stays as it was.
        Assert.Equal(new CameraMiniRows(Name: true, Chart: true), strip.Rows);
    }

    [Fact]
    public void ApplyingSettingsRedrawsTheOpenStripAndKeepsTheRememberedRows()
    {
        using CameraMiniStripViewModel strip = Create(All, new CameraMiniRows(Name: false, Chart: true));
        Raise(strip, Printing());
        int rowsChanged = 0;
        strip.RowsChanged += (_, _) => rowsChanged++;
        List<string?> changes = [];
        strip.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        Assert.Equal(97, strip.StripHeight);
        Assert.Equal("215°", strip.NozzleText);

        strip.ApplySettings(new CameraMiniStripSettings { Remaining = false, ProgressBar = false, ChartRow = false });

        Assert.Equal("19%", strip.Summary);
        Assert.False(strip.HasProgress);
        Assert.False(strip.IsChartRowAvailable);
        Assert.False(strip.ShowsChartRow);
        Assert.Equal(33, strip.StripHeight);
        Assert.Contains(nameof(CameraMiniStripViewModel.StripHeight), changes);
        Assert.Contains(nameof(CameraMiniStripViewModel.IsChartRowAvailable), changes);
        Assert.Contains(nameof(CameraMiniStripViewModel.ShowsToggles), changes);
        Assert.True(strip.IsChartRowExpanded);

        // The chart's temperatures are not computed while it is switched off; switched on again it comes back as it was.
        Raise(strip, Printing() with { Telemetry = new Telemetry { Nozzle = new Temperature(150, 215), Bed = new Temperature(40, 60) } });
        Assert.Equal("215°", strip.NozzleText);
        strip.ApplySettings(All);

        Assert.True(strip.ShowsChartRow);
        Assert.Equal(97, strip.StripHeight);
        Assert.Equal("150°", strip.NozzleText);
        Assert.Equal($"19% · 2{Nbsp}h 35{Nbsp}min", strip.Summary);
        Assert.True(strip.HasProgress);
        Assert.Equal(0, rowsChanged);
        Assert.Equal(new CameraMiniRows(Name: false, Chart: true), strip.Rows);
    }

    [Fact]
    public void SameSettingsChangeNothing()
    {
        using CameraMiniStripViewModel strip = Create(NoRemaining);
        Raise(strip, Printing());
        int changes = 0;
        strip.PropertyChanged += (_, _) => changes++;

        strip.ApplySettings(new CameraMiniStripSettings { Remaining = false });

        Assert.Equal(0, changes);
        Assert.Equal(0, _dispatcher.Pending);
    }

    [Fact]
    public void NameRowSwitchedOnAgainShowsTheCurrentJob()
    {
        using CameraMiniStripViewModel strip = Create(new CameraMiniStripSettings { NameRow = false }, new CameraMiniRows(Name: true, Chart: false));
        Raise(strip, Printing());
        Assert.False(strip.ShowsNameRow);

        strip.ApplySettings(All);

        Assert.True(strip.ShowsNameRow);
        Assert.Equal(67, strip.StripHeight);
    }

    [Fact]
    public void TooltipHasOnlyTheItemsThatAreOn()
    {
        using CameraMiniStripViewModel strip = Create(new CameraMiniStripSettings { Percent = false, Finish = false });
        Raise(strip, Printing());

        Assert.Equal($"Printing, 2{Nbsp}h 35{Nbsp}min remaining", strip.AccessibleText);
        Assert.Equal($"2{Nbsp}h 35{Nbsp}min", strip.Summary);
        Assert.Null(strip.FirstVariant.Finish);
    }
}
