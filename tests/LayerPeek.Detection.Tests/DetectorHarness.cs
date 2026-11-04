using LayerPeek.Core.Detection;

namespace LayerPeek.Detection.Tests;

/// <summary>
/// Runs a detector over a sequence of samples the way <see cref="PrintFailureDetectionService"/> does after its sampling
/// (T-153): warm-up, the threshold of the sensitivity and the orchestration's own K-of-N confirmation window. Reports the
/// first confirmed alarm (per threshold) and the scores.
/// </summary>
internal static class DetectorHarness
{
    /// <summary>With the threshold of <paramref name="sensitivity"/> of the registered bed-change detector.</summary>
    public static DetectorRun Run(
        IPrintFailureDetector detector,
        IEnumerable<(TimeSpan Time, byte[] Pixels)> samples,
        DetectionRegion region,
        int sensitivity = DetectorSettings.DefaultSensitivity) =>
        Run(detector, samples, region, [BedChangeDetector.Registration.Thresholds.ThresholdFor(sensitivity)]);

    /// <summary>With every threshold of <paramref name="thresholds"/> at once (one confirmation window each).</summary>
    public static DetectorRun Run(
        IPrintFailureDetector detector,
        IEnumerable<(TimeSpan Time, byte[] Pixels)> samples,
        DetectionRegion region,
        float[] thresholds,
        int width = 320,
        int height = 180)
    {
        PrintFailureDetectionOptions options = PrintFailureDetectionOptions.Default;
        ConfirmationWindow[] windows = [.. thresholds.Select(_ => new ConfirmationWindow(options.ConfirmationWindow.Ticks, options.ConfirmationRatio, options.MinConfirmationSamples))];
        TimeSpan?[] alarms = new TimeSpan?[thresholds.Length];
        DetectionPixelRegion pixels = region.ToPixels(width, height);
        PrintFailureContext context = new(1, null, 0, TimeSpan.Zero, region);
        detector.Reset(context);
        List<(TimeSpan Time, float Score)> scores = [];
        foreach ((TimeSpan time, byte[] frame) in samples)
        {
            DetectionResult result = detector.Analyze(new DetectionSample(frame, width, height, width, time, pixels, context with { PrintElapsed = time }));
            for (int t = 0; t < thresholds.Length; t++)
            {
                if (!result.IsWarmedUp)
                {
                    windows[t].Clear();
                    continue;
                }

                windows[t].Add(time.Ticks, result.Score, result.Score >= thresholds[t]);
                if (alarms[t] is null && windows[t].IsConfirmed(time.Ticks))
                {
                    alarms[t] = time;
                }
            }

            if (result.IsWarmedUp)
            {
                scores.Add((time, result.Score));
            }
        }

        return new DetectorRun(scores, thresholds, alarms);
    }
}

/// <param name="Scores">Every warmed-up score with its sample time.</param>
/// <param name="Alarms">Per threshold: when the first alarm was confirmed; null: none.</param>
internal sealed record DetectorRun(IReadOnlyList<(TimeSpan Time, float Score)> Scores, float[] Thresholds, TimeSpan?[] Alarms)
{
    /// <summary>The first alarm at the first (or only) threshold.</summary>
    public TimeSpan? FirstAlarm => Alarms[0];

    public float MaxScore(TimeSpan from, TimeSpan to) =>
        Scores.Where(s => s.Time >= from && s.Time < to).Select(static s => s.Score).DefaultIfEmpty(0).Max();

    public float MaxScore() => MaxScore(TimeSpan.Zero, TimeSpan.MaxValue);
}
