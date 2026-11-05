using LayerPeek.Tests.Detection;

namespace LayerPeek.Detection.Tests;

/// <summary>
/// The synthetic sets of T-167, fixed by their seeds. The distributions were chosen before the detector was tuned (as in
/// T-166): plausible Core One prints at 320×180 with the geometry of T-167 phase 1, not what the detector finds.
/// </summary>
internal static class AirPrintingSets
{
    /// <summary>Filament colours as luma on the black sheet (dark filament is a known limitation of every detector, T-163).</summary>
    private static readonly double[] Lumas = [225, 200, 170, 140, 115];

    /// <summary>The Z rates the targets are stated for (mm/min): slow (MMU, big layers), job 354, a fast small part.</summary>
    public static readonly double[] Rates = [0.1, 0.4, 1.0];

    /// <summary>
    /// <paramref name="count"/> failures: one part of 25–110 px (narrower and wider than the head) at a random place on the
    /// bed, any colour, layers of 0.1–0.3 mm, Z hops of 0.2 or 1.5 mm, rates of 0.1 / 0.4 / 1 mm/min in turn, the bed moving
    /// 0.8–1.2× the model; all growth stops at 6–20 mm (a tenth of them: the part is gone as well), the run ends 8 mm later.
    /// A quarter are MMU prints (tool changes and a wipe tower that stops too: nothing is extruded any more).
    /// </summary>
    public static IEnumerable<(string Name, SyntheticGrowingPrint Print)> Failures(int seed, TimeSpan interval, int count = 24)
    {
        Random random = new(seed);
        for (int i = 0; i < count; i++)
        {
            double rate = Rates[i % Rates.Length];
            SyntheticPart part = RandomPart(random);
            bool mmu = random.Next(4) == 0;
            double failure = Math.Round(6 + (random.NextDouble() * 14), 1);
            SyntheticGrowingPrint print = Print(random, interval, rate) with
            {
                Parts = mmu ? [part, WipeTower(random, part)] : [part],
                ToolChangeEvery = mmu ? 1 + random.Next(3) : 0,
                FailureHeight = failure,
                FinalHeight = failure + 8,
                PartsGone = random.Next(10) == 0,
            };
            yield return ($"{Describe(print)}, fails at {failure:0.0} mm{(print.PartsGone ? " (gone)" : "")}", print);
        }
    }

