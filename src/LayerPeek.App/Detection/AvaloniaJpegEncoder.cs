using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using LayerPeek.App.Cameras;
using LayerPeek.Core.Cameras;

namespace LayerPeek.App.Detection;

/// <summary>
/// <see cref="ICameraImageEncoder"/> with the Avalonia imaging API (Skia underneath; no extra package, no reflection,
/// Native AOT safe), for the snapshots of print failure alarms (T-153). Thread-safe: every call has its own bitmap. Needs
/// the Avalonia platform (the app, or a headless session in tests).
/// </summary>
internal sealed class AvaloniaJpegEncoder : ICameraImageEncoder
{
    public static AvaloniaJpegEncoder Instance { get; } = new();

    public byte[] EncodeJpeg(ReadOnlySpan<byte> pixels, int width, int height, int stride, int quality)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, width * CameraFrame.BytesPerPixel);
        ArgumentOutOfRangeException.ThrowIfLessThan(quality, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quality, 100);
        using WriteableBitmap bitmap = new(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (ILockedFramebuffer frame = bitmap.Lock())
        {
            if (stride == width * CameraFrame.BytesPerPixel)
            {
                BgraPixels.CopyTo(pixels, stride, height, frame);
            }
            else
            {
                // Padded rows: one packed row at a time (alarms are rare; no buffer kept).
                for (int y = 0; y < height; y++)
                {
                    BgraPixels.CopyRows(pixels.Slice(y * stride, width * CameraFrame.BytesPerPixel), width * CameraFrame.BytesPerPixel, 1, RowOf(frame, y), frame.RowBytes);
                }
            }
        }

        using MemoryStream jpeg = new();
        bitmap.Save(jpeg, new JpegBitmapEncoderOptions { Quality = quality });
        return jpeg.ToArray();
    }

    private static unsafe Span<byte> RowOf(ILockedFramebuffer frame, int y) => new((byte*)frame.Address + ((long)y * frame.RowBytes), frame.RowBytes);
}
