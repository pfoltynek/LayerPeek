using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;

using LayerPeek.App.Localization;
using LayerPeek.App.Resources;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Tests.Localization;

public sealed partial class StringsTests
{
    /// <summary>Every translation (not English: it is the neutral resources).</summary>
    private static readonly string[] TranslatedCultures = ["cs", "sk", "de", "es", "fr", "it", "pl"];

    public static TheoryData<string> Translations => new(TranslatedCultures);

    [Theory]
    [MemberData(nameof(Translations))]
    public void TranslationHasExactlyTheNeutralKeysAndPlaceholders(string culture)
    {
        Dictionary<string, string> neutral = Load(CultureInfo.InvariantCulture);
        Dictionary<string, string> translated = Load(CultureInfo.GetCultureInfo(culture));

        Assert.NotEmpty(neutral);
        Assert.Equal(neutral.Keys.Order(StringComparer.Ordinal), translated.Keys.Order(StringComparer.Ordinal));
        foreach ((string key, string text) in translated)
        {
            Assert.False(string.IsNullOrWhiteSpace(text), $"'{key}' is empty in {culture}.");
            Assert.True(
                Placeholders(neutral[key]).SequenceEqual(Placeholders(text)),
                $"'{key}' in {culture} has placeholders [{string.Join(", ", Placeholders(text))}] instead of [{string.Join(", ", Placeholders(neutral[key]))}].");
            // A malformed format string (e.g. a stray brace) would only throw when the text is shown.
            _ = string.Format(CultureInfo.InvariantCulture, text, "a", "b", "c", "d");
        }
    }

    /// <summary>French typography: a non-breaking space (U+00A0), never a breaking one, before ":", ";", "?" and "!".</summary>
    [Fact]
    public void FrenchPunctuationFollowsANonBreakingSpace()
    {
        foreach ((string key, string text) in Load(CultureInfo.GetCultureInfo("fr")))
        {
            Assert.False(Regex.IsMatch(text, @" [:;?!]"), $"'{key}' in 'fr' has a breaking space before ':', ';', '?' or '!'.");
        }
    }

    public static TheoryData<string> AllCultures => new(["", .. TranslatedCultures]);

    /// <summary>
    /// A resolution ("1920 × 1080 px") never wraps (decision 2026-10-19, T-034): non-breaking spaces around "×" and before
    /// "px" in every text and language.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllCultures))]
    public void ResolutionsUseNonBreakingSpaces(string culture)
    {
        CultureInfo info = culture.Length == 0 ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(culture);
        Dictionary<string, string> texts = Load(info);
        foreach ((string key, string text) in texts)
        {
            Assert.False(text.Contains(" ×", StringComparison.Ordinal) || text.Contains("× ", StringComparison.Ordinal), $"'{key}' in '{culture}' has a breaking space next to '×'.");
            Assert.False(ResolutionWithBreakingSpace().IsMatch(text), $"'{key}' in '{culture}' has a breaking space before 'px'.");
        }

        string success = string.Format(info, texts["SettingsCameraTestSuccess"], 1920, 1080);
        Assert.Contains("1920\u00A0×\u00A01080\u00A0px", success, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"(\}|\d) px\b")]
    private static partial Regex ResolutionWithBreakingSpace();

    /// <summary>
    /// Keys whose text may legitimately break between a number and a unit, each with its justification. Empty: no text
    /// needs it today; a new entry needs the user's approval (decision 2026-10-25).
    /// </summary>
    private static readonly Dictionary<string, string> NumberUnitBreakAllowList = new(StringComparer.Ordinal);

