namespace LayerPeek.Core.Cameras;

/// <summary>
/// T-195: decides whether the frame for display (<see cref="CameraStreamDemand.DisplaySize"/>) is scaled down by the decoder
/// or converted in the camera's resolution and left to the renderer to scale. Scaling down costs a swscale pass of its own;
/// measured on macOS (T-195, 720p and 1080p), drawing a full frame scaled down to more than about 71 % (1/√2) of its side
/// costs the renderer next to nothing, so there the full conversion is cheaper, while below that the renderer's scaling
/// costs clearly more than the decoder's (it switches to a more expensive filter, presumably mipmaps).
/// </summary>
/// <remarks>
/// <para>
/// The decision uses the larger of the two sides' ratios of the fitted size to the source. Scaling starts once that ratio is
/// at most <see cref="ScaleFromPercent"/> and stops again only once it reaches <see cref="FullFromPercent"/>; in between the
/// previous decision stays (hysteresis), so that resizing a window slowly around the threshold does not switch the decoder
/// (and the window's bitmap) back and forth. The first decision starts from full resolution. The window requests its image
/// area rounded up to 32 device pixels (T-184), so the area drawn may be up to 2.5 % of a 720p side smaller than the
/// requested size: from <see cref="FullFromPercent"/> on it is still above the renderer's 71 %.
/// </para>
/// <para>
/// One instance per stream; not thread-safe (the decoder calls it on its streaming thread). Costs a few integer operations
/// and never allocates.
/// </para>
/// </remarks>
public sealed class CameraDisplayScaler
{
    /// <summary>A frame fitted to at most this share of the source's side (in percent) is scaled down.</summary>
    public const int ScaleFromPercent = 70;

    /// <summary>A frame fitted to at least this share of the source's side (in percent) is converted in full resolution.</summary>
    public const int FullFromPercent = 75;

    /// <summary>Whether the last decision was to scale down.</summary>
    public bool IsScaling { get; private set; }

    /// <summary>
    /// The size the frame for <paramref name="displaySize"/> is converted to from a source of
    /// <paramref name="sourceWidth"/> × <paramref name="sourceHeight"/>: its fitted size (<see cref="CameraFrameSize.Fit"/>)
    /// when scaling down pays off, <see langword="null"/> for the camera's resolution (also for a source that already fits).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A side of the source is below 1.</exception>
    public CameraFrameSize? Target(CameraFrameSize displaySize, int sourceWidth, int sourceHeight)
    {
        CameraFrameSize fit = displaySize.Fit(sourceWidth, sourceHeight);
        IsScaling = IsScaling
            ? Below(fit, sourceWidth, sourceHeight, FullFromPercent, orEqual: false)
            : Below(fit, sourceWidth, sourceHeight, ScaleFromPercent, orEqual: true);
        return IsScaling ? fit : null;
    }

    /// <summary>Whether both sides of <paramref name="fit"/> are below (or equal to) <paramref name="percent"/> % of the source's.</summary>
    private static bool Below(CameraFrameSize fit, int sourceWidth, int sourceHeight, int percent, bool orEqual)
    {
        long width = fit.Width * 100L;
        long height = fit.Height * 100L;
        long maxWidth = (long)sourceWidth * percent;
        long maxHeight = (long)sourceHeight * percent;
        return orEqual ? width <= maxWidth && height <= maxHeight : width < maxWidth && height < maxHeight;
    }
}
