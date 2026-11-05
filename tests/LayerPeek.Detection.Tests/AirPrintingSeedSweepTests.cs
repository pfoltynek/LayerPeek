using System.Globalization;
using System.Text;

namespace LayerPeek.Detection.Tests;

/// <summary>
/// T-167: the accuracy of the air-printing detector on the synthetic Core One prints (<see cref="AirPrintingSets"/>) over
/// ten seeds at both ends of the sampling interval. The whole sweep takes minutes, so it runs only with
/// <c>LAYERPEEK_DETECTION_SWEEP=1</c> (as T-166; its table goes to the test output and, with
/// <c>LAYERPEEK_DETECTION_SWEEP_FILE=&lt;path&gt;</c>, to a file); a smaller set runs every time as a regression.
/// </summary>
public sealed class AirPrintingSeedSweepTests
{
    private static readonly int[] Seeds = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

    /// <summary>
    /// The faster prints of seed 1 (0.4 and 1 mm/min; the slow ones take minutes in a Debug build): its first failures at
    /// both ends of the sampling interval and, at 5 s, its successful prints without partial stops. No alarm before a
    /// failure, at most one missed failure (a part that vanished does not count: bed-change finds it), no false alarm.
    /// </summary>
    [Theory]
    [InlineData(2, false)]
    [InlineData(5, true)]
    public void TheFasterPrintsOfASeedFindTheFailuresAndRaiseNoFalseAlarm(int interval, bool successful)
    {
        float[] thresholds = [AirPrintingSweep.DefaultThreshold];
        AirPrintingSweep.FailureResult failures = AirPrintingSweep.EvaluateFailures(1, interval, thresholds, count: 12, include: static p => p.Rate >= 0.4);
        AirPrintingSweep.SuccessResult successes = AirPrintingSweep.EvaluateSuccessful(
            1,
            interval,
            thresholds,
            p => successful && p.Rate >= 0.4 && p.Parts.All(part => part.TopHeight >= p.FinalHeight));
        string summary = AirPrintingSweep.Table([failures], [successes], thresholds) + string.Join(Environment.NewLine, failures.Missed);
        TestContext.Current.TestOutputHelper?.WriteLine(summary);

        Assert.True(failures.Total >= 6, summary);
        Assert.True(failures.Early[0] == 0, summary);
        Assert.True(failures.Found[0] >= failures.Total - 1, summary);
        Assert.True(!successful || successes.Hours > 2, summary);
        Assert.True(successes.Alarms[0] == 0, summary);
    }

    [Fact]
    public void EverySeedOfTheSyntheticPrints()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LAYERPEEK_DETECTION_SWEEP") == "1", "Set LAYERPEEK_DETECTION_SWEEP=1 to run the multi-seed sweep (minutes).");
        float[] thresholds = AirPrintingSweep.Thresholds;
        int last = thresholds.Length - 1;
        List<AirPrintingSweep.FailureResult> failures = [];
        List<AirPrintingSweep.SuccessResult> successes = [];
        foreach (int interval in (int[])[2, 5])
        {
            (AirPrintingSweep.FailureResult Failures, AirPrintingSweep.SuccessResult Successes)[] results =
            [
                .. Seeds.AsParallel().AsOrdered().Select(seed => (
                    AirPrintingSweep.EvaluateFailures(seed, interval, thresholds),
                    AirPrintingSweep.EvaluateSuccessful(seed, interval, thresholds))),
            ];
            failures.AddRange(results.Select(static r => r.Failures));
            successes.AddRange(results.Select(static r => r.Successes));
        }

        StringBuilder output = new(AirPrintingSweep.Table(failures, successes, thresholds));
        foreach (AirPrintingSweep.FailureResult result in failures)
        {
            output.AppendLine(CultureInfo.InvariantCulture, $"seed {result.Seed}, {result.Interval} s missed: {string.Join("; ", result.Missed)}");
        }

        foreach (AirPrintingSweep.SuccessResult result in successes.Where(static s => s.Alarmed.Count > 0))
        {
            output.AppendLine(CultureInfo.InvariantCulture, $"seed {result.Seed}, {result.Interval} s false alarms: {string.Join("; ", result.Alarmed)}");
        }

        string text = output.ToString();
        TestContext.Current.TestOutputHelper?.WriteLine(text);
        if (Environment.GetEnvironmentVariable("LAYERPEEK_DETECTION_SWEEP_FILE") is { Length: > 0 } file)
        {
            File.WriteAllText(file, text);
        }

        List<string> problems = [];
        foreach (int interval in (int[])[2, 5])
        {
            AirPrintingSweep.FailureResult[] of = [.. failures.Where(f => f.Interval == interval)];
            double share = of.Sum(f => f.Found[last]) / (double)of.Sum(static f => f.Total);
            if (share < AirPrintingSweep.RequiredShare)
            {
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"{interval} s: {share:P1} found"));
            }
        }

        if (failures.Sum(f => f.Early[last]) > 0)
        {
            problems.Add("an alarm before the failure");
        }

        // Without partial stops (a part with a lower step, several parts of different heights: see the task's questions).
        double falseAlarmsPer10Hours = successes.Sum(s => s.Alarms[last]) / successes.Sum(static s => s.Hours) * 10;
        if (falseAlarmsPer10Hours > 1)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"{falseAlarmsPer10Hours:0.00} false alarms per 10 h"));
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems) + Environment.NewLine + text);
    }
}
