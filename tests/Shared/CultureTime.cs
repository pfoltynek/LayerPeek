using System.Globalization;
using System.Text.RegularExpressions;

namespace LayerPeek.Tests.Shared;

/// <summary>
/// Expected en-US times independent of the globalization data of the OS. The app shows the short time of the culture as
/// it is: Windows (NLS) puts a plain space before AM/PM, ICU with CLDR 42+ (macOS) a U+202F NARROW NO-BREAK SPACE
/// ("1:05 PM"). Tests write the expected text with a plain space and pass it through <see cref="EnUs"/>, so the
/// comparison stays exact: only the space between a digit and AM/PM becomes the one of en-US on this machine.
/// </summary>
internal static partial class CultureTime
{
    /// <summary>The character between the time and the AM/PM designator in the en-US short time pattern of this OS.</summary>
    public static string EnUsDesignatorSeparator { get; } = DesignatorSeparator(CultureInfo.GetCultureInfo("en-US"));

    /// <summary><paramref name="text"/> with every "&lt;digit&gt; AM" / "&lt;digit&gt; PM" separated as en-US does on this OS.</summary>
    public static string EnUs(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return SpaceBeforeDesignator().Replace(text, EnUsDesignatorSeparator);
    }

    private static string DesignatorSeparator(CultureInfo culture)
    {
        string pattern = culture.DateTimeFormat.ShortTimePattern;
        int designator = pattern.IndexOf("tt", StringComparison.Ordinal);
        if (designator < 1)
        {
            throw new InvalidOperationException($"The short time pattern of {culture.Name} has no AM/PM designator after the time: '{pattern}'.");
        }

        return pattern[(designator - 1)..designator];
    }

    [GeneratedRegex(@"(?<=\d) (?=[AP]M\b)", RegexOptions.CultureInvariant)]
    private static partial Regex SpaceBeforeDesignator();
}
