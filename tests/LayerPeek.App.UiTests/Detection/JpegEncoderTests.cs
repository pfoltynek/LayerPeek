using LayerPeek.App.Detection;
using LayerPeek.App.Tests.Printers;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.UiTests.Printers;
using LayerPeek.Core.Images;

namespace LayerPeek.App.UiTests.Detection;

/// <summary>T-153: the alarm snapshot as JPEG through Avalonia / Skia (no extra package), also from a thread-pool thread.</summary>
public sealed class JpegEncoderTests(HeadlessSession session)
{
    private const uint Red = 0xFFFF0000;
    private const uint Blue = 0xFF0000FF;

    /// <summary>BGRA, left half red, right half blue, rows <paramref name="stride"/> bytes apart.</summary>
    private static byte[] Frame(int width, int height, int stride)
    {
        byte[] pixels = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * stride) + (x * 4);
                (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = x < width / 2 ? ((byte)0, (byte)0, (byte)255, (byte)255) : ((byte)255, (byte)0, (byte)0, (byte)255);
            }
        }

        return pixels;
    }

    [Theory]
    [InlineData(1280, 720, 1280 * 4)]
    [InlineData(64, 36, (64 * 4) + 64)]
    public Task TheFrameBecomesAJpegOfTheSameSize(int width, int height, int stride) =>
        session.RunAsync(async _ =>
        {
            byte[] frame = Frame(width, height, stride);

            // Off the UI thread, like the detection service.
            byte[] jpeg = await Task.Run(() => AvaloniaJpegEncoder.Instance.EncodeJpeg(frame, width, height, stride, quality: 85), TestContext.Current.CancellationToken);

            Assert.Equal(PrinterImageFormat.Jpeg, PrinterImageFormats.Detect(jpeg));
            (int w, int h, uint[] pixels) = UiTestImages.Read(jpeg);
            Assert.Equal((width, height), (w, h));
            Assert.True(UiTestImages.IsNear(pixels[(h / 2 * w) + (w / 4)], Red, 24));
            Assert.True(UiTestImages.IsNear(pixels[(h / 2 * w) + (3 * w / 4)], Blue, 24));
            // A 720p camera frame stays small (it goes next to the recordings).
            Assert.True(jpeg.Length < 300_000, $"{jpeg.Length} bytes.");
        });

    [Fact]
    public void InvalidArgumentsAreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AvaloniaJpegEncoder.Instance.EncodeJpeg(new byte[16], 2, 2, 4, 85));
        Assert.Throws<ArgumentOutOfRangeException>(() => AvaloniaJpegEncoder.Instance.EncodeJpeg(new byte[16], 2, 2, 8, 0));
    }
}
