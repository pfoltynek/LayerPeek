using System.Globalization;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Cameras.Overlay.Widgets;
using LayerPeek.App.Composition;
using LayerPeek.App.Resources;
using LayerPeek.App.Settings.Widgets;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Cooldown;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;
using LayerPeek.Tests.Shared;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Cameras.Overlay.Widgets;

/// <summary>
/// The Cool-down widget (T-088, design T-076 2.5) driven by the real overlay and the real <see cref="CooldownMonitor"/>
/// (T-079) on a fake clock: "Cooling down…" with the temperatures, the progress to the threshold and the estimate,
/// "Cooled down – safe to turn off" with the end of the print and how long it took; hidden without a cool-down, at the
/// next print, offline and when the nozzle heats again. Allocations: <see cref="CooldownWidgetAllocationTests"/>.
/// </summary>
public sealed class CooldownWidgetTests : IDisposable
{
    private const string Nbsp = " ";

    private const double PollSeconds = 5;

    private readonly ManualDispatcher _dispatcher = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly FakePrinterMonitor _monitor = new();
    private readonly CooldownMonitor _cooldown;
    private readonly CultureScope _culture = new("en-US");
    private readonly List<CameraOverlayViewModel> _created = [];

    public CooldownWidgetTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
        _cooldown = new CooldownMonitor(_monitor, _time, new ListLogger<CooldownMonitor>());
        _cooldown.Configure([NozzleDecides(Definition())], TimeSpan.FromSeconds(PollSeconds));
    }

    public void Dispose()
    {
        foreach (CameraOverlayViewModel overlay in _created)
        {
            overlay.Dispose();
        }

        _cooldown.Dispose();
        _culture.Dispose();
    }

    /// <summary>The nozzle after a print: 215 °C cooling to a 23 °C room with τ = 60 s (the simulator's curve).</summary>
    internal static double Newton(double seconds) => 23 + (192 * Math.Exp(-seconds / 60));

    /// <summary>A printer whose print ended (finished or stopped) <paramref name="seconds"/> after <see cref="TestPrinters.Now"/>.</summary>
    internal static PrinterSnapshot Ended(
        double seconds,
        double nozzle,
        PrinterState state = PrinterState.Finished,
        double? bed = 40.4,
        double nozzleTarget = 0,
        string printerId = "mk4") =>
        Snapshot(state, Job(100), printerId) with
        {
            Timestamp = Now.AddSeconds(seconds),
            Telemetry = new Telemetry { Nozzle = new Temperature(nozzle, nozzleTarget), Bed = bed is { } b ? new Temperature(b, 0) : null },
        };

    internal static PrinterSnapshot Printing(double seconds = 0, string printerId = "mk4") =>
        Snapshot(PrinterState.Printing, Job(50, TimeSpan.FromMinutes(30)), printerId) with
        {
            Timestamp = Now.AddSeconds(seconds),
            Telemetry = new Telemetry { Nozzle = new Temperature(215, 215), Bed = new Temperature(60, 60) },
        };

    private static CooldownState CoolingState(
        double nozzle = 87.4,
        double? bed = 45.2,
        int threshold = 50,
        double progress = 0.6,
        TimeSpan? remaining = null,
        DateTimeOffset? updatedAt = null) =>
        new(CooldownPhase.Cooling, nozzle, bed, threshold, 40, progress, remaining, Now, updatedAt ?? Now, CooldownPrintOutcome.Finished, Now, null);

    private static CooldownState CooledState(
        CooldownPrintOutcome outcome = CooldownPrintOutcome.Finished,
        DateTimeOffset? printEndedAt = null,
        bool endUnknown = false,
        TimeSpan? cooledIn = null,
        bool endedBelowThreshold = false,
        double? bed = 39) =>
        new(
            CooldownPhase.CooledDown,
            48.3,
            bed,
            50,
            40,
            1,
            null,
            Now,
            Now,
            outcome,
            endUnknown ? null : printEndedAt ?? Now.AddHours(7).AddMinutes(52).AddSeconds(31),
            (endUnknown ? Now : printEndedAt ?? Now.AddHours(7).AddMinutes(52).AddSeconds(31)) + (cooledIn ?? TimeSpan.FromMinutes(13.2)),
            endedBelowThreshold);

    private static OverlayWidgetInput Input(PrinterSnapshot? snapshot, DateTimeOffset? now = null) =>
        new(snapshot, Definition(), CameraWindowMode.Normal, now ?? Now, TimeZoneInfo.Utc, CultureInfo.CurrentCulture);

    private CameraOverlayViewModel Create(OverlayWidgetSize size = OverlayWidgetSize.Medium, bool emphasized = true, CooldownMonitor? cooldown = null, bool withCooldown = true)
    {
        CameraOverlayViewModel overlay = new(
            OverlayWidgets.CreateRegistry(),
            new OverlayWidgetContext("mk4", _time, Cooldown: withCooldown ? cooldown ?? _cooldown : null),
            _dispatcher,
            NullLogger.Instance,
            Definition(),
            new CameraOverlayLayout([new OverlayWidgetPlacement(OverlayWidgetIds.Cooldown, OverlayAnchor.BottomLeft, size, emphasized)]),
            CameraOverlayOpacity.DefaultPercent,
            CameraWindowMode.Normal,
            snapshot: null);
        _created.Add(overlay);
        Widget(overlay).RenderedSize = size;
        // Like the camera window in the app: subscribed after the cool-down monitor, only to its printer.
        _monitor.SnapshotUpdated += (_, snapshot) =>
        {
            if (snapshot.PrinterId == "mk4")
            {
                overlay.OnSnapshot(snapshot);
            }
        };
        return overlay;
    }

    private static CooldownWidgetViewModel Widget(CameraOverlayViewModel overlay) => (CooldownWidgetViewModel)Assert.Single(overlay.Widgets);

    private void Raise(PrinterSnapshot snapshot)
    {
        if (snapshot.Timestamp > _time.GetUtcNow())
        {
            _time.SetUtcNow(snapshot.Timestamp);
        }

        _monitor.Raise(snapshot);
        _dispatcher.RunAll();
    }

    /// <summary>A print, its end at 5 s and readings every 5 s along <see cref="Newton"/> until <paramref name="until"/> (inclusive).</summary>
    private void CoolUntil(double until, PrinterState ended = PrinterState.Finished)
    {
        Raise(Printing());
        for (double t = PollSeconds; t <= until; t += PollSeconds)
        {
            Raise(Ended(t, Newton(t - PollSeconds), ended));
        }
    }

    /// <summary>Cools until the cool-down monitor has an estimate; the time of the last reading.</summary>
    private double CoolUntilEstimate()
    {
        Raise(Printing());
        for (double t = PollSeconds; t < 100; t += PollSeconds)
        {
            Raise(Ended(t, Newton(t - PollSeconds)));
            if (_cooldown.GetState("mk4").EstimatedRemaining is not null)
            {
                return t;
            }
        }

        throw new InvalidOperationException("No estimate.");
    }

    // ---- Registration (T-092 contract) ----

    [Fact]
    public void CooldownWidgetIsRegisteredWithItsNamePriorityAndSizes()
    {
        IOverlayWidgetDefinition definition = Assert.IsType<IOverlayWidgetDefinition>(OverlayWidgets.CreateRegistry().Find(OverlayWidgetIds.Cooldown), exactMatch: false);

        Assert.Equal(OverlayWidgetNames.Cooldown, definition.NameKey);
        Assert.Equal(OverlayWidgets.CooldownPriority, definition.Priority);
        Assert.Equal([OverlayWidgetSize.Small, OverlayWidgetSize.Medium, OverlayWidgetSize.Large], definition.SupportedSizes);
        // T-076 2.5: S max 260; M 240–300; L 280–360.
        Assert.Equal(0, definition.MinWidth(OverlayWidgetSize.Small));
        Assert.Equal(240, definition.MinWidth(OverlayWidgetSize.Medium));
        Assert.Equal(280, definition.MinWidth(OverlayWidgetSize.Large));
        Assert.Same(CooldownWidgetView.CardTemplate, definition.CardTemplate);
        using OverlayWidgetViewModel widget = definition.Create(new OverlayWidgetContext("mk4", _time, Cooldown: _cooldown));
        Assert.IsType<CooldownWidgetViewModel>(widget);
    }

    [Fact]
    public void RegistrationDeclaresTheDescriptionIconAndSettingsPanelOfTheWidgetsTab()
    {
        IOverlayWidgetDefinition definition = OverlayWidgets.CreateRegistry().Find(OverlayWidgetIds.Cooldown)!;

        Assert.Equal(OverlayWidgetDescriptions.Cooldown, definition.DescriptionKey);
        Assert.Equal(nameof(Strings.CameraOverlayWidgetCooldownDescription), definition.DescriptionKey);
        Assert.Equal(OverlayWidgetIcons.Cooldown, definition.IconKey);
        Assert.Equal("IconWeatherSnowflake20Regular", definition.IconKey);
        Assert.Same(CooldownWidgetSettings.Instance, definition.Settings);
    }

    [Fact]
    public void CooldownIsTheLastWidgetOfTheSettingsList()
    {
        // The order of the widget list on the Widgets tab (T-076 8): Status, Times, Temperatures, Print name, Cool-down.
        Assert.Equal(
            [OverlayWidgetIds.Status, OverlayWidgetIds.Times, OverlayWidgetIds.Temperatures, OverlayWidgetIds.PrintName, OverlayWidgetIds.Cooldown],
            OverlayWidgets.CreateRegistry().Definitions.Select(static definition => definition.Id));
    }

    [Fact]
    public void WidgetWorksWithoutACooldownMonitor()
    {
        // A window without the cool-down service (tests, other hosts): never a card, nothing to listen to.
        CameraOverlayViewModel overlay = Create(withCooldown: false);
        CoolUntil(30);

        CooldownWidgetViewModel widget = Widget(overlay);
        Assert.False(widget.IsShown);
        Assert.False(widget.IsListening);
    }

    // ---- When the card is shown ----

    [Fact]
    public void WithoutACoolDownTheCardIsHidden()
    {
        CameraOverlayViewModel overlay = Create();

        Raise(Snapshot(PrinterState.Idle));
        Assert.False(Widget(overlay).IsShown);

        Raise(Printing(5));
        Assert.False(Widget(overlay).IsShown);
    }

    [Fact]
    public void CoolingShowsTheTitleTemperaturesAndProgress()
    {
        CameraOverlayViewModel overlay = Create();

        CoolUntil(10);

        CooldownWidgetViewModel widget = Widget(overlay);
        Assert.True(widget.IsShown);
        Assert.True(widget.IsCooling);
        Assert.False(widget.IsCooled);
        Assert.False(widget.IsOutlined);
        Assert.Equal("Cooling down…", widget.HeaderText);
        Assert.True(widget.ShowsHeader);
        long nozzle = (long)Math.Round(Newton(5), MidpointRounding.AwayFromZero);
        Assert.Equal($"{nozzle}{Nbsp}°C", widget.NozzleText);
        Assert.True(widget.ShowsNozzle);
        Assert.Equal($"40{Nbsp}°C", widget.BedText);
        Assert.True(widget.ShowsBed);
        Assert.True(widget.ShowsProgress);
        // From the highest reading since the end (215) to the threshold, rounded down to whole percent.
        Assert.Equal(Math.Floor((215 - Newton(5)) / (215 - 50) * 100), widget.Progress);
        Assert.True(widget.ShowsDetail);
        Assert.Equal($"safe to turn off once the nozzle is below 50{Nbsp}°C and the bed below 45{Nbsp}°C", widget.DetailText);
    }

    [Fact]
    public void CoolingShowsTheEstimateOnceTheMonitorHasOne()
    {
        CameraOverlayViewModel overlay = Create();

        CoolUntilEstimate();

        CooldownWidgetViewModel widget = Widget(overlay);
        long minutes = (long)Math.Ceiling(_cooldown.GetState("mk4").EstimatedRemaining!.Value.TotalMinutes);
        Assert.Equal($"safe to turn off in about {minutes}\u00A0min (nozzle below 50{Nbsp}°C, bed below 45{Nbsp}°C)", widget.DetailText);
    }

    [Theory]
    [InlineData(PrinterState.Finished, "print finished at")]
    [InlineData(PrinterState.Stopped, "print stopped at")]
    public void CooledDownShowsTheOutlineTitleTemperaturesAndTimes(PrinterState ended, string endedText)
    {
        CameraOverlayViewModel overlay = Create();

        CoolUntil(200, ended);

        CooldownState state = _cooldown.GetState("mk4");
        Assert.Equal(CooldownPhase.CooledDown, state.Phase);
        CooldownWidgetViewModel widget = Widget(overlay);
        Assert.True(widget.IsShown);
        Assert.True(widget.IsCooled);
        Assert.False(widget.IsCooling);
        Assert.True(widget.IsOutlined);
        Assert.Equal("Cooled down – safe to turn off", widget.HeaderText);
        Assert.Equal($"{(long)Math.Round(state.NozzleCelsius!.Value, MidpointRounding.AwayFromZero)}{Nbsp}°C", widget.NozzleText);
        Assert.Equal($"40{Nbsp}°C", widget.BedText);
        Assert.False(widget.ShowsProgress);
        long cooledIn = (long)Math.Ceiling((state.CooledDownAt!.Value - state.PrintEndedAt!.Value).TotalMinutes);
        // The print ended at the first Finished / Stopped snapshot: 12:00:05 (UTC).
        Assert.Equal(CultureTime.EnUs($"{endedText} 12:00 PM · cooled down in {cooledIn}\u00A0min"), widget.DetailText);
        Assert.True(widget.ShowsDetail);
    }

    [Fact]
    public void PrintEndedBelowTheThresholdShowsCooledDownRightAwayWithOnlyTheEndTime()
    {
        CameraOverlayViewModel overlay = Create();
        Raise(Printing());
        bool sawCooling = false;
        Raise(Ended(5, 45));
        sawCooling |= Widget(overlay).IsCooling;

        Raise(Ended(10, 44));

        CooldownWidgetViewModel widget = Widget(overlay);
        Assert.False(sawCooling);
        Assert.True(widget.IsCooled);
        Assert.Equal(CultureTime.EnUs("print finished at 12:00 PM"), widget.DetailText);
    }

    [Fact]
    public void UnknownEndOfThePrintLeavesOutTheTimesRow()
    {
        // User decision 10: the print ended while the printer could not be read (PrintEndedAt null).
        CooldownContent content = CooldownWidgetViewModel.ContentOf(Input(Ended(0, 48)), CooledState(endUnknown: true));

        Assert.Equal(CooldownPhase.CooledDown, content.Phase);
        Assert.Null(content.PrintEndedLocal);
        Assert.Null(content.CooledInMinutes);
        Assert.Null(CooldownFormat.Times(content));
        Assert.Equal($"Cooled down – safe to turn off, Nozzle 48{Nbsp}°C, Bed 39{Nbsp}°C", CooldownFormat.AccessibleText(content));
    }

    [Fact]
    public void UnknownEndOfThePrintHidesTheDetailOfTheCooledCard()
    {
        // The real path: the printer was seen printing and was unreadable when the print ended.
        CameraOverlayViewModel overlay = Create();
        Raise(Printing());
        Raise(PrinterSnapshot.Unavailable("mk4", PrinterState.Offline, Now.AddSeconds(5), "unreachable"));
        for (double t = 10; t <= 30; t += PollSeconds)
        {
            Raise(Ended(t, 45));
        }

        CooldownWidgetViewModel widget = Widget(overlay);
        Assert.Null(_cooldown.GetState("mk4").PrintEndedAt);
        Assert.True(widget.IsCooled);
        Assert.False(widget.ShowsDetail);
        Assert.Equal(string.Empty, widget.DetailText);
    }

    [Fact]
    public void CooledContentCarriesTheEndInTheLocalTimeAndTheMinutesRoundedUp()
    {
        CooldownContent content = CooldownWidgetViewModel.ContentOf(Input(Ended(0, 48)), CooledState(cooledIn: TimeSpan.FromMinutes(13.2)));

        Assert.Equal(new DateTime(2026, 10, 16, 19, 52, 0), content.PrintEndedLocal);
        Assert.Equal(14, content.CooledInMinutes);
        Assert.Equal(48, content.NozzleCelsius);
        Assert.Equal(39, content.BedCelsius);
        Assert.Equal(100, content.ProgressPercent);
        Assert.Null(content.EstimateMinutes);
        Assert.Equal(CultureTime.EnUs("print finished at 7:52 PM · cooled down in 14\u00A0min"), CooldownFormat.Times(content));
    }

    [Fact]
    public void StoppedPrintSaysStopped()
    {
        CooldownContent content = CooldownWidgetViewModel.ContentOf(Input(Ended(0, 48, PrinterState.Stopped)), CooledState(CooldownPrintOutcome.Stopped));

        Assert.Equal(CultureTime.EnUs("print stopped at 7:52 PM · cooled down in 14\u00A0min"), CooldownFormat.Times(content));
        Assert.Equal(
            CultureTime.EnUs($"Cooled down – safe to turn off, Nozzle 48{Nbsp}°C, Bed 39{Nbsp}°C, print stopped at 7:52 PM, cooled down in 14\u00A0min"),
            CooldownFormat.AccessibleText(content));
    }

    [Fact]
    public void EndedBelowTheThresholdHasNoCooledInMinutes()
    {
        CooldownContent content = CooldownWidgetViewModel.ContentOf(Input(Ended(0, 48)), CooledState(endedBelowThreshold: true, cooledIn: TimeSpan.FromSeconds(5)));

        Assert.Null(content.CooledInMinutes);
        Assert.Equal(CultureTime.EnUs("print finished at 7:52 PM"), CooldownFormat.Times(content));
    }

    [Fact]
    public void PrintEndedTimeIsInTheLocalTimeZone()
    {
        TimeZoneInfo prague = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");
        OverlayWidgetInput input = Input(Ended(0, 48)) with { TimeZone = prague };

        CooldownContent content = CooldownWidgetViewModel.ContentOf(input, CooledState(printEndedAt: Now.AddHours(17).AddMinutes(52)));

        // 2026-10-16 is summer time in Prague (UTC+2): 05:52 UTC the next day = 07:52.
        Assert.Equal(new DateTime(2026, 10, 17, 7, 52, 0), content.PrintEndedLocal);
    }

    [Theory]
    [InlineData("next print")]
    [InlineData("offline")]
    [InlineData("reheating")]
    public void CardDisappearsAtTheNextPrintOfflineAndWhenTheNozzleHeatsAgain(string reason)
    {
        CameraOverlayViewModel overlay = Create();
        CoolUntil(30);
        Assert.True(Widget(overlay).IsShown);

        Raise(reason switch
        {
            "next print" => Printing(35),
            "offline" => PrinterSnapshot.Unavailable("mk4", PrinterState.Offline, Now.AddSeconds(35), "unreachable"),
            _ => Ended(35, 120, nozzleTarget: 170),
        });

        Assert.False(Widget(overlay).IsShown);
    }

    [Fact]
    public void CooledCardDisappearsAtTheNextPrint()
    {
        CameraOverlayViewModel overlay = Create();
        CoolUntil(200);
        Assert.True(Widget(overlay).IsCooled);

        Raise(Printing(205));

        Assert.False(Widget(overlay).IsShown);
    }

    [Fact]
    public void CardHidesAtOnceForAnUnreachableSnapshotEvenBeforeTheMonitorEndsTheCoolDown()
    {
        // The monitor keeps the cool-down for a minute offline (T-079 decision 7); the card shows no stale temperatures meanwhile.
        CooldownState cooling = CoolingState();

        Assert.Equal(default, CooldownWidgetViewModel.ContentOf(Input(PrinterSnapshot.Unavailable("mk4", PrinterState.Offline, Now, "x")), cooling));
        Assert.Equal(default, CooldownWidgetViewModel.ContentOf(Input(null), cooling));
        Assert.Equal(default, CooldownWidgetViewModel.ContentOf(Input(Printing()), cooling));
        Assert.NotEqual(default, CooldownWidgetViewModel.ContentOf(Input(Ended(0, 87)), cooling));
    }

    [Fact]
    public void WithoutANozzleTemperatureTheCardIsHidden()
    {
        Assert.Equal(default, CooldownWidgetViewModel.ContentOf(Input(Ended(0, 87)), CoolingState(nozzle: double.NaN)));
        Assert.Equal(default, CooldownWidgetViewModel.ContentOf(Input(Ended(0, 87)), CoolingState() with { NozzleCelsius = null }));
    }

    [Fact]
    public void MissingBedShowsOnlyTheNozzle()
    {
        CameraOverlayViewModel overlay = Create();

        Raise(Printing());
        Raise(Ended(5, 200, bed: null));
        Raise(Ended(10, 180, bed: null));

        CooldownWidgetViewModel widget = Widget(overlay);
        Assert.True(widget.IsShown);
        Assert.False(widget.ShowsBed);
        Assert.Equal($"Cooling down…, Nozzle 180{Nbsp}°C, safe to turn off once the nozzle is below 50{Nbsp}°C", widget.AccessibleName);
    }

    // ---- The estimate ----

    [Theory]
    [InlineData(5.5, 6L)]
    [InlineData(0.1, 1L)]
    [InlineData(61, 61L)]
    public void EstimateIsRoundedUpToTheMinute(double minutes, long expected)
    {
        CooldownContent content = CooldownWidgetViewModel.ContentOf(Input(Ended(0, 87)), CoolingState(remaining: TimeSpan.FromMinutes(minutes)));

        Assert.Equal(expected, content.EstimateMinutes);
    }

    [Fact]
    public void EstimateTextNamesTheMinutesAndTheThreshold()
    {
        CooldownContent content = CooldownWidgetViewModel.ContentOf(Input(Ended(0, 87)), CoolingState(remaining: TimeSpan.FromMinutes(5.5)));

        Assert.Equal($"safe to turn off in about 6\u00A0min (nozzle below 50{Nbsp}°C, bed below 40{Nbsp}°C)", CooldownFormat.Detail(content));
        Assert.Equal(" · about 6\u00A0min", CooldownFormat.SmallDetail(content));
        Assert.Equal(
            $"Cooling down…, Nozzle 87{Nbsp}°C, Bed 45{Nbsp}°C, safe to turn off in about 6\u00A0min (nozzle below 50{Nbsp}°C, bed below 40{Nbsp}°C)",
            CooldownFormat.AccessibleText(content));
    }

    [Fact]
    public void WithoutAnEstimateTheTextNamesOnlyTheThresholdNeverZeroMinutes()
    {
        foreach (TimeSpan? remaining in new TimeSpan?[] { null, TimeSpan.Zero })
        {
            CooldownContent content = CooldownWidgetViewModel.ContentOf(Input(Ended(0, 87)), CoolingState(remaining: remaining));

            Assert.Null(content.EstimateMinutes);
            Assert.Equal($"safe to turn off once the nozzle is below 50{Nbsp}°C and the bed below 40{Nbsp}°C", CooldownFormat.Detail(content));
            Assert.Equal(string.Empty, CooldownFormat.SmallDetail(content));
        }
    }

    [Fact]
    public void StaleStateShowsNoEstimate()
    {
        // Monitoring stopped (no snapshot for longer than the longest gap the monitor tolerates): no frozen estimate.
        CooldownState stale = CoolingState(remaining: TimeSpan.FromMinutes(30), updatedAt: Now);

        Assert.Equal(30, CooldownWidgetViewModel.ContentOf(Input(Ended(0, 87), Now + CooldownWidgetViewModel.StaleAfter), stale).EstimateMinutes);
        Assert.Null(CooldownWidgetViewModel.ContentOf(Input(Ended(0, 87), Now + CooldownWidgetViewModel.StaleAfter + TimeSpan.FromSeconds(1)), stale).EstimateMinutes);
    }

    [Fact]
    public void EstimateCountsDownWithTheClockWithoutNewSnapshots()
    {
        CameraOverlayViewModel overlay = Create();
        CoolUntilEstimate();
        CooldownWidgetViewModel widget = Widget(overlay);
        TimeSpan remaining = _cooldown.GetState("mk4").EstimatedRemaining!.Value;
        Assert.True(widget.IsTicking);
        string before = widget.DetailText;

        // A minute later without any snapshot (the monitor counts the estimate down from its reading).
        _time.Advance(TimeSpan.FromMinutes(1));
        _dispatcher.RunAll();

        long minutes = (long)Math.Ceiling((remaining - TimeSpan.FromMinutes(1)).TotalMinutes);
        if (minutes > 0)
        {
            Assert.NotEqual(before, widget.DetailText);
            Assert.Equal($"safe to turn off in about {minutes}\u00A0min (nozzle below 50{Nbsp}°C, bed below 45{Nbsp}°C)", widget.DetailText);
        }

        // Past the estimate: no "0 min", only the threshold, and the clock stops ticking.
        _time.Advance(remaining);
        _dispatcher.RunAll();

        Assert.Equal($"safe to turn off once the nozzle is below 50{Nbsp}°C and the bed below 45{Nbsp}°C", widget.DetailText);
        Assert.False(widget.IsTicking);
    }

    [Fact]
    public void ClockTicksOnlyWhileAnEstimateIsShown()
    {
        CameraOverlayViewModel overlay = Create();
        CooldownWidgetViewModel widget = Widget(overlay);
        Raise(Printing());
        Raise(Ended(5, 200));
        Assert.False(widget.IsTicking);

        CoolUntilEstimate();
        Assert.True(widget.IsTicking);

        Raise(Printing(200));
        Assert.False(widget.IsTicking);
    }

    [Fact]
    public void TicksWithoutAVisibleChangePostNothing()
    {
        CameraOverlayViewModel overlay = Create();
        CoolUntilEstimate();
        CooldownWidgetViewModel widget = Widget(overlay);
        int computes = widget.ComputeCount;

        for (int tick = 0; tick < 4; tick++)
        {
            long? shown = widget.ShownEstimateMinutes;
            int posts = _dispatcher.PostCount;

            _time.Advance(CooldownWidgetViewModel.TickInterval);
            long? now = CooldownWidgetViewModel.ContentOf(
                Input(Ended(0, 87), _time.GetUtcNow()),
                _cooldown.GetState("mk4")).EstimateMinutes;

            // Every tick computes; only a tick that changes the shown minutes posts (and formats).
            Assert.Equal(computes + tick + 1, widget.ComputeCount);
            Assert.Equal(posts + (now == shown ? 0 : 1), _dispatcher.PostCount);
            _dispatcher.RunAll();
        }
    }

    // ---- Threshold ----

    [Fact]
    public void ThresholdChangeAppliesRightAway()
    {
        CameraOverlayViewModel overlay = Create();
        CoolUntil(15);
        Assert.Equal($"safe to turn off once the nozzle is below 50{Nbsp}°C and the bed below 45{Nbsp}°C", Widget(overlay).DetailText);

        _cooldown.Configure([Definition() with { CooldownThresholds = new(65, 35) }], TimeSpan.FromSeconds(PollSeconds));
        _dispatcher.RunAll();

        Assert.Equal($"safe to turn off once the nozzle is below 65{Nbsp}°C and the bed below 35{Nbsp}°C", Widget(overlay).DetailText);
    }

    // ---- T-109: nozzle and bed ----

    [Theory]
    [InlineData(OverlayWidgetSize.Small, "54")]
    [InlineData(OverlayWidgetSize.Medium, "38")]
    [InlineData(OverlayWidgetSize.Large, "38")]
    public void WhileTheBedHoldsTheCoolDownUpTheSmallCardShowsTheBed(OverlayWidgetSize size, string shown)
    {
        // The default thresholds (40 / 40 °C): the nozzle is below, the bed is not.
        _cooldown.Configure([Definition()], TimeSpan.FromSeconds(PollSeconds));
        CameraOverlayViewModel overlay = Create(size);
        Raise(Printing());
        Raise(Ended(5, 39, bed: 55));
        Raise(Ended(10, 38, bed: 54));

        CooldownWidgetViewModel widget = Widget(overlay);
        Assert.True(widget.IsCooling);
        Assert.Equal(CooldownSensor.Bed, _cooldown.GetState("mk4").Limiting);
        Assert.Equal(38, widget.ShownNozzleCelsius);
        Assert.Equal(54, widget.ShownLimitingCelsius);
        Assert.Equal($"{shown}{Nbsp}°C", widget.NozzleText);
        if (size != OverlayWidgetSize.Small)
        {
            Assert.Equal($"54{Nbsp}°C", widget.BedText);
            Assert.Equal($"safe to turn off once the nozzle is below 40{Nbsp}°C and the bed below 40{Nbsp}°C", widget.DetailText);
            Assert.Equal(
                $"Cooling down…, Nozzle 38{Nbsp}°C, Bed 54{Nbsp}°C, safe to turn off once the nozzle is below 40{Nbsp}°C and the bed below 40{Nbsp}°C",
                widget.AccessibleName);
        }
    }

    [Fact]
    public void SmallCooledCardShowsTheTemperatureThatLimitedLast()
    {
        CooldownState cooled = CooledState(bed: 39.6) with { NozzleCelsius = 31, Limiting = CooldownSensor.Bed };

        CooldownContent content = CooldownWidgetViewModel.ContentOf(Input(Ended(0, 31)), cooled);

        Assert.Equal(31, content.NozzleCelsius);
        Assert.Equal(40, content.LimitingCelsius);
        Assert.Equal($"Cooled down · 40{Nbsp}°C", CooldownFormat.SmallCooled(content));
    }

    [Fact]
    public void ProgressIsTheSlowerOfTheTwo()
    {
        _cooldown.Configure([Definition()], TimeSpan.FromSeconds(PollSeconds));
        CameraOverlayViewModel overlay = Create();
        Raise(Printing());
        Raise(Ended(5, 200, bed: 60));
        // Nozzle (200 − 100) / (200 − 40) = 62 %, bed (60 − 55) / (60 − 40) = 25 %.
        Raise(Ended(10, 100, bed: 55));

        Assert.Equal(25, Widget(overlay).Progress);
    }

    [Fact]
    public void BedThresholdIsShownInTheDetailOfEveryLanguage()
    {
        CooldownContent content = CooldownWidgetViewModel.ContentOf(Input(Ended(0, 87)), CoolingState(remaining: TimeSpan.FromMinutes(5.5)) with { BedThresholdCelsius = 35 });
        foreach (string language in AppLanguages.Supported)
        {
            using UiCultureScope culture = new(language);

            Assert.Contains($"35{Nbsp}°C", CooldownFormat.Detail(content), StringComparison.Ordinal);
            Assert.Contains($"50{Nbsp}°C", CooldownFormat.Detail(content), StringComparison.Ordinal);
            Assert.Contains($"35{Nbsp}°C", CooldownFormat.Detail(content with { EstimateMinutes = null }), StringComparison.Ordinal);
        }
    }

    // ---- Sizes and emphasis ----

    [Fact]
    public void SmallCoolingCardIsOneRowWithTheNozzleAndTheEstimate()
    {
        CameraOverlayViewModel overlay = Create(OverlayWidgetSize.Small);
        CoolUntilEstimate();

        CooldownWidgetViewModel widget = Widget(overlay);
        long minutes = (long)Math.Ceiling(_cooldown.GetState("mk4").EstimatedRemaining!.Value.TotalMinutes);
        Assert.False(widget.ShowsHeader);
        Assert.True(widget.ShowsNozzle);
        Assert.False(widget.ShowsBed);
        Assert.True(widget.ShowsProgress);
        Assert.True(widget.ShowsDetail);
        Assert.Equal($" · about {minutes}\u00A0min", widget.DetailText);
    }

    [Fact]
    public void SmallCooledCardIsTheShortStateWithTheNozzle()
    {
        CameraOverlayViewModel overlay = Create(OverlayWidgetSize.Small);
        CoolUntil(200);

        CooldownWidgetViewModel widget = Widget(overlay);
        Assert.True(widget.ShowsHeader);
        Assert.Equal($"Cooled down · {widget.ShownNozzleCelsius}{Nbsp}°C", widget.HeaderText);
        Assert.False(widget.ShowsNozzle);
        Assert.False(widget.ShowsBed);
        Assert.False(widget.ShowsProgress);
        Assert.False(widget.ShowsDetail);
    }

    [Fact]
    public void ChangingTheSizeChangesTheTextsWithoutANewSnapshot()
    {
        CameraOverlayViewModel overlay = Create(OverlayWidgetSize.Medium);
        CoolUntil(200);
        CooldownWidgetViewModel widget = Widget(overlay);
        Assert.Equal("Cooled down – safe to turn off", widget.HeaderText);

        widget.RenderedSize = OverlayWidgetSize.Small;

        Assert.StartsWith("Cooled down · ", widget.HeaderText, StringComparison.Ordinal);
        Assert.False(widget.ShowsDetail);

        widget.RenderedSize = OverlayWidgetSize.Large;

        Assert.Equal("Cooled down – safe to turn off", widget.HeaderText);
        Assert.True(widget.ShowsDetail);
    }

    [Fact]
    public void EmphasisIsTheNozzleWhileCoolingAndTheStateOnceCooled()
    {
        CameraOverlayViewModel overlay = Create(emphasized: true);
        CoolUntil(15);
        CooldownWidgetViewModel widget = Widget(overlay);
        Assert.True(widget.EmphasizesNozzle);
        Assert.False(widget.EmphasizesHeader);

        CoolUntil(200);
        Assert.False(widget.EmphasizesNozzle);
        Assert.True(widget.EmphasizesHeader);
    }

    [Fact]
    public void WithoutEmphasisNothingIsEmphasized()
    {
        CameraOverlayViewModel overlay = Create(emphasized: false);
        CoolUntil(15);
        CooldownWidgetViewModel widget = Widget(overlay);
        Assert.False(widget.EmphasizesNozzle);

        CoolUntil(200);
        Assert.False(widget.EmphasizesHeader);
        Assert.False(widget.EmphasizesNozzle);
    }

    [Fact]
    public void EmphasisFollowsTheLayoutWithoutANewSnapshot()
    {
        CameraOverlayViewModel overlay = Create(emphasized: false);
        CoolUntil(200);
        CooldownWidgetViewModel widget = Widget(overlay);
        Assert.False(widget.EmphasizesHeader);

        widget.IsEmphasized = true;

        Assert.True(widget.EmphasizesHeader);
    }

    // ---- Cheap comparison ----

    [Fact]
    public void PollsThatChangeNothingVisiblePostNothing()
    {
        CameraOverlayViewModel overlay = Create();
        CoolUntil(200);
        Raise(Ended(205, 40.2));
        Assert.True(Widget(overlay).IsCooled);
        int posts = _dispatcher.PostCount;
        int formats = Widget(overlay).FormatCount;

        // Cooled down: every poll updates the monitor's state (its time), the card stays the same.
        for (double t = 210; t < 300; t += PollSeconds)
        {
            Raise(Ended(t, 40.2 + ((t % 10) / 100)));
        }

        Assert.Equal(posts, _dispatcher.PostCount);
        Assert.Equal(formats, Widget(overlay).FormatCount);
    }

    [Fact]
    public void CoolDownOfAnotherPrinterPostsNothing()
    {
        _cooldown.Configure([NozzleDecides(Definition()), NozzleDecides(Definition("other", "Other"))], TimeSpan.FromSeconds(PollSeconds));
        CameraOverlayViewModel overlay = Create();
        Raise(Snapshot(PrinterState.Idle));
        int posts = _dispatcher.PostCount;

        _monitor.Raise(Printing(printerId: "other"));
        _monitor.Raise(Ended(5, 200, printerId: "other"));
        _monitor.Raise(Ended(10, 180, printerId: "other"));

        Assert.Equal(CooldownPhase.Cooling, _cooldown.GetState("other").Phase);
        Assert.Equal(posts, _dispatcher.PostCount);
        Assert.False(Widget(overlay).IsShown);
    }

    // ---- Language, lifetime ----

    [Fact]
    public void CardFollowsALanguageSwitch()
    {
        CameraOverlayViewModel overlay = Create();
        CoolUntil(15);
        CooldownWidgetViewModel widget = Widget(overlay);

        using (new CultureScope("cs"))
        {
            overlay.RefreshTexts();

            Assert.Equal("Chladne…", widget.HeaderText);
            Assert.Equal($"bezpečné vypnutí, až tryska klesne pod 50{Nbsp}°C a podložka pod 45{Nbsp}°C", widget.DetailText);
            Assert.StartsWith("Chladne…, Tryska ", widget.AccessibleName, StringComparison.Ordinal);

            CoolUntil(200);

            Assert.Equal("Vychladlo – můžete vypnout", widget.HeaderText);
            Assert.StartsWith("tisk dokončen v 12:00 · vychladlo za ", widget.DetailText, StringComparison.Ordinal);
        }

        overlay.RefreshTexts();
        Assert.Equal("Cooled down – safe to turn off", widget.HeaderText);
    }

    [Fact]
    public void ClosingTheWindowStopsListeningAndTicking()
    {
        CameraOverlayViewModel overlay = Create();
        CoolUntilEstimate();
        CooldownWidgetViewModel widget = Widget(overlay);
        Assert.True(widget.IsListening);
        Assert.True(widget.IsTicking);
        int posts = _dispatcher.PostCount;

        overlay.Dispose();
        _created.Remove(overlay);
        Raise(Ended(100, 60));
        _time.Advance(TimeSpan.FromMinutes(2));

        Assert.False(widget.IsListening);
        Assert.False(widget.IsTicking);
        Assert.Equal(posts, _dispatcher.PostCount);
    }

    [Fact]
    public void HidingTheWidgetInTheLayoutStopsListening()
    {
        CameraOverlayViewModel overlay = Create();
        CoolUntil(15);
        CooldownWidgetViewModel widget = Widget(overlay);

        overlay.ApplySettings(Definition(), new CameraOverlayLayout([new OverlayWidgetPlacement(OverlayWidgetIds.Cooldown, OverlayAnchor.BottomLeft, visible: false)]), CameraOverlayOpacity.DefaultPercent);

        Assert.Empty(overlay.Widgets);
        Assert.False(widget.IsListening);
    }

    [Theory]
    [InlineData("cs")]
    [InlineData("sk")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("it")]
    [InlineData("pl")]
    [InlineData("en")]
    public void TextsExistInEveryLanguageWithANonBreakingSpaceBeforeTheDegrees(string language)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo(language);
        string[] keys =
        [
            nameof(Strings.CameraOverlayCooling), nameof(Strings.CameraOverlayCooled), nameof(Strings.CameraOverlayCooldownEstimate),
            nameof(Strings.CameraOverlayCooldownNoEstimate), nameof(Strings.CameraOverlayCooldownFinishedAt),
            nameof(Strings.CameraOverlayCooldownStoppedAt), nameof(Strings.CameraOverlayCooldownCooledIn), nameof(Strings.CameraOverlayCooldownAbout),
        ];
        foreach (string key in keys)
        {
            string? text = Strings.ResourceManager.GetString(key, culture);
            Assert.False(string.IsNullOrWhiteSpace(text), $"{key} ({language})");
            Assert.DoesNotContain(" °C", text, StringComparison.Ordinal);
            if (text.Contains('°', StringComparison.Ordinal))
            {
                Assert.Contains(Nbsp + "°C", text, StringComparison.Ordinal);
            }
        }

        Assert.Contains("{0}", Strings.ResourceManager.GetString(nameof(Strings.CameraOverlayCooldownEstimate), culture), StringComparison.Ordinal);
        Assert.Contains("{1}" + Nbsp + "°C", Strings.ResourceManager.GetString(nameof(Strings.CameraOverlayCooldownEstimate), culture), StringComparison.Ordinal);
        Assert.Contains("{0}" + Nbsp + "°C", Strings.ResourceManager.GetString(nameof(Strings.CameraOverlayCooldownNoEstimate), culture), StringComparison.Ordinal);
    }

    [Fact]
    public void CzechTextsUseThePoliteForm()
    {
        // Vykání like the rest of the Czech UI (T-076 Q3).
        CultureInfo cs = CultureInfo.GetCultureInfo("cs");

        Assert.Equal("Vychladlo – můžete vypnout", Strings.ResourceManager.GetString(nameof(Strings.CameraOverlayCooled), cs));
        Assert.Equal("Chladne…", Strings.ResourceManager.GetString(nameof(Strings.CameraOverlayCooling), cs));
        Assert.Equal($"bezpečné vypnutí za cca {{0}} (tryska pod {{1}}{Nbsp}°C)", Strings.ResourceManager.GetString(nameof(Strings.CameraOverlayCooldownEstimate), cs));
        Assert.Equal("tisk dokončen v {0}", Strings.ResourceManager.GetString(nameof(Strings.CameraOverlayCooldownFinishedAt), cs));
        Assert.Equal("vychladlo za {0}", Strings.ResourceManager.GetString(nameof(Strings.CameraOverlayCooldownCooledIn), cs));
    }
}
