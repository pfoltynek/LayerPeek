using System.Drawing;

using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>
/// Puts a window of the app into the foreground for real keyboard input (<c>SendInput</c>, T-071): a click of the real
/// mouse at a point of the window that nothing covers, like a user would do.
/// </summary>
internal static class WindowActivation
{
    /// <summary>
    /// Activates <paramref name="hwnd"/> unless it already is the foreground window: first <c>SetForegroundWindow</c>
    /// (Windows often refuses it to the test process), else a click at <paramref name="point"/> (default: the first of
    /// <see cref="ClickPoints"/> that nothing covers). Never clicks a point where another window is in front.
    /// </summary>
    public static async Task ActivateAsync(nint hwnd, Point? point = null)
    {
        if (Native.GetForegroundWindow() == hwnd || (Native.SetForegroundWindow(hwnd) && Native.GetForegroundWindow() == hwnd))
        {
            return;
        }

        List<Point> candidates = point is { } given
            ? [given]
            : ClickPoints(Native.FrameBounds(hwnd), (Native.Style(hwnd) & Native.WsCaption) == Native.WsCaption, WindowChecks.Scaling(hwnd));
        Point? target = candidates.Cast<Point?>().FirstOrDefault(candidate => Native.TopLevelWindowAt(candidate!.Value) == hwnd);
        if (target is null)
        {
            Point first = candidates[0];
            nint hit = Native.TopLevelWindowAt(first);
            Native.GetWindowThreadProcessId(hit, out uint owner);
            throw new InvalidOperationException($"Other windows cover 0x{hwnd:X} at every click point (at {first.X},{first.Y}: 0x{hit:X} '{Native.ClassName(hit)}' of process {owner} '{ProcessName(owner)}'); not clicking.");
        }

        Mouse.Position = target.Value;
        Mouse.Click(MouseButton.Left);
        await Wait.UntilAsync(() => Native.GetForegroundWindow() == hwnd, TimeSpan.FromSeconds(5), $"0x{hwnd:X} in the foreground after the click");
    }

    /// <summary>
    /// Where a click activates a window with the visible <paramref name="frame"/> without pressing anything: in a title
    /// bar (8 DIP below the top) its middle, a quarter and an eighth of the width from the left, and 160 DIP left of the
    /// right edge (clear of the caption buttons); without a caption the centre, then the middles of the left and right
    /// halves.
    /// </summary>
    public static List<Point> ClickPoints(Native.Rect frame, bool hasCaption, double scaling)
    {
        int y = hasCaption ? frame.Top + (int)Math.Round(8 * scaling) : frame.Top + (frame.Height / 2);
        return hasCaption
            ? [new(frame.Left + (frame.Width / 2), y), new(frame.Left + (frame.Width / 4), y), new(frame.Left + (frame.Width / 8), y), new(frame.Right - (int)Math.Round(160 * scaling), y)]
            : [new(frame.Left + (frame.Width / 2), y), new(frame.Left + (frame.Width / 4), y), new(frame.Left + (frame.Width * 3 / 4), y)];
    }

    /// <summary>The name of a process (no window titles: they may show the user's data), or "?" when it is gone.</summary>
    private static string ProcessName(uint processId)
    {
        try
        {
            using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return "?";
        }
    }

    /// <summary>
    /// Types <paramref name="key"/> with the real keyboard, only while <paramref name="hwnd"/> is the foreground window:
    /// a key must never reach a window of another app (or of the user) that took the foreground meanwhile.
    /// </summary>
    public static void TypeInto(nint hwnd, VirtualKeyShort key)
    {
        nint foreground = Native.GetForegroundWindow();
        if (foreground != hwnd)
        {
            throw new InvalidOperationException($"0x{hwnd:X} is no longer in the foreground (0x{foreground:X} '{Native.ClassName(foreground)}' is); not typing {key}.");
        }

        Keyboard.Type(key);
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
