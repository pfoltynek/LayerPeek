using System.ComponentModel;
using System.Globalization;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Cameras.Overlay.Widgets;
using LayerPeek.App.Composition;
using LayerPeek.App.Diagnostics.Chart;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.History;
using LayerPeek.Core.Printers;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Cameras.Overlay.Widgets;

/// <summary>
/// The Temperatures widget (T-084, design T-076 2.3) driven by the real overlay: nozzle and bed in whole degrees, the
/// target only while heating, each pair only when known, the card hidden without any temperature; the nozzle's actual
/// value is the key value; texts in the app language, formatted only when a shown degree changes. The chart is the
/// compact <see cref="TemperatureChart"/> over the window's history (UI tests). Allocations: <see cref="TemperaturesWidgetAllocationTests"/>.
/// </summary>
public sealed class TemperaturesWidgetTests : IDisposable
{
    private readonly ManualDispatcher _dispatcher = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly TelemetryHistory _history = new();
    private readonly CultureScope _culture = new("en-US");
    private readonly List<CameraOverlayViewModel> _created = [];

    public void Dispose()
    {
        foreach (CameraOverlayViewModel overlay in _created)
        {
            overlay.Dispose();
        }

        _culture.Dispose();
    }

    /// <summary>A reachable printer with the given temperatures (<see langword="null"/> = not reported).</summary>
    internal static PrinterSnapshot Heating(
        Temperature? nozzle = null,
        Temperature? bed = null,
        PrinterState state = PrinterState.Printing,
        bool withBed = true,
        bool withNozzle = true,
        DateTimeOffset? at = null) => new()
        {
            PrinterId = "mk4",
            State = state,
            Timestamp = at ?? Now,
            Telemetry = new Telemetry
            {
                Nozzle = withNozzle ? nozzle ?? new Temperature(214.6, 215) : null,
                Bed = withBed ? bed ?? new Temperature(59.8, 60) : null,
            },
        };

    private CameraOverlayViewModel Create(OverlayWidgetSize size = OverlayWidgetSize.Medium, bool emphasized = true)
    {
        CameraOverlayViewModel overlay = new(
            OverlayWidgets.CreateRegistry(),
            new OverlayWidgetContext("mk4", _time, _history),
            _dispatcher,
            NullLogger.Instance,
            Definition(),
            new CameraOverlayLayout([new OverlayWidgetPlacement(OverlayWidgetIds.Temperatures, OverlayAnchor.BottomRight, size, emphasized)]),
            CameraOverlayOpacity.DefaultPercent,
            CameraWindowMode.Normal,
            snapshot: null);
        _created.Add(overlay);
        TemperaturesWidgetViewModel widget = Widget(overlay);
        widget.RenderedSize = size;
        return overlay;
    }

    private static TemperaturesWidgetViewModel Widget(CameraOverlayViewModel overlay) => (TemperaturesWidgetViewModel)Assert.Single(overlay.Widgets);

    private TemperaturesWidgetViewModel Show(CameraOverlayViewModel overlay, PrinterSnapshot snapshot)
    {
        overlay.OnSnapshot(snapshot);
        _dispatcher.RunAll();
        return Widget(overlay);
    }

    // ---- Registration ----

    [Fact]
    public void TemperaturesWidgetIsRegisteredWithItsNamePriorityAndSizes()
    {
        IOverlayWidgetDefinition definition = Assert.IsType<IOverlayWidgetDefinition>(OverlayWidgets.CreateRegistry().Find(OverlayWidgetIds.Temperatures), exactMatch: false);

        Assert.Equal(OverlayWidgetNames.Temperatures, definition.NameKey);
        Assert.Equal(OverlayWidgets.TemperaturesPriority, definition.Priority);
        Assert.Equal([OverlayWidgetSize.Small, OverlayWidgetSize.Medium, OverlayWidgetSize.Large], definition.SupportedSizes);
        // T-076 2.3: S by content, M 284 (chart 256 + 2 × 14), L 392 (chart 360 + 2 × 16).
        Assert.Equal(0, definition.MinWidth(OverlayWidgetSize.Small));
        Assert.Equal(284, definition.MinWidth(OverlayWidgetSize.Medium));
        Assert.Equal(392, definition.MinWidth(OverlayWidgetSize.Large));
        Assert.Same(TemperaturesWidgetView.CardTemplate, definition.CardTemplate);
        using OverlayWidgetViewModel widget = definition.Create(new OverlayWidgetContext("mk4", _time, _history));
        Assert.IsType<TemperaturesWidgetViewModel>(widget);
    }

