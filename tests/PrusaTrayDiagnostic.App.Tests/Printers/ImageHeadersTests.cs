using System.Buffers.Binary;

using PrusaTrayDiagnostic.App.Printers;
using PrusaTrayDiagnostic.Core.Images;

namespace PrusaTrayDiagnostic.App.Tests.Printers;

/// <summary>Pixel size and EXIF orientation read from the headers before decoding (T-039).</summary>
public sealed class ImageHeadersTests
{
    [Fact]
    public void PngSizeComesFromTheIhdrChunk()
    {
        Assert.True(ImageHeaders.TryReadSize(TestImages.PngHeader(640, 480), PrinterImageFormat.Png, out int width, out int height));
        Assert.Equal((640, 480), (width, height));
    }

    [Fact]
    public void PngWithoutIhdrOrWithZeroSizeIsRejected()
    {
        byte[] png = TestImages.PngHeader(640, 480);
        byte[] wrongChunk = [.. png];
        wrongChunk[12] = (byte)'X';

        Assert.False(ImageHeaders.TryReadSize(wrongChunk, PrinterImageFormat.Png, out _, out _));
        Assert.False(ImageHeaders.TryReadSize(TestImages.PngHeader(0, 480), PrinterImageFormat.Png, out _, out _));
        Assert.False(ImageHeaders.TryReadSize(png.AsSpan(0, 20), PrinterImageFormat.Png, out _, out _));
        Assert.False(ImageHeaders.TryReadSize(TestImages.PngHeader(0x8000_0000, 1), PrinterImageFormat.Png, out _, out _));
    }

    [Fact]
    public void JpegSizeComesFromTheStartOfFrameAfterOtherSegments()
    {
        byte[] jpeg = TestImages.JpegHeaders(width: 4000, height: 3000, orientation: 6, littleEndian: true, sof: 0xC2);

        Assert.True(ImageHeaders.TryReadSize(jpeg, PrinterImageFormat.Jpeg, out int width, out int height));
        Assert.Equal((4000, 3000), (width, height));
    }

    [Fact]
    public void JpegWithoutFrameIsRejected()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00, 0xFF, 0xDA, 0x00, 0x02];

        Assert.False(ImageHeaders.TryReadSize(jpeg, PrinterImageFormat.Jpeg, out _, out _));
        Assert.False(ImageHeaders.TryReadSize([0xFF, 0xD8], PrinterImageFormat.Jpeg, out _, out _));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(3, false)]
    [InlineData(6, true)]
    [InlineData(6, false)]
    [InlineData(8, true)]
    public void ExifOrientationIsReadInBothByteOrders(int orientation, bool littleEndian)
    {
        Assert.Equal(orientation, ImageHeaders.ReadJpegOrientation(TestImages.JpegHeaders(10, 10, orientation, littleEndian)));
    }

    [Fact]
    public void PngExifChunkOrientationIsRead()
    {
        byte[] png = [.. TestImages.PngHeader(10, 10), 0, 0, 0, 0, .. "IDAT"u8, 0, 0, 0, 0];

        Assert.Equal(6, ImageHeaders.ReadPngOrientation(TestImages.WithPngExif(png, 6)));
        Assert.Equal(8, ImageHeaders.ReadOrientation(TestImages.WithPngExif(png, 8), PrinterImageFormat.Png));
        Assert.Equal(1, ImageHeaders.ReadPngOrientation(png));
    }

    [Fact]
    public void PngExifAfterTheImageDataOrTruncatedIsIgnored()
    {
        byte[] withExif = TestImages.WithPngExif(TestImages.PngHeader(10, 10), 6);
        byte[] idatFirst = [.. TestImages.PngHeader(10, 10), 0, 0, 0, 0, .. "IDAT"u8, 0, 0, 0, 0, .. withExif.AsSpan(33)];

        Assert.Equal(1, ImageHeaders.ReadPngOrientation(idatFirst));
        for (int length = 0; length < withExif.Length; length++)
        {
            _ = ImageHeaders.ReadPngOrientation(withExif.AsSpan(0, length));
        }
    }

    [Fact]
    public void MissingOrInvalidOrientationIsNormal()
    {
        Assert.Equal(1, ImageHeaders.ReadJpegOrientation(TestImages.JpegHeaders(10, 10, orientation: null, littleEndian: true)));
        Assert.Equal(1, ImageHeaders.ReadJpegOrientation(TestImages.JpegHeaders(10, 10, orientation: 9, littleEndian: true)));
        Assert.Equal(1, ImageHeaders.ReadJpegOrientation([0xFF, 0xD8, 0xFF, 0xE1, 0x00, 0x08, (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0]));
        Assert.Equal(1, ImageHeaders.ReadJpegOrientation([0xFF, 0xD8]));
    }

    [Fact]
    public void TruncatedSegmentsDoNotThrow()
    {
        byte[] jpeg = TestImages.JpegHeaders(10, 10, 6, littleEndian: true);

        for (int length = 0; length < jpeg.Length; length++)
        {
            _ = ImageHeaders.ReadJpegOrientation(jpeg.AsSpan(0, length));
            _ = ImageHeaders.TryReadSize(jpeg.AsSpan(0, length), PrinterImageFormat.Jpeg, out _, out _);
        }
    }
}
