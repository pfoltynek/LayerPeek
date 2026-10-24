using System.Runtime.InteropServices;

namespace LayerPeek.Core.Cameras;

/// <summary>
/// The monitor chosen for a camera window (T-035): the name the OS reports for it (<see cref="CameraScreen.DisplayName"/>),
/// its resolution in pixels (size of <see cref="CameraScreen.Bounds"/>) and, where the platform has one, the path of the
/// physical monitor (<see cref="DevicePath"/>, Q8), which tells two identical monitors apart, together with the paths of
/// the other monitors with the same name connected when it was chosen (<see cref="KnownTwinPaths"/>, T-071 Q5). Matching:
/// see <see cref="CameraWindowDefaultPlacement.SelectScreen"/>; names are compared ordinally, paths case-insensitively.
/// </summary>
/// <remarks>Value equality includes <see cref="KnownTwinPaths"/> (in order); <see cref="HasSameIdentity"/> ignores them.</remarks>
public sealed record CameraMonitorId
{
    /// <summary>Longest accepted name (real names are short; anything longer comes from a damaged file).</summary>
    public const int MaxDisplayNameLength = 256;

    /// <summary>Longest accepted device path (Windows uses at most 128 characters).</summary>
    public const int MaxDevicePathLength = 256;

    /// <summary>Largest accepted width or height in pixels.</summary>
    public const int MaxResolution = 65535;

    /// <summary>Most remembered twins (real setups have a few identical monitors at most; keeps the settings small).</summary>
    public const int MaxKnownTwinPaths = 8;

    private readonly string[] _knownTwinPaths;

