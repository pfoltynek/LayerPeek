using System.Globalization;
using System.Text;

using LayerPeek.Core.Detection;
using LayerPeek.Tests.Detection;

namespace LayerPeek.Detection.Tests;

/// <summary>
/// Evaluates the air-printing detector on the synthetic sets of T-167 (<see cref="AirPrintingSets"/>) the way the
/// orchestration runs it (<see cref="DetectorHarness"/>), at several thresholds at once. Failures whose part vanished
/// (knocked off: the domain of bed-change) and successful prints in which a part stops growing before the others (a lower
/// step, several parts: "partial stops", user decision 2026-11-04 question 8) are counted apart.
/// </summary>
internal static class AirPrintingSweep
{
    /// <summary>T-167 target: at least nine in ten failures found at the default sensitivity.</summary>
    public const double RequiredShare = 0.9;

    /// <summary>The thresholds of the tables: steps of the sensitivity scale, the default one last.</summary>
    public static float[] Thresholds => [0.2f, 0.3f, 0.4f, 0.6f, 0.7f, 0.8f, DefaultThreshold];

    public static float DefaultThreshold => AirPrintingDetector.Registration.Thresholds.ThresholdFor(DetectorSettings.DefaultSensitivity);

    /// <summary>The failures of one seed: found (an alarm after the failure), alarms before it, the delays.</summary>
    public static FailureResult EvaluateFailures(int seed, int interval, float[] thresholds, int count = 24, Func<SyntheticGrowingPrint, bool>? include = null)
    {
        int[] found = new int[thresholds.Length];
        int[] foundGone = new int[thresholds.Length];
        int[] early = new int[thresholds.Length];
        int gone = 0;
        List<Delay>[] delays = [.. thresholds.Select(static _ => new List<Delay>())];
        List<string> missed = [];
        int total = 0;
        foreach ((string name, SyntheticGrowingPrint print) in AirPrintingSets.Failures(seed, TimeSpan.FromSeconds(interval), count))
        {
            if (include is not null && !include(print))
            {
                continue;
            }

            total++;
            TimeSpan failed = print.FailureTime ?? throw new InvalidOperationException("A failure without its time.");
            DetectorRun run = DetectorHarness.Run(new AirPrintingDetector(), print.Samples(), DetectionRegion.CoreOneBed, thresholds);
            gone += print.PartsGone ? 1 : 0;
            for (int t = 0; t < thresholds.Length; t++)
            {
                if (run.Alarms[t] is not { } alarm)
                {
                    if (t == thresholds.Length - 1)
                    {
                        missed.Add(string.Create(CultureInfo.InvariantCulture, $"{name} (max score {run.MaxScore(failed, TimeSpan.MaxValue):P0})"));
                    }

                    continue;
                }

                if (alarm < failed)
                {
                    early[t]++;
                    continue;
                }

                (print.PartsGone ? foundGone : found)[t]++;
                double z = print.Course().First(c => c.Time >= alarm).Z;
                delays[t].Add(new Delay(print.Rate, z - print.FailureHeight!.Value, alarm - failed));
            }
        }

        return new FailureResult(seed, interval, total - gone, gone, found, foundGone, early, delays, missed);
    }

    /// <summary>The successful prints of one seed: false alarms (at most one per print and threshold) and the hours watched.</summary>
    public static SuccessResult EvaluateSuccessful(int seed, int interval, float[] thresholds, Func<SyntheticGrowingPrint, bool>? include = null)
    {
        int[] alarms = new int[thresholds.Length];
        int[] partialAlarms = new int[thresholds.Length];
        double hours = 0;
        double partialHours = 0;
        float highest = 0;
        float partialHighest = 0;
        List<string> alarmed = [];
        foreach ((string name, SyntheticGrowingPrint print) in AirPrintingSets.Successful(seed, TimeSpan.FromSeconds(interval)))
        {
            if (include is not null && !include(print))
            {
                continue;
            }

            DetectorRun run = DetectorHarness.Run(new AirPrintingDetector(), print.Samples(), DetectionRegion.CoreOneBed, thresholds);
            bool partial = print.Parts.Any(p => p.TopHeight < print.FinalHeight);
            double printed = print.Course().Last().Time.TotalHours;
            if (partial)
            {
                partialHours += printed;
                partialHighest = Math.Max(partialHighest, run.MaxScore());
            }
            else
            {
                hours += printed;
                highest = Math.Max(highest, run.MaxScore());
            }

            for (int t = 0; t < thresholds.Length; t++)
            {
                if (run.Alarms[t] is { } at)
                {
                    (partial ? partialAlarms : alarms)[t]++;
                    if (t == thresholds.Length - 1)
                    {
                        alarmed.Add($"{name} at {at}");
                    }
                }
            }
        }

        return new SuccessResult(seed, interval, hours, partialHours, alarms, partialAlarms, highest, partialHighest, alarmed);
    }