    /// <summary>
    /// A number or a placeholder never breaks from its unit anywhere in the UI ("42 %", "215 °C", "30 min", "2 h",
    /// "15 s", "0.2 mm", "7,200 RPM"; T-036 review, decision 2026-10-25): where a language puts a space between them, it is
    /// a non-breaking one (U+00A0) in every key and language. Spelled-out words ("60 minutes", "{0} characters") are prose,
    /// not unit symbols, and may wrap.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllCultures))]
    public void NumbersAndUnitsNeverBreakApart(string culture)
    {
        CultureInfo info = culture.Length == 0 ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(culture);
        Dictionary<string, string> texts = Load(info);
        foreach ((string key, string text) in texts)
        {
            if (NumberUnitBreakAllowList.ContainsKey(key))
            {
                continue;
            }

            Match match = NumberUnitWithBreakingSpace().Match(text);
            Assert.False(match.Success, $"'{key}' in '{culture}' has a breaking space between a number and its unit ('{match.Value}'): '{text}'.");
        }

        Assert.All(NumberUnitBreakAllowList.Keys, key => Assert.True(texts.ContainsKey(key), $"Allow-listed key '{key}' does not exist."));

        Assert.DoesNotContain(' ', string.Format(info, texts["DiagnosticsTemperatureValue"], 215));
        Assert.EndsWith("215 °C", string.Format(info, texts["CameraOverlayTemperatureWithTarget"], 210, 215), StringComparison.Ordinal);
        Assert.DoesNotContain(' ', string.Format(info, texts["ToolTipProgress"], 42));
        Assert.DoesNotContain(' ', string.Format(info, texts["DiagnosticsRpmValue"], "7,200"));
        Assert.DoesNotContain(' ', string.Format(info, texts["DiagnosticsMillimetreValue"], "0.40"));
        Assert.DoesNotContain(' ', texts["DiagnosticsChartWindow30Minutes"]);
        Assert.DoesNotContain(' ', texts["DiagnosticsChartWindow2Hours"]);
        Assert.DoesNotContain(' ', texts["DiagnosticsChartWindow24Hours"]);
        Assert.Matches(@"15 s\b", string.Format(info, texts["SettingsCameraTestTimeout"], 15));
        Assert.EndsWith("0.2 mm", string.Format(info, texts["CameraOverlayLayerHeightName"], "0.2"), StringComparison.Ordinal);
        Assert.EndsWith("0.4 mm", string.Format(info, texts["CameraOverlayNozzleDiameterName"], "0.4"), StringComparison.Ordinal);
    }

    /// <summary>
    /// A number or a placeholder, then breaking white space (any but U+00A0, U+2007 and U+202F), then a unit symbol as a
    /// whole word: time (ms, s, min, h, d and the day of fr "j" / pl "g"), temperature, percent, lengths, data sizes,
    /// pixels and DIP, rates ("fps", "RPM", "U/min", "ot./min" …).
    /// </summary>
    [GeneratedRegex(@"(\}|\d)[\s-[   ]]+(ms|s|min|h|d|j|g|°C|%|mm|cm|m|B|KB|kB|MB|GB|px|DIP|dpi|fps|RPM|rpm|\S*/min)(?![\p{L}\d])")]
    private static partial Regex NumberUnitWithBreakingSpace();

    /// <summary>
    /// The durations of the overlay widgets, the mini preview strip, the tray tooltip and the toasts ("3 min", "2 h 35 min",
    /// "−60 min"; decision 2026-10-25, T-088).
    /// </summary>
    private static readonly string[] DurationKeys =
    [
        nameof(Strings.DurationLessThanMinute),
        nameof(Strings.DurationMinutes),
        nameof(Strings.DurationHoursMinutes),
        nameof(Strings.DurationDaysHours),
        nameof(Strings.CameraOverlayChartStart),
    ];

