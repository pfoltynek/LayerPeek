namespace LayerPeek.App.Tray.Icons;

/// <summary>Writes a Windows <c>.ico</c> whose images are PNG-compressed (supported by Windows since Vista).</summary>
internal static class IconFile
{
    private const int HeaderSize = 6;
    private const int EntrySize = 16;

    /// <param name="images">Square PNG images (1–256 px), written in the given order.</param>
    public static byte[] FromPngImages(IReadOnlyList<(int Size, byte[] Png)> images)
    {
        ArgumentNullException.ThrowIfNull(images);
        if (images.Count is 0 or > ushort.MaxValue)
        {
            throw new ArgumentException("An icon holds 1 to 65535 images.", nameof(images));
        }

        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)images.Count);
            int offset = HeaderSize + (EntrySize * images.Count);
            foreach ((int size, byte[] png) in images)
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(size, 256);
                byte dimension = size == 256 ? (byte)0 : (byte)size;
                writer.Write(dimension);
                writer.Write(dimension);
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write(png.Length);
                writer.Write(offset);
                offset += png.Length;
            }

            foreach ((_, byte[] png) in images)
            {
                writer.Write(png);
            }
        }

        return stream.ToArray();
    }
}