    [Fact]
    public void ChartGetsTheHistoryPrinterAndClockOfTheWindow()
    {
        TemperaturesWidgetViewModel widget = Widget(Create());

        Assert.Same(_history, widget.History);
        Assert.Equal("mk4", widget.PrinterId);
        Assert.Same(_time, widget.Clock);
    }

    [Fact]
    public void WidgetWorksWithoutAHistory()
    {
        // A window without the history service (tests, other hosts): values only, the chart shows "No data".
        using OverlayWidgetViewModel widget = OverlayWidgets.CreateRegistry().Find(OverlayWidgetIds.Temperatures)!.Create(new OverlayWidgetContext("mk4", _time));

        Assert.Null(((TemperaturesWidgetViewModel)widget).History);
    }

    // ---- What is shown and when ----

    [Fact]
    public void HeatingShowsActualAndTargetOfBoth()
    {
        TemperaturesWidgetViewModel widget = Show(Create(), Heating());

        Assert.True(widget.IsShown);
        Assert.True(widget.HasNozzle);
        Assert.True(widget.HasBed);
        Assert.Equal("215", widget.NozzleActual);
        Assert.Equal(" / 215 °C", widget.NozzleRest);
        Assert.Equal("60 / 60 °C", widget.Bed);
        Assert.Equal("Nozzle", widget.NozzleLabel);
        Assert.Equal("Bed", widget.BedLabel);
        Assert.Equal("Nozzle 215 / 215 °C, Bed 60 / 60 °C, chart of the last 60 minutes", widget.AccessibleName);
    }

    [Fact]
    public void TargetIsShownOnlyWhileHeating()
    {
        // Heaters off: a target of 0 means off (T-036 Q2), only the measured temperature.
        TemperaturesWidgetViewModel widget = Show(Create(), Heating(new Temperature(27.6, 0), new Temperature(24.2, 0), PrinterState.Idle));

        Assert.Equal("28", widget.NozzleActual);
        Assert.Equal(" °C", widget.NozzleRest);
        Assert.Equal("24 °C", widget.Bed);
        Assert.Equal("Nozzle 28 °C, Bed 24 °C, chart of the last 60 minutes", widget.AccessibleName);
    }

    [Fact]
    public void UnknownTargetIsLeftOut()
    {
        TemperaturesWidgetViewModel widget = Show(Create(), Heating(new Temperature(150, null), new Temperature(60, double.NaN)));

        Assert.Equal("150", widget.NozzleActual);
        Assert.Equal(" °C", widget.NozzleRest);
        Assert.Equal("60 °C", widget.Bed);
    }

    [Fact]
    public void MissingBedLeavesOutItsPair()
    {
        TemperaturesWidgetViewModel widget = Show(Create(), Heating(withBed: false));

        Assert.True(widget.IsShown);
        Assert.True(widget.HasNozzle);
        Assert.False(widget.HasBed);
        Assert.Equal("Nozzle 215 / 215 °C, chart of the last 60 minutes", widget.AccessibleName);
    }