    /// <summary>
    /// A duration never breaks between a number and its unit ("3 / min"): a non-breaking space (U+00A0) joins them in
    /// every language, also in "&lt; 1 min". Between the parts ("2 h" and "35 min") a breaking space stays, so a long
    /// duration can still wrap between them (decision 2026-10-25, T-088).
    /// </summary>
    [Theory]
    [MemberData(nameof(AllCultures))]
    public void DurationsKeepNumberAndUnitTogether(string culture)
    {
        CultureInfo info = culture.Length == 0 ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(culture);
        Dictionary<string, string> texts = Load(info);
        foreach (string key in DurationKeys)
        {
            string text = texts[key];
            Assert.False(DurationWithBreakingSpace().IsMatch(text), $"'{key}' in '{culture}' has a breaking space between a number and its unit: '{text}'.");
            Assert.Matches(DurationPart(), text);
        }

        Assert.DoesNotContain(' ', texts[nameof(Strings.DurationLessThanMinute)]);
        Assert.DoesNotContain(' ', string.Format(info, texts[nameof(Strings.DurationMinutes)], 3));
        Assert.DoesNotContain(' ', texts[nameof(Strings.CameraOverlayChartStart)]);

        // "2 h 35 min": each part stays together, the parts may wrap apart.
        string[] parts = string.Format(info, texts[nameof(Strings.DurationHoursMinutes)], 2, 35).Split(' ');
        Assert.Equal(2, parts.Length);
        Assert.All(parts, static part => Assert.Matches(DurationPart(), part));
        parts = string.Format(info, texts[nameof(Strings.DurationDaysHours)], 1, 4).Split(' ');
        Assert.Equal(2, parts.Length);
        Assert.All(parts, static part => Assert.Matches(DurationPart(), part));
    }

    [GeneratedRegex(@"(\}|\d) \p{L}")]
    private static partial Regex DurationWithBreakingSpace();

    [GeneratedRegex(@"(\}|\d)\u00A0\p{L}")]
    private static partial Regex DurationPart();

    [Fact]
    public void EverySupportedLanguageExceptEnglishHasATranslation()
    {
        Assert.Equal(
            AppLanguages.Supported.Where(static code => code != "en").Order(StringComparer.Ordinal),
            TranslatedCultures.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void NeutralFormatStringsAreValid()
    {
        foreach ((string key, string text) in Load(CultureInfo.InvariantCulture))
        {
            Assert.False(string.IsNullOrWhiteSpace(text), $"'{key}' is empty.");
            _ = string.Format(CultureInfo.InvariantCulture, text, "a", "b", "c", "d");
        }
    }

    [Fact]
    public void StronglyTypedAccessFollowsUiCulture()
    {
        using (new UiCultureScope("cs-CZ"))
        {
            Assert.Equal("U_končit", Strings.MenuQuit);
        }

        using (new UiCultureScope("en-GB"))
        {
            Assert.Equal("_Quit", Strings.MenuQuit);
        }

        using (new UiCultureScope("ja-JP"))
        {
            Assert.Equal("_Quit", Strings.MenuQuit);
        }
    }

    [Theory]
    [InlineData("de-AT", "de")]
    [InlineData("fr-CA", "fr")]
    [InlineData("es-MX", "es")]
    [InlineData("it-CH", "it")]
    [InlineData("pl-PL", "pl")]
    [InlineData("sk-SK", "sk")]
    public void RegionalVariantUsesItsLanguage(string regional, string language)
    {
        string expected = Strings.ResourceManager.GetString(nameof(Strings.MenuQuit), CultureInfo.GetCultureInfo(language))!;

        Assert.NotEqual("_Quit", expected);
        Assert.Equal(expected, Strings.ResourceManager.GetString(nameof(Strings.MenuQuit), CultureInfo.GetCultureInfo(regional)));
    }

    [Fact]
    public void LocalizedStringsIndexerReturnsTheResource()
    {
        using (new UiCultureScope("cs-CZ"))
        {
            Assert.Equal("U_končit", LocalizedStrings.Instance[nameof(Strings.MenuQuit)]);
        }

        // A typo must be visible instead of an empty label.
        Assert.Equal("NoSuchKey", LocalizedStrings.Instance["NoSuchKey"]);
    }

    private static Dictionary<string, string> Load(CultureInfo culture)
    {
        ResourceSet? set = Strings.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
        Assert.NotNull(set);
        Dictionary<string, string> strings = new(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in set)
        {
            strings.Add((string)entry.Key, Assert.IsType<string>(entry.Value));
        }

        return strings;
    }

    private static string[] Placeholders(string text) =>
        [.. PlaceholderPattern().Matches(text).Select(match => match.Value).Distinct().Order(StringComparer.Ordinal)];

    [GeneratedRegex(@"\{\d+(?:[,:][^}]*)?\}")]
    private static partial Regex PlaceholderPattern();
}
