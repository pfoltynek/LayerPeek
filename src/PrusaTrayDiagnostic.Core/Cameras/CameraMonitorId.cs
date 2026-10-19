namespace PrusaTrayDiagnostic.Core.Cameras;

/// <summary>
/// The monitor chosen for a camera window (T-035): the name the OS reports for it (<see cref="CameraScreen.DisplayName"/>)
/// and its resolution in pixels (size of <see cref="CameraScreen.Bounds"/>). An exact match (<see cref="Matches"/>) is
/// preferred, a screen with the same name and another resolution (<see cref="MatchesName"/>) comes next; names are
/// compared ordinally (see <see cref="CameraWindowDefaultPlacement.SelectScreen"/>).
/// </summary>
public sealed record CameraMonitorId
{
    /// <summary>Longest accepted name (real names are short; anything longer comes from a damaged file).</summary>
    public const int MaxDisplayNameLength = 256;

    /// <summary>Largest accepted width or height in pixels.</summary>
    public const int MaxResolution = 65535;

    /// <exception cref="ArgumentNullException"><paramref name="displayName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The values are not valid (see <see cref="IsValid"/>); the message never contains the name.</exception>
    public CameraMonitorId(string displayName, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(displayName);
        if (!IsValid(displayName, width, height))
        {
            throw new ArgumentException(
                $"Invalid monitor: the name must have 1–{MaxDisplayNameLength} characters without control characters and the resolution 1–{MaxResolution} pixels.",
                nameof(displayName));
        }

        DisplayName = displayName;
        Width = width;
        Height = height;
    }

    /// <summary>Name reported by the OS, kept exactly (not trimmed).</summary>
    public string DisplayName { get; }

    /// <summary>Width of the monitor in pixels.</summary>
    public int Width { get; }

    /// <summary>Height of the monitor in pixels.</summary>
    public int Height { get; }

    /// <summary>
    /// Not only white space, at most <see cref="MaxDisplayNameLength"/> characters, no control characters; resolution
    /// 1–<see cref="MaxResolution"/> pixels.
    /// </summary>
    public static bool IsValid(string? displayName, int width, int height) =>
        !string.IsNullOrWhiteSpace(displayName)
        && displayName.Length <= MaxDisplayNameLength
        && !displayName.AsSpan().ContainsAnyInRange('\0', '\u001F')
        && !displayName.AsSpan().ContainsAnyInRange('\u007F', '\u009F')
        && width is >= 1 and <= MaxResolution
        && height is >= 1 and <= MaxResolution;

    /// <summary>Identifies <paramref name="screen"/> (e.g. when the user picks it in the settings).</summary>
    /// <exception cref="ArgumentException">The screen name or resolution cannot be stored (see <see cref="IsValid"/>).</exception>
    public static CameraMonitorId FromScreen(CameraScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        return new CameraMonitorId(screen.DisplayName, screen.Bounds.Width, screen.Bounds.Height);
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
}
