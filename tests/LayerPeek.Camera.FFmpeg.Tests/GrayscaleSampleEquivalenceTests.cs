using System.Globalization;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Detection;
using LayerPeek.Detection;
using LayerPeek.Tests.Detection;

namespace LayerPeek.Camera.FFmpeg.Tests;

/// <summary>
/// T-189: the grayscale samples the decoder scales from the luma plane are equivalent to the ones the hub made from the
/// full BGRA frame before (BT.601 luma of the RGB, area average, see Core's <c>GrayscaleScaler</c>), on the Buddy3D fixture
/// and, when present, on the local 60 s clip of a real print (<c>~/LayerPeek-samples/clips</c>, never in the repository):
/// pixel by pixel and in the scores of the bed-change detector, with unchanged thresholds.
/// </summary>
public sealed class GrayscaleSampleEquivalenceTests
{
    private static readonly CameraGrayscaleSize SampleSize = new(320, 180);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string LocalClip =>
        Path.Combine(
            Environment.GetEnvironmentVariable("LAYERPEEK_SAMPLES") is { Length: > 0 } custom
                ? custom
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "LayerPeek-samples"),
            "clips",
            "coreone-20261103-1905-60s.mp4");

    [FFmpegFact]
    public async Task TheLumaPlaneSamplesMatchTheBgraSamplesOfTheFixture()
    {
        List<(byte[] Bgra, byte[] Luma)> samples = await DecodeBothAsync(TestFixtures.Buddy3D, keyFramesOnly: false);

        Assert.Equal(TestFixtures.Buddy3DFrameCount, samples.Count);
        Difference difference = Compare(samples);
        Report($"T-189 fixture buddy3d.h264 ({samples.Count} frames): {difference}");
        Assert.InRange(difference.MeanAbsolute, 0, 1.5);
        Assert.InRange(Math.Abs(difference.MeanBrightness), 0, 1);
    }

    [FFmpegFact]
    public async Task TheDetectorsScoreTheLocalClipAlikeFromEitherSample()
    {
        Assert.SkipUnless(File.Exists(LocalClip), $"The local clip {LocalClip} is missing.");
        // The key frames (every ~2.3 s), as print failure detection samples them.
        List<(byte[] Bgra, byte[] Luma)> samples = await DecodeBothAsync(new Uri(LocalClip), keyFramesOnly: true);
        Assert.True(samples.Count > 20, $"{samples.Count} key frames.");
        Difference difference = Compare(samples);
        Report($"T-189 local clip ({samples.Count} key frames): {difference}");
        Assert.InRange(difference.MeanAbsolute, 0, 1.5);

        // Bed-change on the print as it is and with a lump growing on the bed from minute 10 (dark and light filament).
        // Air-printing needs a bed that moves with the Z axis, which a looped clip does not show (see the task notes for
        // its comparison on the local series of job 354).
        float threshold = BedChangeDetector.Registration.Thresholds.ThresholdFor(DetectorSettings.DefaultSensitivity);
        // A quarter of the threshold (~14 pixels of the Core One bed region at the default); measured: at most 0.0006, at the
        // edge of the lump (the bed's colours give BT.709 luma that differs from BT.601 luma of the RGB by a few levels).
        float tolerance = threshold * 0.25f;
        foreach (SyntheticFailure? failure in (SyntheticFailure?[])[null, new Lump(1, 160, 140, 6, 30, TimeSpan.FromMinutes(5)), new Lump(2, 120, 150, 5, 200, TimeSpan.FromMinutes(5))])
        {
            foreach ((DetectionRegion region, bool darkFilament) in (ReadOnlySpan<(DetectionRegion, bool)>)[(DetectionRegion.CoreOneBed, false), (DetectionRegion.Full, false), (DetectionRegion.CoreOneBed, true)])
            {
                float[] before = Scores(samples, static s => s.Bgra, failure, region, darkFilament);
                float[] after = Scores(samples, static s => s.Luma, failure, region, darkFilament);
                float maxDelta = before.Zip(after, static (a, b) => Math.Abs(a - b)).Max();
                (float Before, float After)[] differently = [.. before.Zip(after).Where(p => (p.First >= threshold) != (p.Second >= threshold))];
                TimeSpan? alarmBefore = FirstAlarm(before, threshold);
                TimeSpan? alarmAfter = FirstAlarm(after, threshold);
                string summary = string.Create(
                    CultureInfo.InvariantCulture,
                    $"T-189 bed-change{(failure is null ? "" : $" + {failure.Name}")}, {(region.IsFull ? "whole frame" : "Core One bed")}{(darkFilament ? ", dark filament" : "")}: max score {before.Max():0.0000} -> {after.Max():0.0000}, max |delta| {maxDelta:0.0000}, {before.Count(b => b >= threshold)} -> {after.Count(a => a >= threshold)} of {before.Length} at or above {threshold:0.0000}, first alarm {alarmBefore?.ToString() ?? "none"} -> {alarmAfter?.ToString() ?? "none"}");
                Report(summary);
                Assert.True(maxDelta <= tolerance, summary);
                // A sample judged differently is one at the threshold (a growing failure crossing it one sample earlier or later).
                Assert.All(differently, p => Assert.True(Math.Abs(p.Before - threshold) <= tolerance, summary));
                // The same alarms (an alarm for each failure, none without), at about the same time.
                Assert.True(alarmBefore.HasValue == alarmAfter.HasValue, summary);
                Assert.True(alarmBefore is null || Math.Abs((alarmAfter!.Value - alarmBefore.Value).TotalSeconds) <= 20, summary);
            }
        }
    }

    /// <summary>
    /// Every frame (or every key frame) of <paramref name="uri"/> as the old sample (BGRA in full resolution, downscaled
    /// like the hub did) and the new one (the decoder's grayscale frame), in one decoding pass.
    /// </summary>
    private static async Task<List<(byte[] Bgra, byte[] Luma)>> DecodeBothAsync(Uri uri, bool keyFramesOnly)
    {
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(uri);
        CameraStreamDemand demand = new(frames: !keyFramesOnly, packets: false);
        demand.RequestGrayscale(SampleSize, long.MinValue);
        if (keyFramesOnly)
        {
            demand.RequestKeyFrame(long.MinValue);
        }

        List<(byte[], byte[])> samples = [];
        await foreach (CameraStreamItem item in feed.ReadAsync(demand, Ct))
        {
            if (item.Kind != CameraStreamItemKind.Frame)
            {
                continue;
            }

            CameraGrayscaleFrame gray = item.GrayscaleFrame;
            Assert.Equal((SampleSize.Width, SampleSize.Height), (gray.Width, gray.Height));
            byte[] luma = new byte[SampleSize.Length];
            for (int y = 0; y < gray.Height; y++)
            {
                gray.Pixels.Span.Slice(y * gray.Stride, gray.Width).CopyTo(luma.AsSpan(y * gray.Width));
            }

            samples.Add((BgraSample(item.Frame), luma));
        }

        return samples;
    }

    /// <summary>The sample of the hub before T-189: BT.601 luma of each BGRA pixel, area average (Core's GrayscaleScaler).</summary>
    private static byte[] BgraSample(in CameraFrame frame)
    {
        int width = SampleSize.Width;
        int height = SampleSize.Height;
        byte[] target = new byte[SampleSize.Length];
        ReadOnlySpan<byte> pixels = frame.Pixels.Span;
        for (int ty = 0; ty < height; ty++)
        {
            int y0 = ty * frame.Height / height;
            int y1 = Math.Max((ty + 1) * frame.Height / height, y0 + 1);
            for (int tx = 0; tx < width; tx++)
            {
                int x0 = tx * frame.Width / width;
                int x1 = Math.Max((tx + 1) * frame.Width / width, x0 + 1);
                int sum = 0;
                for (int y = y0; y < y1; y++)
                {
                    for (int x = x0; x < x1; x++)
                    {
                        int i = (y * frame.Stride) + (x * CameraFrame.BytesPerPixel);
                        sum += ((29 * pixels[i]) + (150 * pixels[i + 1]) + (77 * pixels[i + 2])) >> 8;
                    }
                }

                int count = (y1 - y0) * (x1 - x0);
                target[(ty * width) + tx] = (byte)((sum + (count >> 1)) / count);
            }
        }

        return target;
    }

    private static Difference Compare(List<(byte[] Bgra, byte[] Luma)> samples)
    {
        long absolute = 0;
        long signed = 0;
        int max = 0;
        long count = 0;
        foreach ((byte[] bgra, byte[] luma) in samples)
        {
            for (int i = 0; i < bgra.Length; i++)
            {
                int delta = luma[i] - bgra[i];
                absolute += Math.Abs(delta);
                signed += delta;
                max = Math.Max(max, Math.Abs(delta));
                count++;
            }
        }

        return new Difference((double)absolute / count, (double)signed / count, max);
    }

    /// <summary>
    /// The scores of bed-change over the samples looped for 30 minutes at 2 s, with <paramref name="failure"/> from minute
    /// 10 on; -1 while it is not warmed up.
    /// </summary>
    private static float[] Scores(List<(byte[] Bgra, byte[] Luma)> samples, Func<(byte[] Bgra, byte[] Luma), byte[]> pick, SyntheticFailure? failure, DetectionRegion region, bool darkFilament)
    {
        TimeSpan failureStart = TimeSpan.FromMinutes(10);
        BedChangeDetector detector = new();
        DetectionPixelRegion pixels = region.ToPixels(SampleSize.Width, SampleSize.Height);
        PrintFailureContext context = new(1, null, 0, TimeSpan.Zero, region, darkFilament);
        detector.Reset(context);
        byte[] frame = new byte[SampleSize.Length];
        float[] scores = new float[900];
        for (int k = 0; k < scores.Length; k++)
        {
            TimeSpan time = TimeSpan.FromSeconds(k * 2);
            pick(samples[k % samples.Count]).CopyTo(frame, 0);
            if (failure is not null && time >= failureStart)
            {
                failure.Apply(frame, time - failureStart);
            }

            DetectionResult result = detector.Analyze(new DetectionSample(frame, SampleSize.Width, SampleSize.Height, SampleSize.Width, time, pixels, context with { PrintElapsed = time }));
            scores[k] = result.IsWarmedUp ? result.Score : -1;
        }

        return scores;
    }

    /// <summary>The first alarm of the orchestration's "K of N" confirmation over <paramref name="scores"/> (one per 2 s).</summary>
    private static TimeSpan? FirstAlarm(float[] scores, float threshold)
    {
        PrintFailureDetectionOptions options = PrintFailureDetectionOptions.Default;
        int window = (int)(options.ConfirmationWindow.TotalSeconds / 2);
        for (int k = 0; k < scores.Length; k++)
        {
            int samples = 0;
            int above = 0;
            // Not warmed up clears the window (-1).
            for (int i = k; i >= 0 && k - i <= window && scores[i] >= 0; i--)
            {
                samples++;
                above += scores[i] >= threshold ? 1 : 0;
            }

            if (samples >= options.MinConfirmationSamples && above >= Math.Ceiling(options.ConfirmationRatio * samples))
            {
                return TimeSpan.FromSeconds(k * 2);
            }
        }

        return null;
    }

    private static void Report(string line) => TestContext.Current.TestOutputHelper?.WriteLine(line);

    private readonly record struct Difference(double MeanAbsolute, double MeanBrightness, int Max)
    {
        public override string ToString() =>
            string.Create(CultureInfo.InvariantCulture, $"mean |delta| {MeanAbsolute:0.00}, mean delta {MeanBrightness:+0.00;-0.00;0}, max |delta| {Max} levels");
    }
}
