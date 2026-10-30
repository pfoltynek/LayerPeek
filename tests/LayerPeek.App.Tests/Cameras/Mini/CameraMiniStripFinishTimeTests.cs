using System.Globalization;

using LayerPeek.App.Cameras.Mini;
using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Localization;
using LayerPeek.App.Resources;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cooldown;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;
using LayerPeek.Tests.Shared;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Cameras.Mini;

/// <summary>
/// The finish time and the cool-down variants of the mini preview strip's summary row (T-103, design 3): the content
/// (<see cref="MiniStripContent.Of"/>) per state, and the variants the row chooses from, longest first, with the full
/// wording for the tooltip and screen readers. <see cref="TestPrinters.Now"/> is Friday 2026-10-16 12:00 UTC.
/// </summary>
public sealed class CameraMiniStripFinishTimeTests : IDisposable
{
    private const string Nbsp = "\u00A0";
    private const string Summary = "19% · 2\u00A0h 35\u00A0min";

    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private readonly CultureScope _culture = new("en-US");

    public void Dispose() => _culture.Dispose();

    public static TheoryData<string> Languages => [.. AppLanguages.Supported];

    private static PrinterSnapshot Printing(TimeSpan? remaining, PrinterState state = PrinterState.Printing) =>
        Snapshot(state, Job(19.9, remaining));

    private static MiniStripContent Of(PrinterSnapshot snapshot, TimeZoneInfo? zone = null) =>
        MiniStripContent.Of(snapshot, CooldownState.None, Now, zone ?? Utc);

    private static MiniStripTexts TextsOf(PrinterSnapshot snapshot) => CameraMiniStripViewModel.Texts(Of(snapshot));

    [Fact]
    public void PrintingOnTheSameDayHasTheFinishTimeRoundedUpToTheMinute()
    {
        MiniStripContent content = Of(Printing(TimeSpan.FromMinutes(154.2)));

        Assert.Equal(MiniStripKind.Job, content.Kind);
        Assert.Equal(155, content.Minutes);
        Assert.Equal(new OverlayFinishTime(new DateTime(2026, 10, 16, 14, 35, 0), false), content.Finish);
        Assert.Equal(0, content.FinishDays);
    }

    [Fact]
    public void FinishTimeIsInTheTimeZoneOfTheClock()
    {
        TimeZoneInfo plusTwo = TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2");

        MiniStripContent content = Of(Printing(TimeSpan.FromMinutes(154.2)), plusTwo);

        Assert.Equal(new OverlayFinishTime(new DateTime(2026, 10, 16, 16, 35, 0), false), content.Finish);
    }

    [Theory]
    [InlineData(20 * 60, 1)]
    [InlineData(6 * 24 * 60, 6)]
    [InlineData(7 * 24 * 60, 7)]
    public void PrintingThatFinishesOnAnotherDayCountsTheDays(int minutes, int days)
    {
        MiniStripContent content = Of(Printing(TimeSpan.FromMinutes(minutes)));

        Assert.True(content.Finish?.IsOnAnotherDay);
        Assert.Equal(days, content.FinishDays);
    }

    [Fact]
    public void UnknownFinishTimeHasOnlyTheSummary()
    {
        MiniStripContent content = Of(Printing(null));
        MiniStripTexts texts = CameraMiniStripViewModel.Texts(content);

        Assert.Null(content.Finish);
        Assert.Equal(new MiniStripTexts(new MiniStripVariant("19%"), null, null, "Printing, 19%"), texts);
    }

    [Theory]
    [InlineData(PrinterState.Paused)]
    [InlineData(PrinterState.Attention)]
    public void PausedOrWaitingJobHasNoFinishTime(PrinterState state)
    {
        // T-103 Q5: only while printing, like before the job's state shows next to the percent.
        MiniStripContent content = Of(Printing(TimeSpan.FromMinutes(30), state));
        MiniStripTexts texts = CameraMiniStripViewModel.Texts(content);

        Assert.Null(content.Finish);
        Assert.Null(texts.First.Finish);
        Assert.Null(texts.Second);
        Assert.Null(texts.Third);
        Assert.Equal($"19% · {OverlayFormat.StateName(state)}", texts.First.Summary);
    }

