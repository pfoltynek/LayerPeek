namespace PrusaTrayDiagnostic.Core.Cameras;

/// <summary>
/// The monitor chosen for a camera window (T-035): the name the OS reports for it (<see cref="CameraScreen.DisplayName"/>),
/// its resolution in pixels (size of <see cref="CameraScreen.Bounds"/>) and, where the platform has one, the path of the
/// physical monitor (<see cref="DevicePath"/>, Q8), which tells two identical monitors apart. Matching (see
/// <see cref="CameraWindowDefaultPlacement.SelectScreen"/>, T-071 Q4): a saved device path alone decides; older settings
/// without one match the same name and resolution (<see cref="Matches"/>), then the same name (<see cref="MatchesName"/>);
/// names are compared ordinally.
/// </summary>
public sealed record CameraMonitorId
{
    /// <summary>Longest accepted name (real names are short; anything longer comes from a damaged file).</summary>
    public const int MaxDisplayNameLength = 256;

    /// <summary>Longest accepted device path (Windows uses at most 128 characters).</summary>
    public const int MaxDevicePathLength = 256;

    /// <summary>Largest accepted width or height in pixels.</summary>
    public const int MaxResolution = 65535;

    /// <exception cref="ArgumentNullException"><paramref name="displayName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The values are not valid (see <see cref="IsValid"/> and <see cref="IsValidDevicePath"/>); the message never contains
    /// the name or the path.
    /// </exception>
    public CameraMonitorId(string displayName, int width, int height, string? devicePath = null)
    {
        ArgumentNullException.ThrowIfNull(displayName);
        if (!IsValid(displayName, width, height))
        {
            throw new ArgumentException(
                $"Invalid monitor: the name must have 1–{MaxDisplayNameLength} characters without control characters and the resolution 1–{MaxResolution} pixels.",
                nameof(displayName));
        }

        if (devicePath is not null && !IsValidDevicePath(devicePath))
        {
            throw new ArgumentException(
                $"Invalid monitor device path: it must have 1–{MaxDevicePathLength} characters without control characters.",
                nameof(devicePath));
        }

        DisplayName = displayName;
        Width = width;
        Height = height;
        DevicePath = devicePath;
    }

    /// <summary>Name reported by the OS, kept exactly (not trimmed).</summary>
    public string DisplayName { get; }

    /// <summary>Width of the monitor in pixels.</summary>
    public int Width { get; }

    /// <summary>Height of the monitor in pixels.</summary>
    public int Height { get; }

    /// <summary>
    /// The path of the physical monitor (Windows); <see langword="null"/> on other platforms and in settings written before
    /// it was stored. It changes when the monitor is plugged into another connector; matching then falls back to the name.
    /// </summary>
    public string? DevicePath { get; }

    /// <summary>
    /// Not only white space, at most <see cref="MaxDisplayNameLength"/> characters, no control characters; resolution
    /// 1–<see cref="MaxResolution"/> pixels.
    /// </summary>
    public static bool IsValid(string? displayName, int width, int height) =>
        IsValidText(displayName, MaxDisplayNameLength)
        && width is >= 1 and <= MaxResolution
        && height is >= 1 and <= MaxResolution;

    /// <summary>Same rules as the name: not only white space, at most <see cref="MaxDevicePathLength"/> characters, no control characters.</summary>
    public static bool IsValidDevicePath(string? devicePath) => IsValidText(devicePath, MaxDevicePathLength);

    /// <summary>
    /// Identifies <paramref name="screen"/> (e.g. when the user picks it in the settings). A device path that cannot be
    /// stored is left out (the name and resolution still identify the monitor).
    /// </summary>
    /// <exception cref="ArgumentException">The screen name or resolution cannot be stored (see <see cref="IsValid"/>).</exception>
    public static CameraMonitorId FromScreen(CameraScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        return new CameraMonitorId(
            screen.DisplayName,
            screen.Bounds.Width,
            screen.Bounds.Height,
            IsValidDevicePath(screen.DevicePath) ? screen.DevicePath : null);
    }

    /// <summary>The same physical monitor: both have a device path and it is the same (case-insensitive, like Windows paths).</summary>
    public bool MatchesDevicePath(CameraScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        return DevicePath is not null && string.Equals(DevicePath, screen.DevicePath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Exact match: same name (ordinal) and same resolution.</summary>
    public bool Matches(CameraScreen screen) =>
        MatchesName(screen) && Width == screen.Bounds.Width && Height == screen.Bounds.Height;

    /// <summary>Same name (ordinal), any resolution: the monitor counts as connected (its resolution may have changed or it was rotated).</summary>
    public bool MatchesName(CameraScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        return string.Equals(DisplayName, screen.DisplayName, StringComparison.Ordinal);
    }

    private static bool IsValidText(string? text, int maxLength) =>
        !string.IsNullOrWhiteSpace(text)
        && text.Length <= maxLength
        && !text.AsSpan().ContainsAnyInRange('\0', '\u001F')
        && !text.AsSpan().ContainsAnyInRange('\u007F', '\u009F');
}
