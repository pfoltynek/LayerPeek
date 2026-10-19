using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using PrusaTrayDiagnostic.Core.Cameras;

namespace PrusaTrayDiagnostic.App.Settings;

/// <summary>
/// Turns the snapshot of a camera test into an image the Camera sub-tab can show (T-034). UI thread only. The caller owns
/// the result: when it is <see cref="IDisposable"/> (a bitmap), the caller disposes it once it is no longer shown.
/// </summary>
internal interface ICameraPreviewRenderer
{
    IImage Render(CameraImage image);
}

/// <summary>
/// Copies the BGRA pixels of a snapshot into a new <see cref="WriteableBitmap"/> (opaque, 96 DPI, so one image pixel is
/// one DIP before the <c>Image</c> scales it). One bitmap per successful test; nothing is cached.
/// </summary>
internal sealed class BitmapCameraPreviewRenderer : ICameraPreviewRenderer
{
    public static BitmapCameraPreviewRenderer Instance { get; } = new();

    private BitmapCameraPreviewRenderer()
    {
    }

    public unsafe IImage Render(CameraImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        WriteableBitmap bitmap = new(new PixelSize(image.Width, image.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        try
        {
            using ILockedFramebuffer target = bitmap.Lock();
            ReadOnlySpan<byte> source = image.Pixels.Span;
            int rowBytes = image.Stride;
            if (target.RowBytes == rowBytes)
            {
                source.CopyTo(new Span<byte>((void*)target.Address, rowBytes * image.Height));
            }
            else
            {
                for (int row = 0; row < image.Height; row++)
                {
                    source.Slice(row * rowBytes, rowBytes).CopyTo(new Span<byte>((byte*)target.Address + ((long)row * target.RowBytes), rowBytes));
                }
            }

            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }
}
