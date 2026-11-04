namespace LayerPeek.Core.Cameras;

/// <summary>
/// Encodes camera images as JPEG (T-153: the snapshot of a print failure alarm). Core has no image codec and adds no
/// dependency for one, so the App implements it with the UI toolkit (Avalonia / Skia, like <see cref="Images.IPrinterImageProcessor"/>).
/// </summary>
public interface ICameraImageEncoder
{
    /// <summary>
    /// The BGRA pixels (rows top-down, <paramref name="stride"/> bytes apart, opaque) as a JPEG file of the same size.
    /// Thread-safe; called on the thread pool, never on the UI thread. May throw; the caller logs it and goes on without
    /// a snapshot.
    /// </summary>
    /// <param name="quality">1–100.</param>
    byte[] EncodeJpeg(ReadOnlySpan<byte> pixels, int width, int height, int stride, int quality);
}
