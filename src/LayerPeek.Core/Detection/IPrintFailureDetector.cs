using LayerPeek.Core.Cameras;

namespace LayerPeek.Core.Detection;

/// <summary>
/// One kind of print failure detection (T-153, e.g. "bed-change" of T-155), implemented in <c>LayerPeek.Detection</c>. An
/// instance belongs to one printer and one stretch of printing: <see cref="PrintFailureDetectionService"/> creates it when
/// the printer starts (or resumes) printing, calls <see cref="Reset"/>, then <see cref="Analyze"/> for every sample, and
/// drops it when the print pauses or ends. What it knows of the stream it learns from the samples: it never reads files,
/// opens connections or reads a clock (time is <see cref="DetectionSample.Time"/>), so it can be tested on fixtures
/// deterministically.
/// </summary>
/// <remarks>
/// Synchronous, CPU only and, once warm, without allocations per sample. Called on one thread at a time (a thread-pool
/// thread, never the decoder's or the UI thread). The orchestration does everything else: sampling, the region, the
/// darkness check, sensitivity to threshold, confirmation (K of N) and the alarm with its snapshot.
/// </remarks>
public interface IPrintFailureDetector
{
    /// <summary>
    /// Forgets everything learned (the stream may have changed): a new print, a resumed print, a reconnected camera or the
    /// light back after darkness. Until <see cref="DetectionResult.IsWarmedUp"/> again, no alarm is raised.
    /// </summary>
    void Reset(in PrintFailureContext context);

    /// <summary>
    /// Scores one sample: 0 (nothing suspicious) to 1, computed only inside <see cref="DetectionSample.Region"/>. The
    /// sample's pixels are borrowed until this returns.
    /// </summary>
    DetectionResult Analyze(in DetectionSample sample);
}

/// <summary>The result of <see cref="IPrintFailureDetector.Analyze"/>.</summary>
/// <param name="IsWarmedUp">The detector has seen enough of this print to judge; alarms are suppressed before.</param>
/// <param name="Score">0–1; compared with the threshold of the sensitivity (<see cref="DetectionThresholds"/>).</param>
public readonly record struct DetectionResult(bool IsWarmedUp, float Score);

/// <summary>
/// What a detector analyses (T-152 spike: 8-bit grayscale, 320×180): the orchestration asks the camera hub for frames of
/// exactly this size (<see cref="CameraGrayscaleSize"/>), downscaled while they are copied.
/// </summary>
public readonly record struct DetectionFrameSpec
{
    /// <summary>The sample of the spike (T-152) and of the first detector (T-155).</summary>
    public static DetectionFrameSpec Default { get; } = new(320, 180);

    /// <exception cref="ArgumentOutOfRangeException">A side is outside <see cref="CameraGrayscaleSize.MinSide"/>–<see cref="CameraGrayscaleSize.MaxSide"/>.</exception>
    public DetectionFrameSpec(int width, int height)
    {
        Size = new CameraGrayscaleSize(width, height);
    }

    public int Width => Size.Width;

    public int Height => Size.Height;

    internal CameraGrayscaleSize Size { get; }
}

/// <summary>
/// The print a sample belongs to (T-153), from the latest snapshot of the printer. Lets a detector tell e.g. the first
/// layer (low <see cref="AxisZ"/>) from the rest.
/// </summary>
/// <param name="JobId">The job (<see cref="Printers.PrintJob.Id"/>); <see langword="null"/> when unknown.</param>
/// <param name="AxisZ">Height of the Z axis in mm; <see langword="null"/> when unknown.</param>
/// <param name="ProgressPercent">Progress of the job, 0–100.</param>
/// <param name="PrintElapsed">Time since the printer started or resumed printing (since this detector's <see cref="IPrintFailureDetector.Reset"/> of a new stretch).</param>
/// <param name="Region">The bed region of the printer (normalized, <see cref="DetectionRegion"/>).</param>
public readonly record struct PrintFailureContext(
    long? JobId,
    double? AxisZ,
    double ProgressPercent,
    TimeSpan PrintElapsed,
    DetectionRegion Region);

/// <summary>A rectangle of a sample in whole pixels (<see cref="DetectionRegion.ToPixels"/>); never empty.</summary>
public readonly record struct DetectionPixelRegion(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public int Area => Width * Height;
}

/// <summary>
/// One grayscale sample for <see cref="IPrintFailureDetector.Analyze"/>. The pixels are borrowed (the orchestration reuses
/// the buffer for the next sample) and valid only until <c>Analyze</c> returns.
/// </summary>
public readonly ref struct DetectionSample
{
    public DetectionSample(ReadOnlySpan<byte> pixels, int width, int height, int stride, TimeSpan time, DetectionPixelRegion region, in PrintFailureContext context)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, width);
        if (pixels.Length < ((long)stride * (height - 1)) + width)
        {
            throw new ArgumentException("The sample needs more pixels.", nameof(pixels));
        }

        if (region.Width <= 0 || region.Height <= 0 || region.X < 0 || region.Y < 0 || region.Right > width || region.Bottom > height)
        {
            throw new ArgumentOutOfRangeException(nameof(region), region, "The region must lie inside the sample.");
        }

        Pixels = pixels;
        Width = width;
        Height = height;
        Stride = stride;
        Time = time;
        Region = region;
        Context = context;
    }

    /// <summary>8-bit luma, rows top-down, <see cref="Stride"/> bytes apart.</summary>
    public ReadOnlySpan<byte> Pixels { get; }

    public int Width { get; }

    public int Height { get; }

    public int Stride { get; }

    /// <summary>
    /// When the frame was taken, on a monotonic clock (only differences matter). Detectors measure time with it, never in
    /// samples: the sampling interval is 2–5 s and samples may be dropped.
    /// </summary>
    public TimeSpan Time { get; }

    /// <summary>The bed region in pixels of this sample; scores are computed only inside it.</summary>
    public DetectionPixelRegion Region { get; }

    public PrintFailureContext Context { get; }

    /// <summary>The pixels of row <paramref name="y"/>.</summary>
    public ReadOnlySpan<byte> Row(int y) => Pixels.Slice(y * Stride, Width);
}
