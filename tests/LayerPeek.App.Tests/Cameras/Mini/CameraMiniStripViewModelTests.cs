using System.Globalization;

using LayerPeek.App.Cameras.Mini;
using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Resources;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tray;
using LayerPeek.Core.Cooldown;
using LayerPeek.Core.Printers;
using LayerPeek.Tests.Shared;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Cameras.Mini;

/// <summary>
/// The info strip of the mini preview (T-089, design T-076 4): the summary row with the number first in every state, the
/// progress bar, the cool-down states and the cheap comparison (nothing is formatted or posted for a poll that changes
/// nothing visible).
/// </summary>
public sealed class CameraMiniStripViewModelTests : IDisposable
{
    private const string Nbsp = " ";

    private readonly ManualDispatcher _dispatcher = new();
    private readonly FakePrinterMonitor _monitor = new();
    private readonly CooldownMonitor _cooldown;
    private readonly CultureScope _culture = new("en-US");

    public CameraMiniStripViewModelTests()
    {
        _cooldown = new CooldownMonitor(_monitor, TimeProvider.System, new ListLogger<CooldownMonitor>());
        _cooldown.Configure([Definition()], TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        _cooldown.Dispose();
        _culture.Dispose();
    }

    public static TheoryData<PrinterState> StatesWithoutAJob =>
    [
        PrinterState.Idle, PrinterState.Finished, PrinterState.Stopped, PrinterState.Busy, PrinterState.Offline,
        PrinterState.Error, PrinterState.Unauthorized, PrinterState.Unknown,
    ];

    private static PrinterSnapshot Printing(double progress = 42.7, TimeSpan? remaining = null, PrinterState state = PrinterState.Printing) =>
        Snapshot(state, Job(progress, remaining));

    /// <summary>A strip that gets the snapshots of its printer after the cool-down monitor, like the camera window in the app.</summary>
    private CameraMiniStripViewModel Create()
    {
        // The system clock in UTC: finish times do not depend on the machine (T-103).
        CameraMiniStripViewModel strip = new("mk4", _dispatcher, _cooldown, clock: UtcSystemClock.Instance);
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
        _monitor.Raise(snapshot);
        _dispatcher.RunAll();
    }

    [Fact]
    public void BeforeTheFirstPollItSaysConnecting()
    {
        using CameraMiniStripViewModel strip = Create();

        Assert.Equal("Connecting…", strip.Summary);
        Assert.Equal("Connecting…", strip.AccessibleText);
        Assert.Same(OverlayStateBrushes.NoData, strip.DotBrush);
        Assert.True(strip.ShowsDot);
        Assert.False(strip.ShowsCoolingIcon);
        Assert.False(strip.ShowsCooledIcon);
        Assert.False(strip.HasProgress);
        Assert.Equal(0, strip.Progress);
        Assert.Equal(33, strip.StripHeight);
    }

    [Fact]
    public void PrintingShowsThePercentFirstThenTheRemainingTime()
    {
        using CameraMiniStripViewModel strip = Create();

        Raise(Printing(19.9, TimeSpan.FromMinutes(154.2)));

        Assert.Equal("19% · 2\u00A0h 35\u00A0min", strip.Summary);
        // T-103: the finish time (12:00 + 2 h 34.2 min, rounded up to the minute) in the full wording of the Times widget.
        Assert.Equal(CultureTime.EnUs("Printing, 19%, 2\u00A0h 35\u00A0min remaining, Done at 2:35 PM"), strip.AccessibleText);
        Assert.Equal(new MiniStripVariant("19% · 2\u00A0h 35\u00A0min", CultureTime.EnUs("Done at 2:35 PM")), strip.FirstVariant);
        Assert.Equal(new MiniStripVariant("19% · 2\u00A0h 35\u00A0min", CultureTime.EnUs("2:35 PM")), strip.SecondVariant);
        Assert.Equal(new MiniStripVariant("19% · 2\u00A0h 35\u00A0min"), strip.ThirdVariant);
        Assert.Same(OverlayStateBrushes.Active, strip.DotBrush);
        Assert.True(strip.HasProgress);
        Assert.Equal(19, strip.Progress);
    }

    [Fact]
    public void PrintingWithoutAnEstimateShowsOnlyThePercent()
    {
        using CameraMiniStripViewModel strip = Create();

        Raise(Printing(42.7));

        Assert.Equal("42%", strip.Summary);
        Assert.Equal("Printing, 42%", strip.AccessibleText);
    }

    [Theory]
    [InlineData(PrinterState.Paused)]
    [InlineData(PrinterState.Attention)]
    public void AJobInAnotherStateShowsThePercentThenTheState(PrinterState state)
    {
        using CameraMiniStripViewModel strip = Create();

        Raise(Printing(42.7, TimeSpan.FromMinutes(30), state));

        string name = OverlayFormat.StateName(state);
        Assert.Equal($"42% · {name}", strip.Summary);
        Assert.Equal($"{name}, 42%", strip.AccessibleText);
        Assert.Same(OverlayStateBrushes.For(state), strip.DotBrush);
        Assert.True(strip.HasProgress);
    }

    [Theory]
    [MemberData(nameof(StatesWithoutAJob))]
    public void WithoutAJobTheStateIsShown(PrinterState state)
    {
        using CameraMiniStripViewModel strip = Create();
        Raise(Printing(42.7, TimeSpan.FromMinutes(30)));

        Raise(Snapshot(state));

        Assert.Equal(OverlayFormat.StateName(state), strip.Summary);
        Assert.Equal(strip.Summary, strip.AccessibleText);
        Assert.Same(OverlayStateBrushes.For(state), strip.DotBrush);
        Assert.True(strip.ShowsDot);
        Assert.False(strip.HasProgress);
        Assert.Equal(0, strip.Progress);
    }

    [Fact]
    public void JobOfAnOfflinePrinterIsNotShown()
    {
        using CameraMiniStripViewModel strip = Create();

        Raise(Snapshot(PrinterState.Offline, Job(42.7, TimeSpan.FromMinutes(30))));

        Assert.Equal("Offline", strip.Summary);
        Assert.False(strip.HasProgress);
    }

    [Fact]
    public void AfterThePrintTheCoolDownIsShownUntilTheNozzleIsCool()
    {
        using CameraMiniStripViewModel strip = Create();
        Raise(Printing(50) with { Telemetry = Hot(215, 215) });

        Raise(Finished(seconds: 5, nozzle: 200));

        Assert.Equal(MiniStripKind.Cooling, strip.ShownContent.Kind);
        Assert.StartsWith($"Cooling down… 200{Nbsp}°C", strip.Summary, StringComparison.Ordinal);
        Assert.False(strip.ShowsDot);
        Assert.True(strip.ShowsCoolingIcon);
        Assert.False(strip.ShowsCooledIcon);
        // T-103 Q3: the bar shows the progress towards the threshold, from the first reading of the cool-down (200 → 50 °C).
        Assert.True(strip.HasProgress);
        Assert.Equal(0, strip.Progress);
        Raise(Finished(10, 120));
        Assert.Equal(53, strip.Progress);

        foreach ((int seconds, double nozzle) in new[] { (15, 70.0), (20, 45.0), (25, 40.0) })
        {
            Raise(Finished(seconds, nozzle));
        }

        // T-103 Q2: "safe to turn off" like the widget, the temperature in the shorter variant and the tooltip; the bar full.
        Assert.Equal("Cooled down – safe to turn off", strip.Summary);
        Assert.Equal(new MiniStripVariant($"Cooled down · 40{Nbsp}°C"), strip.SecondVariant);
        Assert.Null(strip.ThirdVariant);
        Assert.Equal($"Cooled down – safe to turn off, nozzle 40{Nbsp}°C", strip.AccessibleText);
        Assert.True(strip.HasProgress);
        Assert.Equal(100, strip.Progress);
        Assert.False(strip.ShowsDot);
        Assert.False(strip.ShowsCoolingIcon);
        Assert.True(strip.ShowsCooledIcon);

        // The next print ends it.
        Raise(Printing(1, TimeSpan.FromMinutes(90)) with { Timestamp = Now.AddSeconds(30), Telemetry = Hot(215, 215) });
        Assert.Equal("1% · 1\u00A0h 30\u00A0min", strip.Summary);
        Assert.True(strip.ShowsDot);
        Assert.False(strip.ShowsCooledIcon);
    }

    [Fact]
    public void CoolDownOfAnotherPrinterPostsNothing()
    {
        _cooldown.Configure([Definition(), Definition("other", "Other")], TimeSpan.FromSeconds(5));
        using CameraMiniStripViewModel strip = Create();
        Raise(Snapshot(PrinterState.Idle));
        int posts = _dispatcher.PostCount;

        _monitor.Raise(Snapshot(PrinterState.Printing, Job(50), "other") with { Telemetry = Hot(215, 215) });
        _monitor.Raise(Snapshot(PrinterState.Finished, Job(100), "other") with { Timestamp = Now.AddSeconds(5), Telemetry = Hot(200, 0) });

        Assert.Equal(CooldownPhase.Cooling, _cooldown.GetState("other").Phase);
        Assert.Equal(posts, _dispatcher.PostCount);
        Assert.Equal("Ready", strip.Summary);
    }

    [Fact]
    public void WithoutACoolDownMonitorTheStateIsShownAfterThePrint()
    {
        using CameraMiniStripViewModel strip = new("mk4", _dispatcher, cooldown: null);

        strip.OnSnapshot(Finished(5, 200));
        _dispatcher.RunAll();

        Assert.Equal("Finished", strip.Summary);
    }

    [Theory]
    [InlineData(5.5, "Cooling down… 87{0}°C · about 6\u00A0min")]
    [InlineData(null, "Cooling down… 87{0}°C")]
    public void CoolingShowsTheNozzleAndTheEstimateRoundedUp(double? minutes, string expected)
    {
        CooldownState cooling = new(CooldownPhase.Cooling, 87.4, 45, 50, 0.3, minutes is { } m ? TimeSpan.FromMinutes(m) : null, Now, Now, CooldownPrintOutcome.Finished, Now, null);

        MiniStripContent content = MiniStripContent.Of(Snapshot(PrinterState.Finished), cooling, Now, TimeZoneInfo.Utc);
        MiniStripTexts texts = CameraMiniStripViewModel.Texts(content);

        Assert.Equal(new MiniStripContent(MiniStripKind.Cooling, PrinterState.Finished, 0, minutes is null ? null : 6, 87, CoolingPercent: 30), content);
        Assert.Equal(string.Format(CultureInfo.InvariantCulture, expected, Nbsp), texts.First.Summary);
        Assert.Equal(texts.First.Summary, texts.Accessible);
    }

    [Fact]
    public void CoolDownIsNotShownForAnUnreachablePrinterOrWithoutATemperature()
    {
        CooldownState cooled = new(CooldownPhase.CooledDown, 48, 39, 50, 1, null, Now, Now, CooldownPrintOutcome.Stopped, Now, Now);

        Assert.Equal(MiniStripKind.Cooled, MiniStripContent.Of(Snapshot(PrinterState.Stopped), cooled, Now, TimeZoneInfo.Utc).Kind);
        Assert.Equal(MiniStripKind.State, MiniStripContent.Of(Snapshot(PrinterState.Offline), cooled, Now, TimeZoneInfo.Utc).Kind);
        Assert.Equal(MiniStripKind.State, MiniStripContent.Of(null, cooled, Now, TimeZoneInfo.Utc).Kind);
        Assert.Equal(MiniStripKind.State, MiniStripContent.Of(Snapshot(PrinterState.Stopped), cooled with { NozzleCelsius = null }, Now, TimeZoneInfo.Utc).Kind);
        Assert.Equal(MiniStripKind.State, MiniStripContent.Of(Snapshot(PrinterState.Stopped), cooled with { NozzleCelsius = double.NaN }, Now, TimeZoneInfo.Utc).Kind);
        // An active job wins: the next print ended the cool-down.
        Assert.Equal(MiniStripKind.Job, MiniStripContent.Of(Printing(), cooled, Now, TimeZoneInfo.Utc).Kind);
    }

    [Fact]
    public void PollsThatChangeNothingVisiblePostNothing()
    {
        using CameraMiniStripViewModel strip = Create();
        Raise(Printing(42.1, TimeSpan.FromMinutes(64.2)));
        int posts = _dispatcher.PostCount;
        int changes = 0;
        strip.PropertyChanged += (_, _) => changes++;

        // Same whole percent and the same minute, other values the strip does not show.
        _monitor.Raise(Printing(42.9, TimeSpan.FromMinutes(64.9)) with { Telemetry = Hot(214, 215) });
        _monitor.Raise(Printing(42.5, TimeSpan.FromMinutes(64.5)) with { Timestamp = Now.AddSeconds(5) });

        Assert.Equal(posts, _dispatcher.PostCount);
        Assert.Equal(0, changes);
        Assert.Equal("42% · 1\u00A0h 5\u00A0min", strip.Summary);
    }

    [Fact]
    public void SeveralChangesWhileTheUiIsBusyArePostedOnce()
    {
        using CameraMiniStripViewModel strip = Create();
        int posts = _dispatcher.PostCount;

        _monitor.Raise(Printing(10));
        _monitor.Raise(Printing(11));
        _monitor.Raise(Printing(12));

        Assert.Equal(posts + 1, _dispatcher.PostCount);
        _dispatcher.RunAll();
        Assert.Equal("12%", strip.Summary);
    }

    [Fact]
    public void ShowCurrentShowsTheLatestSnapshotRightAway()
    {
        using CameraMiniStripViewModel strip = new("mk4", _dispatcher, _cooldown);

        strip.ShowCurrent(Printing(42.7));

        Assert.Equal("42%", strip.Summary);
        Assert.Equal(0, _dispatcher.Pending);
    }

    [Fact]
    public void TextsFollowALanguageSwitch()
    {
        using CameraMiniStripViewModel strip = Create();
        Raise(Printing(19.9, TimeSpan.FromMinutes(154.2)));

        using (new UiCultureScope("cs"))
        {
            strip.RefreshTexts();

            Assert.Equal($"19{Nbsp}% · 2\u00A0h 35\u00A0min", strip.Summary);
            Assert.Equal(CultureTime.EnUs($"Tiskne, 19{Nbsp}%, Zbývá 2\u00A0h 35\u00A0min, Hotovo v 2:35 PM"), strip.AccessibleText);
            Assert.Equal(CultureTime.EnUs("2:35 PM"), strip.SecondVariant?.Finish);
        }

        strip.RefreshTexts();
        Assert.Equal("19% · 2\u00A0h 35\u00A0min", strip.Summary);
        Assert.Equal(CultureTime.EnUs("Done at 2:35 PM"), strip.FirstVariant.Finish);
    }

    [Fact]
    public void DisposedStripIgnoresSnapshotsAndCoolDownChanges()
    {
        CameraMiniStripViewModel strip = Create();
        Raise(Snapshot(PrinterState.Idle));
        strip.Dispose();
        int posts = _dispatcher.PostCount;

        _monitor.Raise(Printing(50) with { Telemetry = Hot(215, 215) });
        _monitor.Raise(Finished(5, 200));

        Assert.Equal(posts, _dispatcher.PostCount);
        Assert.Equal("Ready", strip.Summary);
        strip.Dispose();
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
    public void CoolDownTextsExistInEveryLanguageWithANonBreakingSpaceBeforeTheDegrees(string language)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo(language);
        foreach (string key in new[] { nameof(Strings.CameraMiniCooling), nameof(Strings.CameraMiniCoolingNoEstimate), nameof(Strings.CameraMiniCooled) })
        {
            string? text = Strings.ResourceManager.GetString(key, culture);
            Assert.False(string.IsNullOrWhiteSpace(text), $"{key} ({language})");
            Assert.Contains("{0}" + Nbsp + "°C", text, StringComparison.Ordinal);
            Assert.DoesNotContain(" °C", text, StringComparison.Ordinal);
            Assert.Equal(key == nameof(Strings.CameraMiniCooling), text.Contains("{1}", StringComparison.Ordinal));
            // The design's limit for the short cooled text (T-076 10: 22 characters with a three-digit temperature).
            if (key == nameof(Strings.CameraMiniCooled))
            {
                Assert.True(string.Format(CultureInfo.InvariantCulture, text, 100).Length <= 22, $"{key} ({language}): {text}");
            }
        }
    }

    private static PrinterSnapshot Finished(int seconds, double nozzle) =>
        Snapshot(PrinterState.Finished, Job(100)) with { Timestamp = Now.AddSeconds(seconds), Telemetry = Hot(nozzle, 0) };

    private static Telemetry Hot(double nozzle, double target) => new() { Nozzle = new Temperature(nozzle, target), Bed = new Temperature(55, 0) };
}