    [Fact]
    public void SameDayVariantsAreTheWidgetsWordingThenTheTimeThenNone()
    {
        MiniStripTexts texts = TextsOf(Printing(TimeSpan.FromMinutes(154.2)));

        Assert.Equal(new MiniStripVariant(Summary, CultureTime.EnUs("Done at 2:35 PM")), texts.First);
        Assert.Equal(new MiniStripVariant(Summary, CultureTime.EnUs("2:35 PM")), texts.Second);
        Assert.Equal(new MiniStripVariant(Summary), texts.Third);
        Assert.False(texts.Third!.HasFinish);
        Assert.Equal(CultureTime.EnUs("Printing, 19%, 2\u00A0h 35\u00A0min remaining, Done at 2:35 PM"), texts.Accessible);
    }

    [Fact]
    public void AnotherDayWithinAWeekShortensToTheDayOfTheWeek()
    {
        // T-103 Q7: Friday 12:00 + 20 h = Saturday 8:00.
        MiniStripTexts texts = TextsOf(Printing(TimeSpan.FromHours(20)));

        Assert.Equal(CultureTime.EnUs("Done October 17 at 8:00 AM"), texts.First.Finish);
        Assert.Equal(CultureTime.EnUs($"Sat{Nbsp}8:00 AM"), texts.Second?.Finish);
        Assert.Equal(new MiniStripVariant(texts.First.Summary), texts.Third);
        Assert.EndsWith(CultureTime.EnUs(", Done October 17 at 8:00 AM"), texts.Accessible, StringComparison.Ordinal);
    }

    [Fact]
    public void AWeekOrMoreAwayHasOnlyTheFullDate()
    {
        // The day of the week alone would be ambiguous: the full wording, else nothing (T-103 3.1).
        MiniStripTexts texts = TextsOf(Printing(TimeSpan.FromDays(7)));

        Assert.Equal(CultureTime.EnUs("Done October 23 at 12:00 PM"), texts.First.Finish);
        Assert.False(texts.Second?.HasFinish);
        Assert.Null(texts.Third);
    }

