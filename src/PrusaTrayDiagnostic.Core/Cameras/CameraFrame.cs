namespace PrusaTrayDiagnostic.Core.Cameras;

/// <summary>
/// A decoded video frame in 32-bit BGRA (byte order B, G, R, A; camera images are opaque, alpha is 255), rows top-down.
/// <para>
/// The frame <b>borrows</b> the decoder's buffer: <see cref="Pixels"/> is valid only until the enumerator that produced
/// the frame advances or is disposed. A consumer that needs the image later copies it (e.g. into a bitmap, or with
/// <see cref="ToImage"/>). This keeps streaming free of per-frame allocations.
/// </para>
/// </summary>
public readonly struct CameraFrame
{
    public const int BytesPerPixel = 4;

    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="stride">Bytes from the start of one row to the start of the next; at least <c>width * 4</c>.</param>
    /// <param name="pixels">At least <c>stride * (height - 1) + width * 4</c> bytes (the last row needs no padding).</param>
    public CameraFrame(int width, int height, int stride, ReadOnlyMemory<byte> pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        long rowBytes = (long)width * BytesPerPixel;
        if (stride < rowBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(stride), stride, "Stride is smaller than a row of pixels.");
        }

        long required = ((long)stride * (height - 1)) + rowBytes;
        if (pixels.Length < required)
        {
            throw new ArgumentOutOfRangeException(nameof(pixels), pixels.Length, $"The frame needs at least {required} bytes.");
        }

        Width = width;
        Height = height;
        Stride = stride;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public int Stride { get; }

    /// <summary>BGRA pixels; borrowed, see the type remarks.</summary>
    public ReadOnlyMemory<byte> Pixels { get; }

    /// <summary>Copies the frame into an image the caller owns (rows tightly packed).</summary>
    /// <exception cref="InvalidOperationException">The frame is <see langword="default"/>.</exception>
    public CameraImage ToImage()
    {
        if (Width == 0)
        {
            throw new InvalidOperationException("An empty frame cannot be copied.");
        }

        int rowBytes = Width * BytesPerPixel;
        byte[] copy = GC.AllocateUninitializedArray<byte>(rowBytes * Height);
        ReadOnlySpan<byte> source = Pixels.Span;
        if (Stride == rowBytes)
        {
            source[..copy.Length].CopyTo(copy);
        }
        else
        {
            for (int row = 0; row < Height; row++)
            {
                source.Slice(row * Stride, rowBytes).CopyTo(copy.AsSpan(row * rowBytes, rowBytes));
            }
        }

        return new CameraImage(Width, Height, copy);
    }
}