    [Fact]
    public void MissingNozzleLeavesOutItsPair()
    {
        TemperaturesWidgetViewModel widget = Show(Create(), Heating(withNozzle: false));

        Assert.True(widget.IsShown);
        Assert.False(widget.HasNozzle);
        Assert.True(widget.HasBed);
        Assert.Equal("Bed 60 / 60 °C, chart of the last 60 minutes", widget.AccessibleName);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteTemperatureCountsAsUnknown(double value)
    {
        TemperaturesWidgetViewModel widget = Show(Create(), Heating(new Temperature(value, 215), new Temperature(value, 60)));

        Assert.False(widget.IsShown);
        Assert.False(widget.IsFaulted);
    }

    [Fact]
    public void NoTemperatureHidesTheCard()
    {
        CameraOverlayViewModel overlay = Create();
        Assert.True(Show(overlay, Heating()).IsShown);

        TemperaturesWidgetViewModel widget = Show(overlay, Heating(withNozzle: false, withBed: false));

        Assert.False(widget.HasContent);
        Assert.False(widget.IsShown);
    }

    [Fact]
    public void SnapshotWithoutTelemetryHidesTheCard()
    {
        CameraOverlayViewModel overlay = Create();
        Assert.True(Show(overlay, Heating()).IsShown);

        Assert.False(Show(overlay, Heating() with { Telemetry = null }).IsShown);
    }

    [Theory]
    [InlineData(PrinterState.Offline)]
    [InlineData(PrinterState.Unauthorized)]
    public void UnreachablePrinterHidesTheCard(PrinterState state)
    {
        CameraOverlayViewModel overlay = Create();
        Assert.True(Show(overlay, Heating()).IsShown);

        // The monitor's snapshot of an unreachable printer has no telemetry.
        TemperaturesWidgetViewModel widget = Show(overlay, PrinterSnapshot.Unavailable("mk4", state, Now, "unreachable"));

        Assert.False(widget.IsShown);
    }

    [Theory]
    [InlineData(PrinterState.Idle)]
    [InlineData(PrinterState.Busy)]
    [InlineData(PrinterState.Printing)]
    [InlineData(PrinterState.Paused)]
    [InlineData(PrinterState.Finished)]
    [InlineData(PrinterState.Stopped)]
    [InlineData(PrinterState.Error)]
    [InlineData(PrinterState.Attention)]
    public void TemperaturesAreShownInEveryStateThatReportsThem(PrinterState state) =>
        Assert.True(Show(Create(), Heating(state: state)).IsShown);

    [Fact]
    public void CardIsHiddenBeforeTheFirstPoll() => Assert.False(Widget(Create()).IsShown);

    [Fact]
    public void ContentIsTheRoundedTemperatures()
    {
        OverlayWidgetInput input = new(Heating(), Definition(), CameraWindowMode.Normal, Now, TimeZoneInfo.Utc, CultureInfo.CurrentCulture);

        Assert.Equal(new TemperaturesContent(new OverlayTemperature(215, 215), new OverlayTemperature(60, 60)), TemperaturesWidgetViewModel.ContentOf(input));
        Assert.Equal(default, TemperaturesWidgetViewModel.ContentOf(input with { Snapshot = null }));
        Assert.False(default(TemperaturesContent).HasAny);
    }

    // ---- Cheap comparison ----

    [Fact]
    public void PollWithTheSameWholeDegreesPostsAndFormatsNothing()
    {
        CameraOverlayViewModel overlay = Create();
        TemperaturesWidgetViewModel widget = Show(overlay, Heating(new Temperature(214.6, 215), new Temperature(59.8, 60)));
        int posts = _dispatcher.PostCount;
        int changes = 0;
        widget.PropertyChanged += (_, _) => changes++;

        // 2 s later: other fractions and other values of the snapshot, the same whole degrees.
        overlay.OnSnapshot(Heating(new Temperature(215.4, 215), new Temperature(60.3, 60), at: Now.AddSeconds(2)) with { Job = new PrintJob { ProgressPercent = 43 } });

        Assert.Equal(posts, _dispatcher.PostCount);
        Assert.Equal(0, _dispatcher.Pending);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void NextDegreeFormatsOnlyWhatChanged()
    {
        CameraOverlayViewModel overlay = Create();
        TemperaturesWidgetViewModel widget = Show(overlay, Heating());
        List<string?> changed = [];
        widget.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        widget = Show(overlay, Heating(new Temperature(216.2, 215)));

        Assert.Equal("216", widget.NozzleActual);
        Assert.Contains(nameof(TemperaturesWidgetViewModel.NozzleActual), changed);
        Assert.Contains(nameof(OverlayWidgetViewModel.AccessibleName), changed);
        // The bed and the nozzle target stayed: their texts are not set again.
        Assert.DoesNotContain(nameof(TemperaturesWidgetViewModel.Bed), changed);
        Assert.DoesNotContain(nameof(TemperaturesWidgetViewModel.NozzleRest), changed);
    }

    // ---- Emphasis, sizes ----

    [Fact]
    public void EmphasisFollowsTheLayout()
    {
        CameraOverlayViewModel overlay = Create(emphasized: true);
        TemperaturesWidgetViewModel widget = Show(overlay, Heating());
        Assert.True(widget.IsEmphasized);

        overlay.ApplySettings(
            Definition(),
            new CameraOverlayLayout([new OverlayWidgetPlacement(OverlayWidgetIds.Temperatures, OverlayAnchor.BottomRight, OverlayWidgetSize.Medium, emphasized: false)]),
            CameraOverlayOpacity.DefaultPercent);

        Assert.Same(widget, Widget(overlay));
        Assert.False(widget.IsEmphasized);
    }

    [Fact]
    public void SmallCardHasNoChartAndSaysSo()
    {
        TemperaturesWidgetViewModel widget = Show(Create(OverlayWidgetSize.Small), Heating());

        Assert.True(widget.IsShown);
        Assert.False(widget.ShowsChart);
        Assert.Equal("Nozzle 215 / 215 °C, Bed 60 / 60 °C", widget.AccessibleName);
    }

    [Fact]
    public void SizeChangeUpdatesTheChartAndTheScreenReaderName()
    {
        TemperaturesWidgetViewModel widget = Show(Create(OverlayWidgetSize.Medium), Heating());
        Assert.True(widget.ShowsChart);
        List<string?> changed = [];
        widget.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        widget.RenderedSize = OverlayWidgetSize.Small;

        Assert.False(widget.ShowsChart);
        Assert.Contains(nameof(TemperaturesWidgetViewModel.ShowsChart), changed);
        Assert.Equal("Nozzle 215 / 215 °C, Bed 60 / 60 °C", widget.AccessibleName);
        // The values themselves are not formatted again.
        Assert.DoesNotContain(nameof(TemperaturesWidgetViewModel.NozzleActual), changed);

        widget.RenderedSize = OverlayWidgetSize.Large;

        Assert.True(widget.ShowsChart);
        Assert.EndsWith(", chart of the last 60 minutes", widget.AccessibleName, StringComparison.Ordinal);
    }

    [Fact]
    public void SizeChangeOfAHiddenCardFormatsNothing()
    {
        TemperaturesWidgetViewModel widget = Widget(Create());
        int changes = 0;
        widget.PropertyChanged += (_, e) => changes += e.PropertyName == nameof(OverlayWidgetViewModel.AccessibleName) ? 1 : 0;

        widget.RenderedSize = OverlayWidgetSize.Small;

        Assert.Equal(0, changes);
        Assert.Equal(string.Empty, widget.AccessibleName);
    }

    [Fact]
    public void ChartTextsAreSet()
    {
        TemperaturesWidgetViewModel widget = Show(Create(), Heating());

        Assert.Equal("−60 min", widget.ChartStart);
        Assert.Equal("now", widget.ChartNow);
        Assert.Equal("No data", widget.ChartNoData);
    }

    [Fact]
    public void ChartWindowIsSixtyMinutes() =>
        Assert.Equal(TimeSpan.FromMinutes(60), TemperatureChart.CompactTimeWindow);

    // ---- Languages ----

    [Theory]
    [InlineData("cs-CZ", "Tryska 215 / 215 °C, Podložka 60 / 60 °C, graf posledních 60 minut", "Žádná data", "teď")]
    [InlineData("de-DE", "Düse 215 / 215 °C, Heizbett 60 / 60 °C, Diagramm der letzten 60 Minuten", "Keine Daten", "jetzt")]
    public void TextsFollowTheLanguage(string culture, string accessibleName, string noData, string now)
    {
        using CultureScope scope = new(culture);

        TemperaturesWidgetViewModel widget = Show(Create(), Heating());

        Assert.Equal(accessibleName, widget.AccessibleName);
        Assert.Equal(noData, widget.ChartNoData);
        Assert.Equal(now, widget.ChartNow);
    }

    [Fact]
    public void ThousandsAreGroupedByTheRegion()
    {
        TemperaturesWidgetViewModel widget = Show(Create(), Heating(new Temperature(1215, 1200)));

        Assert.Equal("1,215", widget.NozzleActual);
        Assert.Equal(" / 1,200 °C", widget.NozzleRest);
    }

    [Fact]
    public void LanguageSwitchFormatsTheShownTemperaturesAgainWithoutAPoll()
    {
        CameraOverlayViewModel overlay = Create();
        TemperaturesWidgetViewModel widget = Show(overlay, Heating());
        int posts = _dispatcher.PostCount;

        using (new CultureScope("cs-CZ"))
        {
            overlay.RefreshTexts();

            Assert.Equal("Tryska", widget.NozzleLabel);
            Assert.Equal("Žádná data", widget.ChartNoData);
            Assert.StartsWith("Tryska 215", widget.AccessibleName, StringComparison.Ordinal);
        }

        Assert.Equal(posts, _dispatcher.PostCount);
    }

    [Fact]
    public void EveryLanguageHasTheChartTexts()
    {
        foreach (string language in Core.Settings.AppLanguages.Supported)
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(language);
            foreach ((string key, int max) in new[] { (nameof(Resources.Strings.CameraOverlayChartStart), 8), (nameof(Resources.Strings.CameraOverlayChartNow), 10), (nameof(Resources.Strings.CameraOverlayChartNoData), 14), (nameof(Resources.Strings.CameraOverlayChartName), 60) })
            {
                string? text = Resources.Strings.ResourceManager.GetString(key, culture);
                Assert.False(string.IsNullOrWhiteSpace(text), $"{language}: {key}");
                Assert.True(text.Length <= max, $"{language}: {key} = {text}");
            }

            // T-076 10: the minus sign, not a hyphen.
            Assert.StartsWith("−", Resources.Strings.ResourceManager.GetString(nameof(Resources.Strings.CameraOverlayChartStart), culture), StringComparison.Ordinal);
        }
    }

    // ---- Splitting the key value ----

    [Theory]
    [InlineData("en-US", 215L, 215L, "215", " / 215 °C")]
    [InlineData("en-US", 28L, null, "28", " °C")]
    [InlineData("cs-CZ", 1215L, 215L, "1 215", " / 215 °C")]
    [InlineData("en-US", -4L, null, "-4", " °C")]
    public void TemperaturePartsSplitTheActualValueFromTheRest(string culture, long actual, long? target, string expectedActual, string expectedRest)
    {
        using CultureScope scope = new(culture);
        OverlayTemperature temperature = new(actual, target);

        (string shownActual, string rest) = OverlayFormat.TemperatureParts(temperature);

        Assert.Equal(expectedActual, shownActual);
        Assert.Equal(expectedRest, rest);
        Assert.Equal(OverlayFormat.Temperature(temperature), shownActual + rest);
    }

    [Fact]
    public void ShownTextsDoNotChangeWhenOnlyTheSizeChanges()
    {
        TemperaturesWidgetViewModel widget = Show(Create(), Heating());
        List<string?> changed = [];
        PropertyChangedEventHandler handler = (_, e) => changed.Add(e.PropertyName);
        widget.PropertyChanged += handler;

        widget.RenderedSize = OverlayWidgetSize.Large;
        widget.PropertyChanged -= handler;

        Assert.DoesNotContain(nameof(TemperaturesWidgetViewModel.Bed), changed);
        Assert.DoesNotContain(nameof(TemperaturesWidgetViewModel.NozzleLabel), changed);
    }
}
