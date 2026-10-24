namespace LayerPeek.Core.Cameras;

/// <summary>
/// Default client size of a camera window in whole device-independent units (T-035), any aspect ratio. Width
/// <see cref="MinWidth"/>–<see cref="MaxSize"/>, height <see cref="MinHeight"/>–<see cref="MaxSize"/>: the minimum is
/// the minimum size of the camera window itself, the maximum the largest remembered size it accepts (T-014).
/// </summary>
public sealed record CameraWindowSize
{
    /// <summary>Same as <c>MinWidth</c> of the camera window.</summary>
    public const int MinWidth = 160;

    /// <summary>Same as <c>MinHeight</c> of the camera window.</summary>
    public const int MinHeight = 90;

    /// <summary>Largest width or height.</summary>
    public const int MaxSize = 16384;

    /// <exception cref="ArgumentOutOfRangeException">The width or height is out of range (see <see cref="IsValid"/>).</exception>
    public CameraWindowSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, MinWidth);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, MaxSize);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, MinHeight);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, MaxSize);
        Width = width;
        Height = height;
    }

    /// <summary>The offered presets (T-035 decision), smallest first.</summary>
    public static IReadOnlyList<CameraWindowSize> Presets { get; } =
        [new(640, 360), new(960, 540), new(1280, 720), new(1920, 1080)];

    public int Width { get; }

    public int Height { get; }

    /// <summary>Whether <paramref name="width"/> × <paramref name="height"/> can be used as a window size.</summary>
    public static bool IsValid(int width, int height) =>
        width is >= MinWidth and <= MaxSize && height is >= MinHeight and <= MaxSize;
}
