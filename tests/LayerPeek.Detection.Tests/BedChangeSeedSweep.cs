using System.Globalization;
using System.Text;

using LayerPeek.Core.Detection;
using LayerPeek.Tests.Detection;

namespace LayerPeek.Detection.Tests;

/// <summary>
/// T-166: the accuracy of the bed-change detector over many independent draws of the synthetic failure distribution
/// (<see cref="SyntheticSets.Failures"/> with other seeds; the distribution itself is unchanged), at the default
/// sensitivity and at more sensitive thresholds at once. The whole sweep takes minutes, so it runs only with
/// <c>LAYERPEEK_DETECTION_SWEEP=1</c> (its table goes to the test output and, with
/// <c>LAYERPEEK_DETECTION_SWEEP_FILE=&lt;path&gt;</c>, to a file); two of its sets run every time as a regression.
/// </summary>
public sealed class BedChangeSeedSweepTests
{
    /// <summary>
    /// Thirty sets: the committed one (7), the four of the T-155 review, seven more used for the T-166 tuning and eighteen
    /// held out of it (1–20 without 7 and 11).
    /// </summary>
    private static readonly int[] Seeds = [7, 11, 23, 31, 47, 101, 202, 303, 404, 505, 606, 707, 1, 2, 3, 4, 5, 6, 8, 9, 10, 12, 13, 14, 15, 16, 17, 18, 19, 20];

    /// <summary>The thresholds of the table: the most sensitive end, steps, the default one last.</summary>
    private static readonly float[] Thresholds = [0.001f, 0.0015f, 0.002f, 0.0025f, BedChangeSeedSweep.DefaultThreshold];

    public static TheoryData<int, int> RegressionSets => new() { { 11, 2 }, { 23, 5 } };

    /// <summary>
    /// Two sets of the T-155 review that found only 48 of 56 (86 %) in the Core One bed region, one at each end of the
    /// sampling interval; every run (the whole sweep is opt-in).
    /// </summary>
    [Theory]
    [MemberData(nameof(RegressionSets))]
    public void OtherSeedsAlsoFindNineInTen(int seed, int interval)
    {
        BedChangeSeedSweep.SetResult result = BedChangeSeedSweep.Evaluate(DetectionRegion.CoreOneBed, interval, seed, [BedChangeSeedSweep.DefaultThreshold]);
        string summary = BedChangeSeedSweep.Describe(result);
        TestContext.Current.TestOutputHelper?.WriteLine(summary);
        Assert.True(result.FalseAlarms[0] == 0, summary);
        Assert.True(result.Found[0] >= BedChangeSeedSweep.RequiredShare * result.Total, summary);
    }

    [Fact]
    public void EverySeedOfTheSyntheticFailures()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LAYERPEEK_DETECTION_SWEEP") == "1", "Set LAYERPEEK_DETECTION_SWEEP=1 to run the multi-seed sweep (minutes).");
        StringBuilder output = new();
        List<string> failures = [];
        foreach (DetectionRegion region in (DetectionRegion[])[DetectionRegion.CoreOneBed, DetectionRegion.Full])
        {
            foreach (int interval in (int[])[2, 5])
            {
                List<BedChangeSeedSweep.SetResult> results = [.. Seeds.Select(seed => BedChangeSeedSweep.Evaluate(region, interval, seed, Thresholds))];
                output.Append(BedChangeSeedSweep.Table(region, interval, results, Thresholds));
                int last = Thresholds.Length - 1;
                double falseAlarmsPer10Hours = results.Sum(r => r.FalseAlarms[last]) / results.Sum(r => r.Watched.TotalHours) * 10;
                if (falseAlarmsPer10Hours > 1)
                {
                    failures.Add(string.Create(CultureInfo.InvariantCulture, $"{BedChangeSeedSweep.Name(region)}, {interval} s: {falseAlarmsPer10Hours:0.00} false alarms per 10 h"));
                }

                // A run whose false alarm came first cannot count as found or missed: it leaves the share; the false alarms
                // are checked above (the moving head in the whole frame, see the T-166 notes).
                failures.AddRange(results
                    .Where(r => r.Found[last] < BedChangeSeedSweep.RequiredShare * (r.Total - r.FalseAlarms[last]))
                    .Select(r => $"{BedChangeSeedSweep.Name(region)}, {interval} s: {BedChangeSeedSweep.Describe(r)}"));
            }
        }

