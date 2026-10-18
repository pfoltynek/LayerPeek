using Avalonia;

namespace PrusaTrayDiagnostic.App.Cameras;

/// <summary>Whether a remembered window position can still be used with the screens connected now.</summary>
internal static class CameraWindowPlacementRules
{
    /// <summary>Visible part needed to see and grab the window.</summary>
    public const int MinVisibleWidth = 48;

    public const int MinVisibleHeight = 32;

    /// <summary>
    /// The top edge (title bar of the normal window, drag area of the mini preview) must lie inside one working area for
    /// at least <see cref="MinVisibleWidth"/> pixels, and some working area must show at least
    /// <see cref="MinVisibleWidth"/> × <see cref="MinVisibleHeight"/> of the window. Without screens there is nothing to
    /// check (<see langword="true"/>).
    /// </summary>
    public static bool IsReachable(PixelRect window, IReadOnlyList<PixelRect> workingAreas)
    {
        ArgumentNullException.ThrowIfNull(workingAreas);
        if (workingAreas.Count == 0)
        {
            return true;
        }

        PixelRect topEdge = new(window.X, window.Y, window.Width, Math.Min(MinVisibleHeight, Math.Max(window.Height, 1)));
        bool topReachable = false;
        bool bodyVisible = false;
        foreach (PixelRect area in workingAreas)
        {
            PixelRect top = area.Intersect(topEdge);
            topReachable |= top.Width >= MinVisibleWidth && top.Height == topEdge.Height;
            PixelRect body = area.Intersect(window);
            bodyVisible |= body.Width >= MinVisibleWidth && body.Height >= MinVisibleHeight;
        }

        return topReachable && bodyVisible;
    }
}
