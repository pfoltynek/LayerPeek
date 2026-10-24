using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Compression;

namespace LayerPeek.Simulator.Printers;

/// <summary>
/// G-code previews of the simulated files (T-086), generated here (no image assets, nothing to license): a disc in a
/// colour of its own per file on a dark plate with a lighter border, as PNG (RGB, 8 bit). <c>/thumb/s</c> is 16 × 16 px
/// (the only size Buddy accepts for it), <c>/thumb/l</c> 380 × 285 px (the PNG size of the stock Prusa profiles).
/// </summary>
public static class PreviewImages
{
    public const int SmallSide = 16;

    public const int LargeWidth = 380;

    public const int LargeHeight = 285;

    private static readonly ConcurrentDictionary<(string File, bool Large), byte[]> Cache = new();

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static readonly (byte R, byte G, byte B)[] Colours =
    [
        (0xFA, 0x68, 0x31), // Prusa orange
        (0x3B, 0x8E, 0xD0),
        (0x4C, 0xAF, 0x50),
        (0xE0, 0xC0, 0x30),
    ];

    /// <summary>The PNG of <paramref name="file"/> (cached; callers must not modify it).</summary>
    public static byte[] For(SimulatedFile file, bool large)
    {
        ArgumentNullException.ThrowIfNull(file);
        return Cache.GetOrAdd((file.Name, large), static key =>
            Render(key.Large ? LargeWidth : SmallSide, key.Large ? LargeHeight : SmallSide, Colours[ColourIndex(key.File)]));
    }

    /// <summary>The simulated files get distinct colours in job order; any other name one by its hash.</summary>
    private static int ColourIndex(string fileName)
    {
        IReadOnlyList<SimulatedFile> files = PrinterSimulation.AllFiles;
        for (int i = 0; i < files.Count; i++)
        {
            if (files[i].Name == fileName)
            {
                return i % Colours.Length;
            }
        }

        return (int)(StableHash(fileName) % (uint)Colours.Length);
    }

    /// <summary>
    /// ETag like the firmware's (a quoted unsigned 32-bit number derived from the file's metadata, here name and size), so
    /// that clients see the same value for the same file and send it back in If-None-Match.
    /// </summary>
    public static string ETag(SimulatedFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return "\"" + (StableHash(file.Name) ^ (uint)file.Size).ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"";
    }

    private static byte[] Render(int width, int height, (byte R, byte G, byte B) colour)
    {
        int stride = 1 + (width * 3);
        byte[] raw = new byte[height * stride];
        double cx = (width - 1) / 2.0;
        double cy = (height - 1) / 2.0;
        double radius = Math.Min(width, height) * 0.35;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool border = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                double dx = x - cx;
                double dy = y - cy;
                (byte r, byte g, byte b) = border ? ((byte)0x60, (byte)0x60, (byte)0x60)
                    : (dx * dx) + (dy * dy) <= radius * radius ? colour
                    : ((byte)0x2B, (byte)0x2B, (byte)0x2B);
                int i = (y * stride) + 1 + (x * 3);
                raw[i] = r;
                raw[i + 1] = g;
                raw[i + 2] = b;
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
        header[9] = 2; // colour type RGB
        WriteChunk(png, "IHDR"u8, header);
        WriteChunk(png, "IDAT"u8, compressed.ToArray());
        WriteChunk(png, "IEND"u8, []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length);
        stream.Write(number);
        stream.Write(type);
        stream.Write(data);
        uint crc = Crc(0xFFFFFFFF, type);
        crc = Crc(crc, data) ^ 0xFFFFFFFF;
        BinaryPrimitives.WriteUInt32BigEndian(number, crc);
        stream.Write(number);
    }

    private static uint Crc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte value in data)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc;
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

    /// <summary>FNV-1a over the UTF-16 code units: stable across runs (unlike string.GetHashCode).</summary>
    private static uint StableHash(string value)
    {
        uint hash = 2166136261;
        foreach (char c in value)
        {
            hash = (hash ^ c) * 16777619;
        }

        return hash;
    }
}
