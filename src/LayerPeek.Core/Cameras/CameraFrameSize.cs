namespace LayerPeek.Core.Cameras;

/// <summary>
/// The largest frame a display reader wants, in device pixels (T-184: the size the image is drawn at × the monitor's
/// scaling; see <see cref="CameraFrameSubscriptionOptions.DisplaySize"/>). A frame is scaled down to fit within it with
/// its aspect ratio kept (<see cref="Fit"/>) and never scaled up.
/// </summary>
public readonly record struct CameraFrameSize
{
    /// <summary>The largest frame the decoder accepts.</summary>
    public const int MaxSide = 16384;

    /// <exception cref="ArgumentOutOfRangeException">A side is below 1 or above <see cref="MaxSide"/>.</exception>
    public CameraFrameSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, MaxSide);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, MaxSide);
        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>
    /// The size a source of <paramref name="sourceWidth"/> × <paramref name="sourceHeight"/> is scaled to within this
    /// size: the side that limits gets exactly this size's value, the other one keeps the aspect ratio (rounded, at least
    /// 1). A source that already fits keeps its size (no upscaling).
    /// </summary>
    public CameraFrameSize Fit(int sourceWidth, int sourceHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);
        if (sourceWidth <= Width && sourceHeight <= Height)
        {
            return new CameraFrameSize(sourceWidth, sourceHeight);
        }

        // Integer arithmetic: Width / sourceWidth <= Height / sourceHeight means the width limits.
        if ((long)Width * sourceHeight <= (long)Height * sourceWidth)
        {
            long height = (((long)sourceHeight * Width) + (sourceWidth / 2)) / sourceWidth;
            return new CameraFrameSize(Width, (int)Math.Max(1, height));
        }

        long width = (((long)sourceWidth * Height) + (sourceHeight / 2)) / sourceHeight;
        return new CameraFrameSize((int)Math.Max(1, width), Height);
    }

    /// <summary>The smallest size that contains both (the frame several display readers share).</summary>
    internal static CameraFrameSize Union(CameraFrameSize a, CameraFrameSize b) => new(Math.Max(a.Width, b.Width), Math.Max(a.Height, b.Height));

    /// <summary>Both sides in one value for atomic reads and writes; 0 is no size.</summary>
    internal static long Pack(CameraFrameSize? size) => size is { } value ? ((long)value.Width << 32) | (uint)value.Height : 0;

    internal static CameraFrameSize? Unpack(long packed) => packed == 0 ? null : new CameraFrameSize((int)(packed >> 32), (int)(uint)packed);
}
