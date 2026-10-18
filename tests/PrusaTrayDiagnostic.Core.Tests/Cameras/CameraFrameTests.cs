using PrusaTrayDiagnostic.Core.Cameras;

namespace PrusaTrayDiagnostic.Core.Tests.Cameras;

public class CameraFrameTests
{
    [Fact]
    public void FrameExposesItsValues()
    {
        byte[] buffer = new byte[3 * 16];

        CameraFrame frame = new(width: 3, height: 3, stride: 16, buffer);

        Assert.Equal(3, frame.Width);
        Assert.Equal(3, frame.Height);
        Assert.Equal(16, frame.Stride);
        Assert.Equal(48, frame.Pixels.Length);
    }

    [Fact]
    public void LastRowDoesNotNeedStridePadding()
    {
        // Two padded rows plus one tight row (3 px * 4 B).
        CameraFrame frame = new(width: 3, height: 3, stride: 16, new byte[(2 * 16) + 12]);

        Assert.Equal(44, frame.Pixels.Length);
    }

    [Theory]
    [InlineData(0, 1, 4, 4)]
    [InlineData(1, 0, 4, 4)]
    [InlineData(-1, 1, 4, 4)]
    [InlineData(2, 1, 7, 8)]
    [InlineData(2, 2, 8, 15)]
    [InlineData(2, 2, -8, 16)]
    public void InvalidGeometryIsRejected(int width, int height, int stride, int bufferLength)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraFrame(width, height, stride, new byte[bufferLength]));
    }

    [Fact]
    public void HugeGeometryDoesNotOverflow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraFrame(int.MaxValue, 2, int.MaxValue, new byte[16]));
    }

    [Fact]
    public void ToImageCopiesTightlyPackedRows()
    {
        // 2 x 2 frame, stride 12: 8 bytes of pixels + 4 bytes of padding per row.
        byte[] buffer =
        [
            1, 2, 3, 4, 5, 6, 7, 8, 99, 99, 99, 99,
            9, 10, 11, 12, 13, 14, 15, 16,
        ];
        CameraFrame frame = new(width: 2, height: 2, stride: 12, buffer);

        CameraImage image = frame.ToImage();
        buffer.AsSpan().Clear();

        Assert.Equal(2, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(8, image.Stride);
        Assert.Equal(Enumerable.Range(1, 16).Select(static i => (byte)i), image.Pixels.ToArray());
    }

    [Fact]
    public void DefaultFrameCannotBeCopied()
    {
        Assert.Throws<InvalidOperationException>(() => default(CameraFrame).ToImage());
    }
}
