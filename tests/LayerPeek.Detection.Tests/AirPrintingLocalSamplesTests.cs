using System.Globalization;

using LayerPeek.Core.Detection;

namespace LayerPeek.Detection.Tests;

/// <summary>
/// T-167 on the local recordings of a real, successful Core One print (job 354, 0.2 mm layers, MMU3 printer; never in the
/// repository): <c>z-recordings/coreone-job354-0.2mm.csv</c> (state and height of the Z axis every second, the whole print)
/// and <c>coreone-frames-job354</c> (a frame every 5 s of its last ~45 min, file names in local time, CET). Skipped without
/// the data or FFmpeg (<see cref="LocalSamples"/>).
/// </summary>
public sealed class AirPrintingLocalSamplesTests
{
    private const string ZRecording = "z-recordings/coreone-job354-0.2mm.csv";

    /// <summary>The frames are named in CET (UTC+1).</summary>
    private static readonly TimeSpan FrameNameOffset = TimeSpan.FromHours(1);

    private static float DefaultThreshold => AirPrintingDetector.Registration.Thresholds.ThresholdFor(DetectorSettings.DefaultSensitivity);

    [Fact]
    public void TheHeightOfJob354HasNoNewBaseMidPrintAndZHopsAddNothing()
    {
        ZRecord[] records = LoadZ();
        AirPrintingDetector detector = new();
        byte[] frame = new byte[320 * 180];
        DetectionRegion region = DetectionRegion.CoreOneBed;
        DetectionPixelRegion pixels = region.ToPixels(320, 180);
        PrintFailureContext context = new(354, null, 0, TimeSpan.Zero, region);
        detector.Reset(context);
        DateTime start = records[0].Utc;
        int firstLayerBases = -1;
        double highest = 0;
        int hopsSeen = 0;
        double previous = double.NaN;
        // The orchestration samples every 2 s and takes the height of the latest poll (here: the second of the sample).
        for (int i = 0; i < records.Length; i += 2)
        {
            ZRecord record = records[i];
            if (record.State != "PRINTING")
            {
                break;
            }

            TimeSpan time = record.Utc - start;
            detector.Analyze(new DetectionSample(frame, 320, 180, 320, time, pixels, context with { AxisZ = record.Z, PrintElapsed = time }));
            double level = detector.Level!.Value;
            if (firstLayerBases < 0 && record.Z is >= 0.15 and <= 0.25 && level is >= 0.15 and <= 0.25)
            {
                // The first layer: what the start sequence (40, 15, the bed mesh near 0) made of new bases so far.
                firstLayerBases = detector.NewBases;
            }

            if (record.Z is { } z && !double.IsNaN(previous) && z - previous > 0.25 && level < z - 0.25)
            {
                hopsSeen++;
            }

            if (firstLayerBases >= 0 && record.Z < 100)
            {
                // A Z hop never shows in the level: it is at most the level of the layer below plus one layer.
                Assert.True(level <= highest + 0.45, $"At {time}: level {level} mm, highest so far {highest} mm, Z {record.Z} mm.");
            }

            highest = Math.Max(highest, level);
            previous = record.Z ?? double.NaN;
        }

        string summary = string.Create(
            CultureInfo.InvariantCulture,
            $"job 354: {detector.NewBases} new base(s), {firstLayerBases} of them before the first layer, highest level {highest:0.0} mm, {hopsSeen} Z hop(s) > 0.25 mm filtered out");
        LocalSamples.Report(summary);
        Assert.True(firstLayerBases >= 1, summary);
        Assert.True(detector.NewBases == firstLayerBases, summary);
        Assert.True(hopsSeen > 10, summary);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    public void TheEndOfJob354RaisesNoAlarm(int everySeconds)
    {
        string folder = Path.Combine(LocalSamples.Folder, "coreone-frames-job354");
        Assert.SkipUnless(Directory.Exists(folder), $"The local series {folder} is missing.");
        ZRecord[] records = LoadZ();
        string ffmpeg = LocalSamples.FFmpeg();
        DateTime printed = records.Last(static r => r.State == "PRINTING").Utc;
        (DateTime Utc, string Path)[] frames =
        [
            .. Directory.GetFiles(folder, "frame-*.jpg")
                .Select(path => (Utc: LocalSamples.TakenAt(path) - FrameNameOffset, Path: path))
                .Where(f => f.Utc <= printed && ZAt(records, f.Utc) is < 60)
                .OrderBy(static f => f.Utc)
                .Where((_, i) => i % (everySeconds / 5) == 0),
        ];
        Assert.SkipWhen(frames.Length < 100, $"The local series {folder} has only {frames.Length} frames of the print.");
        byte[] gray = LocalSamples.Decode(ffmpeg, LocalSamples.ConcatList(frames.Select(static f => f.Path)), "-f", "concat", "-safe", "0");
        Assert.Equal(frames.Length * 57600, gray.Length);
        List<(TimeSpan Time, byte[] Pixels, double? Z)> samples =
            [.. frames.Select((f, i) => (f.Utc - frames[0].Utc, gray.AsSpan(i * 57600, 57600).ToArray(), ZAt(records, f.Utc)))];

        // The detector as offered, and without the persistence over 1.5 mm of Z (the raw share of the growth tiles that move:
        // the 5.8 mm of this recording leave only a little of it after the start-up of ~5 mm).
        foreach ((string name, AirPrintingOptions options) in (ReadOnlySpan<(string, AirPrintingOptions)>)[("as offered", AirPrintingOptions.Default), ("raw score", AirPrintingOptions.Default with { PersistHeight = 0 })])
        {
            AirPrintingDetector detector = new(options);
            List<int> growth = [];
            List<double> bed = [];
            DetectorRun run = DetectorHarness.Run(detector, Observed(samples, detector, growth, bed), DetectionRegion.CoreOneBed, [DefaultThreshold]);
            int above = run.Scores.Count(s => s.Score >= DefaultThreshold);
            (TimeSpan at, float max) = run.Scores.Count > 0 ? run.Scores.MaxBy(static s => s.Score) : default;
            string summary = string.Create(
                CultureInfo.InvariantCulture,
                $"air-printing ({name}), job 354 {frames[0].Utc:HH:mm}–{frames[^1].Utc:HH:mm} UTC, Z {samples[0].Z:0.0}–{samples[^1].Z:0.0} mm, a frame every {everySeconds} s: {run.Scores.Count} of {samples.Count} samples judged, growth tiles {Stats(growth)}, bed shift {Stats(bed)} px, max score {max:P0} at {at}, mean {(run.Scores.Count > 0 ? run.Scores.Average(static s => s.Score) : 0):P0}, {above} at or above {DefaultThreshold:P0}, alarm {run.FirstAlarm?.ToString() ?? "none"}");
            LocalSamples.Report(summary);
            Assert.True(run.Scores.Count >= samples.Count / 6, summary);
            Assert.True(run.FirstAlarm is null && above == 0, summary);
        }
    }

    /// <summary>
    /// The 4.4 h series of T-152 (<c>coreone-frames</c>, a frame every 30 s, another successful Core One print) has no record
    /// of Z. Its height is an estimate (T-167 phase 1): the bed moves ~10 px per hour in its frames, at ~0.5–0.7 px/mm that is
    /// ~0.3 mm/min, from ~15 mm at the first frame (the part is ~15 mm tall there). The detector measures the bed's shift in
    /// the image itself, the height only picks the references.
    /// </summary>
    [Fact]
    public void TheLongSeriesWithAnEstimatedHeightRaisesNoAlarm()
    {
        string folder = Path.Combine(LocalSamples.Folder, "coreone-frames");
        Assert.SkipUnless(Directory.Exists(folder), $"The local series {folder} is missing.");
        string ffmpeg = LocalSamples.FFmpeg();
        DateTime start = new(2026, 11, 3, 18, 0, 0);
        DateTime end = new(2026, 11, 3, 22, 36, 13);
        (DateTime Taken, string Path)[] frames =
        [
            .. Directory.GetFiles(folder, "frame-*.jpg")
                .Select(static path => (Taken: LocalSamples.TakenAt(path), Path: path))
                .Where(f => f.Taken >= start && f.Taken <= end)
                .OrderBy(static f => f.Taken),
        ];
        Assert.SkipWhen(frames.Length < 100, $"The local series {folder} has only {frames.Length} frames of the print.");
        byte[] gray = LocalSamples.Decode(ffmpeg, LocalSamples.ConcatList(frames.Select(static f => f.Path)), "-f", "concat", "-safe", "0");
        Assert.Equal(frames.Length * 57600, gray.Length);
        List<(TimeSpan Time, byte[] Pixels, double? Z)> samples =
        [
            .. frames.Select((f, i) => (f.Taken - frames[0].Taken, gray.AsSpan(i * 57600, 57600).ToArray(), (double?)Math.Round(15 + (0.3 * (f.Taken - frames[0].Taken).TotalMinutes), 1))),
        ];

        AirPrintingDetector detector = new();
        List<int> growth = [];
        List<double> bed = [];
        DetectorRun run = DetectorHarness.Run(detector, Observed(samples, detector, growth, bed), DetectionRegion.CoreOneBed, [DefaultThreshold]);
        // A frame every 30 s is longer than the confirmation window (16 s): one score at or above the threshold counts.
        int above = run.Scores.Count(s => s.Score >= DefaultThreshold);
        (TimeSpan at, float max) = run.Scores.Count > 0 ? run.Scores.MaxBy(static s => s.Score) : default;
        string summary = string.Create(
            CultureInfo.InvariantCulture,
            $"air-printing, series {frames[0].Taken:HH:mm}–{frames[^1].Taken:HH:mm} (estimated Z {samples[0].Z:0}–{samples[^1].Z:0} mm), {run.Scores.Count} of {samples.Count} samples judged, growth tiles {Stats(growth)}, bed shift {Stats(bed)} px, max score {max:P0} at {at}, {above} at or above {DefaultThreshold:P0}");
        LocalSamples.Report(summary);
        Assert.True(run.Scores.Count > samples.Count / 2, summary);
        Assert.True(above == 0, summary);
    }

    private static IEnumerable<(TimeSpan Time, byte[] Pixels, double? Z)> Observed(
        IEnumerable<(TimeSpan Time, byte[] Pixels, double? Z)> samples, AirPrintingDetector detector, List<int> growth, List<double> bed)
    {
        foreach ((TimeSpan Time, byte[] Pixels, double? Z) sample in samples)
        {
            yield return sample;
            if (detector.GrowthTiles > 0)
            {
                growth.Add(detector.GrowthTiles);
            }

            if (!double.IsNaN(detector.BedShift))
            {
                bed.Add(detector.BedShift);
            }
        }
    }

    private static string Stats<T>(List<T> values)
        where T : IComparable<T>, IFormattable
    {
        if (values.Count == 0)
        {
            return "none";
        }

        T[] sorted = [.. values.Order()];
        return string.Create(CultureInfo.InvariantCulture, $"{sorted[0]:0.##}/{sorted[sorted.Length / 2]:0.##}/{sorted[^1]:0.##} (min/median/max)");
    }

    /// <summary>The height reported at <paramref name="utc"/>: the latest record at or before it (a poll), or none.</summary>
    private static double? ZAt(ZRecord[] records, DateTime utc)
    {
        int index = Array.FindLastIndex(records, r => r.Utc <= utc);
        return index < 0 ? null : records[index].Z;
    }

    private static ZRecord[] LoadZ()
    {
        string path = Path.Combine(LocalSamples.Folder, ZRecording);
        Assert.SkipUnless(File.Exists(path), $"The local Z recording {path} is missing.");
        return
        [
            .. File.ReadLines(path).Skip(1).Select(static line => line.Split(',')).Select(static p => new ZRecord(
                DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(p[0], CultureInfo.InvariantCulture)).UtcDateTime,
                p[1],
                double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double z) ? z : null)),
        ];
    }

    private sealed record ZRecord(DateTime Utc, string State, double? Z);
}
