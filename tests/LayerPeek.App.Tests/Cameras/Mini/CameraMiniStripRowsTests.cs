using System.Globalization;

using LayerPeek.App.Cameras;
using LayerPeek.App.Cameras.Mini;
using LayerPeek.App.Resources;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Printers;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Cameras.Mini;

/// <summary>
/// The collapsible rows of the mini preview strip (T-090, design T-076 4): the toggle state machine, the strip height the
/// window follows, the name row that disappears without a job, the chart row's values, the toggle tooltips and that a
/// collapsed chart costs nothing per poll.
/// </summary>
public sealed class CameraMiniStripRowsTests : IDisposable
{
    private const string Nbsp = " ";

    private readonly ManualDispatcher _dispatcher = new();
    private readonly CultureScope _culture = new("en-US");

    public void Dispose() => _culture.Dispose();

    private static PrinterSnapshot Printing(double nozzle = 214.6, double bed = 59.8, string? fileName = "benchy.bgcode") =>
        Snapshot(PrinterState.Printing, Job(42.7, TimeSpan.FromMinutes(65), fileName)) with
        {
            Telemetry = new Telemetry { Nozzle = new Temperature(nozzle, 215), Bed = new Temperature(bed, 60) },
        };

    private CameraMiniStripViewModel Create(CameraMiniRows rows = default) => new("mk4", _dispatcher, cooldown: null, rows: rows);

    private void Raise(CameraMiniStripViewModel strip, PrinterSnapshot snapshot)
    {
        strip.OnSnapshot(snapshot);
        _dispatcher.RunAll();
    }

    [Fact]
    public void BothRowsAreCollapsedByDefault()
    {
        using CameraMiniStripViewModel strip = Create();
        Raise(strip, Printing());

        Assert.False(strip.IsNameRowExpanded);
        Assert.False(strip.IsChartRowExpanded);
        Assert.False(strip.ShowsNameRow);
        Assert.Equal(default, strip.Rows);
        Assert.Equal(33, strip.StripHeight);
        Assert.Equal("Show print name", strip.NameToggleTip);
        Assert.Equal("Show temperature chart", strip.ChartToggleTip);
        Assert.Equal("Print name in the strip", strip.NameToggleName);
        Assert.Equal("Temperature chart in the strip", strip.ChartToggleName);
    }

    [Theory]
    [InlineData(false, false, 33)]
    [InlineData(true, false, 67)]
    [InlineData(false, true, 97)]
    [InlineData(true, true, 131)]
    public void StripHeightAddsTheExpandedRows(bool name, bool chart, double height)
    {
        using CameraMiniStripViewModel strip = Create();
        Raise(strip, Printing());
        List<string?> changes = [];
        strip.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        strip.IsNameRowExpanded = name;
        strip.IsChartRowExpanded = chart;

        Assert.Equal(height, strip.StripHeight);
        Assert.Equal(name, strip.ShowsNameRow);
        Assert.Equal(new CameraMiniRows(name, chart), strip.Rows);
        Assert.Equal(name || chart, changes.Contains(nameof(CameraMiniStripViewModel.StripHeight)));
    }

