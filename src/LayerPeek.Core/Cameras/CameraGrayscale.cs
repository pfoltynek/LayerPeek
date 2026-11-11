using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LayerPeek.Core.Cameras;

/// <summary>Pixel layout of a <see cref="CameraFrameBuffer"/>.</summary>
public enum CameraPixelFormat
{
    /// <summary>32-bit BGRA, as decoded (<see cref="CameraFrame"/>).</summary>
    Bgra32,

    /// <summary>
    /// 8-bit full-range luma, one byte per pixel (T-153: samples for print failure detection): the camera's Y plane
    /// (T-189), or from a BGRA frame with BT.601 weights.
    /// </summary>
    Gray8,
}

/// <summary>
/// Size of the 8-bit grayscale copies a frame subscription asks for instead of BGRA copies (T-153,
/// <see cref="CameraFrameSubscriptionOptions.Grayscale"/>): each frame is downscaled to exactly this size while it is copied,
/// so the subscription holds ~57.6 KB per buffer at 320×180 instead of ~3.7 MB of a 1280×720 BGRA frame. A frame of
/// another aspect ratio is stretched (normalized coordinates of the frame stay valid).
/// </summary>
public readonly record struct CameraGrayscaleSize
{
    public const int MinSide = 16;

    public const int MaxSide = 4096;

    /// <exception cref="ArgumentOutOfRangeException">A side is outside <see cref="MinSide"/>–<see cref="MaxSide"/>.</exception>
    public CameraGrayscaleSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, MinSide);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, MaxSide);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, MinSide);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, MaxSide);
        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Bytes of one copy.</summary>
    public int Length => Width * Height;

    internal bool IsValid => Width >= MinSide;

    /// <summary>Both sides in one value for atomic reads and writes; 0 is no size.</summary>
    internal static long Pack(CameraGrayscaleSize? size) => size is { } value ? ((long)value.Width << 32) | (uint)value.Height : 0;

    internal static CameraGrayscaleSize? Unpack(long packed) => packed == 0 ? null : new CameraGrayscaleSize((int)(packed >> 32), (int)(uint)packed);
}

/// <summary>
/// A decoded frame as 8-bit full-range luma (0–255), rows top-down, scaled by the feed to the size a grayscale subscriber
/// wants (T-189, <see cref="CameraStreamItem.GrayscaleFrame"/>): straight from the picture's Y plane, without a BGRA frame
/// in between. Borrows the feed's buffer like <see cref="CameraFrame"/>: valid only until the enumerator advances.
/// </summary>
public readonly struct CameraGrayscaleFrame
{
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="stride">Bytes from the start of one row to the start of the next; at least <paramref name="width"/>.</param>
    /// <param name="pixels">At least <c>stride * (height - 1) + width</c> bytes.</param>
    /// <param name="sourceWidth">Width of the camera's image the frame was scaled from.</param>
    /// <param name="sourceHeight">Height of the camera's image the frame was scaled from.</param>
    public CameraGrayscaleFrame(int width, int height, int stride, ReadOnlyMemory<byte> pixels, int sourceWidth, int sourceHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, width);
        long required = ((long)stride * (height - 1)) + width;
        if (pixels.Length < required)
        {
            throw new ArgumentOutOfRangeException(nameof(pixels), pixels.Length, $"The frame needs at least {required} bytes.");
        }

        Width = width;
        Height = height;
        Stride = stride;
        Pixels = pixels;
        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
    }

    public int Width { get; }

    public int Height { get; }

    public int Stride { get; }

    public int SourceWidth { get; }

    public int SourceHeight { get; }

    /// <summary>Luma pixels; borrowed, see the type remarks.</summary>
    public ReadOnlyMemory<byte> Pixels { get; }

    /// <summary>Whether this frame is of <paramref name="size"/> (an empty frame never is).</summary>
    internal bool Is(CameraGrayscaleSize size) => Width == size.Width && Height == size.Height;
}

