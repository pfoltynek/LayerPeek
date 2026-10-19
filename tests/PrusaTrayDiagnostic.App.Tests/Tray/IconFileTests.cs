using System.Buffers.Binary;

using PrusaTrayDiagnostic.App.Tray.Icons;

namespace PrusaTrayDiagnostic.App.Tests.Tray;

public sealed class IconFileTests
{
    [Fact]
    public void WritesDirectoryAndImagesInOrder()
    {
        byte[] first = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];
        byte[] second = [0x89, 0x50, 0x4E, 0x47, 4];
        byte[] third = [0x89, 0x50, 0x4E, 0x47];

        byte[] ico = IconFile.FromPngImages([(32, first), (16, second), (256, third)]);

        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(0)));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(2)));
        Assert.Equal(3, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4)));
        int offset = 6 + (3 * 16);
        (int Dimension, byte[] Data)[] expected = [(32, first), (16, second), (0, third)];
        for (int i = 0; i < expected.Length; i++)
        {
            ReadOnlySpan<byte> entry = ico.AsSpan(6 + (16 * i), 16);
            Assert.Equal(expected[i].Dimension, entry[0]);
            Assert.Equal(expected[i].Dimension, entry[1]);
            Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(entry[4..]));
            Assert.Equal(32, BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]));
            Assert.Equal(expected[i].Data.Length, BinaryPrimitives.ReadInt32LittleEndian(entry[8..]));
            Assert.Equal(offset, BinaryPrimitives.ReadInt32LittleEndian(entry[12..]));
            Assert.Equal(expected[i].Data, ico.AsSpan(offset, expected[i].Data.Length).ToArray());
            offset += expected[i].Data.Length;
        }

        Assert.Equal(offset, ico.Length);
    }

    [Fact]
    public void RejectsInvalidInput()
    {
        Assert.Throws<ArgumentException>(() => IconFile.FromPngImages([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => IconFile.FromPngImages([(0, [1])]));
        Assert.Throws<ArgumentOutOfRangeException>(() => IconFile.FromPngImages([(257, [1])]));
    }
}
