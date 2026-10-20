using System.Text;

using PrusaTrayDiagnostic.Core.Images;

namespace PrusaTrayDiagnostic.Core.Tests.Images;

public class PrinterImageFormatsTests
{
    [Fact]
    public void PngIsDetectedBySignature()
    {
        Assert.Equal(PrinterImageFormat.Png, PrinterImageFormats.Detect(ImageTestData.Png()));
    }

    [Theory]
    [InlineData(0xE0)]
    [InlineData(0xE1)]
    [InlineData(0xDB)]
    public void JpegIsDetectedBySignature(byte marker)
    {
        Assert.Equal(PrinterImageFormat.Jpeg, PrinterImageFormats.Detect([0xFF, 0xD8, 0xFF, marker, 0x00]));
    }

    public static TheoryData<byte[]> Unsupported =>
    [
        Array.Empty<byte>(),
        new byte[] { 0x89, 0x50, 0x4E, 0x47 },
        new byte[] { 0xFF, 0xD8 },
        Encoding.ASCII.GetBytes("GIF89a......"),
        Encoding.ASCII.GetBytes("BM.........."),
        Encoding.ASCII.GetBytes("RIFF....WEBPVP8 "),
        Encoding.ASCII.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"),
        new byte[] { 0x00, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A },
    ];

    [Theory]
    [MemberData(nameof(Unsupported))]
    public void OtherContentIsRejected(byte[] content)
    {
        Assert.Null(PrinterImageFormats.Detect(content));
    }

    [Fact]
    public void LimitsMatchTheDecision()
    {
        Assert.Equal(5 * 1024 * 1024, PrinterImageFormats.MaxSourceBytes);
        Assert.Equal(512, PrinterImageFormats.MaxPixelSize);
        Assert.True(PrinterImageFormats.MaxStoredBytes > 512 * 512 * 4);
    }
}
