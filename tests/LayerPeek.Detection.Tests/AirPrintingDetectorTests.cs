using System.Diagnostics;

using LayerPeek.Core.Detection;
using LayerPeek.Core.Printers;
using LayerPeek.Tests.Allocation;
using LayerPeek.Tests.Detection;

namespace LayerPeek.Detection.Tests;

/// <summary>
/// T-167: the behaviour of the air-printing detector: its registration, the height of the Z axis (Z hops, the start
/// sequence, sequential printing, no height), standing Z, resets, gaps between samples, the analysed area and a failure
/// and a good print of the synthetic Core One.
/// </summary>
public sealed class AirPrintingDetectorTests
{
    private const int Width = SyntheticGrowingPrint.Width;
    private const int Height = SyntheticGrowingPrint.Height;

    /// <summary>A fast print, so that a failure and the warm-up fit into a short run.</summary>
    private static SyntheticGrowingPrint FastPrint(int interval = 5) => new()
    {
        Interval = TimeSpan.FromSeconds(interval),
        Rate = 1,
        Layer = 0.2,
        FinalHeight = 22,
        StartSequence = false,
        Parts = [SyntheticPart.Box() with { Length = 90, FrontX = 110, FrontY = 105 }],
        Seed = 3,
    };

    [Fact]
    public void TheAppOffersItOffByDefaultAndExperimentalForBedsMovingInZ()
    {
        PrintFailureDetectorRegistration registration = Assert.Single(PrintFailureDetectors.All, static r => r.Id == "air-printing");

        Assert.Same(AirPrintingDetector.Registration, registration);
        Assert.Equal(BedChangeDetector.DetectorId, PrintFailureDetectors.All[0].Id);
        Assert.Equal(DetectionFrameSpec.Default, registration.FrameSpec);
        Assert.False(registration.EnabledByDefault);
        Assert.True(registration.IsExperimental);
        Assert.False(registration.DefaultSettings.Enabled);
        Assert.True(registration.IsOfferedFor(PrinterModel.CoreOne));
        Assert.True(registration.IsOfferedFor(PrinterModel.CoreOneL));
        Assert.True(registration.IsOfferedFor(PrinterModel.Xl));
        Assert.True(registration.IsOfferedFor(PrinterModel.XlPlus));
        Assert.False(registration.IsOfferedFor(PrinterModel.Mk4S));
        Assert.False(registration.IsOfferedFor(PrinterModel.Mini));
        Assert.False(registration.IsOfferedFor(PrinterModel.Unknown));
        Assert.True(registration.Thresholds.ThresholdFor(DetectorSettings.MaxSensitivity) < registration.Thresholds.ThresholdFor(DetectorSettings.DefaultSensitivity));
        Assert.IsType<AirPrintingDetector>(registration.Create());
    }

    [Theory]
    [InlineData(0.2)]
    [InlineData(1.5)]
    public void AZHopAddsNoHeight(double hop)
    {
        Session session = new(new AirPrintingDetector());
        TimeSpan time = TimeSpan.Zero;
        double z = 5;
        for (int i = 0; i < 600; i++, time += TimeSpan.FromSeconds(2))
        {
            // A hop of 2–4 s every 30 s, the layer 0.2 mm every minute.
            z = 5 + (0.2 * (i / 30));
            bool hopping = i % 15 is 3 or 4;
            session.Analyze(SyntheticScene.Median, time, hopping ? z + hop : z);
            // The layer, or the one below for the 15 s after a layer change; never the hop.
            double level = session.Detector.Level!.Value;
            Assert.True(level <= z + 1e-9 && level >= z - 0.2 - 1e-9, $"Level {level} mm at Z {z} mm.");
        }

        Assert.Equal(0, session.Detector.NewBases);
    }

    [Fact]
    public void TheStartSequenceAndSequentialPrintingStartOver()
    {
        Session session = new(new AirPrintingDetector());
        TimeSpan time = TimeSpan.Zero;

        // Job 354: 40 mm while heating, 15 mm, the bed mesh near 0, then the first layer.
        foreach ((double z, int seconds) in (ReadOnlySpan<(double, int)>)[(40, 120), (15, 30), (2, 6), (0, 6), (0.6, 20), (0.2, 60)])
        {
            for (int s = 0; s < seconds; s += 2, time += TimeSpan.FromSeconds(2))
            {
                session.Analyze(SyntheticScene.Median, time, z);
            }
        }

        int bases = session.Detector.NewBases;
        Assert.True(bases >= 2, $"{bases} new bases.");
        Assert.Equal(0.2, session.Detector.Level!.Value, 9);

        // Up to 12 mm, then the next object of a sequential print from the bed again.
        for (double z = 0.4; z <= 12; z += 0.2, time += TimeSpan.FromSeconds(12))
        {
            session.Analyze(SyntheticScene.Median, time, z);
        }

        Assert.Equal(bases, session.Detector.NewBases);
        for (int s = 0; s < 30; s += 2, time += TimeSpan.FromSeconds(2))
        {
            Assert.False(session.Analyze(SyntheticScene.Median, time, 0.2).IsWarmedUp);
        }

        Assert.Equal(bases + 1, session.Detector.NewBases);
    }

