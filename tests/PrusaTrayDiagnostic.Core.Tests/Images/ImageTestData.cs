using PrusaTrayDiagnostic.Core.Images;

namespace PrusaTrayDiagnostic.Core.Tests.Images;

/// <summary>Byte sequences with the signatures of the accepted formats (the content after the signature does not matter to Core).</summary>
internal static class ImageTestData
{
    public static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF, 0xE0];

    public static byte[] Png(int length = 64) => WithSignature(PngSignature, length);

    public static byte[] Jpeg(int length = 64) => WithSignature(JpegSignature, length);

    private static byte[] WithSignature(byte[] signature, int length)
    {
        byte[] content = new byte[Math.Max(length, signature.Length)];
        signature.CopyTo(content, 0);
        for (int i = signature.Length; i < content.Length; i++)
        {
            content[i] = (byte)i;
        }

        return content;
    }
}

/// <summary>Records calls and returns a configurable result.</summary>
internal sealed class FakeImageProcessor : IPrinterImageProcessor
{
    public List<(int Length, PrinterImageFormat Format, int MaxPixelSize)> Calls { get; } = [];

    public Func<byte[]> Result { get; set; } = static () => ImageTestData.Png(128);

    public Exception? Throws { get; set; }

    public Task<byte[]> ConvertToPngAsync(ReadOnlyMemory<byte> source, PrinterImageFormat format, int maxPixelSize, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add((source.Length, format, maxPixelSize));
        return Throws is null ? Task.FromResult(Result()) : Task.FromException<byte[]>(Throws);
    }
}
