using System.Buffers.Binary;

namespace LayerPeek.Core.Previews;

/// <summary>
/// A G-code preview as PNG bytes with the pixel size from its header. Immutable. Only the header is checked here;
/// decoding (and handling a damaged image body) is the UI's job.
/// </summary>
public sealed class PrintPreview
{
    /// <summary>Largest accepted width or height, so that a hostile or broken file cannot make the UI allocate a huge bitmap.</summary>
    public const int MaxSide = 2048;

    private static ReadOnlySpan<byte> Signature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    private static ReadOnlySpan<byte> HeaderChunk => [0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R'];

    // Signature, IHDR length and type, width, height.
    private const int MinimumLength = 8 + 8 + 4 + 4;

    private readonly byte[] _png;

    private PrintPreview(byte[] png, int width, int height)
    {
        _png = png;
        Width = width;
        Height = height;
    }

    /// <summary>The PNG file.</summary>
    public ReadOnlyMemory<byte> Png => _png;

    /// <summary>Width in pixels, 1–<see cref="MaxSide"/>.</summary>
    public int Width { get; }

    /// <summary>Height in pixels, 1–<see cref="MaxSide"/>.</summary>
    public int Height { get; }

    /// <summary>
    /// Copies <paramref name="png"/> when it starts like a PNG (signature and an <c>IHDR</c> chunk) with both sides
    /// within 1–<see cref="MaxSide"/>; otherwise returns <see langword="null"/>.
    /// </summary>
    public static PrintPreview? FromPng(ReadOnlySpan<byte> png)
    {
        if (!TryReadSize(png, out int width, out int height))
        {
            return null;
        }

        return new PrintPreview(png.ToArray(), width, height);
    }

    /// <summary>Reads the pixel size from the PNG header; false when <paramref name="png"/> does not start like a PNG within the limits.</summary>
    public static bool TryReadSize(ReadOnlySpan<byte> png, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (png.Length < MinimumLength || !png.StartsWith(Signature) || !png[8..].StartsWith(HeaderChunk))
        {
            return false;
        }

        uint w = BinaryPrimitives.ReadUInt32BigEndian(png[16..]);
        uint h = BinaryPrimitives.ReadUInt32BigEndian(png[20..]);
        if (w is 0 or > MaxSide || h is 0 or > MaxSide)
        {
            return false;
        }

        width = (int)w;
        height = (int)h;
        return true;
    }
}
