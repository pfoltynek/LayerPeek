using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Cameras.Mini;
using LayerPeek.App.Cameras.Overlay.Widgets;
using LayerPeek.App.Tests.Cameras.Overlay.Widgets;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cooldown;
using LayerPeek.Core.Printers;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Cameras.Mini;

/// <summary>
/// The cool-down estimate of the mini preview strip follows the rules of the Cool-down widget (T-088, shared
/// <see cref="CooldownEstimate"/>): only a running estimate of a fresh state is shown (never "about 0 min", no frozen
/// estimate once snapshots stop), and it counts down with the window clock without snapshots.
/// </summary>
public sealed class CameraMiniStripCooldownEstimateTests : IDisposable
{
    private const string Nbsp = "\u00A0";

    private const double PollSeconds = 5;

    private readonly ManualDispatcher _dispatcher = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly FakePrinterMonitor _monitor = new();
    private readonly CooldownMonitor _cooldown;
    private readonly CultureScope _culture = new("en-US");

    public CameraMiniStripCooldownEstimateTests()
    {
        _cooldown = new CooldownMonitor(_monitor, _time, new ListLogger<CooldownMonitor>());
        _cooldown.Configure([Definition()], TimeSpan.FromSeconds(PollSeconds));
    }

    public void Dispose()
    {
        _cooldown.Dispose();
        _culture.Dispose();
    }

    private static CooldownState Cooling(TimeSpan? remaining, DateTimeOffset updatedAt) =>
        new(CooldownPhase.Cooling, 87.4, 45, 50, 0.3, remaining, Now, updatedAt, CooldownPrintOutcome.Finished, Now, null);

    /// <summary>A strip on the fake clock that gets the snapshots of its printer after the cool-down monitor.</summary>
    private CameraMiniStripViewModel Create()
    {
        CameraMiniStripViewModel strip = new("mk4", _dispatcher, _cooldown, clock: _time);
        _monitor.SnapshotUpdated += (_, snapshot) =>
        {
            if (snapshot.PrinterId == "mk4")
            {
                strip.OnSnapshot(snapshot);
            }
        };
        return strip;
    }

    private void Raise(PrinterSnapshot snapshot)
    {
        if (snapshot.Timestamp > _time.GetUtcNow())
        {
            _time.SetUtcNow(snapshot.Timestamp);
        }

        _monitor.Raise(snapshot);
        _dispatcher.RunAll();
    }

    /// <summary>A print and readings every 5 s along the simulator's curve until the monitor has an estimate.</summary>
    private void CoolUntilEstimate()
    {
        Raise(CooldownWidgetTests.Printing());
        for (double t = PollSeconds; t < 100; t += PollSeconds)
        {
            Raise(CooldownWidgetTests.Ended(t, CooldownWidgetTests.Newton(t - PollSeconds)));
            if (_cooldown.GetState("mk4").EstimatedRemaining is not null)
            {
                return;
            }
        }

        throw new InvalidOperationException("No estimate.");
    }

    [Fact]
    public void ExpiredEstimateShowsOnlyTheNozzleNeverZeroMinutes()
    {
        MiniStripContent content = MiniStripContent.Of(Snapshot(PrinterState.Finished), Cooling(TimeSpan.Zero, Now), Now);

        Assert.Equal(new MiniStripContent(MiniStripKind.Cooling, PrinterState.Finished, 0, null, 87), content);
        Assert.Equal($"Cooling down… 87{Nbsp}°C", CameraMiniStripViewModel.Texts(content).Summary);
    }

    [Fact]
    public void StaleStateShowsNoEstimate()
    {
        // Monitoring stopped: the same rule and the same limit as the Cool-down widget.
        CooldownState state = Cooling(TimeSpan.FromMinutes(30), Now);

        Assert.Equal(30, MiniStripContent.Of(Snapshot(PrinterState.Finished), state, Now + CooldownEstimate.StaleAfter).Minutes);
        Assert.Null(MiniStripContent.Of(Snapshot(PrinterState.Finished), state, Now + CooldownEstimate.StaleAfter + TimeSpan.FromSeconds(1)).Minutes);
    }

