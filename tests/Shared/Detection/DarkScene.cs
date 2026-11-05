namespace LayerPeek.Tests.Detection;

/// <summary>
/// The key frames a synthetic print is made of, with their per-pixel median (the scene without most of the head) and the
/// pixels of the printed part in it. <see cref="SyntheticScene"/> is the fixture itself; <see cref="DarkScene"/> derives
/// the black-filament variants.
/// </summary>
internal sealed record SyntheticBackdrop(string Name, IReadOnlyList<byte[]> KeyFrames, byte[] Median, bool[] Part)
{
    public override string ToString() => Name;
}

/// <summary>
/// How a Prusa print sheet looks in the camera of a Core One (T-163, user feedback 2026-11-05: the sheets come in several
/// colours): its own course of light is the fixture's sheet (the LED glare at the front), smoothed unless
/// <paramref name="Markings"/> keeps the printed grid and logo, scaled by <paramref name="Gain"/> (lighter sheets) and with a
/// fixed grain of standard deviation <paramref name="Grain"/> levels (a textured powder coat).
/// </summary>
internal sealed record SheetLook(string Name, bool Markings, double Gain, double Grain);

/// <summary>
/// T-163 (user decisions 2026-11-04: synthetic data only, black filament only; 2026-11-05: on the colours of the Prusa sheets):
/// the fixture scene with the printed part recoloured as black filament, on a sheet of <see cref="Sheets"/>. Recoloured
/// from the median of the key frames, only where a key frame shows the scene (not the head), so the head keeps moving over
/// the recoloured scene.
/// <para>
/// Fidelity limits: the light of the chamber and the noise stay those of the fixture; the luma of the other sheets is an
/// estimate (the shop does not state it, task notes); glossy highlights of black filament and the glare of the LED strip
/// on a smooth (glossy) PEI sheet are not modelled beyond the fixed highlights of <see cref="Spaghetti.Glint"/>; the grain
/// does not move with the bed in Z; the part's shading is scaled, not re-lit.
/// </para>
/// </summary>
internal static class DarkScene
{
    /// <summary>Black filament in the chamber light (fixed up front: below the black sheet, ~50–90 at the front).</summary>
    public const double PartLuma = 35;

    /// <summary>A key frame pixel this close to the median shows the scene, not the moving head.</summary>
    private const int SceneLevel = 20;

    /// <summary>The sheet next to the silver part (the part edge is a blend of the two).</summary>
    private const double SheetNextToPart = 60;

    /// <summary>The silver part's own level (T-152: ~200; its top is saturated).</summary>
    private const double SilverLuma = 200;

    /// <summary>
    /// The sheet of the fixture (the user's Core One): dark, matte, with the printed dashed grid and logo strip. Which Prusa
    /// sheet it is (smooth PEI on black steel, as the Core One+ ships with a smooth PEI sheet, or satin) is not certain.
    /// </summary>
    public static SheetLook FixtureLook { get; } = new("fixture sheet (dark, printed grid)", Markings: true, Gain: 1, Grain: 0);

    /// <summary>A dark sheet without markings (PA Nylon, smooth PEI on black steel): the least contrast.</summary>
    public static SheetLook DarkPlainLook { get; } = new("dark sheet without markings", Markings: false, Gain: 1, Grain: 0);

    /// <summary>Smooth PEI on green steel (estimate: ~1.6× the satin sheet).</summary>
    public static SheetLook SmoothGreenLook { get; } = new("smooth PEI, green", Markings: false, Gain: 1.6, Grain: 1);

    /// <summary>Smooth PEI on gold steel (estimate: ~2× the satin sheet).</summary>
    public static SheetLook SmoothGoldLook { get; } = new("smooth PEI, gold", Markings: false, Gain: 2.0, Grain: 1);

    /// <summary>Textured PEI powder coat, gold / bronze (estimate: ~2.3× the satin sheet, coarse grain).</summary>
    public static SheetLook TexturedGoldLook { get; } = new("textured PEI, gold", Markings: false, Gain: 2.3, Grain: 6);

    private static readonly Lazy<SyntheticBackdrop> LazyFixture = new(static () => Create(FixtureLook));

    private static readonly Lazy<SyntheticBackdrop> LazyDarkPlain = new(static () => Create(DarkPlainLook));

    private static readonly Lazy<SyntheticBackdrop[]> LazySheets = new(static () =>
        [Fixture, DarkPlain, Create(SmoothGreenLook), Create(SmoothGoldLook), Create(TexturedGoldLook)]);

    /// <summary>The fixture's dark sheet with its printed grid and logo, the part black.</summary>
    public static SyntheticBackdrop Fixture => LazyFixture.Value;

    /// <summary>The same sheet without the grid and logo.</summary>
    public static SyntheticBackdrop DarkPlain => LazyDarkPlain.Value;

    /// <summary>The two dark sheets (black filament on a black sheet, the hard case).</summary>
    public static IReadOnlyList<SyntheticBackdrop> Backdrops => [Fixture, DarkPlain];