    public static string Table(IReadOnlyList<FailureResult> failures, IReadOnlyList<SuccessResult> successes, float[] thresholds)
    {
        StringBuilder text = new();
        text.AppendLine(CultureInfo.InvariantCulture, $"Thresholds: {string.Join(" / ", thresholds.Select(static t => t.ToString("P0", CultureInfo.InvariantCulture)))} (the last is the default)");
        foreach (FailureResult r in failures)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"failures seed {r.Seed}, {r.Interval} s: found {string.Join(" / ", r.Found.Select(f => $"{f}/{r.Total}"))}; part gone: {string.Join(" / ", r.FoundGone.Select(f => $"{f}/{r.Gone}"))}; alarm before the failure {string.Join(" / ", r.Early)}");
        }

        foreach (SuccessResult r in successes)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"successful seed {r.Seed}, {r.Interval} s: {r.Hours:0.0} h, false alarms {string.Join(" / ", r.Alarms)}, max score {r.MaxScore:P0}; partial stops {r.PartialHours:0.0} h, false alarms {string.Join(" / ", r.PartialAlarms)}, max score {r.PartialMaxScore:P0}");
        }

        int last = thresholds.Length - 1;
        foreach (int interval in failures.Select(static f => f.Interval).Distinct())
        {
            FailureResult[] of = [.. failures.Where(f => f.Interval == interval)];
            string shares = string.Join(" / ", Enumerable.Range(0, thresholds.Length).Select(t => (of.Sum(f => f.Found[t]) / (double)of.Sum(static f => f.Total)).ToString("P0", CultureInfo.InvariantCulture)));
            text.AppendLine(CultureInfo.InvariantCulture, $"{interval} s, all seeds: found {shares}, lowest seed at the default {of.Min(f => f.Found[last] / (double)f.Total):P0}");
        }

        foreach (double rate in AirPrintingSets.Rates)
        {
            Delay[] delays = [.. failures.SelectMany(f => f.Delays[last]).Where(d => d.Rate == rate)];
            if (delays.Length > 0)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"delay at {rate} mm/min ({delays.Length} alarms): {Quantiles(delays.Select(static d => d.Height))} mm, {Quantiles(delays.Select(static d => d.Time.TotalMinutes))} min (median / 90 % / max)");
            }
        }

        double hours = successes.Sum(static s => s.Hours);
        double partialHours = successes.Sum(static s => s.PartialHours);
        if (hours > 0)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"false alarms per 10 h at the default: {successes.Sum(s => s.Alarms[last]) / hours * 10:0.00} ({hours:0.0} h); with partial stops {successes.Sum(s => s.Alarms[last] + s.PartialAlarms[last]) / (hours + partialHours) * 10:0.00} ({hours + partialHours:0.0} h)");
        }

        return text.ToString();
    }

    public static string Describe(FailureResult result) =>
        Table([result], [], [DefaultThreshold]) + string.Join(Environment.NewLine, result.Missed.Select(static m => "missed: " + m));

    private static string Quantiles(IEnumerable<double> values)
    {
        double[] sorted = [.. values.Order()];
        return string.Create(CultureInfo.InvariantCulture, $"{sorted[sorted.Length / 2]:0.0} / {sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.9))]:0.0} / {sorted[^1]:0.0}");
    }

    public sealed record Delay(double Rate, double Height, TimeSpan Time);

    /// <param name="Total">Failures whose part stays (rides on the bed).</param>
    /// <param name="Gone">Failures whose part vanished.</param>
    public sealed record FailureResult(int Seed, int Interval, int Total, int Gone, int[] Found, int[] FoundGone, int[] Early, List<Delay>[] Delays, List<string> Missed);

    public sealed record SuccessResult(
        int Seed,
        int Interval,
        double Hours,
        double PartialHours,
        int[] Alarms,
        int[] PartialAlarms,
        float MaxScore,
        float PartialMaxScore,
        List<string> Alarmed);
}
