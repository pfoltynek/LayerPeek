namespace LayerPeek.Core.Images;

/// <summary>Formats accepted for a custom printer image (T-039: PNG and JPG).</summary>
public enum PrinterImageFormat
{
    Png,
    Jpeg,
}

/// <summary>Limits and format detection of custom printer images.</summary>
public static class PrinterImageFormats
{
    /// <summary>Largest file accepted for import: 5 MB (T-039 decision; 5 × 1024 × 1024 bytes).</summary>
    public const int MaxSourceBytes = 5 * 1024 * 1024;

    /// <summary>The longer side of a stored image in pixels at most (T-039 decision); smaller images are not enlarged.</summary>
    public const int MaxPixelSize = 512;

    /// <summary>
    /// Largest stored PNG. A 512 × 512 RGBA image is 1 MiB uncompressed, so a valid result is always far below this; it
    /// only protects memory against a foreign file in the image folder.
    /// </summary>
    public const int MaxStoredBytes = 2 * 1024 * 1024;

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];

    /// <summary>
    /// The format by the file signature ("magic bytes"), regardless of the file name: PNG starts with
    /// <c>89 50 4E 47 0D 0A 1A 0A</c>, JPEG with <c>FF D8 FF</c>. <see langword="null"/> for anything else.
    /// </summary>
    public static PrinterImageFormat? Detect(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith(PngSignature))
        {
            return PrinterImageFormat.Png;
        }

        if (content.StartsWith(JpegSignature))
        {
            return PrinterImageFormat.Jpeg;
        }

        return null;
    }
}
