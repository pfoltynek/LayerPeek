using System.Buffers.Binary;

using LayerPeek.App.Settings;
using LayerPeek.Core.Images;

namespace LayerPeek.App.Tests.Printers;

/// <summary>Byte arrays that pass the signature checks (they are never decoded by these fakes).</summary>
internal static class TestImages
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>A distinct "PNG" (signature + <paramref name="marker"/>); a new array every call.</summary>
    public static byte[] Png(byte marker = 1) => [.. PngSignature, marker, marker, marker];

    public static byte[] Jpeg() => [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10];

    public static byte[] PngHeader(uint width, uint height)
    {
        byte[] png = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(png, 0);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(8), 13);
        "IHDR"u8.CopyTo(png.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20), height);
        return png;
    }

    /// <summary>SOI, APP0, an EXIF APP1 (when <paramref name="orientation"/> is set), SOF, SOS: headers only, no image data.</summary>
    public static byte[] JpegHeaders(int width, int height, int? orientation, bool littleEndian, byte sof = 0xC0)
    {
        List<byte> data = [0xFF, 0xD8];
        data.AddRange([0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1, 0, 0]);
        if (orientation is { } value)
        {
            data.AddRange(ExifSegment(value, littleEndian));
        }

        byte[] frame = new byte[2 + 2 + 6];
        frame[0] = 0xFF;
        frame[1] = sof;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2), 8);
        frame[4] = 8;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(5), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(7), (ushort)width);
        data.AddRange(frame);
        data.AddRange([0xFF, 0xDA, 0x00, 0x02]);
        return [.. data];
    }

    /// <summary>An APP1 segment "Exif\0\0" + TIFF header + IFD0 with one entry: Orientation (SHORT) = <paramref name="orientation"/>.</summary>
    /// <summary>
    /// <paramref name="png"/> with an <c>eXIf</c> chunk (TIFF data with Orientation = <paramref name="orientation"/>, correct CRC)
    /// right after IHDR, as PNG 1.5 writers put it.
    /// </summary>
    public static byte[] WithPngExif(byte[] png, int orientation)
    {
        byte[] tiff = ExifSegment(orientation, littleEndian: false)[(4 + 6)..];
        byte[] chunk = new byte[12 + tiff.Length];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)tiff.Length);
        "eXIf"u8.CopyTo(chunk.AsSpan(4));
        tiff.CopyTo(chunk, 8);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + tiff.Length), Crc32(chunk.AsSpan(4, 4 + tiff.Length)));
        const int AfterIhdr = 8 + 12 + 13;
        return [.. png.AsSpan(0, AfterIhdr), .. chunk, .. png.AsSpan(AfterIhdr)];
    }

    /// <summary>The CRC-32 of PNG chunks (ISO 3309, polynomial 0xEDB88320).</summary>
    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }

        return ~crc;
    }

    public static byte[] ExifSegment(int orientation, bool littleEndian)
    {
        byte[] tiff = new byte[8 + 2 + 12 + 4];
        tiff[0] = tiff[1] = littleEndian ? (byte)'I' : (byte)'M';
        Write16(tiff, 2, 42);
        Write32(tiff, 4, 8);
        Write16(tiff, 8, 1);
        Write16(tiff, 10, 0x0112);
        Write16(tiff, 12, 3);
        Write32(tiff, 14, 1);
        Write16(tiff, 18, (ushort)orientation);
        byte[] payload = [.. "Exif\0\0"u8, .. tiff];
        byte[] segment = new byte[4 + payload.Length];
        segment[0] = 0xFF;
        segment[1] = 0xE1;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(payload.Length + 2));
        payload.CopyTo(segment, 4);
        return segment;

        void Write16(byte[] target, int offset, ushort v)
        {
            if (littleEndian)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(target.AsSpan(offset), v);
            }
            else
            {
                BinaryPrimitives.WriteUInt16BigEndian(target.AsSpan(offset), v);
            }
        }

        void Write32(byte[] target, int offset, uint v)
        {
            if (littleEndian)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(target.AsSpan(offset), v);
            }
            else
            {
                BinaryPrimitives.WriteUInt32BigEndian(target.AsSpan(offset), v);
            }
        }
    }
}