    /// <param name="knownTwinPaths">
    /// Paths of the other monitors with the same name (<see cref="KnownTwinPaths"/>); only with a
    /// <paramref name="devicePath"/>, each valid (<see cref="IsValidDevicePath"/>), none twice or equal to
    /// <paramref name="devicePath"/> (case-insensitive), at most <see cref="MaxKnownTwinPaths"/>. Copied.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="displayName"/> or a twin path is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The values are not valid (see above, <see cref="IsValid"/> and <see cref="IsValidDevicePath"/>); the message never
    /// contains the name or a path.
    /// </exception>
    public CameraMonitorId(string displayName, int width, int height, string? devicePath = null, IEnumerable<string>? knownTwinPaths = null)
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
        _knownTwinPaths = ValidateTwins(devicePath, knownTwinPaths);
    }

    /// <summary>Name reported by the OS, kept exactly (not trimmed).</summary>
    public string DisplayName { get; }

    /// <summary>Width of the monitor in pixels.</summary>
    public int Width { get; }

    /// <summary>Height of the monitor in pixels.</summary>
    public int Height { get; }

    /// <summary>
    /// The path of the physical monitor (Windows); <see langword="null"/> on other platforms and in settings written before
    /// it was stored. It changes when the monitor is plugged into another connector; the monitor is then found by its
    /// name and resolution when that is unambiguous (T-071 Q5, see <see cref="CameraWindowDefaultPlacement.SelectScreen"/>).
    /// </summary>
    public string? DevicePath { get; }

    /// <summary>
    /// Paths of the other monitors with the same name that were connected when this monitor was chosen (T-071 Q5): such a
    /// monitor is never taken for this one, e.g. while this one is switched off. Empty without a <see cref="DevicePath"/>,
    /// in older settings and when there were none. A twin connected only later is not known (documented limitation).
    /// </summary>
    public IReadOnlyList<string> KnownTwinPaths => _knownTwinPaths;

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
    /// Identifies <paramref name="screen"/> without twins. A device path that cannot be stored is left out (the name and
    /// resolution still identify the monitor).
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

    /// <summary>
    /// Identifies <paramref name="screen"/> when the user picks it among <paramref name="connected"/> (T-071 Q5): with a
    /// storable device path, the storable paths of the other connected monitors with the same name (any resolution) become
    /// <see cref="KnownTwinPaths"/>, in list order, without duplicates, at most <see cref="MaxKnownTwinPaths"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The screen name or resolution cannot be stored (see <see cref="IsValid"/>).</exception>
    public static CameraMonitorId FromScreen(CameraScreen screen, IReadOnlyList<CameraScreen> connected)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(connected);
        string? devicePath = IsValidDevicePath(screen.DevicePath) ? screen.DevicePath : null;
        List<string> twins = [];
        if (devicePath is not null)
        {
            foreach (CameraScreen other in connected)
            {
                if (twins.Count == MaxKnownTwinPaths)
                {
                    break;
                }

                if (string.Equals(other.DisplayName, screen.DisplayName, StringComparison.Ordinal)
                    && other.DevicePath is { } path
                    && IsValidDevicePath(path)
                    && !string.Equals(path, devicePath, StringComparison.OrdinalIgnoreCase)
                    && !ContainsPath(CollectionsMarshal.AsSpan(twins), path))
                {
                    twins.Add(path);
                }
            }
        }

        return new CameraMonitorId(screen.DisplayName, screen.Bounds.Width, screen.Bounds.Height, devicePath, twins);
    }

    /// <summary>
    /// This monitor found at <paramref name="devicePath"/> (another connector, user decision 2026-10-23): the same name,
    /// resolution and twins.
    /// </summary>
    /// <exception cref="ArgumentException">The path is not valid or is a known twin.</exception>
    public CameraMonitorId WithDevicePath(string devicePath)
    {
        ArgumentNullException.ThrowIfNull(devicePath);
        return new CameraMonitorId(DisplayName, Width, Height, devicePath, _knownTwinPaths);
    }

    /// <summary>
    /// This monitor with its twins refreshed from <paramref name="connected"/> (user decision 2026-10-23, when the settings
    /// window saves): only when a connected screen has this monitor's device path, the stored twins are followed by the
    /// storable paths of the other connected monitors with the same name that are new, up to
    /// <see cref="MaxKnownTwinPaths"/> (stored ones first), never the monitor's own path. The same instance when nothing is
    /// added.
    /// </summary>
    public CameraMonitorId WithTwinsFrom(IReadOnlyList<CameraScreen> connected)
    {
        ArgumentNullException.ThrowIfNull(connected);
        if (DevicePath is null || _knownTwinPaths.Length == MaxKnownTwinPaths || !connected.Any(MatchesDevicePath))
        {
            return this;
        }

        List<string> twins = [.. _knownTwinPaths];
        foreach (CameraScreen screen in connected)
        {
            if (twins.Count == MaxKnownTwinPaths)
            {
                break;
            }

            if (MatchesName(screen)
                && screen.DevicePath is { } path
                && IsValidDevicePath(path)
                && !string.Equals(path, DevicePath, StringComparison.OrdinalIgnoreCase)
                && !ContainsPath(CollectionsMarshal.AsSpan(twins), path))
            {
                twins.Add(path);
            }
        }

        return twins.Count == _knownTwinPaths.Length ? this : new CameraMonitorId(DisplayName, Width, Height, DevicePath, twins);
    }

    /// <summary>The same physical monitor: both have a device path and it is the same (case-insensitive, like Windows paths).</summary>
    public bool MatchesDevicePath(CameraScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        return DevicePath is not null && string.Equals(DevicePath, screen.DevicePath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary><paramref name="screen"/> is a known twin (<see cref="KnownTwinPaths"/>, case-insensitive), i.e. another monitor.</summary>
    public bool IsKnownTwin(CameraScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        return screen.DevicePath is { } path && ContainsPath(_knownTwinPaths, path);
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

    /// <summary>Equal apart from <see cref="KnownTwinPaths"/>: the same monitor, only recognised with other knowledge.</summary>
    public bool HasSameIdentity(CameraMonitorId? other) =>
        other is not null
        && string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal)
        && Width == other.Width
        && Height == other.Height
        && string.Equals(DevicePath, other.DevicePath, StringComparison.Ordinal);

    // Value equality including the twin list (the synthesized one would compare the array by reference).
    public bool Equals(CameraMonitorId? other) =>
        ReferenceEquals(this, other)
        || (HasSameIdentity(other) && _knownTwinPaths.AsSpan().SequenceEqual(other!._knownTwinPaths));

    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(DisplayName, StringComparer.Ordinal);
        hash.Add(Width);
        hash.Add(Height);
        hash.Add(DevicePath, StringComparer.Ordinal);
        foreach (string path in _knownTwinPaths)
        {
            hash.Add(path, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    private static string[] ValidateTwins(string? devicePath, IEnumerable<string>? knownTwinPaths)
    {
        if (knownTwinPaths is null)
        {
            return [];
        }

        string[] twins = [.. knownTwinPaths];
        if (twins.Length == 0)
        {
            return twins;
        }

        if (devicePath is null)
        {
            throw new ArgumentException("Known twin paths need the device path of the monitor.", nameof(knownTwinPaths));
        }

        if (twins.Length > MaxKnownTwinPaths)
        {
            throw new ArgumentException($"At most {MaxKnownTwinPaths} known twin paths.", nameof(knownTwinPaths));
        }

        for (int i = 0; i < twins.Length; i++)
        {
            string twin = twins[i] ?? throw new ArgumentNullException(nameof(knownTwinPaths), "A known twin path is null.");
            if (!IsValidDevicePath(twin)
                || string.Equals(twin, devicePath, StringComparison.OrdinalIgnoreCase)
                || ContainsPath(twins.AsSpan(0, i), twin))
            {
                throw new ArgumentException(
                    "Invalid known twin path: each must be a valid device path, different from the monitor's own and from the others.",
                    nameof(knownTwinPaths));
            }
        }

        return twins;
    }

    private static bool ContainsPath(ReadOnlySpan<string> paths, string path)
    {
        foreach (string candidate in paths)
        {
            if (string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsValidText(string? text, int maxLength) =>
        !string.IsNullOrWhiteSpace(text)
        && text.Length <= maxLength
        && !text.AsSpan().ContainsAnyInRange('\0', '\u001F')
        && !text.AsSpan().ContainsAnyInRange('\u007F', '\u009F');
}
