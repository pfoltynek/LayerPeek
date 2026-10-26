using System.Buffers.Binary;

using LayerPeek.Core.Previews;
using LayerPeek.Core.Printers;
using LayerPeek.Tests.Shared;

namespace LayerPeek.Core.Tests.Previews;

public sealed class PrintPreviewTests
{
    [Theory]
    [InlineData(16, 16)]
    [InlineData(380, 285)]
    [InlineData(1, PrintPreview.MaxSide)]
    public void PngHeaderGivesTheSize(int width, int height)
    {
        byte[] png = TestPng.Create(width, height);

        PrintPreview? preview = PrintPreview.FromPng(png);

        Assert.NotNull(preview);
        Assert.Equal(width, preview.Width);
        Assert.Equal(height, preview.Height);
        Assert.Equal(png, preview.Png.ToArray());
    }

    [Fact]
    public void BytesAreCopied()
    {
        byte[] png = TestPng.Create(16, 16);
        PrintPreview preview = PrintPreview.FromPng(png)!;

        png.AsSpan().Clear();

        Assert.Equal(0x89, preview.Png.Span[0]);
    }

    [Fact]
    public void NonPngIsRefused()
    {
        Assert.Null(PrintPreview.FromPng("{\"title\": \"404: Not Found\",\"message\":\"\"}"u8));
        Assert.Null(PrintPreview.FromPng([]));
        // QOI, the other preview format of PrusaSlicer.
        Assert.Null(PrintPreview.FromPng("qoif\0\0\0\u0010\0\0\0\u0010\u0004\0"u8));
    }

    [Fact]
    public void TruncatedHeaderIsRefused()
    {
        byte[] png = TestPng.Create(16, 16);

        Assert.Null(PrintPreview.FromPng(png.AsSpan(0, 23)));
        Assert.NotNull(PrintPreview.FromPng(png.AsSpan(0, 24)));
    }

    [Fact]
    public void FirstChunkMustBeTheHeader()
    {
        byte[] png = TestPng.Create(16, 16);
        png[12] = (byte)'X';

        Assert.Null(PrintPreview.FromPng(png));
    }

    [Theory]
    [InlineData(0, 16)]
    [InlineData(16, 0)]
    [InlineData(PrintPreview.MaxSide + 1, 16)]
    [InlineData(16, PrintPreview.MaxSide + 1)]
    public void SizeOutsideTheLimitsIsRefused(int width, int height)
    {
        byte[] png = TestPng.Create(16, 16);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20), (uint)height);

        Assert.Null(PrintPreview.FromPng(png));
    }

    [Fact]
    public void HugeDeclaredSizeIsRefused()
    {
        byte[] png = TestPng.Create(16, 16);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), uint.MaxValue);

        Assert.False(PrintPreview.TryReadSize(png, out int width, out int height));
        Assert.Equal(0, width);
        Assert.Equal(0, height);
    }

    [Fact]
    public void FetchResultFactoriesCarryTheirValues()
    {
        PrintPreview preview = PrintPreview.FromPng(TestPng.Create(16, 16))!;

        Assert.Equal(new PrintPreviewFetchResult(PrintPreviewFetchStatus.Downloaded, preview, "\"7\""), PrintPreviewFetchResult.Downloaded(preview, "\"7\""));
        Assert.Equal(new PrintPreviewFetchResult(PrintPreviewFetchStatus.NotModified, null, "\"7\""), PrintPreviewFetchResult.NotModified("\"7\""));
        Assert.Equal(PrintPreviewFetchStatus.NotAvailable, PrintPreviewFetchResult.NotAvailable.Status);
        Assert.Equal(PrintPreviewFetchStatus.Failed, PrintPreviewFetchResult.Failed.Status);
        Assert.Throws<ArgumentNullException>(static () => PrintPreviewFetchResult.Downloaded(null!, null));
    }

    [Fact]
    public void JobsWithEqualReferencesAreEqual()
    {
        PrintJob a = new() { Id = 42, PreviewReference = new TestPreviewReference("/usb/A.BGC") };
        PrintJob b = new() { Id = 42, PreviewReference = new TestPreviewReference("/usb/A.BGC") };
        PrintJob c = new() { Id = 42, PreviewReference = new TestPreviewReference("/usb/B.BGC") };

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.NotEqual(a, a with { PreviewReference = null });
    }
}
