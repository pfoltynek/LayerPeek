using System.Drawing;

using FlaUI.Core.Input;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>
/// Puts a window of the app into the foreground for real keyboard input (<c>SendInput</c>, T-071): a click of the real
/// mouse at a point of the window that nothing covers, like a user would do.
/// </summary>
internal static class WindowActivation
{
    /// <summary>
    /// Activates <paramref name="hwnd"/> by clicking at <paramref name="point"/> (default: in its title bar, or its centre
    /// without one) unless it already is the foreground window.
    /// </summary>
    public static async Task ActivateAsync(nint hwnd, Point? point = null)
    {
        if (Native.GetForegroundWindow() == hwnd)
        {
            return;
        }

        Point target = point ?? DefaultClickPoint(hwnd);
        nint hit = Native.TopLevelWindowAt(target);
        if (hit != hwnd)
        {
            throw new InvalidOperationException($"Another window (0x{hit:X} '{Native.ClassName(hit)}') covers 0x{hwnd:X} at {target.X},{target.Y}; not clicking it.");
        }

        Mouse.Position = target;
        Mouse.Click(MouseButton.Left);
        await Wait.UntilAsync(() => Native.GetForegroundWindow() == hwnd, TimeSpan.FromSeconds(5), $"0x{hwnd:X} in the foreground after the click");
    }

    /// <summary>The middle of the title bar (8 DIP below the visible frame's top), or the centre of a window without a caption.</summary>
    public static Point DefaultClickPoint(nint hwnd)
    {
        Native.Rect frame = Native.FrameBounds(hwnd);
        int x = frame.Left + (frame.Width / 2);
        return (Native.Style(hwnd) & Native.WsCaption) == Native.WsCaption
            ? new Point(x, frame.Top + (int)Math.Round(8 * WindowChecks.Scaling(hwnd)))
            : new Point(x, frame.Top + (frame.Height / 2));
    }

    /// <summary>
    /// Whether the focused elements after each Tab (<paramref name="seen"/>) go round <paramref name="cycle"/> in its
    /// order (starting anywhere) and visit every element of it.
    /// </summary>
    public static bool FollowsCycle(IReadOnlyList<string> seen, IReadOnlyList<string> cycle)
    {
        if (seen.Count < cycle.Count || cycle.Count == 0)
        {
            return false;
        }

        int start = -1;
        for (int i = 0; i < cycle.Count && start < 0; i++)
        {
            start = cycle[i] == seen[0] ? i : -1;
        }

        if (start < 0)
        {
            return false;
        }

        for (int i = 0; i < seen.Count; i++)
        {
            if (seen[i] != cycle[(start + i) % cycle.Count])
            {
                return false;
            }
        }

        return true;
    }
}