        output.Append(BedChangeSeedSweep.SuccessfulPrints(seeds: 10, Thresholds, failures));
        string text = output.ToString();
        TestContext.Current.TestOutputHelper?.WriteLine(text);
        if (Environment.GetEnvironmentVariable("LAYERPEEK_DETECTION_SWEEP_FILE") is { Length: > 0 } file)
        {
            File.WriteAllText(file, text);
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}

internal static class BedChangeSeedSweep
{
    /// <summary>User decision 2026-11-04 (T-155).</summary>
    public const double RequiredShare = 0.9;

    /// <summary>The timing every synthetic print here has (failure start, duration).</summary>
    private static readonly SyntheticPrint Timing = new() { Interval = TimeSpan.FromSeconds(2) };

    public static float DefaultThreshold => BedChangeDetector.Registration.Thresholds.ThresholdFor(DetectorSettings.DefaultSensitivity);

    /// <summary>The successful prints of <see cref="BedChangeAccuracy"/> (same noise and light courses).</summary>
    private static readonly (string Name, double Noise, Func<TimeSpan, double>? Brightness)[] Profiles =
    [
        ("plain", 2.5, null),
        ("strong noise", 5, null),
        ("+25 %", 2.5, static t => t < TimeSpan.FromMinutes(6) ? 1 : 1.25),
        ("-25 %", 2.5, static t => t < TimeSpan.FromMinutes(6) ? 1 : 0.75),
        ("drift ±20 %", 2.5, static t => 1 + (0.2 * Math.Sin(t.TotalMinutes / 10 * 2 * Math.PI))),
        ("flicker ±8 %", 2.5, static t => 1 + (0.08 * Math.Sin(t.TotalSeconds * 12.9898))),
    ];

    /// <summary>
    /// One set at several thresholds. Per threshold: the failures found and the false alarms (an alarm before the failure
    /// started; that run then does not count as found). At the last threshold: the delays from the start of the failure to
    /// the alarm, and what was missed (with its highest score) or alarmed too early.
    /// </summary>
    public sealed record SetResult(int Seed, int Interval, int Total, int[] Found, int[] FalseAlarms, double[] Delays, TimeSpan Watched, string[] Notes);

    /// <param name="create">The detector (null: the default one).</param>
    public static SetResult Evaluate(DetectionRegion region, int interval, int seed, float[] thresholds, Func<IPrintFailureDetector>? create = null)
    {
        SyntheticFailure[] failures = [.. SyntheticSets.Failures(seed)];
        (int[] Found, int[] False, double? Delay, string? Note)[] runs = new (int[], int[], double?, string?)[failures.Length];
        Parallel.For(0, failures.Length, index =>
        {
            SyntheticFailure failure = failures[index];
            // Independent noise and timing per set (the committed test of seed 7 uses 100 + index).
            SyntheticPrint print = new() { Interval = TimeSpan.FromSeconds(interval), Region = region, Failure = failure, Seed = (seed * 1000) + index };
            DetectorRun run = DetectorHarness.Run(create?.Invoke() ?? new BedChangeDetector(), print.Samples(), region, thresholds);
            int[] found = new int[thresholds.Length];
            int[] early = new int[thresholds.Length];
            for (int t = 0; t < thresholds.Length; t++)
            {
                if (run.Alarms[t] is { } alarm)
                {
                    (alarm < print.FailureStart ? early : found)[t] = 1;
                }
            }

            TimeSpan? last = run.Alarms[^1];
            runs[index] = (found, early, found[^1] == 1 ? (last!.Value - print.FailureStart).TotalSeconds : null, last switch
            {
                null => string.Create(CultureInfo.InvariantCulture, $"missed {failure.Name}, max score {run.MaxScore(print.FailureStart, TimeSpan.MaxValue):P2}"),
                { } at when at < print.FailureStart => string.Create(CultureInfo.InvariantCulture, $"false alarm at {at:mm\\:ss} before {failure.Name}"),
                _ => null,
            });
        });

        return new SetResult(
            seed,
            interval,
            failures.Length,
            [.. Enumerable.Range(0, thresholds.Length).Select(t => runs.Sum(r => r.Found[t]))],
            [.. Enumerable.Range(0, thresholds.Length).Select(t => runs.Sum(r => r.False[t]))],
            [.. runs.Where(static r => r.Delay is not null).Select(static r => r.Delay!.Value).Order()],
            // Watched without an alarm: from the end of the warm-up to the start of the failure.
            (Timing.FailureStart - BedChangeOptions.Default.WarmUp) * failures.Length,
            [.. runs.Where(static r => r.Note is not null).Select(static r => r.Note!).Order(StringComparer.Ordinal)]);
    }

