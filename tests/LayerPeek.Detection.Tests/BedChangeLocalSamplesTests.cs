using System.Diagnostics;
using System.Globalization;

using LayerPeek.Core.Detection;

namespace LayerPeek.Detection.Tests;

/// <summary>
/// T-155 on the full local recordings of a successful Core One print (user decision 2026-11-04): the 30 s series in
/// <c>~/LayerPeek-samples/coreone-frames</c> and the 60 s clip in <c>~/LayerPeek-samples/clips</c> (docs/detection-spike.md,
/// section 1). The data stays outside the repository; without it, or without the FFmpeg command line tool to decode the
/// JPEG / MP4 files (no decoder as a test dependency, T-153 question 7), the tests are skipped.
/// </summary>
public sealed class BedChangeLocalSamplesTests
{
    /// <summary>
    /// The print of the series: the frames up to 22:36:13 (2026-11-03). From 22:36:43 the bed drops to the bottom, the
    /// head parks and the camera switches to night mode: the print had finished (not watched any more).
    /// </summary>
    private static readonly DateTime PrintStart = new(2026, 11, 3, 18, 0, 0);

    private static readonly DateTime PrintEnd = new(2026, 11, 3, 22, 36, 13);

    private static float DefaultThreshold => BedChangeDetector.Registration.Thresholds.ThresholdFor(DetectorSettings.DefaultSensitivity);

    [Fact]
    public void TheWholeSuccessfulPrintRaisesNoAlarmInTheCoreOneRegion()
    {
        string folder = Path.Combine(LocalSamples.Folder, "coreone-frames");
        Assert.SkipUnless(Directory.Exists(folder), $"The local series {folder} is missing.");
        string ffmpeg = LocalSamples.FFmpeg();
        (DateTime Taken, string Path)[] frames =
        [
            .. Directory.GetFiles(folder, "frame-*.jpg")
                .Select(static path => (Taken: LocalSamples.TakenAt(path), Path: path))
                .Where(static f => f.Taken >= PrintStart && f.Taken <= PrintEnd)
                .OrderBy(static f => f.Taken),
        ];
        Assert.SkipWhen(frames.Length < 100, $"The local series {folder} has only {frames.Length} frames of the print.");
        byte[] gray = LocalSamples.Decode(ffmpeg, LocalSamples.ConcatList(frames.Select(static f => f.Path)), "-f", "concat", "-safe", "0");
        Assert.Equal(frames.Length * 57600, gray.Length);
        List<(TimeSpan Time, byte[] Pixels)> samples = [.. frames.Select((f, i) => (f.Taken - frames[0].Taken, gray.AsSpan(i * 57600, 57600).ToArray()))];
        TimeSpan printed = samples[^1].Time;

        foreach (DetectionRegion region in (DetectionRegion[])[DetectionRegion.CoreOneBed, DetectionRegion.Full])
        {
            DetectorRun run = DetectorHarness.Run(new BedChangeDetector(), samples, region);
            // A frame every 30 s is longer than the confirmation window (16 s): one score at or above the threshold
            // stands for 30 s of such scores, so it counts as an alarm (stricter than the orchestration).
            int alarms = run.Scores.Count(s => s.Score >= DefaultThreshold);
            (TimeSpan at, float max) = run.Scores.MaxBy(static s => s.Score);
            string summary = string.Create(
                CultureInfo.InvariantCulture,
                $"bed-change, series {frames[0].Taken:HH:mm}–{frames[^1].Taken:HH:mm} ({printed.TotalHours:0.0} h, {frames.Length} frames), {(region.IsFull ? "whole frame" : "Core One bed region")}: max score {max:P2} at {at}, {alarms} frame(s) at or above {DefaultThreshold:P1}");
            TestContext.Current.TestOutputHelper?.WriteLine(summary);
            if (!region.IsFull)
            {
                // The default region of a Core One (T-153); the whole frame includes the X gantry (see the task notes).
                Assert.True(alarms == 0, summary);
            }
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public void TheLoopedClipOfTheMovingHeadRaisesNoAlarm(int interval)
    {
        string clip = Path.Combine(LocalSamples.Folder, "clips", "coreone-20261103-1905-60s.mp4");
        Assert.SkipUnless(File.Exists(clip), $"The local clip {clip} is missing.");
        // Two frames per second of the 60 s clip, sampled every 2 or 5 s for half an hour (the clip looped).
        byte[] gray = LocalSamples.Decode(LocalSamples.FFmpeg(), clip, "-vf", "fps=2");
        byte[][] frames = [.. Enumerable.Range(0, gray.Length / 57600).Select(i => gray.AsSpan(i * 57600, 57600).ToArray())];
        Assert.True(frames.Length > 100, $"{frames.Length} frames decoded.");

        foreach (DetectionRegion region in (DetectionRegion[])[DetectionRegion.CoreOneBed, DetectionRegion.Full])
        {
            IEnumerable<(TimeSpan, byte[])> Looped()
            {
                for (int k = 0; k * interval < 1800; k++)
                {
                    yield return (TimeSpan.FromSeconds(k * interval), frames[k * interval * 2 % frames.Length]);
                }
            }

            DetectorRun run = DetectorHarness.Run(new BedChangeDetector(), Looped(), region);
            string summary = string.Create(CultureInfo.InvariantCulture, $"bed-change, clip looped 30 min at {interval} s, {(region.IsFull ? "whole frame" : "Core One bed region")}: max score {run.MaxScore():P2}");
            TestContext.Current.TestOutputHelper?.WriteLine(summary);
            Assert.True(run.FirstAlarm is null, $"{summary}, alarm at {run.FirstAlarm}.");
        }
    }
}
