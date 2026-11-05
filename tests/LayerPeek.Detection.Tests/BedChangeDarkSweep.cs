using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using LayerPeek.Core.Detection;
using LayerPeek.Tests.Detection;

namespace LayerPeek.Detection.Tests;

/// <summary>
/// T-163: the bed-change detector on black filament (<see cref="SyntheticSets.DarkFailures"/> on the sheet looks of
/// <see cref="DarkScene.Sheets"/>, user decisions 2026-11-04: synthetic data only; 2026-11-05: the sheet colours and an
/// experimental "dark filament" mode). The whole sweeps (12 seeds, both regions, 2 s and 5 s, with and without the mode,
/// the contrast of the sets; the light filament sets in the mode) take minutes and run only with
/// <c>LAYERPEEK_DETECTION_SWEEP=1</c> (tables to the test output and, with <c>LAYERPEEK_DETECTION_DARK_SWEEP_FILE</c> /
/// <c>LAYERPEEK_DETECTION_DARK_MODE_LIGHT_SWEEP_FILE=&lt;path&gt;</c>, to files); a regression of their numbers and the
/// successful black prints run every time.
/// </summary>
public sealed partial class BedChangeDarkSweepTests
{
    private static readonly int[] Seeds = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];

    private static readonly float[] Thresholds = [0.001f, 0.0015f, 0.002f, 0.0025f, BedChangeSeedSweep.DefaultThreshold];

    /// <summary>
    /// Dark sets (seed, interval, plain sheet or not, dark filament mode) and how many of their 56 failures the detector
    /// found at the default sensitivity when T-163 measured it: black filament on a black sheet stays below the 9 in 10 of
    /// the other sets (the task notes), this keeps it from getting worse, with and without the dark filament mode.
    /// </summary>
    public static TheoryData<int, int, bool, bool, int> RegressionSets => new()
    {
        { 1, 2, true, false, 46 },
        { 3, 5, false, false, 44 },
        { 1, 2, true, true, 48 },
        { 3, 5, false, true, 45 },
    };

    [Theory]
    [MemberData(nameof(RegressionSets))]
    public void BlackFilamentOnABlackSheetIsFoundAsOften(int seed, int interval, bool plain, bool darkFilament, int found)
    {
        SyntheticBackdrop backdrop = plain ? DarkScene.DarkPlain : DarkScene.Fixture;
        BedChangeSeedSweep.SetResult result = BedChangeSeedSweep.Evaluate(DetectionRegion.CoreOneBed, interval, seed, [BedChangeSeedSweep.DefaultThreshold], backdrop: backdrop, darkFilament: darkFilament);
        string summary = $"{backdrop.Name}{(darkFilament ? ", dark filament mode" : "")}: {BedChangeSeedSweep.Describe(result)}";
        TestContext.Current.TestOutputHelper?.WriteLine(summary);
        Assert.True(result.FalseAlarms[0] == 0, summary);
        Assert.True(result.Found[0] >= found, summary);
    }

    public static TheoryData<int, bool, bool> SuccessfulPrints => new() { { 2, true, false }, { 5, false, false }, { 2, false, true }, { 5, true, true } };

    /// <summary>A black part printed on a black sheet, with the light and noise courses of the other successful prints.</summary>
    [Theory]
    [MemberData(nameof(SuccessfulPrints))]
    public void ASuccessfulBlackPrintRaisesNoAlarm(int interval, bool plain, bool darkFilament)
    {
        List<string> failures = [];
        string text = BedChangeSeedSweep.SuccessfulPrints(seeds: 2, [BedChangeSeedSweep.DefaultThreshold], failures, plain ? DarkScene.DarkPlain : DarkScene.Fixture, [interval], darkFilament);
        TestContext.Current.TestOutputHelper?.WriteLine(text);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Every sheet look (<see cref="DarkScene.Sheets"/>) with and without the dark filament mode, both regions, 2 s and 5 s:
    /// detection per seed and the false alarms, the successful prints; the contrast of the sets first.
    /// </summary>
    [Fact]
    public void EverySeedOfTheDarkFailures()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LAYERPEEK_DETECTION_SWEEP") == "1", "Set LAYERPEEK_DETECTION_SWEEP=1 to run the multi-seed sweep (minutes).");
        StringBuilder output = new(Contrast());
        List<string> failures = [];
        foreach (bool darkFilament in (bool[])[false, true])
        {
            foreach (SyntheticBackdrop backdrop in DarkScene.Sheets)
            {
                string name = $"{backdrop.Name}{(darkFilament ? ", dark filament mode" : "")}";
                foreach (DetectionRegion region in (DetectionRegion[])[DetectionRegion.CoreOneBed, DetectionRegion.Full])
                {
                    foreach (int interval in (int[])[2, 5])
                    {
                        List<BedChangeSeedSweep.SetResult> results = [.. Seeds.Select(seed => BedChangeSeedSweep.Evaluate(region, interval, seed, Thresholds, backdrop: backdrop, darkFilament: darkFilament))];
                        output.Append(CultureInfo.InvariantCulture, $"\n# {name}");
                        output.Append(BedChangeSeedSweep.Table(region, interval, results, Thresholds));
                        output.Append(ByKindAndLuma(results, backdrop));
                        int last = Thresholds.Length - 1;
                        double falseAlarmsPer10Hours = results.Sum(r => r.FalseAlarms[last]) / results.Sum(r => r.Watched.TotalHours) * 10;
                        if (falseAlarmsPer10Hours > 1)
                        {
                            failures.Add(string.Create(CultureInfo.InvariantCulture, $"{name}, {BedChangeSeedSweep.Name(region)}, {interval} s: {falseAlarmsPer10Hours:0.00} false alarms per 10 h"));
                        }
                    }
                }

                output.Append(BedChangeSeedSweep.SuccessfulPrints(seeds: 10, Thresholds, failures, backdrop, darkFilament: darkFilament));
            }
        }

        Write(output.ToString(), "LAYERPEEK_DETECTION_DARK_SWEEP_FILE");

        // Accuracy below 9 in 10 is the measured limitation (task notes); false alarms are not allowed to come with it.
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// The T-166 sweep of the light filament (<see cref="SyntheticSets.Failures"/>, 30 seeds) in the dark filament mode: the
    /// mode must not find less, nor raise more false alarms than allowed, nor alarm on the successful prints.
    /// </summary>
    [Fact]
    public void TheLightFilamentSetsInTheDarkFilamentMode()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LAYERPEEK_DETECTION_SWEEP") == "1", "Set LAYERPEEK_DETECTION_SWEEP=1 to run the multi-seed sweep (minutes).");
        StringBuilder output = new("# light filament, dark filament mode\n");
        List<string> failures = [];
        foreach (DetectionRegion region in (DetectionRegion[])[DetectionRegion.CoreOneBed, DetectionRegion.Full])
        {
            foreach (int interval in (int[])[2, 5])
            {
                List<BedChangeSeedSweep.SetResult> results = [.. BedChangeSeedSweepTests.Seeds.Select(seed => BedChangeSeedSweep.Evaluate(region, interval, seed, Thresholds, darkFilament: true))];
                output.Append(BedChangeSeedSweep.Table(region, interval, results, Thresholds));
                int last = Thresholds.Length - 1;
                double falseAlarmsPer10Hours = results.Sum(r => r.FalseAlarms[last]) / results.Sum(r => r.Watched.TotalHours) * 10;
                if (falseAlarmsPer10Hours > 1)
                {
                    failures.Add(string.Create(CultureInfo.InvariantCulture, $"{BedChangeSeedSweep.Name(region)}, {interval} s: {falseAlarmsPer10Hours:0.00} false alarms per 10 h"));
                }

                failures.AddRange(results
                    .Where(r => r.Found[last] < BedChangeSeedSweep.RequiredShare * (r.Total - r.FalseAlarms[last]))
                    .Select(r => $"{BedChangeSeedSweep.Name(region)}, {interval} s: {BedChangeSeedSweep.Describe(r)}"));
            }
        }

        output.Append(BedChangeSeedSweep.SuccessfulPrints(seeds: 10, Thresholds, failures, darkFilament: true));
        Write(output.ToString(), "LAYERPEEK_DETECTION_DARK_MODE_LIGHT_SWEEP_FILE");
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static void Write(string text, string fileVariable)
    {
        TestContext.Current.TestOutputHelper?.WriteLine(text);
        if (Environment.GetEnvironmentVariable(fileVariable) is { Length: > 0 } file)
        {
            File.WriteAllText(file, text);
        }
    }

    /// <summary>At the default threshold: found / total per kind, and per luma of the filament (spaghetti and lumps).</summary>
    private static string ByKindAndLuma(List<BedChangeSeedSweep.SetResult> results, SyntheticBackdrop backdrop)
    {
        StringBuilder text = new("per kind: ");
        text.AppendJoin(", ", results.SelectMany(static r => r.Kinds).GroupBy(static k => k.Key).OrderBy(static g => g.Key, StringComparer.Ordinal)
            .Select(static g => string.Create(CultureInfo.InvariantCulture, $"{g.Key} {g.Sum(static k => k.Value.Found)}/{g.Sum(static k => k.Value.Total)}")));
        Dictionary<double, int> totals = [];
        foreach (SyntheticFailure failure in results.SelectMany(r => SyntheticSets.DarkFailures(r.Seed, backdrop)))
        {
            if (LumaOf(failure) is { } luma)
            {
                totals[luma] = totals.GetValueOrDefault(luma) + 1;
            }
        }

        Dictionary<double, int> missed = [];
        foreach (Match match in results.SelectMany(static r => r.Notes).Where(static n => n.StartsWith("missed", StringComparison.Ordinal)).Select(static n => LumaPattern().Match(n)).Where(static m => m.Success))
        {
            double luma = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            missed[luma] = missed.GetValueOrDefault(luma) + 1;
        }

        text.Append("; per luma (spaghetti and lumps): ")
            .AppendJoin(", ", totals.OrderBy(static t => t.Key).Select(t => string.Create(CultureInfo.InvariantCulture, $"{t.Key:0} {t.Value - missed.GetValueOrDefault(t.Key)}/{t.Value}")))
            .Append('\n');
        return text.ToString();
    }

    private static double? LumaOf(SyntheticFailure failure) => failure switch
    {
        Spaghetti spaghetti => spaghetti.Luma,
        Lump lump => lump.Luma,
        _ => null,
    };

    [GeneratedRegex(@"luma (\d+)")]
    private static partial Regex LumaPattern();

    /// <summary>
    /// The contrast of the sets in the Core One bed region on the scene without noise and head (the median of the key
    /// frames), each failure at its full size: the scene's own levels; per kind the median luma difference of the pixels a
    /// failure changed, the share of them changed by more than 25 (T-152/T-155) and 14 levels (T-166); and the share of the
    /// failures a perfect detector of the default rule would find (3×3 mean outside the ±1/±2 px range of the scene by
    /// more than <see cref="BedChangeOptions.ChangeLevel"/>, on at least the default threshold's area), with and without
    /// the shift tolerance. Seeds as in the sweep.
    /// </summary>
    private static string Contrast()
    {
        const int width = SyntheticPrint.Width;
        const int height = SyntheticPrint.Height;
        DetectionPixelRegion region = DetectionRegion.CoreOneBed.ToPixels(width, height);
        int level = BedChangeOptions.Default.ChangeLevel;
        int area = (int)Math.Ceiling(BedChangeSeedSweep.DefaultThreshold * BedChangeOptions.Default.ScoreArea * width * height);
        StringBuilder text = new(string.Create(CultureInfo.InvariantCulture, $"## contrast (Core One bed region, no noise, failures at full size; perfect detector: {level} levels, {area} px)\n"));
        foreach (SyntheticBackdrop? backdrop in (SyntheticBackdrop?[])[null, .. DarkScene.Sheets])
        {
            byte[] scene = backdrop?.Median ?? SyntheticScene.Median;
            bool[] part = backdrop?.Part ?? SyntheticScene.Part;
            List<byte> sheet = [];
            for (int y = region.Y; y < region.Y + region.Height; y++)
            {
                for (int x = region.X; x < region.X + region.Width; x++)
                {
                    if (!part[(y * width) + x])
                    {
                        sheet.Add(scene[(y * width) + x]);
                    }
                }
            }

            sheet.Sort();
            text.Append(CultureInfo.InvariantCulture, $"{backdrop?.Name ?? "fixture (silver part, light filament)"}: scene without the part p10 / p50 / p90 {sheet[sheet.Count / 10]} / {sheet[sheet.Count / 2]} / {sheet[sheet.Count * 9 / 10]}\n");
            (byte[] low, byte[] high) = Range(scene);
            Dictionary<string, ContrastTally> kinds = [];
            byte[] image = new byte[scene.Length];
            foreach (int seed in Seeds)
            {
                foreach (SyntheticFailure failure in backdrop is null ? SyntheticSets.Failures(seed) : SyntheticSets.DarkFailures(seed, backdrop))
                {
                    scene.CopyTo(image, 0);
                    failure.Apply(image, TimeSpan.FromMinutes(10));
                    string kind = BedChangeSeedSweep.Kind(failure);
                    if (!kinds.TryGetValue(kind, out ContrastTally? tally))
                    {
                        kinds[kind] = tally = new ContrastTally();
                    }

                    int changed = 0;
                    int changedWithoutShift = 0;
                    for (int y = region.Y + 1; y < region.Y + region.Height - 1; y++)
                    {
                        for (int x = region.X + 1; x < region.X + region.Width - 1; x++)
                        {
                            int i = (y * width) + x;
                            int difference = Math.Abs(image[i] - scene[i]);
                            if (difference > 0)
                            {
                                tally.Differences.Add(difference);
                                tally.Over25 += difference > 25 ? 1 : 0;
                                tally.Over14 += difference > 14 ? 1 : 0;
                            }

                            (int outside, int absolute) = Neighbourhood(image, scene, low, high, i);
                            changed += outside > 9 * level ? 1 : 0;
                            changedWithoutShift += absolute > 9 * level ? 1 : 0;
                        }
                    }

                    tally.Ideal += changed >= area ? 1 : 0;
                    tally.IdealWithoutShift += changedWithoutShift >= area ? 1 : 0;
                    tally.Total++;
                }
            }

            foreach ((string kind, ContrastTally tally) in kinds.OrderBy(static k => k.Key, StringComparer.Ordinal))
            {
                tally.Differences.Sort();
                int pixels = Math.Max(1, tally.Differences.Count);
                text.Append(CultureInfo.InvariantCulture, $"  {kind}: median difference {(tally.Differences.Count > 0 ? tally.Differences[tally.Differences.Count / 2] : 0)} levels, changed > 25: {(double)tally.Over25 / pixels:P0}, > 14: {(double)tally.Over14 / pixels:P0}; perfect detector {tally.Ideal}/{tally.Total} ({(double)tally.Ideal / tally.Total:P0}), without the shift tolerance {tally.IdealWithoutShift}/{tally.Total} ({(double)tally.IdealWithoutShift / tally.Total:P0})\n");
            }
        }

        return text.ToString();
    }

    private sealed class ContrastTally
    {
        public List<int> Differences { get; } = [];

        public int Over25 { get; set; }

        public int Over14 { get; set; }

        public int Ideal { get; set; }

        public int IdealWithoutShift { get; set; }

        public int Total { get; set; }
    }

    /// <summary>The lowest and highest level within the detector's shift tolerance (±<see cref="BedChangeOptions.ShiftX"/>, ±<see cref="BedChangeOptions.ShiftY"/> px).</summary>
    private static (byte[] Low, byte[] High) Range(byte[] scene)
    {
        const int width = SyntheticPrint.Width;
        const int height = SyntheticPrint.Height;
        byte[] low = new byte[scene.Length];
        byte[] high = new byte[scene.Length];
        BedChangeOptions options = BedChangeOptions.Default;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte min = byte.MaxValue;
                byte max = 0;
                for (int dy = -options.ShiftY; dy <= options.ShiftY; dy++)
                {
                    for (int dx = -options.ShiftX; dx <= options.ShiftX; dx++)
                    {
                        byte value = scene[(Math.Clamp(y + dy, 0, height - 1) * width) + Math.Clamp(x + dx, 0, width - 1)];
                        min = Math.Min(min, value);
                        max = Math.Max(max, value);
                    }
                }

                low[(y * width) + x] = min;
                high[(y * width) + x] = max;
            }
        }

        return (low, high);
    }

    /// <summary>The 3×3 sums around <paramref name="i"/> of the distance outside the range and of the absolute difference.</summary>
    private static (int Outside, int Absolute) Neighbourhood(byte[] image, byte[] scene, byte[] low, byte[] high, int i)
    {
        int outside = 0;
        int absolute = 0;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int j = i + (dy * SyntheticPrint.Width) + dx;
                outside += Math.Max(0, Math.Max(image[j] - high[j], low[j] - image[j]));
                absolute += Math.Abs(image[j] - scene[j]);
            }
        }

        return (outside, absolute);
    }
}
