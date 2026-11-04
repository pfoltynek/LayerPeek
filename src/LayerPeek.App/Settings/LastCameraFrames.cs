using System.Security.Cryptography;
using System.Text;

using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using Microsoft.Extensions.Logging;

using LayerPeek.App.Cameras;
using LayerPeek.Core.Cameras;

namespace LayerPeek.App.Settings;

/// <summary>
/// One small last camera frame per printer for the bed region editor (T-154, user decision 2026-11-04: the editor works
/// with the last saved frame while the camera is not available). Written only when the settings window got a frame anyway
/// (a successful camera test or "Load frame"), read only when the editor of that printer is shown: no background work,
/// no timer, nothing while the settings window is closed.
/// </summary>
internal interface ILastCameraFrames
{
    /// <summary>
    /// Keeps <paramref name="image"/> as the last frame of <paramref name="printerId"/> (downscaled JPEG, written on the thread
    /// pool; the image is immutable). Never throws for file problems (logged).
    /// </summary>
    void Save(string printerId, CameraImage image);

    /// <summary>The last frame of <paramref name="printerId"/>, decoded off the UI thread; <see langword="null"/>: none.</summary>
    Task<IImage?> LoadAsync(string printerId, CancellationToken cancellationToken);

    /// <summary>The printer was removed: its frame goes too (on the thread pool).</summary>
    void Delete(string printerId);
}

/// <summary>
/// <see cref="ILastCameraFrames"/> in the cache folder of the app (<c>last-frames</c>): one JPEG per printer,
/// at most <see cref="MaxWidth"/> × <see cref="MaxHeight"/> (~20–60 KB; the editor shows 320 × 180), named by a hash of the
/// printer id (no name or address on disk). Written through a partial file and a rename, so a crash never leaves a broken
/// frame. Avalonia's imaging (Skia, already part of the app; Native AOT safe). Thread-safe.
/// </summary>
internal sealed partial class LastCameraFrameStore : ILastCameraFrames
{
    public const int MaxWidth = 640;

    public const int MaxHeight = 360;

    private const int JpegQuality = 80;

    private readonly ILogger<LastCameraFrameStore> _logger;

    // One write at a time: frames are rare (a click), the last one wins.
    private readonly Lock _writeGate = new();

    public LastCameraFrameStore(string folder, ILogger<LastCameraFrameStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(logger);
        Folder = folder;
        _logger = logger;
    }

    public string Folder { get; }

    /// <summary>Completes once the writes started so far are done (tests).</summary>
    internal Task Pending { get; private set; } = Task.CompletedTask;

    public void Save(string printerId, CameraImage image)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        ArgumentNullException.ThrowIfNull(image);
        if (image.Width <= 0 || image.Height <= 0)
        {
            return;
        }

        string path = PathOf(printerId);
        Task write = Task.Run(() => Write(path, image));
        lock (_writeGate)
        {
            Pending = Task.WhenAll(Pending, write);
        }
    }

    public Task<IImage?> LoadAsync(string printerId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        string path = PathOf(printerId);
        return Task.Run<IImage?>(
            () =>
            {
                try
                {
                    if (!File.Exists(path))
                    {
                        return null;
                    }

                    using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    return new Bitmap(stream);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A broken or unreadable file: the editor shows no frame, a later frame replaces it.
                    LogLoadFailed(ex);
                    return null;
                }
            },
            cancellationToken);
    }

    public void Delete(string printerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        string path = PathOf(printerId);
        Task delete = Task.Run(() =>
        {
            lock (_writeGate)
            {
                TryDelete(path);
            }
        });
        lock (_writeGate)
        {
            Pending = Task.WhenAll(Pending, delete);
        }
    }

    /// <summary>The file of <paramref name="printerId"/>: the first 32 hex digits of its SHA-256 (ids are case-insensitive).</summary>
    internal string PathOf(string printerId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(printerId.ToUpperInvariant()));
        return Path.Combine(Folder, Convert.ToHexStringLower(hash.AsSpan(0, 16)) + ".jpg");
    }

    /// <summary>The size a frame is stored in: at most <see cref="MaxWidth"/> × <see cref="MaxHeight"/>, the aspect ratio kept.</summary>
    internal static PixelSize StoredSize(int width, int height)
    {
        double scale = Math.Min(1, Math.Min((double)MaxWidth / width, (double)MaxHeight / height));
        return new PixelSize(Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    /// <summary>
    /// Area-average downscale of tightly packed BGRA (<paramref name="source"/>, <paramref name="width"/> ×
    /// <paramref name="height"/>) into <paramref name="target"/> (<paramref name="targetWidth"/> × <paramref name="targetHeight"/>,
    /// not larger): every target pixel is the mean of the source pixels it covers (whole pixels). Opaque output.
    /// </summary>
    internal static void Downscale(ReadOnlySpan<byte> source, int width, int height, Span<byte> target, int targetWidth, int targetHeight)
    {
        const int Bpp = CameraFrame.BytesPerPixel;
        for (int ty = 0; ty < targetHeight; ty++)
        {
            int y0 = (int)((long)ty * height / targetHeight);
            int y1 = Math.Max(y0 + 1, (int)((long)(ty + 1) * height / targetHeight));
            for (int tx = 0; tx < targetWidth; tx++)
            {
                int x0 = (int)((long)tx * width / targetWidth);
                int x1 = Math.Max(x0 + 1, (int)((long)(tx + 1) * width / targetWidth));
                int b = 0, g = 0, r = 0;
                for (int y = y0; y < y1; y++)
                {
                    ReadOnlySpan<byte> row = source.Slice(((y * width) + x0) * Bpp, (x1 - x0) * Bpp);
                    for (int i = 0; i < row.Length; i += Bpp)
                    {
                        b += row[i];
                        g += row[i + 1];
                        r += row[i + 2];
                    }
                }

                int count = (x1 - x0) * (y1 - y0);
                int offset = ((ty * targetWidth) + tx) * Bpp;
                target[offset] = (byte)(b / count);
                target[offset + 1] = (byte)(g / count);
                target[offset + 2] = (byte)(r / count);
                target[offset + 3] = 0xFF;
            }
        }
    }

    private void Write(string path, CameraImage image)
    {
        string partial = path + ".partial";
        try
        {
            // Downscaled here (Skia cannot resize a WriteableBitmap); one small buffer per frame, a click at most.
            PixelSize size = StoredSize(image.Width, image.Height);
            byte[] pixels = new byte[size.Width * size.Height * CameraFrame.BytesPerPixel];
            Downscale(image.Pixels.Span, image.Width, image.Height, pixels, size.Width, size.Height);
            using WriteableBitmap stored = new(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (ILockedFramebuffer target = stored.Lock())
            {
                BgraPixels.CopyTo(pixels, size.Width * CameraFrame.BytesPerPixel, size.Height, target);
            }

            lock (_writeGate)
            {
                Directory.CreateDirectory(Folder);
                using (FileStream stream = new(partial, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stored.Save(stream, new JpegBitmapEncoderOptions { Quality = JpegQuality });
                }

                File.Move(partial, path, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            // Never escapes the thread pool; the editor keeps the previous frame.
            LogSaveFailed(ex);
            TryDelete(partial);
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            LogDeleteFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The last camera frame for the bed region editor could not be saved.")]
    private partial void LogSaveFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The last camera frame for the bed region editor could not be read.")]
    private partial void LogLoadFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "A last camera frame could not be deleted.")]
    private partial void LogDeleteFailed(Exception exception);
}
