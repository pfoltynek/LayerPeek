using LayerPeek.App.Cameras;

namespace LayerPeek.App.Tests.Cameras;

/// <summary>The BGRA row copy shared by the camera window and the settings preview (T-034).</summary>
public sealed class BgraPixelsTests
{
    private static byte[] Source(int rowBytes, int height)
    {
        byte[] source = new byte[rowBytes * height];
        for (int i = 0; i < source.Length; i++)
        {
            source[i] = (byte)(i + 1);
        }

        return source;
    }

    [Fact]
    public void SameRowLayoutIsCopiedAsOneBlock()
    {
        byte[] source = Source(8, 3);
        byte[] target = new byte[24];

        BgraPixels.CopyRows(source, rowBytes: 8, height: 3, target, targetRowBytes: 8);

        Assert.Equal(source, target);
    }

    [Fact]
    public void PaddedTargetRowsGetEachRowAndKeepTheirPadding()
    {
        // 2 pixels per row (8 bytes) into rows of 12 bytes (4 bytes of padding, e.g. an aligned bitmap).
        byte[] source = Source(8, 3);
        byte[] target = new byte[(12 * 2) + 8];
        Array.Fill(target, (byte)0xEE);

        BgraPixels.CopyRows(source, rowBytes: 8, height: 3, target, targetRowBytes: 12);

        for (int row = 0; row < 3; row++)
        {
            Assert.Equal(source.AsSpan(row * 8, 8).ToArray(), target.AsSpan(row * 12, 8).ToArray());
            if (row < 2)
            {
                Assert.All(target.AsSpan((row * 12) + 8, 4).ToArray(), static b => Assert.Equal(0xEE, b));
            }
        }
    }

    [Fact]
    public void LargerTargetIsNotWrittenBeyondTheImage()
    {
        byte[] source = Source(8, 2);
        byte[] target = new byte[64];

        BgraPixels.CopyRows(source, rowBytes: 8, height: 2, target, targetRowBytes: 8);

        Assert.Equal(source, target.AsSpan(0, 16).ToArray());
        Assert.All(target.AsSpan(16).ToArray(), static b => Assert.Equal(0, b));
    }

    [Theory]
    [InlineData(8, 3, 15, 8, 24)] // source too short
    [InlineData(8, 3, 24, 8, 23)] // target too short
    [InlineData(8, 3, 24, 4, 24)] // target rows narrower than a row
    [InlineData(0, 3, 24, 8, 24)] // no row
    [InlineData(8, 0, 24, 8, 24)] // no height
    public void InvalidLayoutsAreRejected(int rowBytes, int height, int sourceLength, int targetRowBytes, int targetLength)
    {
        byte[] source = new byte[sourceLength];
        byte[] target = new byte[targetLength];

        Assert.ThrowsAny<ArgumentException>(() => BgraPixels.CopyRows(source, rowBytes, height, target, targetRowBytes));
    }

    [Fact]
    public void AlignedSourceRowsAreCopiedWithoutTheirPadding()
    {
        // T-184: 2 pixels per row (8 bytes) in rows of 16 bytes (a decoder's aligned rows); the last row has no padding.
        byte[] source = Source(16, 3)[..((16 * 2) + 8)];
        byte[] target = new byte[24];

        BgraPixels.CopyRows(source, sourceRowBytes: 16, rowBytes: 8, height: 3, target, targetRowBytes: 8);

        for (int row = 0; row < 3; row++)
        {
            Assert.Equal(source.AsSpan(row * 16, 8).ToArray(), target.AsSpan(row * 8, 8).ToArray());
        }
    }

    [Fact]
    public void AlignedSourceIntoPaddedTargetKeepsTheTargetPadding()
    {
        byte[] source = Source(16, 2)[..(16 + 8)];
        byte[] target = new byte[12 + 8];
        Array.Fill(target, (byte)0xEE);

        BgraPixels.CopyRows(source, sourceRowBytes: 16, rowBytes: 8, height: 2, target, targetRowBytes: 12);

        Assert.Equal(source.AsSpan(0, 8).ToArray(), target.AsSpan(0, 8).ToArray());
        Assert.All(target.AsSpan(8, 4).ToArray(), static b => Assert.Equal(0xEE, b));
        Assert.Equal(source.AsSpan(16, 8).ToArray(), target.AsSpan(12, 8).ToArray());
    }

    [Theory]
    [InlineData(16, 8, 2, 23)] // the last source row is cut short
    [InlineData(4, 8, 2, 64)] // source rows narrower than a row
    public void InvalidSourceLayoutsAreRejected(int sourceRowBytes, int rowBytes, int height, int sourceLength)
    {
        byte[] source = new byte[sourceLength];
        byte[] target = new byte[64];

        Assert.ThrowsAny<ArgumentException>(() => BgraPixels.CopyRows(source, sourceRowBytes, rowBytes, height, target, rowBytes));
    }
}
