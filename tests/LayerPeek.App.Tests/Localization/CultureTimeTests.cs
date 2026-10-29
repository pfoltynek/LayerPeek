using System.Globalization;

using LayerPeek.Tests.Shared;

namespace LayerPeek.App.Tests.Localization;

/// <summary>
/// The helper for expected en-US times (T-095): it follows the globalization data of the OS exactly and changes nothing
/// but the space before AM/PM, so the tests that use it stay as strict as a plain string comparison.
/// </summary>
public sealed class CultureTimeTests
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    [Fact]
    public void TheSeparatorIsTheOneTheOsFormatsWith()
    {
        string formatted = new DateTime(2026, 10, 16, 13, 5, 0).ToString("t", EnUs);

        Assert.Equal(formatted, CultureTime.EnUs("1:05 PM"));
        // NLS (Windows) separates with a space, ICU with CLDR 42+ (macOS) with U+202F NARROW NO-BREAK SPACE.
        Assert.True(
            CultureTime.EnUsDesignatorSeparator is " " or " ",
            $"Unexpected separator U+{(int)CultureTime.EnUsDesignatorSeparator[0]:X4}.");
    }

    [Fact]
    public void OnlyTheSpaceBetweenADigitAndTheDesignatorChanges()
    {
        string expected = $"Done October 17 at 8:00{CultureTime.EnUsDesignatorSeparator}AM, PM label, 5 AMPS, 2 h";

        Assert.Equal(expected, CultureTime.EnUs("Done October 17 at 8:00 AM, PM label, 5 AMPS, 2 h"));
    }
}
