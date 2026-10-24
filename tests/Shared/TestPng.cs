using System.Buffers.Binary;
using System.IO.Compression;

namespace LayerPeek.Tests.Shared;

/// <summary>Small, valid PNG files (RGB, 8 bit, one solid colour) built in memory for preview tests.</summary>
internal static class TestPng
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] Create(int width, int height, byte red = 0xFA, byte green = 0x68, byte blue = 0x31)
    {
        // Raw scanlines: filter byte 0 + RGB per pixel.
        byte[] raw = new byte[height * (1 + (width * 3))];
        for (int y = 0; y < height; y++)
        {
            int row = y * (1 + (width * 3));
            for (int x = 0; x < width; x++)
            {
                raw[row + 1 + (x * 3)] = red;
                raw[row + 2 + (x * 3)] = green;
                raw[row + 3 + (x * 3)] = blue;
            }
        }

        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        using MemoryStream png = new();
        png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        byte[] header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        header[8] = 8; // bit depth
        header[9] = 2; // RGB
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length);
        stream.Write(number);
        byte[] typeAndData = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++)
        {
            typeAndData[i] = (byte)type[i];
        }

        data.CopyTo(typeAndData.AsSpan(4));
        stream.Write(typeAndData);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc(typeAndData));
        stream.Write(number);
    }

    private static uint Crc(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFF;
    }

    private static uint[] BuildCrcTable()
    {
        uint[] table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