    [Fact]
    public void CzechWithCzechRegionUses24HoursAndCzechWords()
    {
        using CultureScope region = new("cs-CZ");
        using UiCultureScope language = new("cs");

        MiniStripTexts today = TextsOf(Printing(TimeSpan.FromMinutes(154.2)));
        MiniStripTexts tomorrow = TextsOf(Printing(TimeSpan.FromHours(20)));

        Assert.Equal("Hotovo v 14:35", today.First.Finish);
        Assert.Equal("14:35", today.Second?.Finish);
        Assert.EndsWith(", Hotovo v 14:35", today.Accessible, StringComparison.Ordinal);
        Assert.Equal("Hotovo 17. října v 8:00", tomorrow.First.Finish);
        Assert.Equal($"so{Nbsp}8:00", tomorrow.Second?.Finish);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void DayOfTheWeekFollowsTheAppLanguageAndTheTimeTheRegion(string language)
    {
        using UiCultureScope scope = new(language);
        CultureInfo culture = CultureInfo.CreateSpecificCulture(language);
        string saturday = culture.DateTimeFormat.GetAbbreviatedDayName(DayOfWeek.Saturday);

        string? finish = TextsOf(Printing(TimeSpan.FromHours(20))).Second?.Finish;

        Assert.False(string.IsNullOrWhiteSpace(saturday));
        Assert.True(language == "en" || saturday != "Sat", $"{language}: '{saturday}' is not in the app language");
        Assert.Equal(string.Format(CultureInfo.InvariantCulture, Strings.ResourceManager.GetString(nameof(Strings.CameraMiniFinishDayTime), CultureInfo.GetCultureInfo(language))!, saturday, CultureTime.EnUs("8:00 AM")), finish);
    }

    [Fact]
    public void DayAbbreviationsOfTheLanguagesWithStableCalendarData()
    {
        // The data comes from ICU (CLDR) on both OSes; these do not differ between its versions (T-103 7).
        foreach ((string language, string friday) in new[] { ("en", "Fri"), ("cs", "pá"), ("de", "Fr"), ("it", "ven") })
        {
            Assert.Equal(friday, CultureInfo.CreateSpecificCulture(language).DateTimeFormat.GetAbbreviatedDayName(DayOfWeek.Friday).TrimEnd('.'));
        }
    }

    [Fact]
    public void CoolingVariantsDropTheWordThenTheEstimate()
    {
        CooldownState cooling = new(CooldownPhase.Cooling, 87.4, 45, 50, 40, 0.42, TimeSpan.FromMinutes(5.5), Now, Now, CooldownPrintOutcome.Finished, Now, null);

        MiniStripContent content = MiniStripContent.Of(Snapshot(PrinterState.Finished), cooling, Now, Utc);
        MiniStripTexts texts = CameraMiniStripViewModel.Texts(content);

        Assert.Equal(42, content.CoolingPercent);
        Assert.Equal(new MiniStripVariant($"Cooling down… 87{Nbsp}°C · about 6\u00A0min"), texts.First);
        Assert.Equal(new MiniStripVariant($"87{Nbsp}°C · about 6\u00A0min"), texts.Second);
        Assert.Equal(new MiniStripVariant($"87{Nbsp}°C"), texts.Third);
        // T-109: the row keeps the limiting temperature, the tooltip names both.
        Assert.Equal($"Cooling down… nozzle 87{Nbsp}°C, bed 45{Nbsp}°C, about 6\u00A0min", texts.Accessible);
    }

    [Fact]
    public void CoolingWithoutAnEstimateGoesStraightToTheTemperature()
    {
        CooldownState cooling = new(CooldownPhase.Cooling, 87.4, 45, 50, 40, double.NaN, null, Now, Now, CooldownPrintOutcome.Finished, Now, null);

        MiniStripContent content = MiniStripContent.Of(Snapshot(PrinterState.Finished), cooling, Now, Utc);
        MiniStripTexts texts = CameraMiniStripViewModel.Texts(content);

        Assert.Equal(0, content.CoolingPercent);
        Assert.Equal(new MiniStripTexts(new MiniStripVariant($"Cooling down… 87{Nbsp}°C"), new MiniStripVariant($"87{Nbsp}°C"), null, $"Cooling down… nozzle 87{Nbsp}°C, bed 45{Nbsp}°C"), texts);
    }

    [Fact]
    public void RowShowsTheBedWhileTheBedHoldsTheCoolDownUp()
    {
        // T-109: the nozzle is below its threshold, the bed is not; the row names the bed, the tooltip both.
        CooldownState cooling = new(
            CooldownPhase.Cooling, 38, 52.4, 40, 40, 0.4, TimeSpan.FromMinutes(3.2), Now, Now, CooldownPrintOutcome.Finished, Now, null, Limiting: CooldownSensor.Bed);

        MiniStripContent content = MiniStripContent.Of(Snapshot(PrinterState.Finished), cooling, Now, Utc);
        MiniStripTexts texts = CameraMiniStripViewModel.Texts(content);

        Assert.Equal(52, content.Celsius);
        Assert.Equal(40, content.CoolingPercent);
        Assert.Equal(new MiniStripVariant($"Cooling down… 52{Nbsp}°C · about 4\u00A0min"), texts.First);
        Assert.Equal(new MiniStripVariant($"52{Nbsp}°C · about 4\u00A0min"), texts.Second);
        Assert.Equal(new MiniStripVariant($"52{Nbsp}°C"), texts.Third);
        Assert.Equal($"Cooling down… nozzle 38{Nbsp}°C, bed 52{Nbsp}°C, about 4\u00A0min", texts.Accessible);
    }

    [Fact]
    public void CooledRowShowsTheTemperatureThatLimitedLast()
    {
        CooldownState cooled = new(
            CooldownPhase.CooledDown, 33, 39.6, 40, 40, 1, null, Now, Now, CooldownPrintOutcome.Finished, Now, Now, Limiting: CooldownSensor.Bed);

        MiniStripTexts texts = CameraMiniStripViewModel.Texts(MiniStripContent.Of(Snapshot(PrinterState.Finished), cooled, Now, Utc));

        Assert.Equal(new MiniStripVariant($"Cooled down · 40{Nbsp}°C"), texts.Second);
        Assert.Equal($"Cooled down – safe to turn off, nozzle 33{Nbsp}°C, bed 40{Nbsp}°C", texts.Accessible);
    }

    [Fact]
    public void WithoutABedTheTooltipNamesTheNozzleAsBefore()
    {
        CooldownState cooling = new(CooldownPhase.Cooling, 87.4, null, 40, 40, 0.42, TimeSpan.FromMinutes(5.5), Now, Now, CooldownPrintOutcome.Finished, Now, null);
        CooldownState cooled = cooling with { Phase = CooldownPhase.CooledDown, NozzleCelsius = 38, EstimatedRemaining = null, Progress = 1, CooledDownAt = Now };

        MiniStripTexts coolingTexts = CameraMiniStripViewModel.Texts(MiniStripContent.Of(Snapshot(PrinterState.Finished), cooling, Now, Utc));
        MiniStripTexts cooledTexts = CameraMiniStripViewModel.Texts(MiniStripContent.Of(Snapshot(PrinterState.Finished), cooled, Now, Utc));

        Assert.Equal(coolingTexts.First.Summary, coolingTexts.Accessible);
        Assert.Equal($"Cooled down – safe to turn off, nozzle 38{Nbsp}°C", cooledTexts.Accessible);
    }

    [Fact]
    public void CooledSaysSafeToTurnOffThenTheTemperature()
    {
        CooldownState cooled = new(CooldownPhase.CooledDown, 48, 39, 50, 40, 1, null, Now, Now, CooldownPrintOutcome.Finished, Now, Now);

        MiniStripContent content = MiniStripContent.Of(Snapshot(PrinterState.Finished), cooled, Now, Utc);
        MiniStripTexts texts = CameraMiniStripViewModel.Texts(content);

        Assert.Equal(100, content.CoolingPercent);
        Assert.Equal(new MiniStripTexts(
            new MiniStripVariant("Cooled down – safe to turn off"),
            new MiniStripVariant($"Cooled down · 48{Nbsp}°C"),
            null,
            $"Cooled down – safe to turn off, nozzle 48{Nbsp}°C, bed 39{Nbsp}°C"), texts);
    }

    [Theory]
    [InlineData(PrinterState.Idle)]
    [InlineData(PrinterState.Finished)]
    [InlineData(PrinterState.Offline)]
    [InlineData(PrinterState.Error)]
    public void WithoutAJobThereIsOneVariant(PrinterState state)
    {
        MiniStripTexts texts = TextsOf(Snapshot(state, Job(19.9, TimeSpan.FromMinutes(30))));

        Assert.Equal(new MiniStripTexts(new MiniStripVariant(OverlayFormat.StateName(state)), null, null, OverlayFormat.StateName(state)), texts);
    }

    [Fact]
    public void BeforeTheFirstPollThereIsOneVariant()
    {
        MiniStripTexts texts = CameraMiniStripViewModel.Texts(MiniStripContent.Of(null, CooldownState.None, Now, Utc));

        Assert.Equal(new MiniStripTexts(new MiniStripVariant("Connecting…"), null, null, "Connecting…"), texts);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void NewTextsExistInEveryLanguage(string language)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo(language);
        string Text(string key) => Strings.ResourceManager.GetString(key, culture) ?? string.Empty;

        string coolingShort = Text(nameof(Strings.CameraMiniCoolingShort));
        Assert.StartsWith("{0}" + Nbsp + "°C · ", coolingShort, StringComparison.Ordinal);
        Assert.EndsWith(" {1}", coolingShort, StringComparison.Ordinal);
        // The same abbreviation of "about" as the long cooling text.
        Assert.EndsWith(coolingShort["{0}".Length..], Text(nameof(Strings.CameraMiniCooling)), StringComparison.Ordinal);

        string accessible = Text(nameof(Strings.CameraMiniCooledAccessible));
        Assert.StartsWith(Text(nameof(Strings.CameraOverlayCooled)) + ", ", accessible, StringComparison.Ordinal);
        Assert.EndsWith("{0}" + Nbsp + "°C", accessible, StringComparison.Ordinal);

        string dayTime = Text(nameof(Strings.CameraMiniFinishDayTime));
        Assert.Contains("{0}", dayTime, StringComparison.Ordinal);
        Assert.Contains("{1}", dayTime, StringComparison.Ordinal);
    }
}