    /// <summary>Every sheet look, darkest first.</summary>
    public static IReadOnlyList<SyntheticBackdrop> Sheets => LazySheets.Value;

    private static SyntheticBackdrop Create(SheetLook look)
    {
        byte[] median = SyntheticScene.Median;
        double[] target = Recolour(median, look);
        List<byte[]> frames = new(SyntheticScene.KeyFrames.Count);
        foreach (byte[] key in SyntheticScene.KeyFrames)
        {
            byte[] frame = (byte[])key.Clone();
            for (int i = 0; i < frame.Length; i++)
            {
                if (Math.Abs(frame[i] - median[i]) <= SceneLevel)
                {
                    frame[i] = SyntheticScene.Clamp(frame[i] + (target[i] - median[i]));
                }
            }

            frames.Add(frame);
        }

        // The part with its blended edge, which is black filament now too (it moves and vanishes with the part).
        bool[] part = new bool[median.Length];
        for (int i = 0; i < part.Length; i++)
        {
            part[i] = SyntheticScene.Part[i] || (IsEdge(i) && median[i] > SheetNextToPart);
        }

        return new SyntheticBackdrop("black part, " + look.Name, frames, SyntheticScene.MedianOf(frames), part);
    }

    /// <summary>The median scene recoloured: the sheet as <paramref name="look"/>, the part (and its blended edge) black.</summary>
    private static double[] Recolour(byte[] median, SheetLook look)
    {
        int width = SyntheticPrint.Width;
        int height = SyntheticPrint.Height;
        bool[] part = SyntheticScene.Part;
        double[] target = new double[median.Length];
        byte[]? sheet = look.Markings ? null : PlainSheet(median, part);
        Random grain = new(163);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width) + x;
                double value = sheet?[i] ?? median[i];
                if (OnSheet(x, y) && !part[i] && (look.Gain != 1 || look.Grain > 0))
                {
                    value = (value * look.Gain) + (look.Grain * Gaussian(grain));
                }

                if (part[i])
                {
                    // Black filament keeps a trace of the part's shading (the saturated top a little brighter).
                    value = PartLuma + (0.1 * (median[i] - SilverLuma));
                }
                else if (NextToPart(part, x, y) && median[i] > SheetNextToPart)
                {
                    // The blended edge: the share of the silver part replaced by black filament.
                    double share = Math.Min(1, (median[i] - SheetNextToPart) / (SilverLuma - SheetNextToPart));
                    value = Math.Max(PartLuma, value - (share * (SilverLuma - PartLuma)));
                }

                target[i] = value;
            }
        }

        return target;
    }

    private static double Gaussian(Random random)
    {
        double u = 1 - random.NextDouble();
        double v = random.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v);
    }

    private static bool IsEdge(int i) => NextToPart(SyntheticScene.Part, i % SyntheticPrint.Width, i / SyntheticPrint.Width);

    private static bool NextToPart(bool[] part, int x, int y)
    {
        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                int nx = x + dx;
                int ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < SyntheticPrint.Width && ny < SyntheticPrint.Height && part[(ny * SyntheticPrint.Width) + nx])
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The sheet without its light grid and logo: in the quadrilateral of the sheet in front of the part, every pixel no
    /// brighter than the lower quartile of its 9×9 neighbourhood (thin light lines and lettering vanish, the dark sheet and
    /// its light course stay).
    /// </summary>
    private static byte[] PlainSheet(byte[] median, bool[] part)
    {
        int width = SyntheticPrint.Width;
        int height = SyntheticPrint.Height;
        byte[] sheet = (byte[])median.Clone();
        Span<byte> window = stackalloc byte[81];
        for (int y = SheetTop; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width) + x;
                if (part[i] || !OnSheet(x, y))
                {
                    continue;
                }

                int n = 0;
                for (int dy = -4; dy <= 4; dy++)
                {
                    for (int dx = -4; dx <= 4; dx++)
                    {
                        int nx = Math.Clamp(x + dx, 0, width - 1);
                        int ny = Math.Clamp(y + dy, 0, height - 1);
                        window[n++] = median[(ny * width) + nx];
                    }
                }

                Span<byte> values = window[..n];
                values.Sort();
                sheet[i] = Math.Min(median[i], values[n / 4]);
            }
        }

        return sheet;
    }

    /// <summary>The first row of <see cref="OnSheet"/>.</summary>
    private const int SheetTop = 70;

    /// <summary>
    /// The black sheet in the 320×180 fixture frame from <see cref="SheetTop"/> down: between its left edge ((76, 70) to
    /// (107, 180)) and its right edge with the logo strip ((285, 70) to (240, 180)).
    /// </summary>
    private static bool OnSheet(int x, int y)
    {
        double t = (y - SheetTop) / (double)(SyntheticPrint.Height - SheetTop);
        double left = 76 + (t * (107 - 76));
        double right = 285 + (t * (240 - 285));
        return y >= SheetTop && x >= left && x <= right;
    }
}
