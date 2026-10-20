using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.Printers;

using static PrusaTrayDiagnostic.App.Tests.Tray.TestPrinters;

namespace PrusaTrayDiagnostic.App.Tests.Cameras;

/// <summary>Content and texts of the print overlay of the camera window (T-036).</summary>
public sealed class CameraPrintContentTests : IDisposable
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private readonly CultureScope _culture = new("en-US");

    public void Dispose() => _culture.Dispose();

    /// <summary>A running print at <see cref="TestPrinters.Now"/> (12:00 UTC) with every value known.</summary>
    private static PrinterSnapshot Printing(PrinterState state = PrinterState.Printing, Telemetry? telemetry = null, PrintJob? job = null) => new()
    {
        PrinterId = "mk4",
        State = state,
        Timestamp = Now,
        Job = job ?? new PrintJob
        {
            FileName = "benchy_0.4n_0.2mm_PLA_MK4S_2h10m.bgcode",
            ProgressPercent = 42.7,
            Elapsed = TimeSpan.FromMinutes(130),
            Remaining = TimeSpan.FromMinutes(65),
        },
        Telemetry = telemetry ?? new Telemetry { Nozzle = new Temperature(214.6, 215), Bed = new Temperature(59.8, 60) },
    };

    private static IReadOnlyList<CameraOverlayLine> Lines(PrinterSnapshot? snapshot, CameraOverlayItems items, string? name = "MK4", TimeZoneInfo? zone = null) =>
        CameraPrintContent.From(snapshot, items, name, zone ?? Utc).Lines();

    private static string[] Texts(PrinterSnapshot? snapshot, CameraOverlayItems items, string? name = "MK4", TimeZoneInfo? zone = null) =>
        [.. Lines(snapshot, items, name, zone).Select(static line => line.Text)];

    // ---- Defaults (T-014 overlay) ----

    [Fact]
    public void DefaultItemsReproduceTodaysOverlay()
    {
        IReadOnlyList<CameraOverlayLine> lines = Lines(Printing(), CameraOverlayOptions.DefaultItems);

        Assert.Equal(
            [
                new CameraOverlayLine(CameraOverlayItems.State, "Printing – 42%", IsEmphasized: true),
                new CameraOverlayLine(CameraOverlayItems.RemainingTime, "1 h 5 min remaining", IsEmphasized: false),
            ],
            lines);
    }

    [Fact]
    public void AllItemsInDisplayOrder()
    {
        string[] expected =
        [
            "MK4",
            "Printing – 42%",
            "Nozzle: 215 / 215\u00A0°C",
            "Bed: 60 / 60\u00A0°C",
            "1 h 5 min remaining",
            "Done at 1:05 PM",
            "Print time: 2 h 10 min",
            "benchy_0.4n_0.2mm_PLA_MK4S_2h10m.bgcode",
        ];

        Assert.Equal(expected, Texts(Printing(), CameraOverlayOptions.AllItems));
        Assert.Equal(
            [true, true, false, false, false, false, false, false],
            Lines(Printing(), CameraOverlayOptions.AllItems).Select(static line => line.IsEmphasized));
    }

    // ---- Each item alone ----

    public static TheoryData<CameraOverlayItems, string> SingleItems => new()
    {
        { CameraOverlayItems.PrinterName, "MK4" },
        { CameraOverlayItems.State, "Printing" },
        { CameraOverlayItems.NozzleTemperature, "Nozzle: 215 / 215\u00A0°C" },
        { CameraOverlayItems.BedTemperature, "Bed: 60 / 60\u00A0°C" },
        { CameraOverlayItems.Progress, "42%" },
        { CameraOverlayItems.RemainingTime, "1 h 5 min remaining" },
        { CameraOverlayItems.FinishTime, "Done at 1:05 PM" },
        { CameraOverlayItems.PrintTime, "Print time: 2 h 10 min" },
        { CameraOverlayItems.FileName, "benchy_0.4n_0.2mm_PLA_MK4S_2h10m.bgcode" },
    };

    [Theory]
    [MemberData(nameof(SingleItems))]
    public void EachItemIsShownOnlyWhenSelected(CameraOverlayItems item, string text)
    {
        CameraOverlayLine line = Assert.Single(Lines(Printing(), item));

        Assert.Equal(item, line.Item);
        Assert.Equal(text, line.Text);
        Assert.DoesNotContain(Lines(Printing(), CameraOverlayOptions.AllItems & ~item), other => other.Item == item);
    }

    [Fact]
    public void ProgressAloneIsEmphasizedLikeTheStateLineItReplaces()
    {
        Assert.True(Assert.Single(Lines(Printing(), CameraOverlayItems.Progress)).IsEmphasized);
        Assert.Equal(["Printing – 42%"], Texts(Printing(), CameraOverlayItems.State | CameraOverlayItems.Progress));
    }

    [Fact]
    public void NothingSelectedIsEmpty()
    {
        CameraPrintContent content = CameraPrintContent.From(Printing(), CameraOverlayItems.None, "MK4", Utc);

        Assert.True(content.IsEmpty);
        Assert.Empty(content.Lines());
    }

    // ---- When items are shown ----

    [Fact]
    public void BeforeTheFirstPollOnlyTheNameIsKnown()
    {
        Assert.Equal(["MK4"], Texts(null, CameraOverlayOptions.AllItems));
        Assert.True(CameraPrintContent.From(null, CameraOverlayOptions.AllItems & ~CameraOverlayItems.PrinterName, "MK4", Utc).IsEmpty);
        Assert.True(CameraPrintContent.From(null, CameraOverlayOptions.DefaultItems, "MK4", Utc).IsEmpty);
    }

    [Theory]
    [InlineData(PrinterState.Idle)]
    [InlineData(PrinterState.Busy)]
    [InlineData(PrinterState.Finished)]
    [InlineData(PrinterState.Stopped)]
    [InlineData(PrinterState.Error)]
    public void JobItemsOnlyDuringAJobPrinterItemsAlways(PrinterState state)
    {
        string stateName = state switch
        {
            PrinterState.Idle => "Ready",
            PrinterState.Busy => "Busy",
            PrinterState.Finished => "Finished",
            PrinterState.Stopped => "Stopped",
            _ => "Error",
        };

        string[] texts = Texts(Printing(state), CameraOverlayOptions.AllItems);

        Assert.Equal(["MK4", stateName, "Nozzle: 215 / 215\u00A0°C", "Bed: 60 / 60\u00A0°C"], texts);
    }

    [Theory]
    [InlineData(PrinterState.Paused, "Paused – 42%")]
    [InlineData(PrinterState.Attention, "Needs attention – 42%")]
    public void PausedAndWaitingJobsShowTheJobItems(PrinterState state, string stateLine)
    {
        string[] texts = Texts(Printing(state), CameraOverlayOptions.AllItems);

        Assert.Equal(8, texts.Length);
        Assert.Equal(stateLine, texts[1]);
    }

    [Theory]
    [InlineData(PrinterState.Offline)]
    [InlineData(PrinterState.Unauthorized)]
    [InlineData(PrinterState.Unknown)]
    public void UnreachablePrinterShowsNoJobItems(PrinterState state)
    {
        PrinterSnapshot snapshot = Printing(state) with { Telemetry = null };

        Assert.Equal(["MK4", PrusaTrayDiagnostic.App.Tray.TrayText.StateName(state)], Texts(snapshot, CameraOverlayOptions.AllItems));
    }

    [Fact]
    public void MissingValuesAreLeftOut()
    {
        PrinterSnapshot snapshot = Printing(job: new PrintJob { ProgressPercent = 10 }, telemetry: new Telemetry { Bed = new Temperature(double.NaN, 60) });

        Assert.Equal(["MK4", "Printing – 10%"], Texts(snapshot, CameraOverlayOptions.AllItems));
    }

    [Fact]
    public void RemainingTimeIsExtrapolatedLikeTheTooltip()
    {
        PrinterSnapshot snapshot = Printing(job: new PrintJob { ProgressPercent = 50, Elapsed = TimeSpan.FromMinutes(30) });

        Assert.Equal(["30 min remaining", "Done at 12:30 PM"], Texts(snapshot, CameraOverlayItems.RemainingTime | CameraOverlayItems.FinishTime));
    }

    [Theory]
    [InlineData(25.4, null, "Nozzle: 25\u00A0°C")]
    [InlineData(25.4, 0.0, "Nozzle: 25\u00A0°C")]
    [InlineData(25.4, 0.4, "Nozzle: 25\u00A0°C")]
    [InlineData(170.5, 215.0, "Nozzle: 171 / 215\u00A0°C")]
    [InlineData(-3.6, 0.0, "Nozzle: -4\u00A0°C")]
    [InlineData(25.4, double.NaN, "Nozzle: 25\u00A0°C")]
    public void TemperaturesAreWholeDegreesWithTheTargetOnlyWhileHeating(double actual, double? target, string text)
    {
        PrinterSnapshot snapshot = Printing(telemetry: new Telemetry { Nozzle = new Temperature(actual, target) });

        Assert.Equal([text], Texts(snapshot, CameraOverlayItems.NozzleTemperature));
    }

    [Fact]
    public void TemperatureOfAnIdlePrinterIsShown()
    {
        PrinterSnapshot snapshot = Snapshot(PrinterState.Idle) with { Telemetry = new Telemetry { Bed = new Temperature(23, 0) } };

        Assert.Equal(["Bed: 23\u00A0°C"], Texts(snapshot, CameraOverlayItems.BedTemperature));
    }

    // ---- Finish time ----

    [Fact]
    public void FinishTimeIsRoundedUpToTheMinuteLikeTheRemainingTime()
    {
        PrinterSnapshot snapshot = Printing() with
        {
            Timestamp = Now.AddSeconds(20),
            Job = new PrintJob { ProgressPercent = 1, Remaining = TimeSpan.FromSeconds(270) },
        };

        Assert.Equal(["5 min remaining", "Done at 12:05 PM"], Texts(snapshot, CameraOverlayItems.RemainingTime | CameraOverlayItems.FinishTime));
    }

    [Fact]
    public void FinishTimeIsLocalTime()
    {
        TimeZoneInfo prague = TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2");

        Assert.Equal(["Done at 3:05 PM"], Texts(Printing(), CameraOverlayItems.FinishTime, zone: prague));
    }

    [Fact]
    public void FinishOnAnotherDayShowsTheDate()
    {
        PrinterSnapshot snapshot = Printing(job: new PrintJob { ProgressPercent = 5, Remaining = TimeSpan.FromHours(20) });

        Assert.Equal(["Done October 17 at 8:00 AM"], Texts(snapshot, CameraOverlayItems.FinishTime));
        using (new CultureScope("cs-CZ"))
        {
            Assert.Equal(["Hotovo 17. října v 8:00"], Texts(snapshot, CameraOverlayItems.FinishTime));
        }
    }

    /// <summary>
    /// User decision 2026-10-20 (T-036 Q3): the words of the date (month name, day–month order) follow the app language,
    /// the time follows the region.
    /// </summary>
    [Theory]
    [InlineData("en-US", "cs", "Hotovo 17. října v 8:00 AM")]
    [InlineData("cs-CZ", "en", "Done October 17 at 8:00")]
    [InlineData("en-US", "de", "Fertig am 17. Oktober um 8:00 AM")]
    [InlineData("de-DE", "cs", "Hotovo 17. října v 08:00")]
    [InlineData("cs-CZ", "de", "Fertig am 17. Oktober um 8:00")]
    [InlineData("de-DE", "en", "Done October 17 at 08:00")]
    public void DateWordsFollowTheAppLanguageAndTheTimeTheRegion(string region, string language, string expected)
    {
        PrinterSnapshot snapshot = Printing(job: new PrintJob { ProgressPercent = 5, Remaining = TimeSpan.FromHours(20) });

        using (new CultureScope(region))
        using (new UiCultureScope(language))
        {
            Assert.Equal([expected], Texts(snapshot, CameraOverlayItems.FinishTime));
        }
    }

    [Fact]
    public void LocalZoneAloneCanMoveTheFinishToTheNextDay()
    {
        // 20:00 UTC + 3 h 30 min = 23:30 UTC: the same day in UTC, but 01:30 the next day at UTC+2.
        PrinterSnapshot snapshot = Printing(job: new PrintJob { ProgressPercent = 5, Remaining = TimeSpan.FromMinutes(210) }) with
        {
            Timestamp = new DateTimeOffset(2026, 10, 16, 20, 0, 0, TimeSpan.Zero),
        };
        TimeZoneInfo plusTwo = TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2");

        Assert.Equal(["Done at 11:30 PM"], Texts(snapshot, CameraOverlayItems.FinishTime));
        Assert.False(CameraPrintContent.From(snapshot, CameraOverlayItems.FinishTime, null, Utc).FinishesOnAnotherDay);
        Assert.Equal(["Done October 17 at 1:30 AM"], Texts(snapshot, CameraOverlayItems.FinishTime, zone: plusTwo));
        Assert.True(CameraPrintContent.From(snapshot, CameraOverlayItems.FinishTime, null, plusTwo).FinishesOnAnotherDay);
    }

    [Fact]
    public void FinishAcrossADaylightSavingTransitionUsesTheOffsetAtTheFinish()
    {
        // Like Central Europe: UTC+1, summer time UTC+2 from the last Sunday of March to the last Sunday of October
        // (2026-10-25, 03:00 CEST → 02:00 CET, i.e. 01:00 UTC).
        TimeZoneInfo.AdjustmentRule rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            DateTime.MinValue.Date,
            DateTime.MaxValue.Date,
            TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday));
        TimeZoneInfo central = TimeZoneInfo.CreateCustomTimeZone("Test CET", TimeSpan.FromHours(1), "Test CET", "Test CET", "Test CEST", [rule]);

        // Started at 22:00 UTC on 24 October = 00:00 CEST on 25 October; 4 h later is 02:00 UTC = 03:00 CET (not 04:00).
        PrinterSnapshot snapshot = Printing(job: new PrintJob { ProgressPercent = 5, Remaining = TimeSpan.FromHours(4) }) with
        {
            Timestamp = new DateTimeOffset(2026, 10, 24, 22, 0, 0, TimeSpan.Zero),
        };

        Assert.Equal(["Done at 3:00 AM"], Texts(snapshot, CameraOverlayItems.FinishTime, zone: central));
        Assert.False(CameraPrintContent.From(snapshot, CameraOverlayItems.FinishTime, null, central).FinishesOnAnotherDay);

        // 21:30 UTC on 24 October = 23:30 CEST: finishing at 01:30 UTC = 02:30 CET is already the next local day.
        PrinterSnapshot evening = snapshot with { Timestamp = new DateTimeOffset(2026, 10, 24, 21, 30, 0, TimeSpan.Zero) };
        Assert.Equal(["Done October 25 at 2:30 AM"], Texts(evening, CameraOverlayItems.FinishTime, zone: central));
    }

    [Fact]
    public void FinishTimeOfAnEndlessEstimateDoesNotOverflow()
    {
        PrinterSnapshot snapshot = Printing(job: new PrintJob { ProgressPercent = 1, Remaining = TimeSpan.MaxValue });

        Assert.DoesNotContain(Lines(snapshot, CameraOverlayItems.FinishTime | CameraOverlayItems.RemainingTime), static line => line.Item == CameraOverlayItems.FinishTime);
    }

    // ---- Cultures ----

    [Fact]
    public void TextsFollowTheLanguageAndNumbersTheCulture()
    {
        using (new CultureScope("cs-CZ"))
        {
            Assert.Equal(
                ["MK4", "Tiskne – 42\u00A0%", "Tryska: 215 / 215\u00A0°C", "Podložka: 60 / 60\u00A0°C", "Zbývá 1 h 5 min", "Hotovo v 13:05", "Doba tisku: 2 h 10 min", "benchy_0.4n_0.2mm_PLA_MK4S_2h10m.bgcode"],
                Texts(Printing(), CameraOverlayOptions.AllItems));
        }

        using (new CultureScope("de-DE"))
        {
            Assert.Equal(
                ["Düse: 215 / 215\u00A0°C", "Heizbett: 60 / 60\u00A0°C", "Fertig um 13:05", "Druckzeit: 2 h 10 min"],
                Texts(Printing(), CameraOverlayItems.NozzleTemperature | CameraOverlayItems.BedTemperature | CameraOverlayItems.FinishTime | CameraOverlayItems.PrintTime));
        }

        using (new CultureScope("fr-FR"))
        {
            Assert.Equal(["Buse : 215 / 215\u00A0°C"], Texts(Printing(), CameraOverlayItems.NozzleTemperature));
        }
    }

    [Fact]
    public void LanguageSwitchFormatsTheSameContentAgain()
    {
        CameraPrintContent content = CameraPrintContent.From(Printing(), CameraOverlayOptions.AllItems, "MK4", Utc);
        string english = content.Lines()[4].Text;

        using (new UiCultureScope("pl"))
        {
            Assert.StartsWith("Pozostało ", content.Lines()[4].Text, StringComparison.Ordinal);
        }

        Assert.Equal("1 h 5 min remaining", english);
    }

    // ---- Long names ----

    [Fact]
    public void ShortenedNamesAreReusedWhileTheRawNameIsUnchanged()
    {
        // Every poll brings a new string instance with the same text: the shortened one is not built again.
        CameraOverlayTextCache cache = new();
        PrinterSnapshot Poll(char c) => Printing(job: new PrintJob { FileName = new string(c, 60) + ".bgcode", ProgressPercent = 1 });
        string? File(char c) => CameraPrintContent.From(Poll(c), CameraOverlayItems.FileName, null, Utc, cache).FileName;

        string? first = File('f');
        Assert.Same(first, File('f'));
        string? other = File('g');
        Assert.NotEqual(first, other);
        Assert.Equal(CameraPrintContent.ShortenFileName(new string('g', 60) + ".bgcode"), other);

        string? name = CameraPrintContent.From(null, CameraOverlayItems.PrinterName, new string('N', 100), Utc, cache).Name;
        Assert.Same(name, CameraPrintContent.From(null, CameraOverlayItems.PrinterName, new string('N', 100), Utc, cache).Name);
        Assert.Same(other, File('g'));
    }

    [Fact]
    public void LongFileNameKeepsItsStartAndEnd()
    {
        string name = "very_long_model_name_with_many_parts_0.4n_0.2mm_PETG_COREONE_12h34m.bgcode";
        PrinterSnapshot snapshot = Printing(job: new PrintJob { FileName = name, ProgressPercent = 1 });

        string shown = Assert.Single(Texts(snapshot, CameraOverlayItems.FileName));

        Assert.Equal(CameraPrintContent.MaxFileNameLength, shown.Length);
        Assert.Equal("very_long_model_name_wi…NE_12h34m.bgcode", shown);
    }

    [Theory]
    [InlineData("benchy.bgcode")]
    [InlineData("exactly_forty_characters_long_name.gcode")]
    public void ShortFileNameIsKept(string name)
    {
        Assert.True(name.Length <= CameraPrintContent.MaxFileNameLength);
        Assert.Equal(name, CameraPrintContent.ShortenFileName(name));
    }

    [Fact]
    public void ShorteningNeverSplitsASurrogatePair()
    {
        string emoji = "\U0001F600";
        string name = new string('a', 22) + emoji + new string('b', 30) + emoji + new string('c', 15) + ".gcode";

        string shown = CameraPrintContent.ShortenFileName(name);

        Assert.True(shown.Length <= CameraPrintContent.MaxFileNameLength);
        Assert.DoesNotContain(shown, static c => char.IsSurrogate(c));
        Assert.EndsWith(".gcode", shown, StringComparison.Ordinal);
        Assert.Contains('…', shown);
    }

    [Fact]
    public void LongPrinterNameIsShortened()
    {
        string name = new('N', 100);

        string shown = Assert.Single(Texts(null, CameraOverlayItems.PrinterName, name));

        Assert.Equal(CameraPrintContent.MaxNameLength, shown.Length);
        Assert.EndsWith("…", shown, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void MissingPrinterNameIsLeftOut(string? name) =>
        Assert.True(CameraPrintContent.From(null, CameraOverlayItems.PrinterName, name, Utc).IsEmpty);

    // ---- Cheap comparison ----

    [Fact]
    public void UnselectedValuesDoNotChangeTheContent()
    {
        PrinterSnapshot first = Printing();
        PrinterSnapshot second = Printing(telemetry: new Telemetry { Nozzle = new Temperature(190, 215), Bed = new Temperature(40, 60) }) with
        {
            Timestamp = Now.AddSeconds(5),
        };

        Assert.Equal(
            CameraPrintContent.From(first, CameraOverlayOptions.DefaultItems, "MK4", Utc),
            CameraPrintContent.From(second, CameraOverlayOptions.DefaultItems, "MK4", Utc));
        Assert.NotEqual(
            CameraPrintContent.From(first, CameraOverlayItems.NozzleTemperature, "MK4", Utc),
            CameraPrintContent.From(second, CameraOverlayItems.NozzleTemperature, "MK4", Utc));
    }

    [Fact]
    public void ChangesBelowTheDisplayedPrecisionDoNotChangeTheContent()
    {
        PrinterSnapshot first = Printing(telemetry: new Telemetry { Nozzle = new Temperature(214.6, 215) });
        PrinterSnapshot second = Printing(telemetry: new Telemetry { Nozzle = new Temperature(215.4, 215) }) with
        {
            Job = Printing().Job! with { ProgressPercent = 42.9, Remaining = TimeSpan.FromMinutes(64.5) },
        };

        Assert.Equal(
            CameraPrintContent.From(first, CameraOverlayOptions.AllItems, "MK4", Utc),
            CameraPrintContent.From(second, CameraOverlayOptions.AllItems, "MK4", Utc));
    }
}
