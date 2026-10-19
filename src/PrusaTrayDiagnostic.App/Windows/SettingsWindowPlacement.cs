using Avalonia;

namespace PrusaTrayDiagnostic.App.Windows;

/// <summary>Where the settings window opens: its client size (DIP) and the top left corner of the whole window (pixels).</summary>
internal readonly record struct SettingsWindowBounds(Size ClientSize, PixelPoint Position);

/// <summary>
/// Size of the settings window (T-033). The default fits the smallest supported screen (800 × 600 at 100 %: about
/// 800 × 560 DIP of work area, so at most 780 × 540 including the title bar); on a smaller work area (higher scaling, a
/// large taskbar) the window shrinks to fit, never below its minimum, and is centred. The content scrolls when it does
/// not fit. Pure functions, testable without a screen.
/// </summary>
internal static class SettingsWindowPlacement
{
    /// <summary>Title bar height assumed while the real frame is unknown (Windows 11 at 100 %).</summary>
    public const double TitleBarHeight = 32;

    /// <summary>Client size of the window as designed (matches <c>Width</c> / <c>Height</c> in SettingsWindow.axaml).</summary>
    public static readonly Size DefaultClientSize = new(780, 508);

    /// <summary>Minimum client size (matches <c>MinWidth</c> / <c>MinHeight</c> in SettingsWindow.axaml).</summary>
    public static readonly Size MinClientSize = new(640, 400);

    /// <summary>Window frame around the client area while the real one is unknown: only the title bar.</summary>
    public static readonly Thickness DefaultChrome = new(0, TitleBarHeight, 0, 0);

    /// <summary>
    /// Fits <paramref name="preferredClient"/> plus <paramref name="chrome"/> into <paramref name="workArea"/> (pixels of a
    /// screen with <paramref name="scaling"/>): each dimension shrinks only when it does not fit, never below
    /// <paramref name="minClient"/>. The whole window is centred; a window larger than the work area starts at its top
    /// left corner, so that the title bar stays reachable.
    /// </summary>
    public static SettingsWindowBounds Fit(Size preferredClient, Size minClient, PixelRect workArea, double scaling, Thickness chrome)
    {
        if (!double.IsFinite(scaling) || scaling <= 0)
        {
            scaling = 1.0;
        }

        double horizontal = chrome.Left + chrome.Right;
        double vertical = chrome.Top + chrome.Bottom;
        double areaWidth = workArea.Width / scaling;
        double areaHeight = workArea.Height / scaling;
        Size client = new(
            Math.Max(minClient.Width, Math.Min(preferredClient.Width, areaWidth - horizontal)),
            Math.Max(minClient.Height, Math.Min(preferredClient.Height, areaHeight - vertical)));

        double outerWidth = (client.Width + horizontal) * scaling;
        double outerHeight = (client.Height + vertical) * scaling;
        PixelPoint position = new(
            workArea.X + (int)Math.Floor(Math.Max(0, (workArea.Width - outerWidth) / 2)),
            workArea.Y + (int)Math.Floor(Math.Max(0, (workArea.Height - outerHeight) / 2)));
        return new SettingsWindowBounds(client, position);
    }

    /// <summary>
    /// The frame around the client area from the measured window size (DIP): left, right and bottom borders are equal, the
    /// rest of the height is the title bar. <see cref="DefaultChrome"/> while nothing usable is measured (not shown yet,
    /// headless).
    /// </summary>
    public static Thickness ChromeOf(Size? frameSize, Size clientSize)
    {
        if (frameSize is not { } frame || frame.Width < clientSize.Width || frame.Height <= clientSize.Height)
        {
            return DefaultChrome;
        }

        double vertical = frame.Height - clientSize.Height;
        double side = Math.Min((frame.Width - clientSize.Width) / 2, vertical);
        return new Thickness(side, vertical - side, side, side);
    }
}
