using LayerPeek.Core.Printers;

namespace LayerPeek.App.Printers;

/// <summary>
/// The bundled printer illustrations (T-039, variant A "flat, transparent background"): which one a model shows and which
/// pre-rendered size to use. Pixel sizes up to 24 are drawn from simplified art (16 / 20 / 24 px grids), larger ones from
/// the detailed art; the SVG sources next to the PNGs are not bundled.
/// </summary>
public static class PrinterIllustrations
{
    /// <summary>The generic printer (app motif in a dashed outline): unknown model, iX, Core One INDX, Prusa Connect.</summary>
    public const string Generic = "generic";

    /// <summary>Illustration ids, i.e. the file name prefixes in <c>Assets/Printers</c>.</summary>
    public static IReadOnlyList<string> Ids { get; } = ["mk4", "core-one", "xl", "mini", Generic];

    /// <summary>
    /// Pre-rendered sizes in physical pixels, ascending: 16–32 for the tray menu (16 DIP at 100–200 %), 24–48 for the
    /// printer list (24 DIP), 48–96 for the diagnostics window (48 DIP), 56–112 for the Printer sub-tab (56 DIP), and larger
    /// ones for higher scaling.
    /// </summary>
    public static IReadOnlyList<int> PixelSizes { get; } = [16, 20, 24, 32, 48, 56, 64, 96, 112, 128, 256];

    /// <summary>
    /// User decisions 2026-10-20: MK4, MK4S, MK3.9(S) and MK3.5(S) share one illustration; XL and XL+ always the
    /// five-tool look; Core One L uses the Core One illustration; everything else is <see cref="Generic"/>.
    /// </summary>
    public static string IdFor(PrinterModel model) => model switch
    {
        PrinterModel.Mk4 or PrinterModel.Mk4S or PrinterModel.Mk39 or PrinterModel.Mk39S or PrinterModel.Mk35 or PrinterModel.Mk35S => "mk4",
        PrinterModel.CoreOne or PrinterModel.CoreOneL => "core-one",
        PrinterModel.Xl or PrinterModel.XlPlus => "xl",
        PrinterModel.Mini => "mini",
        _ => Generic,
    };

    /// <summary>The illustration of a resolved image; a custom image has none (<see cref="Generic"/> is its fallback).</summary>
    public static string IdFor(PrinterImage image) => image.Kind == PrinterImageKind.Model ? IdFor(image.Model) : Generic;

    /// <summary>
    /// The pre-rendered size to draw <paramref name="sizeInDip"/> at <paramref name="scaling"/>: the smallest one with at
    /// least as many physical pixels (so the image is never enlarged), the largest one beyond that.
    /// </summary>
    public static int SelectPixelSize(double sizeInDip, double scaling)
    {
        double pixels = sizeInDip * (double.IsFinite(scaling) && scaling > 0 ? scaling : 1);
        // A hair below a whole pixel (e.g. 24 × 1.25 = 30.000000004) still counts as that pixel.
        int needed = Math.Max(1, (int)Math.Ceiling(pixels - 0.01));
        foreach (int size in PixelSizes)
        {
            if (size >= needed)
            {
                return size;
            }
        }

        return PixelSizes[^1];
    }

    /// <summary>The bundled PNG of illustration <paramref name="id"/> at <paramref name="pixelSize"/> (one of <see cref="PixelSizes"/>).</summary>
    public static Uri AssetUri(string id, int pixelSize) => new($"avares://LayerPeek/Assets/Printers/{id}-{pixelSize}.png");
}