    public static string Describe(SetResult r) => string.Create(
        CultureInfo.InvariantCulture,
        $"seed {r.Seed,3}, {r.Interval} s: found {string.Join(" / ", r.Found.Select(f => $"{f}/{r.Total}"))}, false alarms {string.Join("/", r.FalseAlarms)} in {r.Watched.TotalHours:0.0} h, alarm {Median(r.Delays):0} s (median) / {(r.Delays.Length > 0 ? r.Delays[^1] : 0):0} s (max) after the start{string.Concat(r.Notes.Select(static n => "; " + n))}");

    public static string Table(DetectionRegion region, int interval, IReadOnlyList<SetResult> results, float[] thresholds)
    {
        StringBuilder text = new();
        text.Append(CultureInfo.InvariantCulture, $"\n## {Name(region)}, {interval} s; per threshold {string.Join(" / ", thresholds.Select(static t => t.ToString("P2", CultureInfo.InvariantCulture)))}\n");
        foreach (SetResult r in results)
        {
            text.Append(Describe(r)).Append('\n');
        }

        double[] delays = [.. results.SelectMany(static r => r.Delays).Order()];
        for (int t = 0; t < thresholds.Length; t++)
        {
            double[] shares = [.. results.Select(r => (double)r.Found[t] / r.Total).Order()];
            text.Append(CultureInfo.InvariantCulture, $"threshold {thresholds[t]:P2}: per seed min {shares[0]:P1}, median {Median(shares):P1}, total {results.Sum(r => r.Found[t])}/{results.Sum(r => r.Total)}, false alarms {results.Sum(r => r.FalseAlarms[t])} in {results.Sum(r => r.Watched.TotalHours):0.0} h\n");
        }

        if (delays.Length > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"alarm after the start (last threshold): median {Median(delays):0} s, 90 % within {delays[(int)(delays.Length * 0.9)]:0} s, max {delays[^1]:0} s\n");
        }
        return text.ToString();
    }

    /// <summary>The successful prints of every profile with <paramref name="seeds"/> seeds, in both regions and at both intervals.</summary>
    public static string SuccessfulPrints(int seeds, float[] thresholds, List<string> failures)
    {
        StringBuilder text = new("\n## successful prints\n");
        foreach (DetectionRegion region in (DetectionRegion[])[DetectionRegion.CoreOneBed, DetectionRegion.Full])
        {
            foreach (int interval in (int[])[2, 5])
            {
                (int Profile, float Max, bool[] Alarms)[] runs = new (int, float, bool[])[Profiles.Length * seeds];
                Parallel.For(0, runs.Length, i =>
                {
                    (string _, double noise, Func<TimeSpan, double>? brightness) = Profiles[i / seeds];
                    SyntheticPrint print = new() { Interval = TimeSpan.FromSeconds(interval), Region = region, Noise = noise, Brightness = brightness, Seed = 500 + (i % seeds) };
                    DetectorRun run = DetectorHarness.Run(new BedChangeDetector(), print.Samples(), region, thresholds);
                    runs[i] = (i / seeds, run.MaxScore(), [.. run.Alarms.Select(static a => a is not null)]);
                });

                TimeSpan watched = (Timing.Duration - BedChangeOptions.Default.WarmUp) * runs.Length;
                int[] alarms = [.. Enumerable.Range(0, thresholds.Length).Select(t => runs.Count(r => r.Alarms[t]))];
                string line = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Name(region)}, {interval} s, {watched.TotalHours:0.0} h: alarms {string.Join("/", alarms)}; max score {string.Join(", ", Profiles.Select((p, k) => string.Create(CultureInfo.InvariantCulture, $"{p.Name} {runs.Where(r => r.Profile == k).Max(static r => r.Max):P2}")))}");
                text.Append(line).Append('\n');
                if (alarms[^1] > 0)
                {
                    failures.Add("successful prints: " + line);
                }
            }
        }

        return text.ToString();
    }

    public static string Name(DetectionRegion region) => region.IsFull ? "whole frame" : "Core One bed region";

    private static double Median(double[] values) => values.Length == 0 ? 0 : values[values.Length / 2];
}
