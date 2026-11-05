using LayerPeek.Core.Detection;

namespace LayerPeek.Tests.Detection;

/// <summary>
/// A synthetic print made of the committed fixtures only (user decision 2026-11-04: generated in test code, nothing new in
/// the repository): the six consecutive key frames of the Core One clip, cycled as the moving head and X bridge over the
/// still bed and part, sampled every <see cref="Interval"/> (plus the jitter of waiting for the next key frame), with
/// optional H.264-like noise, a brightness course and a failure that starts at <see cref="FailureStart"/>.
/// </summary>
internal sealed record SyntheticPrint
{
    public const int Width = 320;

    public const int Height = 180;

    /// <summary>The sampling interval of the orchestration (T-153: 2–5 s).</summary>
    public required TimeSpan Interval { get; init; }

    public TimeSpan Duration { get; init; } = TimeSpan.FromMinutes(12);

    /// <summary>When the failure appears; long after the warm-up so that the whole reference ring is in use.</summary>
    public TimeSpan FailureStart { get; init; } = TimeSpan.FromMinutes(6);

    /// <summary>The failure (null: a successful print).</summary>
    public SyntheticFailure? Failure { get; init; }

    /// <summary>Brightness factor over time (null: constant light).</summary>
    public Func<TimeSpan, double>? Brightness { get; init; }

    /// <summary>Noise of the stream's compression (0: none), standard deviation in gray levels of the pixel noise.</summary>
    public double Noise { get; init; } = 2.5;

    public DetectionRegion Region { get; init; } = DetectionRegion.CoreOneBed;

    public int Seed { get; init; } = 1;

    /// <summary>The key frames the print is made of (null: the fixture itself, <see cref="SyntheticScene.KeyFrames"/>; T-163: <see cref="DarkScene"/>).</summary>
    public SyntheticBackdrop? Backdrop { get; init; }

    /// <summary>The samples in time order: the frame is reused, valid until the next one is produced.</summary>
    public IEnumerable<(TimeSpan Time, byte[] Pixels)> Samples()
    {
        Random random = new(Seed);
        byte[] frame = new byte[Width * Height];
        TimeSpan time = TimeSpan.Zero;
        IReadOnlyList<byte[]> keyFrames = Backdrop?.KeyFrames ?? SyntheticScene.KeyFrames;
        int index = random.Next(keyFrames.Count);
        while (time <= Duration)
        {
            keyFrames[index].CopyTo(frame, 0);
            if (Failure is { } failure && time >= FailureStart)
            {
                failure.Apply(frame, time - FailureStart);
            }

            if (Brightness is { } brightness)
            {
                SyntheticScene.Scale(frame, brightness(time));
            }

            if (Noise > 0)
            {
                SyntheticScene.AddCompressionNoise(frame, Noise, random);
            }

            yield return (time, frame);

            // The orchestration takes the first key frame after the interval (Buddy3D: one every ~2.3 s).
            time += Interval + TimeSpan.FromMilliseconds(random.Next(0, 600));
            index = (index + 1 + random.Next(2)) % keyFrames.Count;
        }
    }
}

/// <summary>The fixture frames the synthetic prints are made of, and image operations on them.</summary>
internal static class SyntheticScene
{
    private static readonly Lazy<byte[]> LazyMedian = new(ComputeMedian);
    private static readonly Lazy<bool[]> LazyPart = new(ComputePart);

    /// <summary>Six key frames 2.3 s apart: the head and the X bridge move, the bed and the part do not.</summary>
    public static IReadOnlyList<byte[]> KeyFrames { get; } = [.. CoreOneFixtures.Names.Where(static n => n.StartsWith("clip-", StringComparison.Ordinal)).Select(CoreOneFixtures.Load)];

    /// <summary>Per-pixel median of <see cref="KeyFrames"/>: the scene without most of the head.</summary>
    public static byte[] Median => LazyMedian.Value;

