using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LayerPeek.Camera.FFmpeg;

/// <summary>Pinned, aligned output buffers of the swscale conversions (<see cref="BgraConverter"/>, <see cref="GrayscaleConverter"/>).</summary>
internal static unsafe class AlignedBuffer
{
    /// <summary>Row and buffer alignment for converted images (SIMD friendly for swscale).</summary>
    public const int Alignment = 64;

    public static int AlignUp(int value) => (value + (Alignment - 1)) & ~(Alignment - 1);

    /// <summary>
    /// A pinned buffer of at least <paramref name="length"/> bytes plus alignment and its aligned start: <paramref name="buffer"/>
    /// is kept when it is large enough; the first one is exact, a growing one gets half again as much room.
    /// </summary>
    public static byte* Ensure(ref byte[]? buffer, int length, out int offset)
    {
        if (buffer is null || buffer.Length < length + Alignment)
        {
            // Pinned (POH): FFmpeg writes through a raw pointer and the address must never move.
            long size = buffer is null ? length + Alignment : Math.Max(length + Alignment, buffer.Length * 3L / 2);
            buffer = GC.AllocateUninitializedArray<byte>((int)Math.Min(size, Array.MaxLength), pinned: true);
        }

        byte* start = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(buffer));
        offset = (int)((Alignment - ((nuint)start % Alignment)) % Alignment);
        return start + offset;
    }
}