    /// <summary>
    /// Successful prints (~9 h of printing per seed): a single part at every rate, MMU prints with long tool changes and a
    /// wipe tower, a part with a step (the wider bottom ends, a column narrower or wider than the head goes on), several parts
    /// of different heights, Z hops of 1.5 mm, a slowly changing light.
    /// </summary>
    public static IEnumerable<(string Name, SyntheticGrowingPrint Print)> Successful(int seed, TimeSpan interval)
    {
        Random random = new(seed + 1000);
        foreach (double rate in Rates)
        {
            SyntheticGrowingPrint single = Print(random, interval, rate) with { Parts = [RandomPart(random)], FinalHeight = 6 + (rate * 60) };
            yield return ($"single, {Describe(single)}", single);
        }

        for (int i = 0; i < 2; i++)
        {
            SyntheticPart part = RandomPart(random);
            SyntheticGrowingPrint mmu = Print(random, interval, 0.4) with
            {
                Parts = [part, WipeTower(random, part)],
                ToolChangeEvery = 0.6 + random.Next(3),
                ToolChange = TimeSpan.FromMinutes(1 + random.Next(4)),
                FinalHeight = 25,
            };
            yield return ($"MMU, {Describe(mmu)}", mmu);
        }

        foreach (int columnLength in (int[])[30, 75])
        {
            // A wide bottom ends at 8–14 mm, a column on it (narrower or wider than the head) goes on.
            SyntheticPart bottom = RandomPart(random) with { Length = 100 + random.Next(15) };
            double step = 8 + random.Next(7);
            bottom = bottom with { TopHeight = step, FrontX = Math.Min(bottom.FrontX, 225 - bottom.Length) };
            SyntheticPart column = bottom with
            {
                FrontX = bottom.FrontX + random.Next(5, (int)(bottom.Length - columnLength)),
                FrontY = bottom.FrontY - 3,
                Length = columnLength,
                BaseHeight = step,
                TopHeight = double.PositiveInfinity,
            };
            SyntheticGrowingPrint stepped = Print(random, interval, 0.4 + (random.NextDouble() * 0.6)) with { Parts = [column, bottom], FinalHeight = 30 };
            yield return ($"step at {step} mm, column {columnLength} px, {Describe(stepped)}", stepped);
        }

        {
            // Three parts that end at different heights; the tallest goes on.
            SyntheticPart[] parts =
            [
                RandomPart(random) with { FrontX = 75, Length = 45, TopHeight = 9 + random.Next(5) },
                RandomPart(random) with { FrontX = 135, Length = 40, TopHeight = double.PositiveInfinity },
                RandomPart(random) with { FrontX = 190, Length = 40, TopHeight = 15 + random.Next(6) },
            ];
            SyntheticGrowingPrint several = Print(random, interval, 0.4) with { Parts = [parts[1], parts[0], parts[2]], FinalHeight = 30 };
            yield return ($"three parts, {Describe(several)}", several);
        }

        SyntheticGrowingPrint hops = Print(random, interval, 0.6) with { Parts = [RandomPart(random)], ZHop = 1.5, FinalHeight = 30 };
        yield return ($"Z hops 1.5 mm, {Describe(hops)}", hops);

        SyntheticGrowingPrint light = Print(random, interval, 0.3) with
        {
            Parts = [RandomPart(random)],
            FinalHeight = 25,
            Brightness = static t => 0.75 + (0.25 * Math.Cos(t.TotalMinutes / 20)),
        };
        yield return ($"changing light, {Describe(light)}", light);
    }

    private static SyntheticGrowingPrint Print(Random random, TimeSpan interval, double rate) => new()
    {
        Interval = interval,
        Rate = rate,
        Layer = (double[])[0.1, 0.15, 0.2, 0.25, 0.3] is var layers ? layers[random.Next(layers.Length)] : 0.2,
        ZHop = random.Next(2) == 0 ? 0.2 : 1.5,
        KScale = 0.8 + (random.NextDouble() * 0.4),
        Noise = 1.5 + (random.NextDouble() * 2.5),
        StartSequence = random.Next(2) == 0,
        Seed = random.Next(),
    };

    private static SyntheticPart RandomPart(Random random)
    {
        double length = random.Next(25, 111);
        double face = Lumas[random.Next(Lumas.Length)];
        return new SyntheticPart
        {
            Length = length,
            FrontX = 70 + (random.NextDouble() * (160 - length)),
            FrontY = 85 + (random.NextDouble() * 45),
            Slope = -0.2 - (random.NextDouble() * 0.4),
            Depth = 2 + random.Next(11),
            FaceLuma = face,
            TopLuma = face * (0.6 + (random.NextDouble() * 0.3)),
            RimLuma = Math.Min(250, face + 25),
        };
    }

    /// <summary>A wipe tower (12–18 px, light) beside <paramref name="part"/>, growing with it.</summary>
    private static SyntheticPart WipeTower(Random random, SyntheticPart part) => new()
    {
        Length = 12 + random.Next(7),
        FrontX = part.FrontX + part.Length < 200 ? 230 : 70,
        FrontY = 80 + random.Next(20),
        Slope = -0.4,
        Depth = 6,
        FaceLuma = 190,
        TopLuma = 140,
        RimLuma = 220,
        IsWipeTower = true,
    };

    private static string Describe(SyntheticGrowingPrint print) => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"{print.Rate:0.0#} mm/min, layer {print.Layer} mm, hop {print.ZHop} mm, k×{print.KScale:0.00}, part {print.Parts[0].Length:0} px luma {print.Parts[0].FaceLuma:0}{(print.ToolChangeEvery > 0 ? $", tool change every {print.ToolChangeEvery} mm for {print.ToolChange.TotalMinutes:0} min" : "")}");
}
