using PrusaTrayDiagnostic.App.Printers;
using PrusaTrayDiagnostic.App.Tests.Printers;
using PrusaTrayDiagnostic.App.UiTests.Infrastructure;
using PrusaTrayDiagnostic.Core.Images;

namespace PrusaTrayDiagnostic.App.UiTests.Printers;

/// <summary>
/// Decoding and downscaling custom printer images with Avalonia/Skia (T-039): PNG and JPEG, at most 512 px on the longer
/// side, never enlarged, JPEG EXIF orientation applied, metadata dropped, invalid data reported as undecodable.
/// </summary>
public sealed class PrinterImageProcessorTests(HeadlessSession session)
{
    private const uint Red = 0xFFFF0000;
    private const uint Blue = 0xFF0000FF;
    private const uint Green = 0xFF00FF00;

    private static Task<byte[]> ConvertAsync(byte[] source, PrinterImageFormat format) =>
        AvaloniaPrinterImageProcessor.Instance.ConvertToPngAsync(source, format, PrinterImageFormats.MaxPixelSize, TestContext.Current.CancellationToken);

    [Fact]
    public Task SmallPngKeepsItsSizeAndPixels() =>
        session.RunAsync(static async _ =>
        {
            byte[] source = UiTestImages.Png(100, 50, static (x, _) => x < 50 ? Red : 0x800000FFu);

            (int width, int height, uint[] pixels) = UiTestImages.Read(await ConvertAsync(source, PrinterImageFormat.Png));

            Assert.Equal((100, 50), (width, height));
            Assert.True(UiTestImages.IsNear(pixels[(10 * 100) + 10], Red, 2));
            // Transparency survives (PNG in, PNG out).
            Assert.True(UiTestImages.IsNear(pixels[(10 * 100) + 90], 0x800000FF, 2));
        });

    [Theory]
    [InlineData(1024, 512, 512, 256)]
    [InlineData(300, 1200, 128, 512)]
    [InlineData(4000, 4000, 512, 512)]
    [InlineData(513, 100, 512, 100)]
    [InlineData(2000, 2, 512, 1)]
    public Task LargePngIsScaledDownKeepingItsAspectRatio(int width, int height, int expectedWidth, int expectedHeight) =>
        session.RunAsync(async _ =>
        {
            byte[] source = UiTestImages.Png(width, height, Green);

            (int w, int h, uint[] pixels) = UiTestImages.Read(await ConvertAsync(source, PrinterImageFormat.Png));

            Assert.Equal((expectedWidth, expectedHeight), (w, h));
            Assert.True(UiTestImages.IsNear(pixels[pixels.Length / 2], Green, 8));
        });

    [Fact]
    public Task JpegIsDecodedAndScaled() =>
        session.RunAsync(static async _ =>
        {
            byte[] source = UiTestImages.Jpeg(800, 600, static (x, _) => x < 400 ? Red : Blue);

            byte[] png = await ConvertAsync(source, PrinterImageFormat.Jpeg);

            Assert.Equal(PrinterImageFormat.Png, PrinterImageFormats.Detect(png));
            (int width, int height, uint[] pixels) = UiTestImages.Read(png);
            Assert.Equal((512, 384), (width, height));
            Assert.True(UiTestImages.IsNear(pixels[(192 * 512) + 100], Red));
            Assert.True(UiTestImages.IsNear(pixels[(192 * 512) + 400], Blue));
        });

    [Theory]
    [InlineData(6, 32, 64, Red, Blue)] // rotated 90° clockwise: the stored left half (red) ends on top
    [InlineData(8, 32, 64, Blue, Red)] // rotated 90° counter-clockwise: the stored right half (blue) ends on top
    [InlineData(3, 64, 32, Blue, Red)] // rotated 180°: the stored right half ends left (top-left probe)
    [InlineData(1, 64, 32, Red, Blue)]
    public Task JpegExifOrientationIsApplied(int orientation, int width, int height, uint first, uint second) =>
        session.RunAsync(async _ =>
        {
            // Stored 64 × 32: left half red, right half blue.
            byte[] source = UiTestImages.Jpeg(64, 32, static (x, _) => x < 32 ? Red : Blue, orientation);
            Assert.Equal(orientation, ImageHeaders.ReadJpegOrientation(source));

            (int w, int h, uint[] pixels) = UiTestImages.Read(await ConvertAsync(source, PrinterImageFormat.Jpeg));

            Assert.Equal((width, height), (w, h));
            // Probes: top-left area and the opposite area along the axis that holds the two colours.
            (int x1, int y1, int x2, int y2) = w > h ? (8, 16, 56, 16) : (16, 8, 16, 56);
            Assert.True(UiTestImages.IsNear(pixels[(y1 * w) + x1], first), $"First probe is {pixels[(y1 * w) + x1]:X8}.");
            Assert.True(UiTestImages.IsNear(pixels[(y2 * w) + x2], second), $"Second probe is {pixels[(y2 * w) + x2]:X8}.");
        });

