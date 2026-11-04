using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using LayerPeek.Core.Cameras;

namespace LayerPeek.App.Cameras.Recording;

/// <summary>
/// <see cref="ICameraJpegSequenceEncoderFactory"/> with the Avalonia imaging API (T-165, variant A1 of the spike T-149: Skia
/// and libjpeg-turbo, already part of the app; no package, no native code of its own, no reflection, Native AOT safe).
/// Needs the Avalonia platform (the app, or a headless session in tests).
/// </summary>
internal sealed class AvaloniaJpegSequenceEncoderFactory : ICameraJpegSequenceEncoderFactory
{
    public static AvaloniaJpegSequenceEncoderFactory Instance { get; } = new();

    public ICameraJpegSequenceEncoder Create(int quality) => new AvaloniaJpegSequenceEncoder(quality);
}

/// <summary>
/// One bitmap of the frame size, kept for the whole sequence: every frame is copied into it and saved as JPEG straight into
/// the destination stream, so the managed side allocates nothing per frame but what Avalonia's <c>Save</c> needs (Skia's
/// encoder works in native memory). A frame of another size replaces the bitmap. Not thread-safe (one sequence).
/// </summary>
internal sealed class AvaloniaJpegSequenceEncoder : ICameraJpegSequenceEncoder
{
    private readonly JpegBitmapEncoderOptions _options;
    private WriteableBitmap? _bitmap;
    private bool _disposed;

    /// <param name="quality">1–100.</param>
    public AvaloniaJpegSequenceEncoder(int quality)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quality, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quality, 100);
        _options = new JpegBitmapEncoderOptions { Quality = quality };
    }

    public void Encode(in CameraFrame frame, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (frame.Width == 0)
        {
            throw new ArgumentException("An empty frame cannot be encoded.", nameof(frame));
        }

        PixelSize size = new(frame.Width, frame.Height);
        if (_bitmap is null || _bitmap.PixelSize != size)
        {
            _bitmap?.Dispose();
            _bitmap = null;
            _bitmap = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        }

        int rowBytes = frame.Width * CameraFrame.BytesPerPixel;
        ReadOnlySpan<byte> pixels = frame.Pixels.Span;
        using (ILockedFramebuffer target = _bitmap.Lock())
        {
            if (frame.Stride == rowBytes)
            {
                BgraPixels.CopyTo(pixels, rowBytes, frame.Height, target);
            }
            else
            {
                // Padded rows of the decoder: one row at a time into the bitmap's rows.
                for (int y = 0; y < frame.Height; y++)
                {
                    pixels.Slice(y * frame.Stride, rowBytes).CopyTo(RowOf(target, y, rowBytes));
                }
            }
        }

        _bitmap.Save(destination, _options);
    }

    public void Dispose()
    {
        _disposed = true;
        _bitmap?.Dispose();
        _bitmap = null;
    }

    private static unsafe Span<byte> RowOf(ILockedFramebuffer target, int y, int rowBytes) =>
        new((byte*)target.Address + ((long)y * target.RowBytes), rowBytes);
}
