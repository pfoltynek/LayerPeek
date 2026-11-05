using System.Globalization;

using LayerPeek.App.Resources;

namespace LayerPeek.App.Detection;

/// <summary>
/// The texts of the detector kinds (T-154): a detector id (<see cref="Core.Detection.PrintFailureDetectorRegistration.Id"/>)
/// is never shown, every known id has a name, a description of what it detects and what not, and optionally a known
/// limitation. A new detector (T-155, T-156) adds its texts here and to every <c>Strings.*.resx</c>; an id without texts
/// (a newer version wrote its settings) is shown as "Detector &lt;id&gt;" without a description.
/// </summary>
internal static class DetectorTexts
{
    /// <summary>"Change on the bed" (T-155): a part that vanished or moved, new material on the bed.</summary>
    public const string BedChange = "bed-change";

    /// <summary>"Printing into the air" (T-167): the Z axis rises but nothing grows on the bed any more.</summary>
    public const string AirPrinting = "air-printing";

    /// <summary>The name in <paramref name="culture"/> (<see langword="null"/>: the language of the UI).</summary>
    public static string Name(string detectorId, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(detectorId);
        return detectorId switch
        {
            BedChange => Text(nameof(Strings.DetectorBedChange), culture),
            AirPrinting => Text(nameof(Strings.DetectorAirPrinting), culture),
            _ => string.Format(culture ?? CultureInfo.CurrentCulture, Text(nameof(Strings.DetectorUnknown), culture), detectorId),
        };
    }

    /// <summary>What it detects and what not; <see langword="null"/> for an unknown id.</summary>
    public static string? Description(string detectorId, CultureInfo? culture = null) => detectorId switch
    {
        BedChange => Text(nameof(Strings.DetectorBedChangeDescription), culture),
        AirPrinting => Text(nameof(Strings.DetectorAirPrintingDescription), culture),
        _ => null,
    };

    /// <summary>A known limitation, shown marked under the description; <see langword="null"/>: none.</summary>
    public static string? Limitation(string detectorId, CultureInfo? culture = null) => detectorId switch
    {
        BedChange => Text(nameof(Strings.DetectorBedChangeLimitation), culture),
        AirPrinting => Text(nameof(Strings.DetectorAirPrintingLimitation), culture),
        _ => null,
    };

    /// <summary>A resource text in exactly <paramref name="culture"/>; <see langword="null"/>: <see cref="Strings.Culture"/>.</summary>
    private static string Text(string name, CultureInfo? culture) => Strings.ResourceManager.GetString(name, culture ?? Strings.Culture) ?? name;
}