    [Fact]
    public void StripAndWidgetShowTheSameMinutes()
    {
        foreach (TimeSpan? remaining in new TimeSpan?[] { null, TimeSpan.Zero, TimeSpan.FromMinutes(0.1), TimeSpan.FromMinutes(5.5) })
        {
            foreach (DateTimeOffset now in new[] { Now, Now + CooldownEstimate.StaleAfter, Now + TimeSpan.FromMinutes(16) })
            {
                CooldownState state = Cooling(remaining, Now);

                Assert.Equal(CooldownEstimate.Minutes(state, now), MiniStripContent.Of(Snapshot(PrinterState.Finished), state, now).Minutes);
            }
        }
    }

    [Fact]
    public void EstimateCountsDownWithTheClockWithoutNewSnapshots()
    {
        using CameraMiniStripViewModel strip = Create();
        CoolUntilEstimate();
        TimeSpan remaining = _cooldown.GetState("mk4").EstimatedRemaining!.Value;
        long minutes = (long)Math.Ceiling(remaining.TotalMinutes);
        Assert.EndsWith($" · about {minutes}\u00A0min", strip.Summary, StringComparison.Ordinal);
        Assert.True(strip.IsTicking);

        // A minute later without any snapshot.
        _time.Advance(TimeSpan.FromMinutes(1));
        _dispatcher.RunAll();

        long later = (long)Math.Ceiling((remaining - TimeSpan.FromMinutes(1)).TotalMinutes);
        if (later > 0)
        {
            Assert.EndsWith($" · about {later}\u00A0min", strip.Summary, StringComparison.Ordinal);
        }

        // Past the estimate: no "about 0 min", and the clock stops.
        _time.Advance(remaining);
        _dispatcher.RunAll();

        Assert.StartsWith("Cooling down… ", strip.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("about", strip.Summary, StringComparison.Ordinal);
        Assert.False(strip.IsTicking);
    }

    [Fact]
    public void ClockTicksOnlyWhileAnEstimateIsShown()
    {
        using CameraMiniStripViewModel strip = Create();
        Assert.False(strip.IsTicking);

        CoolUntilEstimate();
        Assert.True(strip.IsTicking);

        // The next print: the job is shown, no estimate.
        Raise(CooldownWidgetTests.Printing(200));
        Assert.Equal(MiniStripKind.Job, strip.ShownContent.Kind);
        Assert.False(strip.IsTicking);
    }

    [Fact]
    public void TicksWithoutAVisibleChangePostNothing()
    {
        using CameraMiniStripViewModel strip = Create();
        CoolUntilEstimate();

        for (int tick = 0; tick < 4; tick++)
        {
            long? shown = strip.ShownContent.Minutes;
            int posts = _dispatcher.PostCount;

            _time.Advance(CooldownEstimate.TickInterval);
            long? now = CooldownEstimate.Minutes(_cooldown.GetState("mk4"), _time.GetUtcNow());

            Assert.Equal(posts + (now == shown ? 0 : 1), _dispatcher.PostCount);
            _dispatcher.RunAll();
        }
    }

    [Fact]
    public void DisposedStripStopsTicking()
    {
        CameraMiniStripViewModel strip = Create();
        CoolUntilEstimate();
        Assert.True(strip.IsTicking);
        int posts = _dispatcher.PostCount;

        strip.Dispose();
        _time.Advance(TimeSpan.FromMinutes(5));

        Assert.False(strip.IsTicking);
        Assert.Equal(posts, _dispatcher.PostCount);
    }

    [Fact]
    public void WithoutACoolDownMonitorNothingTicks()
    {
        using CameraMiniStripViewModel strip = new("mk4", _dispatcher, cooldown: null, clock: _time);

        strip.OnSnapshot(CooldownWidgetTests.Ended(5, 200));
        _dispatcher.RunAll();

        Assert.False(strip.IsTicking);
        Assert.Equal("Finished", strip.Summary);
    }
}