    /// <summary>
    /// The printed part (silver on the black sheet, T-152: ~200 against ~40–60): bright pixels of <see cref="Median"/> in
    /// the box around it.
    /// </summary>
    public static bool[] Part => LazyPart.Value;

    /// <summary>Box the part lies in (pixels of the 320×180 frame).</summary>
    public static (int X, int Y, int Width, int Height) PartBox { get; } = (110, 55, 95, 85);

    /// <summary>Multiplies every pixel by <paramref name="factor"/> (clamped to 0–255).</summary>
    public static void Scale(byte[] frame, double factor)
    {
        for (int i = 0; i < frame.Length; i++)
        {
            frame[i] = Clamp(frame[i] * factor);
        }
    }

    /// <summary>
    /// What a ~1 Mbit/s H.264 stream adds: pixel noise of standard deviation <paramref name="sigma"/> and a small random
    /// offset per 8×8 block (blocking). One of <see cref="NoisePatternCount"/> precomputed unit patterns, scaled, at a
    /// random offset (generating Gaussian noise per sample would dominate the run time of the test sets).
    /// </summary>
    public static void AddCompressionNoise(byte[] frame, double sigma, Random random)
    {
        float[] pattern = NoisePatterns.Value[random.Next(NoisePatternCount)];
        int offset = random.Next(pattern.Length - frame.Length);
        float scale = (float)sigma;
        for (int i = 0; i < frame.Length; i++)
        {
            frame[i] = (byte)Math.Clamp((int)MathF.Round(frame[i] + (pattern[offset + i] * scale)), 0, 255);
        }
    }

    private const int NoisePatternCount = 16;

    private static readonly Lazy<float[][]> NoisePatterns = new(static () =>
    {
        Random random = new(4242);
        float[][] patterns = new float[NoisePatternCount][];
        for (int p = 0; p < patterns.Length; p++)
        {
            // Two frames long, so that a random offset still covers one frame.
            float[] pattern = new float[2 * SyntheticPrint.Width * SyntheticPrint.Height];
            for (int i = 0; i < pattern.Length; i++)
            {
                pattern[i] = (float)Gaussian(random);
            }

            for (int row = 0; row < pattern.Length / SyntheticPrint.Width; row += 8)
            {
                for (int bx = 0; bx < SyntheticPrint.Width; bx += 8)
                {
                    float block = (float)((random.NextDouble() - 0.5) * 2);
                    for (int y = row; y < Math.Min(row + 8, pattern.Length / SyntheticPrint.Width); y++)
                    {
                        for (int x = bx; x < bx + 8; x++)
                        {
                            pattern[(y * SyntheticPrint.Width) + x] += block;
                        }
                    }
                }
            }

            patterns[p] = pattern;
        }

        return patterns;
    });

    public static byte Clamp(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);

    private static double Gaussian(Random random)
    {
        // Box–Muller.
        double u = 1 - random.NextDouble();
        double v = random.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v);
    }

    private static byte[] ComputeMedian() => MedianOf(KeyFrames);

    /// <summary>Per-pixel median of <paramref name="frames"/>.</summary>
    public static byte[] MedianOf(IReadOnlyList<byte[]> frames)
    {
        byte[] median = new byte[SyntheticPrint.Width * SyntheticPrint.Height];
        Span<byte> values = stackalloc byte[frames.Count];
        for (int i = 0; i < median.Length; i++)
        {
            for (int f = 0; f < frames.Count; f++)
            {
                values[f] = frames[f][i];
            }

            values.Sort();
            median[i] = values[values.Length / 2];
        }

        return median;
    }

    private static bool[] ComputePart()
    {
        bool[] part = new bool[SyntheticPrint.Width * SyntheticPrint.Height];
        (int bx, int by, int bw, int bh) = PartBox;
        for (int y = by; y < by + bh; y++)
        {
            for (int x = bx; x < bx + bw; x++)
            {
                int i = (y * SyntheticPrint.Width) + x;
                part[i] = Median[i] >= 140;
            }
        }

        return part;
    }
}
