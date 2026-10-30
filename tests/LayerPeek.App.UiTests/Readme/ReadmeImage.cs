using System.Reflection;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using SkiaSharp;

using LayerPeek.App.UiTests.Infrastructure;

namespace LayerPeek.App.UiTests.Readme;

/// <summary>
/// Captures windows for the README (<c>docs/images/&lt;language&gt;/</c>) at <see cref="Scaling"/>, so that the images stay
/// crisp on high-DPI screens and GitHub's zoom. Flat window content is saved as PNG (best zlib level); images with the
/// camera picture as JPEG, because a photo as PNG would weigh megabytes. UI thread only.
/// </summary>
internal sealed class ReadmeImage : IDisposable
{
    /// <summary>The render scaling of every image (a 2× / "Retina" screen).</summary>
    public const double Scaling = 2;

    /// <summary>JPEG quality of the images with the camera picture: no visible artefacts around the overlay texts.</summary>
    private const int JpegQuality = 86;

    private ReadmeImage(SKBitmap bitmap) => Bitmap = bitmap;

    /// <summary>The <c>docs/images</c> folder of the repository.</summary>
    public static string Directory { get; } =
        typeof(ReadmeImage).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(static attribute => attribute.Key == "ReadmeImageDirectory").Value
        ?? throw new InvalidOperationException("The ReadmeImageDirectory assembly metadata is missing.");

    public SKBitmap Bitmap { get; }

    public int Width => Bitmap.Width;

    public int Height => Bitmap.Height;

    /// <summary>
    /// The frame of <paramref name="window"/> rendered at <see cref="Scaling"/> without keyboard focus (no focus adorner)
    /// once the layout settled.
    /// </summary>
    public static ReadmeImage Capture(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.SetRenderScaling(Scaling);
        window.Settle();
        window.FocusManager?.Focus(null);
        window.Settle();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(10);
        window.Settle();
        using WriteableBitmap frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("The window rendered no frame.");
        return From(frame);
    }

    /// <summary>A copy of the rectangle (<paramref name="x"/>, <paramref name="y"/>, <paramref name="width"/>, <paramref name="height"/>) in DIP.</summary>
    public ReadmeImage Crop(double x, double y, double width, double height)
    {
        SKRectI area = SKRectI.Create((int)Math.Round(x * Scaling), (int)Math.Round(y * Scaling), (int)Math.Round(width * Scaling), (int)Math.Round(height * Scaling));
        area.Intersect(new SKRectI(0, 0, Width, Height));
        SKBitmap copy = new(area.Width, area.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (SKCanvas canvas = new(copy))
        {
            canvas.DrawBitmap(Bitmap, area, new SKRect(0, 0, area.Width, area.Height));
        }

        return new ReadmeImage(copy);
    }

    /// <summary>
    /// Several images side by side on <paramref name="background"/>, bottom aligned, with <paramref name="gap"/> DIP between
    /// them (a collage of crops of real renders, e.g. one widget in its three sizes).
    /// </summary>
    public static ReadmeImage Row(IReadOnlyList<ReadmeImage> parts, double gap, SKColor background)
    {
        ArgumentNullException.ThrowIfNull(parts);
        int space = (int)Math.Round(gap * Scaling);
        int width = parts.Sum(static part => part.Width) + (space * (parts.Count - 1));
        int height = parts.Max(static part => part.Height);
        SKBitmap bitmap = new(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (SKCanvas canvas = new(bitmap))
        {
            canvas.Clear(background);
            int left = 0;
            foreach (ReadmeImage part in parts)
            {
                canvas.DrawBitmap(part.Bitmap, left, height - part.Height);
                left += part.Width + space;
            }
        }

        return new ReadmeImage(bitmap);
    }

    /// <summary>Saves the image as <c>docs/images/<paramref name="language"/>/<paramref name="name"/>.png</c>.</summary>
    public string SavePng(string language, string name) =>
        Save(language, name + ".png", Bitmap.PeekPixels().Encode(new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, zLibLevel: 9)));

    /// <summary>Saves the image as <c>docs/images/<paramref name="language"/>/<paramref name="name"/>.jpg</c>.</summary>
    public string SaveJpeg(string language, string name) =>
        Save(language, name + ".jpg", Bitmap.PeekPixels().Encode(new SKJpegEncoderOptions(JpegQuality, SKJpegEncoderDownsample.Downsample420, SKJpegEncoderAlphaOption.Ignore)));

    public void Dispose() => Bitmap.Dispose();

    private static string Save(string language, string fileName, SKData? encoded)
    {
        if (encoded is null)
        {
            throw new InvalidOperationException($"The image '{fileName}' could not be encoded.");
        }

        using (encoded)
        {
            string folder = Path.Combine(Directory, language);
            System.IO.Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, fileName);
            using FileStream file = File.Create(path);
            encoded.SaveTo(file);
            return path;
        }
    }

    private static ReadmeImage From(WriteableBitmap frame)
    {
        using ILockedFramebuffer buffer = frame.Lock();
        SKColorType type = buffer.Format == PixelFormat.Rgba8888 ? SKColorType.Rgba8888
            : buffer.Format == PixelFormat.Bgra8888 ? SKColorType.Bgra8888
            : throw new NotSupportedException($"Pixel format {buffer.Format} is not supported.");
        SKImageInfo info = new(buffer.Size.Width, buffer.Size.Height, type, SKAlphaType.Premul);
        SKBitmap bitmap = new(new SKImageInfo(info.Width, info.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using SKPixmap source = new(info, buffer.Address, buffer.RowBytes);
        if (!source.ReadPixels(bitmap.PeekPixels()))
        {
            bitmap.Dispose();
            throw new InvalidOperationException("The rendered frame could not be copied.");
        }

        return new ReadmeImage(bitmap);
    }
}
