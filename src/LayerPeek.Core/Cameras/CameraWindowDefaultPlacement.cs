namespace LayerPeek.Core.Cameras;

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
    /// The monitor for a camera window (T-035 Q4, Q8; T-071 Q4, Q5), in this order:
    /// <list type="number">
    /// <item>A saved device path: the screen with that path (<see cref="CameraMonitorId.MatchesDevicePath"/>).</item>
    /// <item>A saved device path no screen has (another connector): the screen with the same name and resolution
    /// (<see cref="CameraMonitorId.Matches"/>) when it is the only one and not a known twin
    /// (<see cref="CameraMonitorId.IsKnownTwin"/>), so a switched-off monitor is not replaced by the twin it was chosen
    /// beside; known twins count as candidates.</item>
    /// <item>Older settings without a device path: the first screen with the same name and resolution, else the first
    /// with the same name (<see cref="CameraMonitorId.MatchesName"/>).</item>
    /// </list>
    /// Without a match: the primary screen (the first one when none is flagged primary) with
    /// <see cref="CameraScreenSelection.PreferredMonitorMissing"/>. "First" is list order. <see langword="null"/> when
    /// there are no screens.
    /// </summary>
    public static CameraScreenSelection? SelectScreen(CameraMonitorId? monitor, IReadOnlyList<CameraScreen> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);
        if (screens.Count == 0)
        {
            return null;
        }

        if (monitor is not null && Resolve(monitor, screens).Screen is { } match)
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
    /// Whether the saved <paramref name="monitor"/> is connected, by the rule of <see cref="SelectScreen"/>. The settings
    /// show it as "not connected" otherwise; it is the inverse of <see cref="CameraScreenSelection.PreferredMonitorMissing"/>
    /// for a non-empty screen list.
    /// </summary>
    public static bool IsConnected(CameraMonitorId monitor, IReadOnlyList<CameraScreen> screens)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(screens);
        return Resolve(monitor, screens).Screen is not null;
    }

    /// <summary>
    /// The saved <paramref name="monitor"/> with the device path it was found at, when that is unambiguous and differs
    /// (user decision 2026-10-23), else <see langword="null"/>:
    /// <list type="bullet">
    /// <item>a saved device path that matched by name and resolution (rule 2 of <see cref="SelectScreen"/>): the path of
    /// that screen, the name, resolution and twins stay;</item>
    /// <item>older settings without a path: the path of the only connected screen with the same name, when it also has the
    /// saved resolution (no twins: there is no other monitor with the name).</item>
    /// </list>
    /// Only a storable path (<see cref="CameraMonitorId.IsValidDevicePath"/>) is used.
    /// </summary>
    public static CameraMonitorId? Relocate(CameraMonitorId monitor, IReadOnlyList<CameraScreen> screens)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(screens);
        if (monitor.DevicePath is not null)
        {
            return Resolve(monitor, screens) is { ByDevicePath: false, Screen.DevicePath: { } path } && CameraMonitorId.IsValidDevicePath(path)
                ? monitor.WithDevicePath(path)
                : null;
        }

        CameraScreen? only = null;
        foreach (CameraScreen screen in screens)
        {
            if (monitor.MatchesName(screen))
            {
                if (only is not null)
                {
                    return null;
                }

                only = screen;
            }
        }

        return only is { DevicePath: { } onlyPath } && monitor.Matches(only) && CameraMonitorId.IsValidDevicePath(onlyPath)
            ? new CameraMonitorId(monitor.DisplayName, monitor.Width, monitor.Height, onlyPath)
            : null;
    }

    /// <summary>The screen <paramref name="monitor"/> resolves to (see <see cref="SelectScreen"/>) and whether by its device path.</summary>
    private static Resolution Resolve(CameraMonitorId monitor, IReadOnlyList<CameraScreen> screens)
    {
        if (monitor.DevicePath is not null)
        {
            CameraScreen? candidate = null;
            int candidates = 0;
            foreach (CameraScreen screen in screens)
            {
                if (monitor.MatchesDevicePath(screen))
                {
                    return new Resolution(screen, ByDevicePath: true);
                }

                if (monitor.Matches(screen))
                {
                    candidate = screen;
                    candidates++;
                }
            }

            return new Resolution(candidates == 1 && !monitor.IsKnownTwin(candidate!) ? candidate : null, ByDevicePath: false);
        }

        CameraScreen? sameName = null;
        foreach (CameraScreen screen in screens)
        {
            if (monitor.Matches(screen))
            {
                return new Resolution(screen, ByDevicePath: false);
            }

            sameName ??= monitor.MatchesName(screen) ? screen : null;
        }

        return new Resolution(sameName, ByDevicePath: false);
    }

    /// <param name="Screen">The matching screen; <see langword="null"/>: not connected.</param>
    /// <param name="ByDevicePath">It has the saved device path.</param>
    private readonly record struct Resolution(CameraScreen? Screen, bool ByDevicePath);

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
