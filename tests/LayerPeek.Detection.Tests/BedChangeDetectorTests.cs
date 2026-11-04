using System.Diagnostics;

using LayerPeek.Core.Detection;
using LayerPeek.Tests.Allocation;
using LayerPeek.Tests.Detection;

namespace LayerPeek.Detection.Tests;

/// <summary>
/// T-155: the behaviour of the bed-change detector on the Core One fixtures: its registration, warm-up in time, the first
/// layers, the region, the brightness, a bed moving in Z, and no allocation per sample.
/// </summary>
public sealed class BedChangeDetectorTests
{
    private const int Width = SyntheticPrint.Width;
    private const int Height = SyntheticPrint.Height;

    private static readonly Lump BigLump = new(Seed: 1, CenterX: 160, CenterY: 150, Radius: 14, Luma: 225, Growth: TimeSpan.Zero);

    [Fact]
    public void TheAppOffersItWithItsThresholds()
    {
        PrintFailureDetectorRegistration registration = Assert.Single(PrintFailureDetectors.All);

        Assert.Same(BedChangeDetector.Registration, registration);
        Assert.Equal("bed-change", registration.Id);
        Assert.Equal(DetectionFrameSpec.Default, registration.FrameSpec);
        Assert.Equal(0.005f, registration.Thresholds.ThresholdFor(DetectorSettings.MinSensitivity));
        Assert.Equal(0.003f, registration.Thresholds.ThresholdFor(DetectorSettings.DefaultSensitivity), 6);
        Assert.Equal(0.001f, registration.Thresholds.ThresholdFor(DetectorSettings.MaxSensitivity));
        Assert.IsType<BedChangeDetector>(registration.Create());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public void ItJudgesOnlyAfterTheWarmUpWhateverTheInterval(int interval)
    {
        BedChangeDetector detector = new();
        Session session = new(detector, DetectionRegion.CoreOneBed);

        for (TimeSpan time = TimeSpan.Zero; time < BedChangeOptions.Default.WarmUp; time += TimeSpan.FromSeconds(interval))
        {
            Assert.False(session.Analyze(SyntheticScene.Median, time).IsWarmedUp, $"Warm at {time}.");
        }

        DetectionResult warm = session.Analyze(SyntheticScene.Median, BedChangeOptions.Default.WarmUp);
        Assert.True(warm.IsWarmedUp);
        Assert.Equal(0, warm.Score);
    }

    [Fact]
    public void TheFirstLayersAreLearnedNotJudged()
    {
        BedChangeDetector detector = new();
        Session session = new(detector, DetectionRegion.CoreOneBed) { AxisZ = 0.2 };
        TimeSpan time = TimeSpan.Zero;
        for (; time < TimeSpan.FromMinutes(5); time += TimeSpan.FromSeconds(2))
        {
            // A Z hop on the first layer is still the first layers.
            session.AxisZ = time.TotalSeconds % 20 < 2 ? 0.8 : 0.2;
            Assert.False(session.Analyze(SyntheticScene.Median, time).IsWarmedUp);
        }

        // Above them the warm-up starts.
        session.AxisZ = 1.2;
        TimeSpan above = time;
        for (; time < above + BedChangeOptions.Default.WarmUp; time += TimeSpan.FromSeconds(2))
        {
            Assert.False(session.Analyze(SyntheticScene.Median, time).IsWarmedUp);
        }

        Assert.True(session.Analyze(SyntheticScene.Median, time).IsWarmedUp);
        // Not again when a later sample reads lower (a resumed print with an old height, a Z hop read wrongly).
        session.AxisZ = 0.4;
        Assert.True(session.Analyze(SyntheticScene.Median, time + TimeSpan.FromSeconds(2)).IsWarmedUp);
    }

    [Fact]
    public void ResetStartsOver()
    {
        BedChangeDetector detector = new();
        Session session = new(detector, DetectionRegion.CoreOneBed);
        TimeSpan time = session.WarmUp(SyntheticScene.Median, TimeSpan.Zero);
        Assert.True(session.Analyze(SyntheticScene.Median, time).IsWarmedUp);

        detector.Reset(session.Context);

        Assert.False(session.Analyze(SyntheticScene.Median, time + TimeSpan.FromSeconds(2)).IsWarmedUp);
    }

    [Fact]
    public void ALumpOnTheBedScoresAndTheSameLumpScoresTheSameInAnyRegionThatHoldsIt()
    {
        float core = ScoreOfLump(DetectionRegion.CoreOneBed);
        float full = ScoreOfLump(DetectionRegion.Full);
        float small = ScoreOfLump(new DetectionRegion(0.4, 0.75, 0.2, 0.2));

        // Radius 14 px, flattened: ~370 px of 18 144 (the score area).
        Assert.InRange(core, 0.01f, 0.04f);
        Assert.Equal(core, full, 0.001f);
        Assert.Equal(core, small, 0.001f);
    }

    [Fact]
    public void AChangeOutsideTheRegionDoesNotCount()
    {
        // The lump lies at the front of the bed; the region is the top left corner.
        Assert.Equal(0, ScoreOfLump(new DetectionRegion(0, 0, 0.3, 0.3)));
    }

    [Theory]
    [InlineData(0.75)]
    [InlineData(1.3)]
    public void ABrighterOrDimmerLightIsNoChange(double factor)
    {
        BedChangeDetector detector = new();
        Session session = new(detector, DetectionRegion.Full);
        TimeSpan time = session.WarmUp(SyntheticScene.Median, TimeSpan.Zero);
        byte[] changed = [.. SyntheticScene.Median];
        SyntheticScene.Scale(changed, factor);

        float max = 0;
        for (TimeSpan end = time + TimeSpan.FromMinutes(6); time < end; time += TimeSpan.FromSeconds(2))
        {
            max = Math.Max(max, session.Analyze(changed, time).Score);
        }

        Assert.True(max < BedChangeDetector.Registration.Thresholds.MostSensitive, $"Score {max:P2} after the light changed by {factor}.");
    }

    [Fact]
    public void TheReferencesFollowAFastRisingBed()
    {
        // A small part with short layers: the bed sinks 3 mm a minute, the image ~2 px a minute (~0.64 px per mm).
        float withHeight = MaxScoreOfSinkingBed(reportHeight: true);
        float withoutHeight = MaxScoreOfSinkingBed(reportHeight: false);

        Assert.True(withHeight < BedChangeDetector.Registration.Thresholds.MostSensitive, $"Score {withHeight:P2} with the Z height.");
        // Without the height the references are minutes old: the edges have moved past the tolerance.
        Assert.True(withoutHeight > BedChangeDetector.Registration.Thresholds.LeastSensitive, $"Score {withoutHeight:P2} without the Z height.");
    }

    [Fact]
    public void ANewRegionStartsOver()
    {
        BedChangeDetector detector = new();
        Session first = new(detector, new DetectionRegion(0.1, 0.5, 0.4, 0.4));
        TimeSpan time = first.WarmUp(SyntheticScene.Median, TimeSpan.Zero);
        Session second = new(detector, new DetectionRegion(0.5, 0.5, 0.4, 0.4));

        Assert.False(second.Analyze(SyntheticScene.Median, time).IsWarmedUp);
    }

    [Fact]
    public void TheOptionsAreValidated()
    {
        BedChangeOptions valid = BedChangeOptions.Default;
        Assert.Throws<ArgumentOutOfRangeException>(() => new BedChangeDetector(valid with { Rate = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BedChangeDetector(valid with { ReferenceCount = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BedChangeDetector(valid with { Quiet = TimeSpan.FromMinutes(5) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BedChangeDetector(valid with { ChangeLevel = 255 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BedChangeDetector(valid with { ScoreArea = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BedChangeDetector(valid with { ReferenceZSpacing = 0 }));
    }

    private static float ScoreOfLump(DetectionRegion region)
    {
        BedChangeDetector detector = new();
        Session session = new(detector, region);
        TimeSpan time = session.WarmUp(SyntheticScene.Median, TimeSpan.Zero);
        byte[] failed = [.. SyntheticScene.Median];
        BigLump.Apply(failed, TimeSpan.Zero);
        float max = 0;
        for (TimeSpan end = time + TimeSpan.FromMinutes(3); time < end; time += TimeSpan.FromSeconds(2))
        {
            max = Math.Max(max, session.Analyze(failed, time).Score);
        }

        return max;
    }

    private static float MaxScoreOfSinkingBed(bool reportHeight)
    {
        BedChangeDetector detector = new();
        Session session = new(detector, DetectionRegion.CoreOneBed) { AxisZ = reportHeight ? 5 : null };
        byte[] frame = new byte[Width * Height];
        float max = 0;
        for (TimeSpan time = TimeSpan.Zero; time < TimeSpan.FromMinutes(10); time += TimeSpan.FromSeconds(2))
        {
            double z = 5 + (time.TotalMinutes * 3);
            session.AxisZ = reportHeight ? z : null;
            Shift(SyntheticScene.Median, frame, (z - 5) * 0.64);
            max = Math.Max(max, session.Analyze(frame, time).Score);
        }

        return max;
    }

    /// <summary>
    /// The scene moved down by <paramref name="rows"/>, also by a fraction of a pixel (interpolated, as a bed sinking
    /// continuously looks to the camera); the top rows repeat the first one.
    /// </summary>
    private static void Shift(byte[] source, byte[] target, double rows)
    {
        int whole = (int)Math.Floor(rows);
        double fraction = rows - whole;
        for (int y = 0; y < Height; y++)
        {
            int near = Math.Clamp(y - whole, 0, Height - 1) * Width;
            int far = Math.Clamp(y - whole - 1, 0, Height - 1) * Width;
            for (int x = 0; x < Width; x++)
            {
                target[(y * Width) + x] = SyntheticScene.Clamp((source[near + x] * (1 - fraction)) + (source[far + x] * fraction));
            }
        }
    }

    /// <summary>Samples of one stretch of printing for a detector.</summary>
    private sealed class Session(IPrintFailureDetector detector, DetectionRegion region)
    {
        private readonly DetectionPixelRegion _pixels = region.ToPixels(Width, Height);

        public double? AxisZ { get; set; }

        public PrintFailureContext Context => new(1, AxisZ, 0, TimeSpan.Zero, region);

        public DetectionResult Analyze(byte[] frame, TimeSpan time) =>
            detector.Analyze(new DetectionSample(frame, Width, Height, Width, time, _pixels, Context with { PrintElapsed = time }));

        /// <summary>Samples <paramref name="frame"/> every 2 s until warm and the reference ring is full; returns the next time.</summary>
        public TimeSpan WarmUp(byte[] frame, TimeSpan start)
        {
            TimeSpan end = start + BedChangeOptions.Default.WarmUp + (BedChangeOptions.Default.ReferenceSpacing * BedChangeOptions.Default.ReferenceCount);
            TimeSpan time = start;
            for (; time < end; time += TimeSpan.FromSeconds(2))
            {
                Analyze(frame, time);
            }

            return time;
        }
    }
}

/// <summary>T-155: no allocation per sample (steady state) and the time per sample (written to the test output).</summary>
[Collection(nameof(AllocationSensitive))]
public sealed class BedChangeDetectorCostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASampleAllocatesNothing(bool fullFrame)
    {
        DetectionRegion region = fullFrame ? DetectionRegion.Full : DetectionRegion.CoreOneBed;
        BedChangeDetector detector = new();
        IEnumerator<(TimeSpan Time, byte[] Pixels)> samples = new SyntheticPrint { Interval = TimeSpan.FromSeconds(2), Region = region, Duration = TimeSpan.FromHours(1) }.Samples().GetEnumerator();
        DetectionPixelRegion pixels = region.ToPixels(SyntheticPrint.Width, SyntheticPrint.Height);
        PrintFailureContext context = new(1, 5, 50, TimeSpan.Zero, region);
        detector.Reset(context);

        void Next()
        {
            samples.MoveNext();
            (TimeSpan time, byte[] frame) = samples.Current;
            detector.Analyze(new DetectionSample(frame, SyntheticPrint.Width, SyntheticPrint.Height, SyntheticPrint.Width, time, pixels, context));
        }

        // Past the warm-up and the first references (the buffers are allocated with the first sample).
        for (int i = 0; i < 200; i++)
        {
            Next();
        }

        long allocated = AllocationProbe.MinAllocated(() =>
        {
            (TimeSpan time, byte[] frame) = (samples.Current.Time, samples.Current.Pixels);
            return AllocationProbe.Measure(() =>
            {
                for (int i = 0; i < 50; i++)
                {
                    time += TimeSpan.FromSeconds(2);
                    detector.Analyze(new DetectionSample(frame, SyntheticPrint.Width, SyntheticPrint.Height, SyntheticPrint.Width, time, pixels, context));
                }
            });
        });

        Assert.Equal(0, allocated);

        // Time per sample (Debug build of the tests; the task notes have the Release / Native AOT figures).
        Stopwatch watch = Stopwatch.StartNew();
        for (int i = 0; i < 200; i++)
        {
            Next();
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"bed-change, {(fullFrame ? "whole frame" : "Core One region")}: {watch.Elapsed.TotalMilliseconds / 200:0.000} ms per sample (with generating it).");
    }
}
