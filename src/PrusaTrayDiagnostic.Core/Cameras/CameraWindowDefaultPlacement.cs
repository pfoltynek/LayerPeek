namespace PrusaTrayDiagnostic.Core.Cameras;

/// <summary>
/// Where a camera window opens from its <see cref="CameraWindowDefaults"/> (T-035), as pure functions over the connected
/// monitors (<see cref="CameraScreen"/>), so that several monitors can be tested without real screens.
/// </summary>
/// <remarks>
/// Sizes and the margin are device-independent units converted to pixels with the <see cref="CameraScreen.Scaling"/> of
/// the chosen monitor (rounded half away from zero). The outer window (client area + frame) never leaves the usable area
/// of the monitor: a bigger window is shrunk to it, and a corner whose margin does not fit moves the window towards the
/// opposite edge just enough to stay inside.
/// </remarks>
public static class CameraWindowDefaultPlacement
{
    /// <summary>Distance of a corner-placed window from both edges of the working area, in device-independent units.</summary>
    public const double MarginDip = 16;

    /// <summary>
    /// The monitor for a camera window (T-035 Q4, Q8; T-071 Q4): a saved monitor with a device path is the screen with
    /// that path (<see cref="CameraMonitorId.MatchesDevicePath"/>) and nothing else, so a switched-off monitor is never
    /// replaced by a twin with the same name. Older settings without a device path match the first screen with the same
    /// name and resolution (<see cref="CameraMonitorId.Matches"/>), else the first with the same name
    /// (<see cref="CameraMonitorId.MatchesName"/>). Without a match: the primary screen (the first one when none is flagged
    /// primary) with <see cref="CameraScreenSelection.PreferredMonitorMissing"/>. "First" is list order.
    /// <see langword="null"/> when there are no screens.
    /// </summary>
    public static CameraScreenSelection? SelectScreen(CameraMonitorId? monitor, IReadOnlyList<CameraScreen> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);
        if (screens.Count == 0)
        {
            return null;
        }

        if (monitor is not null && Find(monitor, screens) is { } match)
        {
            return new CameraScreenSelection(match, PreferredMonitorMissing: false);
        }

        CameraScreen primary = screens[0];
        foreach (CameraScreen screen in screens)
        {
            if (screen.IsPrimary)
            {
                primary = screen;
                break;
            }
        }

