using System.Globalization;

using LayerPeek.App.Diagnostics;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Tests.Diagnostics;

public sealed class DiagnosticsFormatterTests
{
    private const string Missing = "–";
    private const char NoBreakSpace = ' ';

    private static readonly TimeZoneInfo Prague = TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2");

    [Theory]
    [InlineData("en-US", 215.34, "215.3\u00A0°C")]
    [InlineData("en-US", 215.36, "215.4\u00A0°C")]
    [InlineData("en-US", 24, "24.0\u00A0°C")]
    [InlineData("cs-CZ", 215.34, "215,3\u00A0°C")]
    [InlineData("en-US", -3.2, "-3.2\u00A0°C")]
    public void TemperatureHasOneDecimalInTheUiCulture(string culture, double value, string expected)
    {
        using CultureScope scope = new(culture);

        Assert.Equal(expected, DiagnosticsFormatter.Temperature(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void MissingOrInvalidNumbersAreADash(double? value)
    {
        using CultureScope scope = new("en-US");

        Assert.Equal(Missing, DiagnosticsFormatter.Temperature(value));
        Assert.Equal(Missing, DiagnosticsFormatter.TargetTemperature(value));
        Assert.Equal(Missing, DiagnosticsFormatter.Millimetres(value, 2));
    }

    [Theory]
    [InlineData("en-US", 215, "215\u00A0°C")]
    [InlineData("en-US", 0, "Off")]
    [InlineData("cs-CZ", 0, "Vypnuto")]
    [InlineData("cs-CZ", 60, "60\u00A0°C")]
    public void TargetZeroMeansTheHeaterIsOff(string culture, double value, string expected)
    {
        using CultureScope scope = new(culture);

        Assert.Equal(expected, DiagnosticsFormatter.TargetTemperature(value));
    }

    [Fact]
    public void RpmUsesGroupSeparatorsAndLocalizedUnit()
    {
        using (new CultureScope("en-US"))
        {
            Assert.Equal("7,200 RPM", DiagnosticsFormatter.Rpm(7200));
            Assert.Equal("0 RPM", DiagnosticsFormatter.Rpm(0));
            Assert.Equal(Missing, DiagnosticsFormatter.Rpm(null));
        }

        using (new CultureScope("cs-CZ"))
        {
            string separator = CultureInfo.GetCultureInfo("cs-CZ").NumberFormat.NumberGroupSeparator;
            Assert.Equal($"7{separator}200 ot./min", DiagnosticsFormatter.Rpm(7200));
        }
    }

    [Fact]
    public void PercentFollowsTheLanguageConvention()
    {
        using (new CultureScope("en-US"))
        {
            Assert.Equal("100%", DiagnosticsFormatter.Percent(100));
            Assert.Equal(Missing, DiagnosticsFormatter.Percent(null));
        }

        using (new CultureScope("cs-CZ"))
        {
            Assert.Equal($"95{NoBreakSpace}%", DiagnosticsFormatter.Percent(95));
        }
    }

    [Fact]
    public void MillimetresUseTheRequestedDecimals()
    {
        using (new CultureScope("en-US"))
        {
            Assert.Equal("14.00 mm", DiagnosticsFormatter.Millimetres(14, 2));
            Assert.Equal("0.40 mm", DiagnosticsFormatter.Millimetres(0.4, 2));
        }

        using (new CultureScope("cs-CZ"))
        {
            Assert.Equal("0,25 mm", DiagnosticsFormatter.Millimetres(0.249, 2));
        }
    }

    [Fact]
    public void TimestampOfTodayShowsTheLocalTimeOnly()
    {
        DateTimeOffset now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset timestamp = new(2026, 10, 16, 10, 15, 30, TimeSpan.Zero);

        using (new CultureScope("cs-CZ"))
        {
            Assert.Equal("12:15:30", DiagnosticsFormatter.Timestamp(timestamp, now, Prague));
        }

        using (new CultureScope("en-US"))
        {
            Assert.Equal($"12:15:30{NarrowOrRegularSpace("en-US")}PM", DiagnosticsFormatter.Timestamp(timestamp, now, Prague));
        }
    }

    [Fact]
    public void TimestampOfAnotherDayIncludesTheDate()
    {
        DateTimeOffset now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        using CultureScope scope = new("cs-CZ");

        // 22:30 UTC on the 15th is already 00:30 on the 16th in UTC+2: "today" is decided in local time.
        Assert.Equal("0:30:00", DiagnosticsFormatter.Timestamp(new DateTimeOffset(2026, 10, 15, 22, 30, 0, TimeSpan.Zero), now, Prague));

        string text = DiagnosticsFormatter.Timestamp(new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero), now, Prague);

        Assert.Contains("14.", text, StringComparison.Ordinal);
        Assert.Contains("10:00", text, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingTimestampIsADash()
    {
        using CultureScope scope = new("en-US");

        Assert.Equal(Missing, DiagnosticsFormatter.Timestamp(null, DateTimeOffset.UnixEpoch, Prague));
    }

    [Fact]
    public void TextAndYesNoAreLocalizedAndNeverEmpty()
    {
        using (new CultureScope("en-US"))
        {
            Assert.Equal("Yes", DiagnosticsFormatter.YesNo(true));
            Assert.Equal("No", DiagnosticsFormatter.YesNo(false));
            Assert.Equal(Missing, DiagnosticsFormatter.Text(null));
            Assert.Equal(Missing, DiagnosticsFormatter.Text("  "));
            Assert.Equal("6.9.2+10123", DiagnosticsFormatter.Text("6.9.2+10123"));
        }

        using (new CultureScope("cs-CZ"))
        {
            Assert.Equal("Ano", DiagnosticsFormatter.YesNo(true));
            Assert.Equal("Ne", DiagnosticsFormatter.YesNo(false));
        }
    }

    [Fact]
    public void AxisTemperatureIsAWholeNumberWithUnit()
    {
        using (new CultureScope("en-US"))
        {
            Assert.Equal("250\u00A0°C", DiagnosticsFormatter.AxisTemperature(250));
            Assert.Equal("0\u00A0°C", DiagnosticsFormatter.AxisTemperature(0));
        }

        using (new CultureScope("cs-CZ"))
        {
            string separator = CultureInfo.GetCultureInfo("cs-CZ").NumberFormat.NumberGroupSeparator;
            Assert.Equal($"1{separator}000\u00A0°C", DiagnosticsFormatter.AxisTemperature(1000));
        }
    }

    [Fact]
    public void InfoUnavailableNamesTheState()
    {
        using CultureScope scope = new("cs-CZ");

        Assert.Equal("Nelze načíst: Nedostupná", DiagnosticsFormatter.InfoUnavailable(LayerPeek.Core.Printers.PrinterState.Offline));
    }

    /// <summary>ICU puts a narrow no-break space before AM/PM; NLS a regular space.</summary>
    private static string NarrowOrRegularSpace(string culture)
    {
        string designator = new DateTime(2026, 1, 1, 13, 0, 0).ToString("T", CultureInfo.GetCultureInfo(culture));
        return designator[^3].ToString();
    }

    /// <summary>Every known problem has a translated text in every language (T-044); none keeps the technical message.</summary>
    [Fact]
    public void EveryPrinterProblemHasATextInEveryLanguage()
    {
        foreach (string language in AppLanguages.Supported)
        {
            using CultureScope scope = new(language);
            foreach (PrinterProblem problem in Enum.GetValues<PrinterProblem>())
            {
                string? text = DiagnosticsFormatter.Problem(problem);
                Assert.True(problem == PrinterProblem.None ? text is null : !string.IsNullOrWhiteSpace(text), $"{language}: {problem}");
            }
        }

        using (new CultureScope("cs-CZ"))
        {
            Assert.Equal("Tiskárna není dostupná.", DiagnosticsFormatter.Problem(PrinterProblem.NotReachable));
        }
    }
}