    [Fact]
    public Task PngExifOrientationIsApplied() =>
        session.RunAsync(static async _ =>
        {
            // Stored 64 × 32 (left red, right blue) with eXIf orientation 6: shown 32 × 64 with red on top.
            byte[] source = TestImages.WithPngExif(UiTestImages.Png(64, 32, static (x, _) => x < 32 ? Red : Blue), 6);

            byte[] png = await ConvertAsync(source, PrinterImageFormat.Png);

            (int width, int height, uint[] pixels) = UiTestImages.Read(png);
            Assert.Equal((32, 64), (width, height));
            Assert.True(UiTestImages.IsNear(pixels[(8 * 32) + 16], Red, 2));
            Assert.True(UiTestImages.IsNear(pixels[(56 * 32) + 16], Blue, 2));
            Assert.True(png.AsSpan().IndexOf("eXIf"u8) < 0);
        });

    [Fact]
    public Task MetadataIsNotCopied() =>
        session.RunAsync(static async _ =>
        {
            byte[] source = UiTestImages.Jpeg(64, 32, static (_, _) => Green, orientation: 6);

            byte[] png = await ConvertAsync(source, PrinterImageFormat.Jpeg);

            Assert.True(png.AsSpan().IndexOf("Exif"u8) < 0);
            Assert.True(png.AsSpan().IndexOf("eXIf"u8) < 0);
        });

    [Fact]
    public Task CorruptDataIsUndecodable() =>
        session.RunAsync(static async _ =>
        {
            byte[] header = TestImages.PngHeader(10, 10);
            byte[] corrupt = [.. header, .. new byte[200]];

            await Assert.ThrowsAsync<PrinterImageDecodeException>(() => ConvertAsync(corrupt, PrinterImageFormat.Png));
            await Assert.ThrowsAsync<PrinterImageDecodeException>(() => ConvertAsync(TestImages.Jpeg(), PrinterImageFormat.Jpeg));
            await Assert.ThrowsAsync<PrinterImageDecodeException>(() => ConvertAsync(UiTestImages.Png(4, 4, Red)[..40], PrinterImageFormat.Png));
        });

    [Fact]
    public Task TooManyPixelsAreRefusedBeforeDecoding() =>
        session.RunAsync(static async _ =>
        {
            // A header claiming 10 000 × 10 000 (100 MP) would need 400 MB.
            byte[] huge = [.. TestImages.PngHeader(10_000, 10_000), .. new byte[16]];

            PrinterImageDecodeException error = await Assert.ThrowsAsync<PrinterImageDecodeException>(() => ConvertAsync(huge, PrinterImageFormat.Png));

            Assert.Contains("too many pixels", error.Message, StringComparison.Ordinal);
        });

    [Fact]
    public Task CancellationIsHonoured() =>
        session.RunAsync(static async _ =>
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AvaloniaPrinterImageProcessor.Instance.ConvertToPngAsync(UiTestImages.Png(10, 10, Red), PrinterImageFormat.Png, PrinterImageFormats.MaxPixelSize, new CancellationToken(canceled: true)));
        });

    [Fact]
    public Task ImporterAcceptsARealPhoto() =>
        session.RunAsync(static async scope =>
        {
            byte[] jpeg = UiTestImages.Jpeg(1600, 1200, static (x, y) => (x + y) % 2 == 0 ? Red : Blue, orientation: 6);
            PrinterImageImporter importer = new(AvaloniaPrinterImageProcessor.Instance);

            PrinterImageImportResult result = await importer.ImportAsync(new MemoryStream(jpeg), scope.CancellationToken);

            Assert.Equal(PrinterImageImportStatus.Imported, result.Status);
            (int width, int height, _) = UiTestImages.Read(result.Png!);
            Assert.Equal((384, 512), (width, height));
        });
}
