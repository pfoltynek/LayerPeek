using Avalonia.Platform;

namespace PrusaTrayDiagnostic.App.Cameras;

/// <summary>
/// Copies tightly packed BGRA rows into a bitmap: the camera window (every frame) and the settings preview (T-034).
/// One block copy when the row layouts match (the usual case), else row by row into the padded target rows.
/// </summary>
internal static class BgraPixels
{
    /// <summary>
    /// Copies <paramref name="height"/> rows of <paramref name="rowBytes"/> bytes from <paramref name="source"/> (packed)
    /// into <paramref name="target"/>, whose rows start <paramref name="targetRowBytes"/> apart. Target padding is left
    /// untouched; the last target row needs no padding.
    /// </summary>
    /// <exception cref="ArgumentException">The sizes do not describe the given buffers.</exception>
    public static void CopyRows(ReadOnlySpan<byte> source, int rowBytes, int height, Span<byte> target, int targetRowBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rowBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfLessThan(targetRowBytes, rowBytes);
        long sourceBytes = (long)rowBytes * height;
        if (source.Length < sourceBytes)
        {
            throw new ArgumentException($"The source needs at least {sourceBytes} bytes.", nameof(source));
        }

        long targetBytes = ((long)targetRowBytes * (height - 1)) + rowBytes;
        if (target.Length < targetBytes)
        {
            throw new ArgumentException($"The target needs at least {targetBytes} bytes.", nameof(target));
        }

        if (targetRowBytes == rowBytes)
        {
            source[..(int)sourceBytes].CopyTo(target);
            return;
        }

        for (int row = 0; row < height; row++)
        {
            source.Slice(row * rowBytes, rowBytes).CopyTo(target.Slice(row * targetRowBytes, rowBytes));
        }
    }

    /// <summary>Copies packed BGRA rows into a locked bitmap of the same pixel size.</summary>
    public static unsafe void CopyTo(ReadOnlySpan<byte> source, int rowBytes, int height, ILockedFramebuffer target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Size.Height < height || (long)target.Size.Width * 4 < rowBytes)
        {
            throw new ArgumentException("The bitmap is smaller than the image.", nameof(target));
        }

        // The framebuffer holds RowBytes × its height bytes.
        int length = checked(target.RowBytes * target.Size.Height);
        CopyRows(source, rowBytes, height, new Span<byte>((void*)target.Address, length), target.RowBytes);
    }
}
