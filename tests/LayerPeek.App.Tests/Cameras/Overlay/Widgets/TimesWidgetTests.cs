using System.ComponentModel;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Cameras.Overlay.Widgets;
using LayerPeek.App.Composition;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Printers;
using LayerPeek.Tests.Shared;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Cameras.Overlay.Widgets;

/// <summary>
/// The Times widget (T-083, design T-076 2.2) driven by the real overlay: the times of a job only during the job, each only
/// when known, in the local time zone (another day, daylight saving), words in the app language and times in the region,
/// formatted only when the shown minute changes. The per-snapshot path without allocations: <see cref="TimesWidgetAllocationTests"/>.
/// </summary>
public sealed class TimesWidgetTests : IDisposable
{
    private readonly ManualDispatcher _dispatcher = new();
    private readonly FakeTimeProvider _time = new(Now);
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

    /// <summary>Like Central Europe: UTC+1, summer time UTC+2 until the last Sunday of October (2026-10-25, 01:00 UTC).</summary>
    internal static TimeZoneInfo CentralEurope()
    {
        TimeZoneInfo.AdjustmentRule rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            DateTime.MinValue.Date,
            DateTime.MaxValue.Date,
            TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday));
        return TimeZoneInfo.CreateCustomTimeZone("Test CET", TimeSpan.FromHours(1), "Test CET", "Test CET", "Test CEST", [rule]);
    }

    /// <summary>A job at <paramref name="at"/> (default <see cref="TestPrinters.Now"/>, 12:00 UTC): 65 min left, 130 min printed.</summary>
    internal static PrinterSnapshot Printing(
        PrinterState state = PrinterState.Printing,
        TimeSpan? remaining = null,
        TimeSpan? elapsed = null,
        double progress = 42.7,
        DateTimeOffset? at = null,
        bool knownRemaining = true,
        bool knownElapsed = true) => new()
        {
            PrinterId = "mk4",
            State = state,
            Timestamp = at ?? Now,
            Job = new PrintJob
            {
                FileName = "benchy.bgcode",
                ProgressPercent = progress,
                Remaining = knownRemaining ? remaining ?? TimeSpan.FromMinutes(65) : null,
                Elapsed = knownElapsed ? elapsed ?? TimeSpan.FromMinutes(130) : null,
            },
        };

    private CameraOverlayViewModel Create(OverlayWidgetSize size = OverlayWidgetSize.Medium, bool emphasized = true)
    {
        CameraOverlayViewModel overlay = new(
            OverlayWidgets.CreateRegistry(),
            new OverlayWidgetContext("mk4", _time),
            _dispatcher,
            NullLogger.Instance,
            Definition(),
            new CameraOverlayLayout([new OverlayWidgetPlacement(OverlayWidgetIds.Times, OverlayAnchor.BottomLeft, size, emphasized)]),
            CameraOverlayOpacity.DefaultPercent,
            CameraWindowMode.Normal,
            snapshot: null);
        _created.Add(overlay);
        return overlay;
    }

    private static TimesWidgetViewModel Widget(CameraOverlayViewModel overlay) => (TimesWidgetViewModel)Assert.Single(overlay.Widgets);

    private TimesWidgetViewModel Show(CameraOverlayViewModel overlay, PrinterSnapshot snapshot)
    {
        overlay.OnSnapshot(snapshot);
        _dispatcher.RunAll();
        return Widget(overlay);
    }

    // ---- Registration ----

    [Fact]
    public void TimesWidgetIsRegisteredWithItsNamePriorityAndSizes()
    {
        IOverlayWidgetDefinition definition = Assert.IsType<IOverlayWidgetDefinition>(OverlayWidgets.CreateRegistry().Find(OverlayWidgetIds.Times), exactMatch: false);

        Assert.Equal(OverlayWidgetNames.Times, definition.NameKey);
        Assert.Equal(OverlayWidgets.TimesPriority, definition.Priority);
        Assert.Equal([OverlayWidgetSize.Small, OverlayWidgetSize.Medium, OverlayWidgetSize.Large], definition.SupportedSizes);
        Assert.Equal(0, definition.MinWidth(OverlayWidgetSize.Small));
        Assert.Equal(180, definition.MinWidth(OverlayWidgetSize.Medium));
        Assert.Equal(220, definition.MinWidth(OverlayWidgetSize.Large));
        Assert.Same(TimesWidgetView.CardTemplate, definition.CardTemplate);
        using OverlayWidgetViewModel widget = definition.Create(new OverlayWidgetContext("mk4", _time));
        Assert.IsType<TimesWidgetViewModel>(widget);
    }

    // ---- What is shown and when ----

    [Fact]
    public void RunningJobShowsAllThreeTimes()
    {
        TimesWidgetViewModel widget = Show(Create(), Printing());

        Assert.True(widget.IsShown);
        Assert.Equal("Remaining", widget.RemainingLabel);
        Assert.Equal("1\u00A0h 5\u00A0min", widget.Remaining);
        Assert.Equal(CultureTime.EnUs("Done at 1:05 PM"), widget.Finish);
        Assert.Equal("2\u00A0h 10\u00A0min", widget.PrintTime);
        Assert.True(widget.HasRemaining);
        Assert.True(widget.HasFinish);
        Assert.True(widget.HasPrintTime);
        Assert.Equal(CultureTime.EnUs("1\u00A0h 5\u00A0min remaining, Done at 1:05 PM, Print time: 2\u00A0h 10\u00A0min"), widget.AccessibleName);
    }

    [Theory]
    [InlineData(PrinterState.Printing)]
    [InlineData(PrinterState.Paused)]
    [InlineData(PrinterState.Attention)]
    public void TimesAreShownDuringARunningPausedOrWaitingJob(PrinterState state) =>
        Assert.True(Show(Create(), Printing(state)).IsShown);

    [Theory]
    [InlineData(PrinterState.Idle)]
    [InlineData(PrinterState.Busy)]
    [InlineData(PrinterState.Finished)]
    [InlineData(PrinterState.Stopped)]
    [InlineData(PrinterState.Error)]
    [InlineData(PrinterState.Offline)]
    [InlineData(PrinterState.Unauthorized)]
    [InlineData(PrinterState.Unknown)]
    public void CardHidesWithoutAJobOrWhileThePrinterIsUnreachable(PrinterState state)
    {
        CameraOverlayViewModel overlay = Create();
        Assert.True(Show(overlay, Printing()).IsShown);

        TimesWidgetViewModel widget = Show(overlay, Printing(state));

        Assert.False(widget.HasContent);
        Assert.False(widget.IsShown);
    }

    [Fact]
    public void CardIsHiddenBeforeTheFirstPollAndWithoutAJob()
    {
        CameraOverlayViewModel overlay = Create();
        Assert.False(Widget(overlay).IsShown);

        Assert.False(Show(overlay, Printing() with { Job = null }).IsShown);
    }

    [Fact]
    public void UnknownRemainingTimeLeavesOutTheRemainingAndTheFinishTime()
    {
        // No estimate and no progress to extrapolate from: only the print time is known.
        TimesWidgetViewModel widget = Show(Create(), Printing(progress: 0, knownRemaining: false, elapsed: TimeSpan.FromSeconds(150)));

        Assert.True(widget.IsShown);
        Assert.False(widget.HasRemaining);
        Assert.False(widget.HasFinish);
        Assert.True(widget.HasPrintTime);
        Assert.Equal("3\u00A0min", widget.PrintTime);
        Assert.True(widget.ShowsRows);
        Assert.False(widget.ShowsFinishRow);
        Assert.Equal("Print time: 3\u00A0min", widget.AccessibleName);
    }

    [Fact]
    public void UnknownPrintTimeLeavesOutItsRow()
    {
        TimesWidgetViewModel widget = Show(Create(), Printing(knownElapsed: false));

        Assert.True(widget.HasRemaining);
        Assert.True(widget.HasFinish);
        Assert.False(widget.HasPrintTime);
        Assert.Equal(CultureTime.EnUs("1\u00A0h 5\u00A0min remaining, Done at 1:05 PM"), widget.AccessibleName);
    }

    [Fact]
    public void RemainingTimeIsExtrapolatedWhenThePrinterGivesNone()
    {
        TimesWidgetViewModel widget = Show(Create(), Printing(progress: 50, knownRemaining: false, elapsed: TimeSpan.FromMinutes(30)));

        Assert.Equal("30\u00A0min", widget.Remaining);
        Assert.Equal(CultureTime.EnUs("Done at 12:30 PM"), widget.Finish);
    }

    [Fact]
    public void JobWithoutAnyKnownTimeHidesTheCard()
    {
        CameraOverlayViewModel overlay = Create();
        Assert.True(Show(overlay, Printing()).IsShown);

        TimesWidgetViewModel widget = Show(overlay, Printing(progress: 0, knownRemaining: false, knownElapsed: false));

        Assert.False(widget.IsShown);
    }

    [Fact]
    public void TimesAreWholeMinutesRoundedUp()
    {
        TimesWidgetViewModel widget = Show(Create(), Printing(remaining: TimeSpan.FromSeconds(270), elapsed: TimeSpan.FromSeconds(61), at: Now.AddSeconds(20)));

        Assert.Equal("5\u00A0min", widget.Remaining);
        Assert.Equal("2\u00A0min", widget.PrintTime);
        // The finish agrees with the remaining time: 12:00:20 + 4:30 = 12:04:50, rounded up to 12:05.
        Assert.Equal(CultureTime.EnUs("Done at 12:05 PM"), widget.Finish);
    }

    [Fact]
    public void EndlessEstimateDoesNotOverflow()
    {
        TimesWidgetViewModel widget = Show(Create(), Printing(remaining: TimeSpan.MaxValue, progress: 0.01));

        Assert.True(widget.HasRemaining);
        Assert.NotEmpty(widget.Remaining);
        // Timestamp + TimeSpan.MaxValue is not a date: the finish row is left out.
        Assert.False(widget.HasFinish);
    }

    [Fact]
    public void FinishAtTheEndOfTimeEastOfUtcDoesNotFault()
    {
        // A finish that is a valid date in UTC but past DateTime.MaxValue in the local zone (UTC+14) must not throw while
        // computing (that would fault the widget for the rest of the window's life).
        _time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("Test+14", TimeSpan.FromHours(14), "Test+14", "Test+14"));
        TimeSpan remaining = DateTimeOffset.MaxValue - Now - TimeSpan.FromHours(1);

        TimesWidgetViewModel widget = Show(Create(), Printing(remaining: remaining, progress: 0.01));

        Assert.False(widget.IsFaulted);
        Assert.True(widget.IsShown);
        Assert.True(widget.HasRemaining);
    }

    // ---- Cheap comparison ----

    [Fact]
    public void PollWithinTheSameMinutePostsAndFormatsNothing()
    {
        CameraOverlayViewModel overlay = Create();
        TimesWidgetViewModel widget = Show(overlay, Printing(remaining: TimeSpan.FromMinutes(65), elapsed: TimeSpan.FromSeconds(7790)));
        int posts = _dispatcher.PostCount;
        int changes = 0;
        widget.PropertyChanged += (_, _) => changes++;

        // 5 s later: 64:55 left (still 65 min), 2:09:55 printed (still 130 min), the same finish minute; other values differ.
        overlay.OnSnapshot(Printing(remaining: TimeSpan.FromSeconds(3895), elapsed: TimeSpan.FromSeconds(7795), progress: 43.1, at: Now.AddSeconds(5)));

        Assert.Equal(posts, _dispatcher.PostCount);
        Assert.Equal(0, _dispatcher.Pending);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void NextMinuteFormatsAgain()
    {
        CameraOverlayViewModel overlay = Create();
        TimesWidgetViewModel widget = Show(overlay, Printing());
        List<string?> changed = [];
        widget.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        widget = Show(overlay, Printing(remaining: TimeSpan.FromMinutes(64), elapsed: TimeSpan.FromMinutes(131), at: Now.AddMinutes(1)));

        Assert.Equal("1\u00A0h 4\u00A0min", widget.Remaining);
        Assert.Equal("2\u00A0h 11\u00A0min", widget.PrintTime);
        Assert.Contains(nameof(TimesWidgetViewModel.Remaining), changed);
        // The finish time stayed: its text is not set again.
        Assert.DoesNotContain(nameof(TimesWidgetViewModel.Finish), changed);
    }

    [Fact]
    public void ContentIsTheTimesOfTheActiveJobOnly()
    {
        OverlayWidgetInput input = new(Printing(), Definition(), CameraWindowMode.Normal, Now, TimeZoneInfo.Utc, System.Globalization.CultureInfo.CurrentCulture);

        Assert.Equal(new TimesContent(65, new OverlayFinishTime(new DateTime(2026, 10, 16, 13, 5, 0), false), 130), TimesWidgetViewModel.ContentOf(input));
        Assert.Equal(default, TimesWidgetViewModel.ContentOf(input with { Snapshot = Printing(PrinterState.Finished) }));
        Assert.Equal(default, TimesWidgetViewModel.ContentOf(input with { Snapshot = null }));
        Assert.False(default(TimesContent).HasAny);
    }

    // ---- Time zones ----

    [Fact]
    public void FinishTimeIsInTheLocalTimeZoneOfTheClock()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2"));

        Assert.Equal(CultureTime.EnUs("Done at 3:05 PM"), Show(Create(), Printing()).Finish);
    }

    [Fact]
    public void FinishAfterMidnightShowsTheDateUntilTheDayChanges()
    {
        // 22:00 local (UTC+2) + 3 h 20 min = 01:20 the next local day.
        _time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2"));
        DateTimeOffset evening = new(2026, 10, 16, 20, 0, 0, TimeSpan.Zero);
        CameraOverlayViewModel overlay = Create();

        TimesWidgetViewModel widget = Show(overlay, Printing(remaining: TimeSpan.FromMinutes(200), at: evening));
        Assert.Equal(CultureTime.EnUs("Done October 17 at 1:20 AM"), widget.Finish);

        // After local midnight the same finish is today.
        widget = Show(overlay, Printing(remaining: TimeSpan.FromMinutes(70), at: evening.AddMinutes(130)));
        Assert.Equal(CultureTime.EnUs("Done at 1:20 AM"), widget.Finish);
    }

    [Fact]
    public void FinishAcrossTheEndOfSummerTimeUsesTheOffsetAtTheFinish()
    {
        _time.SetLocalTimeZone(CentralEurope());
        using CultureScope german = new("de-DE");

        // 22:00 UTC on 24 October = 00:00 CEST; 4 h later is 02:00 UTC = 03:00 CET (clocks went back at 01:00 UTC), not 04:00.
        TimesWidgetViewModel widget = Show(Create(), Printing(remaining: TimeSpan.FromHours(4), at: new DateTimeOffset(2026, 10, 24, 22, 0, 0, TimeSpan.Zero)));
        Assert.Equal("Fertig um 03:00", widget.Finish);
        Assert.Equal("4\u00A0h 0\u00A0min", widget.Remaining);
    }

    [Fact]
    public void FinishAcrossTheStartOfSummerTimeUsesTheOffsetAtTheFinish()
    {
        _time.SetLocalTimeZone(CentralEurope());
        using CultureScope czech = new("cs-CZ");

        // 23:30 UTC on 28 March 2026 = 00:30 CET; 3 h later is 02:30 UTC = 04:30 CEST (clocks went forward at 01:00 UTC).
        TimesWidgetViewModel widget = Show(Create(), Printing(remaining: TimeSpan.FromHours(3), at: new DateTimeOffset(2026, 3, 28, 23, 30, 0, TimeSpan.Zero)));
        Assert.Equal("Hotovo v 4:30", widget.Finish);
    }

    // ---- Languages and regions ----

    [Theory]
    [InlineData("en-US", "Remaining", "1\u00A0h 5\u00A0min", "Done at 1:05 PM", "1\u00A0h 5\u00A0min remaining, Done at 1:05 PM, Print time: 2\u00A0h 10\u00A0min")]
    [InlineData("cs-CZ", "Zbývá", "1\u00A0h 5\u00A0min", "Hotovo v 13:05", "Zbývá 1\u00A0h 5\u00A0min, Hotovo v 13:05, Doba tisku: 2\u00A0h 10\u00A0min")]
    [InlineData("de-DE", "Verbleibend", "1\u00A0h 5\u00A0min", "Fertig um 13:05", "Noch 1\u00A0h 5\u00A0min, Fertig um 13:05, Druckzeit: 2\u00A0h 10\u00A0min")]
    [InlineData("fr-FR", "Restant", "1\u00A0h 5\u00A0min", "Fin à 13:05", "Temps restant : 1\u00A0h 5\u00A0min, Fin à 13:05, Durée d'impression : 2\u00A0h 10\u00A0min")]
    public void TextsFollowTheLanguageAndTheRegion(string culture, string label, string remaining, string finish, string accessibleName)
    {
        using CultureScope scope = new(culture);

        TimesWidgetViewModel widget = Show(Create(), Printing());

        Assert.Equal(label, widget.RemainingLabel);
        Assert.Equal(remaining, widget.Remaining);
        Assert.Equal(CultureTime.EnUs(finish), widget.Finish);
        Assert.Equal(CultureTime.EnUs(accessibleName), widget.AccessibleName);
    }

    [Fact]
    public void FinishOnAnotherDayInCzech()
    {
        using CultureScope czech = new("cs-CZ");

        TimesWidgetViewModel widget = Show(Create(), Printing(remaining: TimeSpan.FromHours(20)));

        Assert.Equal("Hotovo 17. října v 8:00", widget.Finish);
        Assert.Equal("20\u00A0h 0\u00A0min", widget.Remaining);
    }

    [Fact]
    public void LanguageSwitchFormatsTheShownTimesAgainWithoutAPoll()
    {
        CameraOverlayViewModel overlay = Create();
        TimesWidgetViewModel widget = Show(overlay, Printing());
        int posts = _dispatcher.PostCount;

        using (new CultureScope("cs-CZ"))
        {
            overlay.RefreshTexts();

            Assert.Equal("Zbývá", widget.RemainingLabel);
            Assert.Equal("Hotovo v 13:05", widget.Finish);
            Assert.Equal("Zbývá 1\u00A0h 5\u00A0min, Hotovo v 13:05, Doba tisku: 2\u00A0h 10\u00A0min", widget.AccessibleName);
        }

        Assert.Equal(posts, _dispatcher.PostCount);
    }

    [Fact]
    public void EveryLanguageHasTheRemainingLabel()
    {
        foreach (string language in Core.Settings.AppLanguages.Supported)
        {
            string? text = Resources.Strings.ResourceManager.GetString(nameof(Resources.Strings.CameraOverlayRemainingLabel), System.Globalization.CultureInfo.GetCultureInfo(language));
            Assert.False(string.IsNullOrWhiteSpace(text), language);
            // T-076 10: at most 14 characters.
            Assert.True(text.Length <= 14, $"{language}: {text}");
        }
    }

    // ---- Sizes and emphasis ----

    [Fact]
    public void SmallCardShowsOnlyTheRemainingTime()
    {
        TimesWidgetViewModel widget = Show(Create(OverlayWidgetSize.Small), Printing());
        widget.RenderedSize = OverlayWidgetSize.Small;

        Assert.True(widget.IsSmall);
        Assert.True(widget.HasRemaining);
        Assert.False(widget.ShowsRows);
        Assert.False(widget.ShowsFinishRow);
        Assert.False(widget.ShowsPrintTimeRow);
        // The screen reader still gets every value.
        Assert.Equal(CultureTime.EnUs("1\u00A0h 5\u00A0min remaining, Done at 1:05 PM, Print time: 2\u00A0h 10\u00A0min"), widget.AccessibleName);
    }

    [Fact]
    public void SmallCardWithoutARemainingTimeHides()
    {
        // T-076 2: a card left without a row hides; S has only the remaining time.
        TimesWidgetViewModel widget = Show(Create(OverlayWidgetSize.Small), Printing(progress: 0, knownRemaining: false));
        Assert.True(widget.IsShown);

        widget.RenderedSize = OverlayWidgetSize.Small;

        Assert.False(widget.IsShown);
        Assert.False(widget.ShowsRows);

        widget.RenderedSize = OverlayWidgetSize.Medium;

        Assert.True(widget.IsShown);
        Assert.True(widget.ShowsPrintTimeRow);
    }

    [Fact]
    public void ContentHiddenInTheSmallSizeIsFormattedWhenTheCardGrows()
    {
        CameraOverlayViewModel overlay = Create(OverlayWidgetSize.Small);
        Widget(overlay).RenderedSize = OverlayWidgetSize.Small;
        TimesWidgetViewModel widget = Show(overlay, Printing(progress: 0, knownRemaining: false, elapsed: TimeSpan.FromMinutes(3)));
        Assert.False(widget.IsShown);
        Assert.Equal(string.Empty, widget.PrintTime);

        using (new CultureScope("cs-CZ"))
        {
            // A language switch while hidden formats nothing; growing formats in the language of that moment.
            overlay.RefreshTexts();
            Assert.Equal(string.Empty, widget.PrintTime);

            widget.RenderedSize = OverlayWidgetSize.Large;

            Assert.True(widget.IsShown);
            Assert.Equal("3\u00A0min", widget.PrintTime);
            Assert.Equal("Doba tisku: 3\u00A0min", widget.AccessibleName);
        }

        // Back to S: hidden again, no formatting needed.
        widget.RenderedSize = OverlayWidgetSize.Small;
        Assert.False(widget.IsShown);
    }

    [Fact]
    public void SmallCardHidesAndComesBackWithTheRemainingTime()
    {
        CameraOverlayViewModel overlay = Create(OverlayWidgetSize.Small);
        Widget(overlay).RenderedSize = OverlayWidgetSize.Small;

        Assert.True(Show(overlay, Printing()).IsShown);
        Assert.False(Show(overlay, Printing(progress: 0, knownRemaining: false)).IsShown);
        TimesWidgetViewModel widget = Show(overlay, Printing(remaining: TimeSpan.FromMinutes(30)));
        Assert.True(widget.IsShown);
        Assert.Equal("30\u00A0min", widget.Remaining);
    }

    [Theory]
    [InlineData(OverlayWidgetSize.Medium)]
    [InlineData(OverlayWidgetSize.Large)]
    public void MediumAndLargeCardsShowEveryKnownRow(OverlayWidgetSize size)
    {
        TimesWidgetViewModel widget = Show(Create(size), Printing());
        widget.RenderedSize = size;

        Assert.True(widget.ShowsRows);
        Assert.True(widget.ShowsFinishRow);
        Assert.True(widget.ShowsPrintTimeRow);
    }

    [Fact]
    public void RenderedSizeChangeUpdatesTheRows()
    {
        TimesWidgetViewModel widget = Show(Create(), Printing());
        widget.RenderedSize = OverlayWidgetSize.Medium;
        List<string?> changed = [];
        widget.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        widget.RenderedSize = OverlayWidgetSize.Small;

        Assert.Contains(nameof(TimesWidgetViewModel.ShowsRows), changed);
        Assert.Contains(nameof(TimesWidgetViewModel.ShowsFinishRow), changed);
        Assert.False(widget.ShowsFinishRow);
    }

    [Fact]
    public void EmphasisFollowsTheLayout()
    {
        CameraOverlayViewModel overlay = Create(emphasized: true);
        TimesWidgetViewModel widget = Show(overlay, Printing());
        Assert.True(widget.IsEmphasized);

        overlay.ApplySettings(
            Definition(),
            new CameraOverlayLayout([new OverlayWidgetPlacement(OverlayWidgetIds.Times, OverlayAnchor.BottomLeft, OverlayWidgetSize.Medium, emphasized: false)]),
            CameraOverlayOpacity.DefaultPercent);

        // The same card, changed in place.
        Assert.Same(widget, Widget(overlay));
        Assert.False(widget.IsEmphasized);
    }

    [Fact]
    public void ShownTextsDoNotChangeWhenTheRowsChange()
    {
        // Size changes only toggle rows; nothing is formatted again.
        TimesWidgetViewModel widget = Show(Create(), Printing());
        List<string?> changed = [];
        PropertyChangedEventHandler handler = (_, e) => changed.Add(e.PropertyName);
        widget.PropertyChanged += handler;

        widget.RenderedSize = OverlayWidgetSize.Large;
        widget.RenderedSize = OverlayWidgetSize.Small;
        widget.PropertyChanged -= handler;

        Assert.DoesNotContain(nameof(TimesWidgetViewModel.Remaining), changed);
        Assert.DoesNotContain(nameof(OverlayWidgetViewModel.AccessibleName), changed);
    }
}
