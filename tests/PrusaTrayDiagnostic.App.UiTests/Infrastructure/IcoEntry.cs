using System.Buffers.Binary;

namespace PrusaTrayDiagnostic.App.UiTests.Infrastructure;

/// <summary>One image of a Windows <c>.ico</c> file, read from its directory (ICONDIR / ICONDIRENTRY).</summary>
internal sealed record IcoEntry(int Size, int BitCount, int Offset, int Length, bool IsPng)
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public byte[] Data(byte[] ico) => ico.AsSpan(Offset, Length).ToArray();

    /// <summary>Every entry in file order; fails the test when the header or an entry is malformed.</summary>
    public static IReadOnlyList<IcoEntry> ReadAll(byte[] ico)
    {
        ArgumentNullException.ThrowIfNull(ico);
        Assert.True(ico.Length >= 6, "The icon file is shorter than its header.");
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(0)));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(2)));
        int count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));
        Assert.True(ico.Length >= 6 + (16 * count), "The icon directory is truncated.");

        List<IcoEntry> entries = [];
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> entry = ico.AsSpan(6 + (16 * i), 16);
            int width = entry[0] == 0 ? 256 : entry[0];
            int height = entry[1] == 0 ? 256 : entry[1];
            Assert.Equal(width, height);
            int bitCount = BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]);
            int length = BinaryPrimitives.ReadInt32LittleEndian(entry[8..]);
            int offset = BinaryPrimitives.ReadInt32LittleEndian(entry[12..]);
            Assert.True(offset >= 0 && length > 0 && offset + length <= ico.Length, $"Image {i} lies outside the file.");
            bool isPng = ico.AsSpan(offset).StartsWith(PngSignature);
            if (!isPng)
            {
                // BITMAPINFOHEADER: 40 bytes, width, double height (image + AND mask).
                Assert.Equal(40, BinaryPrimitives.ReadInt32LittleEndian(ico.AsSpan(offset)));
                Assert.Equal(width, BinaryPrimitives.ReadInt32LittleEndian(ico.AsSpan(offset + 4)));
                Assert.Equal(2 * height, BinaryPrimitives.ReadInt32LittleEndian(ico.AsSpan(offset + 8)));
            }

            entries.Add(new IcoEntry(width, bitCount, offset, length, isPng));
        }

        return entries;
    }
}
