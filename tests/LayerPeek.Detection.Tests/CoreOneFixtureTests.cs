using LayerPeek.Core.Detection;
using LayerPeek.Tests.Detection;

namespace LayerPeek.Detection.Tests;

/// <summary>
/// T-153: the Core One fixtures as raw 320×180 grayscale samples (Fixtures/coreone/README.md) and the sample a detector gets
/// from them with the Core One bed region (T-155 builds its tests on this).
/// </summary>
public sealed class CoreOneFixtureTests
{
    public static TheoryData<string> Fixtures => [.. CoreOneFixtures.Names];

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EveryFixtureIsAGraySampleOfALitChamber(string name)
    {
        byte[] pixels = CoreOneFixtures.Load(name);

        Assert.Equal(DetectionFrameSpec.Default.Width * DetectionFrameSpec.Default.Height, pixels.Length);
        // The chamber light is on (T-152: mean 63–79), far above the darkness threshold of the orchestration.
        Assert.InRange(pixels.Average(static b => (double)b), 55, 90);
    }

    [Fact]
    public void ADetectorSeesOnlyTheBedRegion()
    {
        byte[] pixels = CoreOneFixtures.Load(CoreOneFixtures.Names[0]);
        DetectionPixelRegion region = DetectionRegion.CoreOneBed.ToPixels(DetectionFrameSpec.Default.Width, DetectionFrameSpec.Default.Height);
        RegionMeanDetector detector = new();
        PrintFailureContext context = new(1, 0.2, 10, TimeSpan.FromMinutes(13), DetectionRegion.CoreOneBed);
        detector.Reset(context);

        DetectionResult result = detector.Analyze(new DetectionSample(pixels, 320, 180, 320, TimeSpan.Zero, region, context));

        double inside = 0;
        for (int y = 99; y < 180; y++)
        {
            for (int x = 48; x < 272; x++)
            {
                inside += pixels[(y * 320) + x];
            }
        }

        inside /= 224 * 81;
        double full = pixels.Average(static b => (double)b);
        Assert.Equal(new DetectionPixelRegion(48, 99, 224, 81), region);
        Assert.True(result.IsWarmedUp);
        Assert.Equal(inside, result.Score * 255, precision: 3);
        // The front of the bed (sheet and part) differs from the whole image (frame, LED strip, head).
        Assert.True(Math.Abs(inside - full) > 2, $"Region mean {inside:0.0}, whole frame {full:0.0}.");
    }

    /// <summary>Test-only: the score is the mean brightness of the region.</summary>
    private sealed class RegionMeanDetector : IPrintFailureDetector
    {
        public void Reset(in PrintFailureContext context)
        {
        }

        public DetectionResult Analyze(in DetectionSample sample)
        {
            long sum = 0;
            for (int y = sample.Region.Y; y < sample.Region.Bottom; y++)
            {
                foreach (byte value in sample.Row(y).Slice(sample.Region.X, sample.Region.Width))
                {
                    sum += value;
                }
            }

            return new DetectionResult(true, (float)sum / sample.Region.Area / 255);
        }
    }
}
