using LayerPeek.Tests.Detection;

namespace LayerPeek.Detection.Tests;

/// <summary>
/// The synthetic test sets of T-155, fixed by their seeds (the same variants on every run). Sizes are chosen as plausible
/// failures seen at 320×180 (~1 mm per pixel on the front of the Core One bed), not by what the detector finds.
/// </summary>
internal static class SyntheticSets
{
    /// <summary>Filament colours as luma (black filament on the black sheet is <see cref="DarkFailures"/>, T-163).</summary>
    private static readonly double[] Lumas = [225, 190, 160, 130, 105];

    /// <summary>
    /// The failures: spaghetti tangles (15–40 strands, partly transparent, growing for 1–4 min), lumps (radius 5–14 px)
    /// and the part moved by 6–25 px or gone.
    /// </summary>
    public static IEnumerable<SyntheticFailure> Failures(int seed)
    {
        Random random = new(seed);
        for (int i = 0; i < 30; i++)
        {
            int width = random.Next(35, 81);
            int height = random.Next(20, 41);
            yield return new Spaghetti(
                Seed: random.Next(),
                Strands: random.Next(15, 41),
                Luma: Lumas[random.Next(Lumas.Length)],
                Alpha: 0.5 + (random.NextDouble() * 0.5),
                Thick: random.Next(2) == 0,
                Box: (random.Next(60, 270 - width), random.Next(100, 174 - height), width, height),
                Growth: TimeSpan.FromSeconds(random.Next(60, 241)));
        }

        for (int i = 0; i < 15; i++)
        {
            yield return new Lump(
                Seed: random.Next(),
                CenterX: random.Next(70, 260),
                CenterY: random.Next(110, 168),
                Radius: random.Next(5, 15),
                Luma: Lumas[random.Next(Lumas.Length)],
                Growth: TimeSpan.FromSeconds(random.Next(0, 181)));
        }

        for (int i = 0; i < 10; i++)
        {
            int dx = random.Next(6, 26) * (random.Next(2) == 0 ? -1 : 1);
            int dy = random.Next(-10, 11);
            yield return new MovedPart(dx, dy, Gone: false);
        }

        yield return new MovedPart(0, 0, Gone: true);
    }

    /// <summary>
    /// T-163: black filament in the chamber light as luma (fixed up front, not tuned to the detector): from below the dark
    /// corners of the sheet to its level at the front (~50–90 in <see cref="DarkScene"/>).
    /// </summary>
    private static readonly double[] DarkLumas = [20, 30, 45, 60];

    /// <summary>T-163: the share of a strand's pixels with a highlight: matte or glossy black filament.</summary>
    private static readonly double[] Glints = [0, 0.05];

    /// <summary>
    /// T-163: the same kinds, sizes and places as <see cref="Failures"/>, in black filament on <paramref name="backdrop"/>
    /// (one of <see cref="DarkScene.Backdrops"/>, the part black too): spaghetti matte or glossy, lumps, the black part
    /// moved or gone.
    /// </summary>
    public static IEnumerable<SyntheticFailure> DarkFailures(int seed, SyntheticBackdrop backdrop)
    {
        Random random = new(seed);
        for (int i = 0; i < 30; i++)
        {
            int width = random.Next(35, 81);
            int height = random.Next(20, 41);
            yield return new Spaghetti(
                Seed: random.Next(),
                Strands: random.Next(15, 41),
                Luma: DarkLumas[random.Next(DarkLumas.Length)],
                Alpha: 0.5 + (random.NextDouble() * 0.5),
                Thick: random.Next(2) == 0,
                Box: (random.Next(60, 270 - width), random.Next(100, 174 - height), width, height),
                Growth: TimeSpan.FromSeconds(random.Next(60, 241)),
                Glint: Glints[random.Next(Glints.Length)]);
        }

        for (int i = 0; i < 15; i++)
        {
            yield return new Lump(
                Seed: random.Next(),
                CenterX: random.Next(70, 260),
                CenterY: random.Next(110, 168),
                Radius: random.Next(5, 15),
                Luma: DarkLumas[random.Next(DarkLumas.Length)],
                Growth: TimeSpan.FromSeconds(random.Next(0, 181)));
        }

        for (int i = 0; i < 10; i++)
        {
            int dx = random.Next(6, 26) * (random.Next(2) == 0 ? -1 : 1);
            int dy = random.Next(-10, 11);
            yield return new MovedPart(dx, dy, Gone: false, backdrop);
        }

        yield return new MovedPart(0, 0, Gone: true, backdrop);
    }
}