    [Fact]
    public void RowsOpenAsRememberedWithoutReportingAChange()
    {
        int changes = 0;
        using CameraMiniStripViewModel strip = Create(new CameraMiniRows(true, true));
        strip.RowsChanged += (_, _) => changes++;
        Raise(strip, Printing());

        Assert.True(strip.IsNameRowExpanded);
        Assert.True(strip.IsChartRowExpanded);
        Assert.Equal(131, strip.StripHeight);
        Assert.Equal("Hide print name", strip.NameToggleTip);
        Assert.Equal("Hide temperature chart", strip.ChartToggleTip);
        Assert.Equal("215°", strip.NozzleText);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void EveryToggleReportsTheRowsOnceAndTheSameValueNothing()
    {
        using CameraMiniStripViewModel strip = Create();
        List<CameraMiniRows> reported = [];
        strip.RowsChanged += (_, _) => reported.Add(strip.Rows);

        strip.IsNameRowExpanded = true;
        strip.IsNameRowExpanded = true;
        strip.IsChartRowExpanded = true;
        strip.IsNameRowExpanded = false;
        strip.IsChartRowExpanded = false;

        Assert.Equal([new(true, false), new(true, true), new(false, true), default], reported);
    }

    [Fact]
    public void WithoutAJobTheNameRowDisappearsAndTheToggleStaysOn()
    {
        using CameraMiniStripViewModel strip = Create(new CameraMiniRows(true, false));
        Raise(strip, Printing());
        Assert.True(strip.ShowsNameRow);
        Assert.Equal(67, strip.StripHeight);
        Assert.Equal("Benchy", strip.NameRow.Title);

        // The job ends: the row disappears, the strip (and the window) gets smaller.
        Raise(strip, Snapshot(PrinterState.Idle));

        Assert.True(strip.IsNameRowExpanded);
        Assert.False(strip.ShowsNameRow);
        Assert.Equal(33, strip.StripHeight);
        Assert.Equal("The print name appears during a print", strip.NameToggleTip);

        // Collapsed without a job: the tooltip offers to show it again.
        strip.IsNameRowExpanded = false;
        Assert.Equal("Show print name", strip.NameToggleTip);

        // The next job brings the row back once expanded.
        strip.IsNameRowExpanded = true;
        Raise(strip, Printing(fileName: "foldable_book_holder_v1_0.4n_0.2mm_PLA_COREONE_2h51m.bgcode"));
        Assert.True(strip.ShowsNameRow);
        Assert.Equal(67, strip.StripHeight);
        Assert.Equal("Foldable book holder v1", strip.NameRow.Title);
        Assert.Equal("PLA", strip.NameRow.MaterialChip);
    }

    [Fact]
    public void ExpandingTheNameRowWithoutAJobKeepsTheStripCollapsed()
    {
        using CameraMiniStripViewModel strip = Create();
        Raise(strip, Snapshot(PrinterState.Idle));

        strip.IsNameRowExpanded = true;

        Assert.False(strip.ShowsNameRow);
        Assert.Equal(33, strip.StripHeight);
    }

    [Fact]
    public void ChartRowShowsTheActualTemperaturesInWholeDegrees()
    {
        using CameraMiniStripViewModel strip = Create();
        Raise(strip, Printing(214.6, 59.4));

        strip.IsChartRowExpanded = true;

        Assert.Equal("215°", strip.NozzleText);
        Assert.Equal("59°", strip.BedText);
        Assert.True(strip.HasNozzle);
        Assert.True(strip.HasBed);
        Assert.Equal("60\u00A0min", strip.ChartRange);
        Assert.Equal("No data", strip.ChartNoData);
        Assert.Equal($"chart of the last 60 minutes, Nozzle 215{Nbsp}°C, Bed 59{Nbsp}°C", strip.ChartAccessibleText);

        Raise(strip, Printing(190.2, 60));
        Assert.Equal("190°", strip.NozzleText);
        Assert.Equal("60°", strip.BedText);
    }

    [Fact]
    public void ChartRowWithoutTemperaturesHasNoValues()
    {
        using CameraMiniStripViewModel strip = Create(new CameraMiniRows(false, true));
        Raise(strip, PrinterSnapshot.Unavailable("mk4", PrinterState.Offline, Now, "unreachable"));

        Assert.False(strip.HasNozzle);
        Assert.False(strip.HasBed);
        Assert.Equal(string.Empty, strip.NozzleText);
        Assert.Equal("chart of the last 60 minutes", strip.ChartAccessibleText);
        // The row stays (with "No data" in the chart).
        Assert.Equal(97, strip.StripHeight);
    }

    [Fact]
    public void CollapsedChartIgnoresTemperatureChangesButExpandingShowsTheLatest()
    {
        using CameraMiniStripViewModel strip = Create();
        Raise(strip, Printing(200));
        int posts = _dispatcher.PostCount;

        // Only the temperatures change: nothing the collapsed strip shows.
        strip.OnSnapshot(Printing(205));
        strip.OnSnapshot(Printing(210));
        Assert.Equal(posts, _dispatcher.PostCount);

        strip.IsChartRowExpanded = true;
        Assert.Equal("210°", strip.NozzleText);

        // Expanded: a temperature change is posted once.
        strip.OnSnapshot(Printing(212));
        Assert.Equal(posts + 1, _dispatcher.PostCount);
        _dispatcher.RunAll();
        Assert.Equal("212°", strip.NozzleText);

        // Collapsed again: no more posts for temperatures.
        strip.IsChartRowExpanded = false;
        strip.OnSnapshot(Printing(180));
        Assert.Equal(posts + 1, _dispatcher.PostCount);
    }

    [Fact]
    public void ResetRowsCollapsesBothWithoutReportingAChange()
    {
        using CameraMiniStripViewModel strip = Create(new CameraMiniRows(true, true));
        Raise(strip, Printing());
        int changes = 0;
        strip.RowsChanged += (_, _) => changes++;

        strip.ResetRows();

        Assert.Equal(default, strip.Rows);
        Assert.Equal(33, strip.StripHeight);
        Assert.Equal(0, changes);

        // Later toggles are reported again.
        strip.IsChartRowExpanded = true;
        Assert.Equal(1, changes);
    }

    [Fact]
    public void DisposedStripIgnoresToggles()
    {
        CameraMiniStripViewModel strip = Create();
        strip.Dispose();
        int changes = 0;
        strip.RowsChanged += (_, _) => changes++;

        strip.IsNameRowExpanded = true;
        strip.IsChartRowExpanded = true;

        Assert.Equal(default, strip.Rows);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void RowTextsFollowALanguageSwitch()
    {
        using CameraMiniStripViewModel strip = Create(new CameraMiniRows(true, true));
        Raise(strip, Printing(1234.4, 60));
        Assert.Equal("1,234°", strip.NozzleText);

        using (new CultureScope("de-DE"))
        using (new UiCultureScope("de"))
        {
            strip.RefreshTexts();

            Assert.Equal("Druckname ausblenden", strip.NameToggleTip);
            Assert.Equal("Temperaturdiagramm ausblenden", strip.ChartToggleTip);
            Assert.Equal("Druckname in der Leiste", strip.NameToggleName);
            Assert.Equal("Temperaturdiagramm in der Leiste", strip.ChartToggleName);
            Assert.Equal("1.234°", strip.NozzleText);
            Assert.Equal(Strings.CameraOverlayChartNoData, strip.ChartNoData);
        }

        strip.RefreshTexts();
        Assert.Equal("Hide print name", strip.NameToggleTip);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("cs")]
    [InlineData("sk")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("it")]
    [InlineData("pl")]
    public void ToggleTextsExistInEveryLanguageWithinTheDesignLimits(string language)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo(language);
        // T-076 10: tooltips at most 40, the no-job tooltip at most 50 characters; the names have no limit.
        (string Key, int Max)[] texts =
        [
            (nameof(Strings.CameraMiniToggleName), int.MaxValue),
            (nameof(Strings.CameraMiniToggleChart), int.MaxValue),
            (nameof(Strings.CameraMiniShowName), 40),
            (nameof(Strings.CameraMiniHideName), 40),
            (nameof(Strings.CameraMiniShowChart), 40),
            (nameof(Strings.CameraMiniHideChart), 40),
            (nameof(Strings.CameraMiniNameNoJob), 50),
        ];
        foreach ((string key, int max) in texts)
        {
            string? text = Strings.ResourceManager.GetString(key, culture);
            Assert.False(string.IsNullOrWhiteSpace(text), $"{key} ({language})");
            Assert.True(text.Length <= max, $"{key} ({language}): {text}");
            if (language != "en")
            {
                Assert.NotEqual(Strings.ResourceManager.GetString(key, CultureInfo.InvariantCulture), text);
            }
        }
    }
}
