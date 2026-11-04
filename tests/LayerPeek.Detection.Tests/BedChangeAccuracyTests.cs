using System.Globalization;
using System.Text;

using LayerPeek.Core.Detection;
using LayerPeek.Tests.Detection;

namespace LayerPeek.Detection.Tests;

/// <summary>
/// T-155 accuracy (user decision 2026-11-04: at least 90 % of the synthetic failures found, no alarm on a successful
/// print) on the synthetic set (<see cref="SyntheticSets"/>) at the default sensitivity, at both ends of the sampling
/// interval and in the Core One bed region; <see cref="BedChangeAccuracyFullFrameTests"/> repeats it for the whole frame.
/// The numbers go to the test output (the task notes quote them).
/// </summary>
public sealed class BedChangeAccuracyCoreOneTests
{
    public static TheoryData<int> Intervals => [2, 5];

    [Theory]
    [MemberData(nameof(Intervals))]
    public void AtLeastNineInTenSyntheticFailuresRaiseAnAlarm(int interval) =>
        BedChangeAccuracy.AssertFailuresFound(DetectionRegion.CoreOneBed, interval);

    [Theory]
    [MemberData(nameof(Intervals))]
    public void ASuccessfulPrintRaisesNoAlarmWhateverTheLightAndNoise(int interval) =>
        BedChangeAccuracy.AssertNoAlarm(DetectionRegion.CoreOneBed, interval);
}

/// <summary>T-155: the same as <see cref="BedChangeAccuracyCoreOneTests"/> with the default region of other printers, the whole frame.</summary>
public sealed class BedChangeAccuracyFullFrameTests
{
    public static TheoryData<int> Intervals => [2, 5];

    [Theory]
    [MemberData(nameof(Intervals))]
    public void AtLeastNineInTenSyntheticFailuresRaiseAnAlarm(int interval) =>
        BedChangeAccuracy.AssertFailuresFound(DetectionRegion.Full, interval);

    [Theory]
    [MemberData(nameof(Intervals))]
    public void ASuccessfulPrintRaisesNoAlarmWhateverTheLightAndNoise(int interval) =>
        BedChangeAccuracy.AssertNoAlarm(DetectionRegion.Full, interval);
}

internal static class BedChangeAccuracy
{
    /// <summary>User decision 2026-11-04.</summary>
    private const double RequiredShare = 0.9;

    /// <summary>The successful prints: noise and light courses a real chamber has (camera exposure, LED, daylight).</summary>
    private static readonly (string Name, double Noise, Func<TimeSpan, double>? Brightness)[] SuccessfulPrints =
    [
        ("plain", 2.5, null),
        ("strong compression noise", 5, null),
        ("light 25 % brighter at once", 2.5, static t => t < TimeSpan.FromMinutes(6) ? 1 : 1.25),
        ("light 25 % dimmer at once", 2.5, static t => t < TimeSpan.FromMinutes(6) ? 1 : 0.75),
        ("light drifting ±20 % over 10 min", 2.5, static t => 1 + (0.2 * Math.Sin(t.TotalMinutes / 10 * 2 * Math.PI))),
        ("light flickering ±8 % per sample", 2.5, static t => 1 + (0.08 * Math.Sin(t.TotalSeconds * 12.9898))),
    ];

    public static void AssertFailuresFound(DetectionRegion region, int interval)
    {
        List<SyntheticFailure> failures = [.. SyntheticSets.Failures(seed: 7)];
        Dictionary<string, (int Found, int Total)> byKind = [];
        List<double> delays = [];
        StringBuilder missed = new();
        int found = 0;
        foreach ((SyntheticFailure failure, int index) in failures.Select(static (f, i) => (f, i)))
        {
            SyntheticPrint print = new() { Interval = TimeSpan.FromSeconds(interval), Region = region, Failure = failure, Seed = 100 + index };
            DetectorRun run = DetectorHarness.Run(new BedChangeDetector(), print.Samples(), region);
            Assert.False(run.FirstAlarm < print.FailureStart, $"Alarm at {run.FirstAlarm} before {failure.Name} started.");
            bool alarm = run.FirstAlarm is not null;
            string kind = failure.GetType().Name;
            (int kindFound, int kindTotal) = byKind.GetValueOrDefault(kind);
            byKind[kind] = (kindFound + (alarm ? 1 : 0), kindTotal + 1);
            if (alarm)
            {
                found++;
                delays.Add((run.FirstAlarm!.Value - print.FailureStart).TotalSeconds);
            }
            else
            {
                missed.Append(CultureInfo.InvariantCulture, $"; {failure.Name} (max score {run.MaxScore():P2})");
            }
        }

        delays.Sort();
        string summary = string.Create(
            CultureInfo.InvariantCulture,
            $"bed-change, {Name(region)}, {interval} s: {found}/{failures.Count} found ({(double)found / failures.Count:P0}; {string.Join(", ", byKind.Select(static k => $"{k.Key} {k.Value.Found}/{k.Value.Total}"))}), alarm {delays[delays.Count / 2]:0} s (median) / {delays[^1]:0} s (max) after the start{missed}");
        TestContext.Current.TestOutputHelper?.WriteLine(summary);
        Assert.True(found >= RequiredShare * failures.Count, summary);
    }

    public static void AssertNoAlarm(DetectionRegion region, int interval)
    {
        StringBuilder summary = new(string.Create(CultureInfo.InvariantCulture, $"bed-change, {Name(region)}, {interval} s, successful prints (max score):"));
        foreach ((string name, double noise, Func<TimeSpan, double>? brightness) in SuccessfulPrints)
        {
            foreach (int seed in (int[])[1, 2])
            {
                SyntheticPrint print = new() { Interval = TimeSpan.FromSeconds(interval), Region = region, Noise = noise, Brightness = brightness, Seed = seed };
                DetectorRun run = DetectorHarness.Run(new BedChangeDetector(), print.Samples(), region);
                summary.Append(CultureInfo.InvariantCulture, $" {name} {run.MaxScore():P2};");
                Assert.True(run.FirstAlarm is null, $"Alarm at {run.FirstAlarm} on a successful print ({name}, max score {run.MaxScore():P2}).");
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine(summary.ToString());
    }

    private static string Name(DetectionRegion region) => region.IsFull ? "whole frame" : "Core One bed region";
}