        return new CameraScreenSelection(primary, PreferredMonitorMissing: monitor is not null);
    }

    /// <summary>
    /// Whether the saved <paramref name="monitor"/> is connected (T-071 Q4): with a device path, some screen has that path;
    /// without one (older settings), some screen has its name, whatever its resolution. The settings show it as "not
    /// connected" otherwise; it is the inverse of <see cref="CameraScreenSelection.PreferredMonitorMissing"/> for a
    /// non-empty screen list.
    /// </summary>
    public static bool IsConnected(CameraMonitorId monitor, IReadOnlyList<CameraScreen> screens)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(screens);
        return Find(monitor, screens) is not null;
    }

    /// <summary>The screen <paramref name="monitor"/> resolves to (see <see cref="SelectScreen"/>), or <see langword="null"/>.</summary>
    private static CameraScreen? Find(CameraMonitorId monitor, IReadOnlyList<CameraScreen> screens)
    {
        if (monitor.DevicePath is not null)
        {
            foreach (CameraScreen screen in screens)
            {
                if (monitor.MatchesDevicePath(screen))
                {
                    return screen;
                }
            }

            return null;
        }

        CameraScreen? sameName = null;
        foreach (CameraScreen screen in screens)
        {
            if (monitor.Matches(screen))
            {
                return screen;
            }

            sameName ??= monitor.MatchesName(screen) ? screen : null;
        }

        return sameName;
    }

    /// <summary>
    /// Where the window opens in <paramref name="mode"/>: <see cref="CameraWindowDefaults.Normal"/> or
    /// <see cref="CameraWindowDefaults.Mini"/> placed on the selected monitor (<see cref="Place"/>), or the whole selected
    /// monitor for <see cref="CameraWindowMode.FullScreen"/>. <see langword="null"/> when there are no screens.
    /// </summary>
    /// <param name="frameWidth">Width the window frame adds to the client area, in device-independent units (0 for borderless windows).</param>
    /// <param name="frameHeight">Height the window frame (title bar, borders) adds to the client area, in device-independent units.</param>
    /// <exception cref="ArgumentOutOfRangeException">Unknown <paramref name="mode"/>, or a negative or non-finite frame size.</exception>
    public static CameraWindowTarget? Compute(
        CameraWindowDefaults defaults,
        CameraWindowMode mode,
        IReadOnlyList<CameraScreen> screens,
        double frameWidth = 0,
        double frameHeight = 0)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        ArgumentNullException.ThrowIfNull(screens);
        ValidateFrame(frameWidth, frameHeight);
        CameraWindowLayout? layout = mode switch
        {
            CameraWindowMode.Normal => defaults.Normal,
            CameraWindowMode.Mini => defaults.Mini,
            CameraWindowMode.FullScreen => null,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown camera window mode."),
        };

        if (SelectScreen(defaults.Monitor, screens) is not { } selection)
        {
            return null;
        }

        CameraScreen screen = selection.Screen;
        return layout is null
            ? new CameraWindowTarget(screen, selection.PreferredMonitorMissing, screen.Bounds, screen.Bounds.Width / screen.Scaling, screen.Bounds.Height / screen.Scaling)
            : Place(layout, screen, frameWidth, frameHeight) with { PreferredMonitorMissing = selection.PreferredMonitorMissing };
    }

    /// <summary>
    /// Places <paramref name="layout"/> in the usable area of <paramref name="screen"/> (e.g. the mini preview on the
    /// monitor the window is on when the user switches to it). <see cref="CameraWindowTarget.PreferredMonitorMissing"/> is
    /// <see langword="false"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A negative or non-finite frame size.</exception>
    public static CameraWindowTarget Place(CameraWindowLayout layout, CameraScreen screen, double frameWidth = 0, double frameHeight = 0)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(screen);
        ValidateFrame(frameWidth, frameHeight);
        double scaling = screen.Scaling;
        PixelBounds area = screen.UsableArea;
        int margin = ToPixels(MarginDip, scaling);
        (int width, double clientWidth) = Fit(layout.Size.Width, frameWidth, area.Width, scaling);
        (int height, double clientHeight) = Fit(layout.Size.Height, frameHeight, area.Height, scaling);

        (int x, int y) = layout.Corner switch
        {
            CameraWindowCorner.TopLeft => (area.X + margin, area.Y + margin),
            CameraWindowCorner.TopRight => (area.Right - margin - width, area.Y + margin),
            CameraWindowCorner.BottomLeft => (area.X + margin, area.Bottom - margin - height),
            CameraWindowCorner.BottomRight => (area.Right - margin - width, area.Bottom - margin - height),
            CameraWindowCorner.Center => (area.X + ((area.Width - width) / 2), area.Y + ((area.Height - height) / 2)),
            _ => throw new ArgumentOutOfRangeException(nameof(layout), layout.Corner, "Unknown camera window corner."),
        };

        // Fit guarantees width <= area.Width, so the range is never empty.
        x = Math.Clamp(x, area.X, area.Right - width);
        y = Math.Clamp(y, area.Y, area.Bottom - height);
        return new CameraWindowTarget(screen, PreferredMonitorMissing: false, new PixelBounds(x, y, width, height), clientWidth, clientHeight);
    }

    /// <returns>The outer size in pixels (at most <paramref name="available"/>) and the client size in DIP.</returns>
    private static (int Outer, double Client) Fit(int clientDip, double frameDip, int available, double scaling)
    {
        int client = ToPixels(clientDip, scaling);
        int frame = ToPixels(frameDip, scaling);
        if ((long)client + frame <= available)
        {
            // Exact DIP value: no rounding drift when nothing is clamped.
            return (client + frame, clientDip);
        }

        int outer = Math.Max(available, 1);
        return (outer, Math.Max(outer - frame, 1) / scaling);
    }

    private static int ToPixels(double dip, double scaling) =>
        (int)Math.Min(Math.Round(dip * scaling, MidpointRounding.AwayFromZero), int.MaxValue / 4);

    private static void ValidateFrame(double frameWidth, double frameHeight)
    {
        if (!double.IsFinite(frameWidth) || frameWidth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameWidth), frameWidth, "The frame width must be a non-negative finite number.");
        }

        if (!double.IsFinite(frameHeight) || frameHeight < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameHeight), frameHeight, "The frame height must be a non-negative finite number.");
        }
    }
}
