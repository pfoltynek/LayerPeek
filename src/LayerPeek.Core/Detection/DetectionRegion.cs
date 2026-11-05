using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Detection;

/// <summary>
/// The bed region of a printer's camera image (T-153, user decision 2026-11-04): a rectangle in coordinates normalized to
/// the frame (0–1 of its width and height), so it does not depend on the camera resolution or the sample size. Detectors
/// score only inside it. Always valid: the constructor clips it into the frame and keeps it at least
/// <see cref="MinSize"/> wide and high.
/// </summary>
public readonly record struct DetectionRegion
{
    /// <summary>Smallest width and height (1 % of the frame).</summary>
    public const double MinSize = 0.01;

    /// <summary>
    /// Region of the standard camera mount of the Core One (T-152 prototype): the front of the bed, the lower 45 % of the
    /// height and the middle 70 % of the width.
    /// </summary>
    public static DetectionRegion CoreOneBed { get; } = new(0.15, 0.55, 0.70, 0.45);

    /// <summary>The whole frame (the default).</summary>
    public static DetectionRegion Full { get; } = new(0, 0, 1, 1);

    /// <summary>
    /// The region clipped into the frame: <paramref name="x"/> and <paramref name="y"/> into 0–(1 − <see cref="MinSize"/>),
    /// the size into what is left (at least <see cref="MinSize"/>). Not-a-number values count as 0 (size: the whole rest).
    /// </summary>
    public DetectionRegion(double x, double y, double width, double height)
    {
        X = Clip(x, 0, 1 - MinSize, 0);
        Y = Clip(y, 0, 1 - MinSize, 0);
        Width = Clip(width, MinSize, 1 - X, 1 - X);
        Height = Clip(height, MinSize, 1 - Y, 1 - Y);
    }

    public double X { get; }

    public double Y { get; }

    public double Width { get; }

    public double Height { get; }

    /// <summary>Whether this is (practically) <see cref="Full"/>; <c>default</c> counts as full too.</summary>
    public bool IsFull => this == default || (X <= 1e-9 && Y <= 1e-9 && Width >= 1 - 1e-9 && Height >= 1 - 1e-9);

    /// <summary>
    /// The region a printer starts with when the user never set one (user decision 2026-11-04, proposal of the architect):
    /// <see cref="CoreOneBed"/> for a Core One (the model the user chose for its image, otherwise the detected one; the
    /// camera mount is not known), the whole frame for every other printer. The user changes it in the editor (T-154).
    /// </summary>
    public static DetectionRegion DefaultFor(PrinterDefinition printer)
    {
        ArgumentNullException.ThrowIfNull(printer);
        return DefaultFor(printer.Image, printer.DetectedModel);
    }

    /// <summary>
    /// <see cref="DefaultFor(PrinterDefinition)"/> of a printer being edited (T-154: the settings window has no definition
    /// yet): <see cref="CoreOneBed"/> when <see cref="IsCoreOne"/>, otherwise the whole frame.
    /// </summary>
    public static DetectionRegion DefaultFor(PrinterImageChoice image, PrinterModelCode? detectedModel) =>
        IsCoreOne(image, detectedModel) ? CoreOneBed : Full;

    /// <summary>
    /// Whether a printer counts as a Core One for its default region: the model the user chose for its image, otherwise the
    /// detected one (T-154 offers "Default for Core One" only then).
    /// </summary>
    public static bool IsCoreOne(PrinterImageChoice image, PrinterModelCode? detectedModel) => ModelOf(image, detectedModel) == PrinterModel.CoreOne;

    /// <summary>
    /// The model detection assumes for a printer: the model the user chose for its image, otherwise the detected one
    /// (<see cref="PrinterModel.Unknown"/> when neither is known). Decides the default region (<see cref="IsCoreOne"/>) and
    /// which detectors are offered (<see cref="PrintFailureDetectorRegistration.IsOfferedFor(PrinterModel)"/>, T-167).
    /// </summary>
    public static PrinterModel ModelOf(PrinterImageChoice image, PrinterModelCode? detectedModel)
    {
        ArgumentNullException.ThrowIfNull(image);
        return image.Mode == PrinterImageMode.Model
            ? image.Model
            : detectedModel is { } code ? PrinterModels.FromCode(code) : PrinterModel.Unknown;
    }

    /// <summary><see cref="ModelOf(PrinterImageChoice, PrinterModelCode?)"/> of a configured printer.</summary>
    public static PrinterModel ModelOf(PrinterDefinition printer)
    {
        ArgumentNullException.ThrowIfNull(printer);
        return ModelOf(printer.Image, printer.DetectedModel);
    }

    /// <summary>
    /// The region in whole pixels of a <paramref name="width"/> × <paramref name="height"/> image: the pixels it covers at
    /// least half (rounded edges), never empty and never outside the image. <c>default</c> is the whole image.
    /// </summary>
    public DetectionPixelRegion ToPixels(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (this == default)
        {
            return new DetectionPixelRegion(0, 0, width, height);
        }

        (int left, int right) = Span(X, Width, width);
        (int top, int bottom) = Span(Y, Height, height);
        return new DetectionPixelRegion(left, top, right - left, bottom - top);
    }

    private static (int Start, int End) Span(double start, double size, int pixels)
    {
        int first = Math.Clamp((int)Math.Round(start * pixels, MidpointRounding.AwayFromZero), 0, pixels - 1);
        int end = Math.Clamp((int)Math.Round((start + size) * pixels, MidpointRounding.AwayFromZero), first + 1, pixels);
        return (first, end);
    }

    private static double Clip(double value, double min, double max, double fallback) =>
        double.IsNaN(value) ? Math.Max(min, fallback) : Math.Clamp(value, min, Math.Max(min, max));
}