/// <summary>
/// Area-averaging downscale of a BGRA frame into 8-bit luma (Y = (77 R + 150 G + 29 B) / 256 per pixel, then the mean of
/// the source block of every target pixel). One pass over the source, integer arithmetic only; the block bounds are
/// computed once per size and reused, so the steady state allocates nothing. A target larger than the source repeats
/// source pixels (nearest). Not thread-safe: one instance per producer.
/// <para>
/// T-189: the FFmpeg feed scales the luma plane itself (<see cref="CameraGrayscaleFrame"/>); this is the fallback for a
/// feed that yields only BGRA frames, and for a grayscale subscriber of another size than the one the stream converts.
/// </para>
/// </summary>
internal sealed class GrayscaleScaler
{
    private int _sourceWidth;
    private int _sourceHeight;
    private int _targetWidth;
    private int _targetHeight;

    // [start, end) of the source columns / rows of every target column / row.
    private int[] _columnStart = [];
    private int[] _columnEnd = [];
    private int[] _rowStart = [];
    private int[] _rowEnd = [];
    private int[] _sums = [];

    /// <summary>Writes the luma of <paramref name="frame"/> downscaled to <paramref name="size"/> into <paramref name="target"/> (rows tightly packed).</summary>
    public void Scale(in CameraFrame frame, CameraGrayscaleSize size, Span<byte> target)
    {
        int width = size.Width;
        int height = size.Height;
        if (target.Length < width * height)
        {
            throw new ArgumentException("The target is smaller than the grayscale size.", nameof(target));
        }

        if (frame.Width != _sourceWidth || frame.Height != _sourceHeight || width != _targetWidth || height != _targetHeight)
        {
            Configure(frame.Width, frame.Height, width, height);
        }

        ReadOnlySpan<byte> source = frame.Pixels.Span;
        Span<int> sums = _sums;
        ref byte output = ref MemoryMarshal.GetReference(target);
        for (int ty = 0; ty < height; ty++)
        {
            sums.Clear();
            int y0 = _rowStart[ty];
            int y1 = _rowEnd[ty];
            for (int y = y0; y < y1; y++)
            {
                ref byte row = ref Unsafe.Add(ref MemoryMarshal.GetReference(source), y * frame.Stride);
                for (int tx = 0; tx < width; tx++)
                {
                    int sum = 0;
                    int end = _columnEnd[tx];
                    for (int x = _columnStart[tx]; x < end; x++)
                    {
                        ref byte pixel = ref Unsafe.Add(ref row, x * CameraFrame.BytesPerPixel);
                        // BGRA: B, G, R.
                        sum += ((29 * pixel) + (150 * Unsafe.Add(ref pixel, 1)) + (77 * Unsafe.Add(ref pixel, 2))) >> 8;
                    }

                    sums[tx] += sum;
                }
            }

            int rows = y1 - y0;
            int offset = ty * width;
            for (int tx = 0; tx < width; tx++)
            {
                int count = rows * (_columnEnd[tx] - _columnStart[tx]);
                Unsafe.Add(ref output, offset + tx) = (byte)((sums[tx] + (count >> 1)) / count);
            }
        }
    }

    private void Configure(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        if (_columnStart.Length != targetWidth)
        {
            _columnStart = new int[targetWidth];
            _columnEnd = new int[targetWidth];
            _sums = new int[targetWidth];
        }

        if (_rowStart.Length != targetHeight)
        {
            _rowStart = new int[targetHeight];
            _rowEnd = new int[targetHeight];
        }

        Bounds(sourceWidth, _columnStart, _columnEnd);
        Bounds(sourceHeight, _rowStart, _rowEnd);
        _sourceWidth = sourceWidth;
        _sourceHeight = sourceHeight;
        _targetWidth = targetWidth;
        _targetHeight = targetHeight;
    }

    /// <summary>Splits <paramref name="source"/> pixels into <c>start.Length</c> blocks of at least one pixel each.</summary>
    private static void Bounds(int source, int[] start, int[] end)
    {
        int target = start.Length;
        for (int i = 0; i < target; i++)
        {
            int first = (int)((long)i * source / target);
            int last = (int)((long)(i + 1) * source / target);
            first = Math.Min(first, source - 1);
            start[i] = first;
            end[i] = Math.Max(last, first + 1);
        }
    }
}
