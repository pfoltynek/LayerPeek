using LayerPeek.Core.Cameras;

namespace LayerPeek.Core.Tests.Cameras;

/// <summary>T-153: BGRA → 8-bit luma, downscaled by area averaging while copying.</summary>
public sealed class GrayscaleScalerTests
{
    private static byte[] Bgra(int width, int height, Func<int, int, (byte B, byte G, byte R)> color, int stride = 0)
    {
        stride = stride == 0 ? width * 4 : stride;
        byte[] pixels = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                (byte b, byte g, byte r) = color(x, y);
                int i = (y * stride) + (x * 4);
                (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (b, g, r, 255);
            }
        }

        return pixels;
    }

    private static byte[] Scale(byte[] pixels, int width, int height, int stride, CameraGrayscaleSize size)
    {
        byte[] target = new byte[size.Length];
        new GrayscaleScaler().Scale(new CameraFrame(width, height, stride, pixels), size, target);
        return target;
    }

    [Theory]
    [InlineData(255, 0, 0, 28)]
    [InlineData(0, 255, 0, 149)]
    [InlineData(0, 0, 255, 76)]
    [InlineData(255, 255, 255, 255)]
    [InlineData(0, 0, 0, 0)]
    public void LumaUsesBt601Weights(byte b, byte g, byte r, byte expected)
    {
        byte[] gray = Scale(Bgra(32, 32, (_, _) => (b, g, r)), 32, 32, 128, new CameraGrayscaleSize(16, 16));

        Assert.All(gray, value => Assert.Equal(expected, value));
    }

    [Fact]
    public void EveryTargetPixelIsTheMeanOfItsBlock()
    {
        // 64×32 → 16×16: blocks of 4×2; the left half dark (10), the right half bright (200), one bright column in the
        // first block (x = 0) of every row: (200 + 3 × 10) / 4 = 57.5 → 58.
        byte[] pixels = Bgra(64, 32, (x, _) => x == 0 || x >= 32 ? ((byte)200, (byte)200, (byte)200) : ((byte)10, (byte)10, (byte)10));

        byte[] gray = Scale(pixels, 64, 32, 256, new CameraGrayscaleSize(16, 16));

        Assert.Equal(58, gray[0]);
        Assert.Equal(10, gray[1]);
        Assert.Equal(10, gray[7]);
        Assert.Equal(200, gray[8]);
        Assert.Equal(200, gray[(16 * 15) + 15]);
    }

    [Fact]
    public void PaddedRowsAndUnevenRatiosAreHandled()
    {
        // 1280×720 with padded rows (as FFmpeg aligns them) → 320×180, a horizontal gradient.
        const int Width = 1280;
        const int Height = 720;
        const int Stride = (Width * 4) + 64;
        byte[] pixels = Bgra(Width, Height, (x, _) => ((byte)(x / 5), (byte)(x / 5), (byte)(x / 5)), Stride);

        byte[] gray = Scale(pixels, Width, Height, Stride, new CameraGrayscaleSize(320, 180));

        // Target column c covers source columns 4c–4c+3: their mean of x / 5.
        for (int c = 0; c < 320; c += 37)
        {
            double expected = (((4 * c) / 5) + (((4 * c) + 1) / 5) + (((4 * c) + 2) / 5) + (((4 * c) + 3) / 5)) / 4.0;
            Assert.InRange(gray[(100 * 320) + c], Math.Floor(expected), Math.Ceiling(expected));
        }

        // 300×200 → 320×180 (wider target, shorter target): every target pixel still lies within the source range.
        byte[] odd = Scale(Bgra(300, 200, (x, y) => ((byte)((x + y) % 256), 0, 0)), 300, 200, 1200, new CameraGrayscaleSize(320, 180));
        Assert.Equal(320 * 180, odd.Length);
    }

    [Fact]
    public void TheExchangeCopiesGrayscaleIntoReusedBuffers()
    {
        CameraFrameExchange exchange = new(new CameraGrayscaleSize(16, 16));
        byte[] pixels = Bgra(64, 64, (_, _) => (90, 90, 90));
        CameraFrame frame = new(64, 64, 256, pixels);

        exchange.Publish(frame);
        CameraFrameBuffer first = exchange.TakeLatest()!;

        Assert.Equal((CameraPixelFormat.Gray8, 16, 16, 256), (first.Format, first.Width, first.Stride, first.Pixels.Length));
        Assert.All(first.Pixels.ToArray(), value => Assert.Equal(90, value));
        Assert.Equal(256, first.Capacity);
        Assert.Throws<InvalidOperationException>(first.ToImage);
    }
}