    [Fact]
    public void WithoutAHeightItNeverJudges()
    {
        DetectorRun run = DetectorHarness.Run(
            new AirPrintingDetector(),
            FastPrint().Samples().Select(static s => (s.Time, s.Pixels, (double?)null)),
            DetectionRegion.CoreOneBed,
            [0f]);

        Assert.Empty(run.Scores);
    }

    [Fact]
    public void ABedThatDoesNotMoveInTheImageIsNeverJudged()
    {
        // A printer whose bed does not move in Z (or a camera that moves with the bed).
        DetectorRun run = DetectorHarness.Run(
            new AirPrintingDetector(),
            (FastPrint() with { KScale = 0.01, FailureHeight = 10 }).Samples(),
            DetectionRegion.CoreOneBed,
            [0f]);

        Assert.Empty(run.Scores);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public void AGoodPrintRaisesNothingAndAFailureIsFound(int interval)
    {
        float threshold = AirPrintingSweep.DefaultThreshold;
        DetectorRun good = DetectorHarness.Run(new AirPrintingDetector(), FastPrint(interval).Samples(), DetectionRegion.CoreOneBed, [threshold]);
        SyntheticGrowingPrint failing = FastPrint(interval) with { FailureHeight = 12 };
        DetectorRun failed = DetectorHarness.Run(new AirPrintingDetector(), failing.Samples(), DetectionRegion.CoreOneBed, [threshold]);

        Assert.NotEmpty(good.Scores);
        Assert.Null(good.FirstAlarm);
        TimeSpan failure = failing.FailureTime!.Value;
        Assert.True(failed.FirstAlarm > failure, $"Alarm at {failed.FirstAlarm}, failure at {failure}, max score {failed.MaxScore():P0}.");
        // At 1 mm/min the alarm comes within ~5 mm of Z.
        Assert.True(failed.FirstAlarm - failure < TimeSpan.FromMinutes(5), $"Alarm at {failed.FirstAlarm}, failure at {failure}.");
    }

    [Fact]
    public void AStandingZAxisRaisesNothing()
    {
        // MMU tool changes: the Z axis stands for 4 min every millimetre while the head is at the wipe tower.
        SyntheticGrowingPrint print = FastPrint() with
        {
            Parts = [.. FastPrint().Parts, SyntheticPart.Box() with { Length = 14, FrontX = 230, FrontY = 85, IsWipeTower = true }],
            ToolChangeEvery = 1,
            ToolChange = TimeSpan.FromMinutes(4),
            FinalHeight = 16,
        };
        DetectorRun run = DetectorHarness.Run(new AirPrintingDetector(), print.Samples(), DetectionRegion.CoreOneBed, [AirPrintingSweep.DefaultThreshold]);

        Assert.NotEmpty(run.Scores);
        Assert.Null(run.FirstAlarm);
    }

    [Fact]
    public void AResetForgetsEverything()
    {
        // A new print, a resumed one, a reconnected camera or the light back on: the orchestration resets the detector.
        AirPrintingDetector detector = new();
        Session session = new(detector);
        foreach ((TimeSpan time, byte[] pixels, double? z) in FastPrint().Samples().Take(150))
        {
            session.Analyze(pixels, time, z);
        }

        Assert.True(session.Last.IsWarmedUp);
        detector.Reset(session.Context);
        byte[] dark = (byte[])SyntheticScene.Median.Clone();
        SyntheticScene.Scale(dark, 0.6);
        Assert.False(session.Analyze(dark, session.Time + TimeSpan.FromSeconds(5), session.Z).IsWarmedUp);
        Assert.False(session.Analyze(dark, session.Time + TimeSpan.FromMinutes(1), session.Z).IsWarmedUp);
        Assert.Equal(0, detector.GrowthTiles);
        Assert.Equal(0, detector.NewBases);
    }

    [Fact]
    public void DroppedSamplesAndLongGapsDoNotHarm()
    {
        // Every third sample dropped and a stalled stream of 3 min in the middle.
        static IEnumerable<(TimeSpan Time, byte[] Pixels, double? Z)> Thinned()
        {
            int i = 0;
            foreach ((TimeSpan Time, byte[] Pixels, double? Z) sample in FastPrint(2).Samples())
            {
                if (i++ % 3 != 2 && (sample.Time < TimeSpan.FromMinutes(12) || sample.Time > TimeSpan.FromMinutes(15)))
                {
                    yield return sample;
                }
            }
        }

        DetectorRun run = DetectorHarness.Run(new AirPrintingDetector(), Thinned(), DetectionRegion.CoreOneBed, [AirPrintingSweep.DefaultThreshold]);

        Assert.NotEmpty(run.Scores);
        Assert.Null(run.FirstAlarm);
    }

    [Fact]
    public void ItLooksUpToTheNozzleRowAboveTheBedRegion()
    {
        AirPrintingDetector detector = new();
        Session session = new(detector);
        session.Analyze(SyntheticScene.Median, TimeSpan.Zero, 5);

        // The Core One region starts at 55 % of the height; the area reaches the nozzle row (28 %).
        Assert.Equal(new DetectionPixelRegion(48, 50, 224, 130), detector.Area);

        Session whole = new(new AirPrintingDetector(), DetectionRegion.Full);
        whole.Analyze(SyntheticScene.Median, TimeSpan.Zero, 5);
        Assert.Equal(new DetectionPixelRegion(0, 0, Width, Height), whole.Detector.Area);
    }

    [Fact]
    public void OptionsAreValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AirPrintingDetector(AirPrintingOptions.Default with { NozzleRow = 1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AirPrintingDetector(AirPrintingOptions.Default with { CompareHeight = 0.1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AirPrintingDetector(AirPrintingOptions.Default with { StillShare = 0.6 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AirPrintingDetector(AirPrintingOptions.Default with { HeightSamples = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AirPrintingDetector(AirPrintingOptions.Default with { TileSize = 1 }));
    }

    /// <summary>One detector fed like the orchestration does.</summary>
    private sealed class Session(AirPrintingDetector detector, DetectionRegion? region = null)
    {
        public AirPrintingDetector Detector { get; } = detector;

        public PrintFailureContext Context { get; } = new(1, null, 0, TimeSpan.Zero, region ?? DetectionRegion.CoreOneBed);

        public TimeSpan Time { get; private set; }

        public double? Z { get; private set; }

        public DetectionResult Last { get; private set; }

        public DetectionResult Analyze(byte[] pixels, TimeSpan time, double? z)
        {
            Time = time;
            Z = z;
            Last = Detector.Analyze(new DetectionSample(pixels, Width, Height, Width, time, Context.Region.ToPixels(Width, Height), Context with { AxisZ = z, PrintElapsed = time }));
            return Last;
        }
    }
}

/// <summary>T-167: no allocation per sample (steady state) and the time per sample (written to the test output).</summary>
[Collection(nameof(AllocationSensitive))]
public sealed class AirPrintingDetectorCostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASampleAllocatesNothing(bool fullFrame)
    {
        DetectionRegion region = fullFrame ? DetectionRegion.Full : DetectionRegion.CoreOneBed;
        AirPrintingDetector detector = new();
        SyntheticGrowingPrint print = new() { Interval = TimeSpan.FromSeconds(2), Rate = 1, FinalHeight = 30, StartSequence = false };
        // Rendered first: generating a sample allocates (the enumerator), the detector must not.
        List<(TimeSpan Time, byte[] Pixels, double? Z)> samples = [.. print.Samples().Select(static s => (s.Time, (byte[])s.Pixels.Clone(), s.Z))];
        DetectionPixelRegion pixels = region.ToPixels(SyntheticGrowingPrint.Width, SyntheticGrowingPrint.Height);
        PrintFailureContext context = new(1, null, 50, TimeSpan.Zero, region);
        detector.Reset(context);
        int next = 0;

        void Analyze(int count)
        {
            for (int i = 0; i < count; i++, next++)
            {
                (TimeSpan time, byte[] frame, double? z) = samples[next];
                detector.Analyze(new DetectionSample(frame, SyntheticGrowingPrint.Width, SyntheticGrowingPrint.Height, SyntheticGrowingPrint.Width, time, pixels, context with { AxisZ = z }));
            }
        }

        // Past the warm-up, the references and the first judged samples (the buffers are allocated with the first sample).
        Analyze(400);
        Assert.True(detector.GrowthTiles > 0 || !double.IsNaN(detector.BedShift), "Not judging yet.");
        long allocated = AllocationProbe.MinAllocated(() => AllocationProbe.Measure(() => Analyze(50)));

        Assert.Equal(0, allocated);

        // Time per sample (Debug build of the tests; the task notes have the Release figures).
        Stopwatch watch = Stopwatch.StartNew();
        Analyze(200);
        LocalSamples.Report($"air-printing, {(fullFrame ? "whole frame" : "Core One region")}: {watch.Elapsed.TotalMilliseconds / 200:0.000} ms per sample.");
    }
}
