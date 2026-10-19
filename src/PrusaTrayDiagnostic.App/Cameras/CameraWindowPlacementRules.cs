using Avalonia;

namespace PrusaTrayDiagnostic.App.Cameras;

/// <summary>Whether a remembered window position can still be used with the screens connected now.</summary>
internal static class CameraWindowPlacementRules
{
    /// <summary>Width of the top edge that must be reachable to grab the window.</summary>
    public const int MinVisibleWidth = 48;

    /// <summary>Height of the top edge (title bar of the normal window, drag area of the mini preview).</summary>
    public const int TitleBarHeight = 32;

    /// <summary>
    /// The top edge of the window (<see cref="TitleBarHeight"/> high, or the whole window when it is lower) must be covered
    /// by the working areas for at least <see cref="MinVisibleWidth"/> contiguous pixels. The areas may cover it together
    /// (two monitors side by side or above each other). Without screens there is nothing to check (<see langword="true"/>).
    /// </summary>
    public static bool IsReachable(PixelRect window, IReadOnlyList<PixelRect> workingAreas)
    {
        ArgumentNullException.ThrowIfNull(workingAreas);
        if (workingAreas.Count == 0)
        {
            return true;
        }

        PixelRect strip = new(window.X, window.Y, Math.Max(window.Width, 0), Math.Min(TitleBarHeight, Math.Max(window.Height, 1)));
        List<PixelRect> parts = [];
        SortedSet<int> edges = [strip.X, strip.Right];
        foreach (PixelRect area in workingAreas)
        {
            PixelRect part = area.Intersect(strip);
            if (part.Width > 0 && part.Height > 0)
            {
                parts.Add(part);
                edges.Add(part.X);
                edges.Add(part.Right);
            }
        }

        // Columns between neighbouring edges are covered or not as a whole; the longest run of covered columns counts.
        int run = 0;
        int longest = 0;
        int? previous = null;
        foreach (int edge in edges)
        {
            if (previous is { } left && edge > left)
            {
                run = CoversColumn(parts, left, edge, strip) ? run + (edge - left) : 0;
                longest = Math.Max(longest, run);
            }

            previous = edge;
        }

        return longest >= Math.Min(MinVisibleWidth, Math.Max(strip.Width, 1));
    }

    /// <summary>Whether the parts covering the column [<paramref name="left"/>, <paramref name="right"/>) span the strip's full height.</summary>
    private static bool CoversColumn(List<PixelRect> parts, int left, int right, PixelRect strip)
    {
        List<(int Top, int Bottom)> spans = [];
        foreach (PixelRect part in parts)
        {
            if (part.X <= left && part.Right >= right)
            {
                spans.Add((part.Y, part.Bottom));
            }
        }

        spans.Sort();
        int covered = strip.Y;
        foreach ((int top, int bottom) in spans)
        {
            if (top > covered)
            {
                return false;
            }

            covered = Math.Max(covered, bottom);
        }

        return covered >= strip.Bottom;
    }
}
