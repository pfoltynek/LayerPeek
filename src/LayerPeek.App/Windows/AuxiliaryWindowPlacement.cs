using Avalonia;

namespace LayerPeek.App.Windows;

/// <summary>Where an auxiliary window opens: its client size (DIP) and the top left corner of the whole window (pixels).</summary>
internal readonly record struct AuxiliaryWindowBounds(Size ClientSize, PixelPoint Position);

/// <summary>
/// Size of the settings (T-033) and diagnostics (T-041) windows, which share their designed and minimum size. The
/// default (T-094) fits the smallest common laptop work area, about 1093 × 580 DIP (1366 × 768 at 125 % or
/// 1920 × 1080 at 175 %, with a 48 DIP taskbar); on a smaller work area (higher scaling, a large taskbar) the window
/// shrinks to fit, never below its minimum, and is centred. A size the user chose (remembered in <c>windows.json</c>)
/// is fitted the same way on the monitor the window opens on. The content scrolls when it does not fit. Pure functions,
/// testable without a screen; <see cref="WorkAreaFitter"/> applies them to a window.
/// </summary>
internal static class AuxiliaryWindowPlacement
{
    /// <summary>Title bar height assumed while the real frame is unknown (Windows 11 at 100 %).</summary>
    public const double TitleBarHeight = 32;

    /// <summary>Client size of the windows as designed (matches <c>Width</c> / <c>Height</c> in SettingsWindow.axaml and DiagnosticsWindow.axaml).</summary>
    public static readonly Size DefaultClientSize = new(780, 532);

    /// <summary>Minimum client size (matches <c>MinWidth</c> / <c>MinHeight</c> in SettingsWindow.axaml and DiagnosticsWindow.axaml).</summary>
    public static readonly Size MinClientSize = new(640, 400);

    /// <summary>Largest accepted remembered width or height (anything bigger comes from a damaged file).</summary>
    public const double MaxClientSize = 16384;

    /// <summary>Window frame around the client area while the real one is unknown: only the title bar.</summary>
    public static readonly Thickness DefaultChrome = new(0, TitleBarHeight, 0, 0);

    /// <summary>A usable remembered client size: finite, positive and at most <see cref="MaxClientSize"/> in each dimension.</summary>
    public static bool IsUsable(Size size) =>
        double.IsFinite(size.Width) && double.IsFinite(size.Height)
        && size.Width > 0 && size.Height > 0
        && size.Width <= MaxClientSize && size.Height <= MaxClientSize;

    /// <summary>
    /// The client size a window wants before it is fitted to the work area (T-094): the remembered one, never below
    /// <paramref name="minClient"/>; <paramref name="designedClient"/> when nothing usable is remembered.
    /// </summary>
    public static Size PreferredClientSize(Size? remembered, Size designedClient, Size minClient) =>
        remembered is { } size && IsUsable(size)
            ? new Size(Math.Max(size.Width, minClient.Width), Math.Max(size.Height, minClient.Height))
            : designedClient;

    /// <summary>
    /// Fits <paramref name="preferredClient"/> plus <paramref name="chrome"/> into <paramref name="workArea"/> (pixels of a
    /// screen with <paramref name="scaling"/>): each dimension shrinks only when it does not fit, never below
    /// <paramref name="minClient"/>. The whole window is centred; a window larger than the work area starts at its top
    /// left corner, so that the title bar stays reachable.
    /// </summary>
    public static AuxiliaryWindowBounds Fit(Size preferredClient, Size minClient, PixelRect workArea, double scaling, Thickness chrome)
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
        return new AuxiliaryWindowBounds(client, position);
    }

    /// <summary>
    /// Client size to give the window before it is shown, so that it appears already fitted (no visible jump): the frame
    /// is not known yet, so <see cref="DefaultChrome"/> is assumed; <c>CenterScreen</c> then centres this size. Without a
    /// screen (<paramref name="workArea"/> <see langword="null"/>) the window keeps <paramref name="preferredClient"/>.
    /// </summary>
    public static Size SizeBeforeShowing(Size preferredClient, Size minClient, PixelRect? workArea, double scaling) =>
        workArea is { } area ? Fit(preferredClient, minClient, area, scaling, DefaultChrome).ClientSize : preferredClient;

    /// <summary>
    /// After the window opened, with its real screen and frame: new bounds only when the window as shown
    /// (<paramref name="shownClient"/>) does not fit (a thicker frame than assumed, another screen than expected);
    /// <see langword="null"/> otherwise. The window never grows here, which would be a visible jump as well.
    /// </summary>
    public static AuxiliaryWindowBounds? CorrectionAfterOpening(Size shownClient, Size preferredClient, Size minClient, PixelRect workArea, double scaling, Thickness chrome)
    {
        AuxiliaryWindowBounds fitted = Fit(preferredClient, minClient, workArea, scaling, chrome);
        if (fitted.ClientSize.Width >= shownClient.Width && fitted.ClientSize.Height >= shownClient.Height)
        {
            return null;
        }

        // Shrink only: a dimension that fits keeps its size as shown.
        Size target = new(Math.Min(fitted.ClientSize.Width, shownClient.Width), Math.Min(fitted.ClientSize.Height, shownClient.Height));
        return Fit(target, minClient, workArea, scaling, chrome);
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