/// <summary>In-memory <see cref="IPrinterImageStore"/> that records operations and fails on demand.</summary>
internal sealed class FakeImageStore : IPrinterImageStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, byte[]> _images = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _operations = [];

    public string DirectoryPath => "memory";

    /// <summary>Ids whose save fails.</summary>
    public HashSet<string> FailSave { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ids whose read fails with an <see cref="IOException"/>.</summary>
    public HashSet<string> FailRead { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ids whose delete fails.</summary>
    public HashSet<string> FailDelete { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>When set, reads wait for it (to test changes during a load).</summary>
    public TaskCompletionSource? ReadGate { get; set; }

    /// <summary>"read id", "save id", "delete id", "orphans id1,id2" in call order.</summary>
    public IReadOnlyList<string> Operations
    {
        get
        {
            lock (_lock)
            {
                return [.. _operations];
            }
        }
    }

    public IReadOnlyDictionary<string, byte[]> Images
    {
        get
        {
            lock (_lock)
            {
                return new Dictionary<string, byte[]>(_images, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public void Seed(string printerId, byte[] png)
    {
        lock (_lock)
        {
            _images[printerId] = png;
        }
    }

    public string GetFilePath(string printerId) => Path.Combine(DirectoryPath, printerId + ".png");

    public async Task<byte[]?> ReadAsync(string printerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Record("read " + printerId);
        if (ReadGate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        lock (_lock)
        {
            if (FailRead.Contains(printerId))
            {
                throw new IOException("read failed");
            }

            return _images.GetValueOrDefault(printerId);
        }
    }

    public Task SaveAsync(string printerId, ReadOnlyMemory<byte> png, CancellationToken cancellationToken)
    {
        Record("save " + printerId);
        lock (_lock)
        {
            if (FailSave.Contains(printerId))
            {
                return Task.FromException(new IOException("save failed"));
            }

            _images[printerId] = png.ToArray();
            return Task.CompletedTask;
        }
    }

    public Task<bool> DeleteAsync(string printerId, CancellationToken cancellationToken)
    {
        Record("delete " + printerId);
        lock (_lock)
        {
            if (FailDelete.Contains(printerId))
            {
                return Task.FromException<bool>(new IOException("delete failed"));
            }

            return Task.FromResult(_images.Remove(printerId));
        }
    }

    public Task<int> RemoveOrphansAsync(IEnumerable<string> printerIds, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        HashSet<string> keep = new(printerIds, StringComparer.OrdinalIgnoreCase);
        Record("orphans " + string.Join(",", keep.Order(StringComparer.Ordinal)));
        lock (_lock)
        {
            string[] orphans = [.. _images.Keys.Where(id => !keep.Contains(id))];
            foreach (string id in orphans)
            {
                _images.Remove(id);
            }

            return Task.FromResult(orphans.Length);
        }
    }

    private void Record(string operation)
    {
        lock (_lock)
        {
            _operations.Add(operation);
        }
    }
}

/// <summary>Image processor without a graphics platform: hands back <see cref="Result"/> or fails to decode.</summary>
internal sealed class FakeImageProcessor : IPrinterImageProcessor
{
    public byte[] Result { get; set; } = TestImages.Png(7);

    public bool Fail { get; set; }

    public int Calls { get; private set; }

    public Task<byte[]> ConvertToPngAsync(ReadOnlyMemory<byte> source, PrinterImageFormat format, int maxPixelSize, CancellationToken cancellationToken)
    {
        Calls++;
        return Fail ? Task.FromException<byte[]>(new PrinterImageDecodeException()) : Task.FromResult(Result);
    }
}

/// <summary>The file dialog, answered by the test: a file content, "cancelled" or an exception.</summary>
internal sealed class FakeImagePicker : IImageFilePicker
{
    /// <summary>The content of the chosen file; <see langword="null"/> = the user cancelled.</summary>
    public byte[]? Content { get; set; } = TestImages.Png();

    public Exception? Throw { get; set; }

    public int Calls { get; private set; }

    /// <summary>The folder the last dialog was asked to open in.</summary>
    public string? StartDirectory { get; private set; }

    public Task<Stream?> PickAsync(string? startDirectory = null)
    {
        Calls++;
        StartDirectory = startDirectory;
        if (Throw is not null)
        {
            return Task.FromException<Stream?>(Throw);
        }

        return Task.FromResult<Stream?>(Content is null ? null : new MemoryStream(Content, writable: false));
    }
}
