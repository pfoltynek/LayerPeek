namespace PrusaTrayDiagnostic.Core.Images;

/// <summary>
/// Decodes and downscales a custom printer image. Pixel work needs the UI toolkit (Avalonia/Skia), so the App implements
/// it; Core (<see cref="PrinterImageImporter"/>) does the file handling and validation around it.
/// </summary>
public interface IPrinterImageProcessor
{
    /// <summary>
    /// Decodes <paramref name="source"/> and encodes it as PNG whose longer side is at most <paramref name="maxPixelSize"/>
    /// pixels: the aspect ratio is kept, nothing is cropped, smaller images keep their size. Metadata (EXIF, location…)
    /// is not copied; the JPEG orientation should be applied to the pixels.
    /// </summary>
    /// <param name="source">At most <see cref="PrinterImageFormats.MaxSourceBytes"/>; its signature already matched <paramref name="format"/>.</param>
    /// <exception cref="PrinterImageDecodeException">The data cannot be decoded (corrupt, truncated, unsupported variant).</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<byte[]> ConvertToPngAsync(ReadOnlyMemory<byte> source, PrinterImageFormat format, int maxPixelSize, CancellationToken cancellationToken);
}

/// <summary>Thrown by <see cref="IPrinterImageProcessor"/> for image data it cannot decode. The message never contains a path.</summary>
public sealed class PrinterImageDecodeException : Exception
{
    public PrinterImageDecodeException()
        : base("The image cannot be decoded.")
    {
    }

    public PrinterImageDecodeException(string message)
        : base(message)
    {
    }

    public PrinterImageDecodeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
