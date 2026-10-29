using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

using LayerPeek.App.Platform.MacOS;
using LayerPeek.App.Platform.Windows;
using LayerPeek.Core.Cameras;

namespace LayerPeek.App.Cameras;

/// <summary>
/// The connected monitors as <see cref="CameraScreen"/> (T-035), so that the camera window placement and the monitor list
/// of the settings can be tested with several fake monitors (headless Avalonia has one).
/// </summary>
internal interface IScreenCatalog
{
    /// <summary>
    /// The monitors now, ordered left to right, then top to bottom (the numbers "Monitor 1, 2…" of the settings and "first"
    /// for monitors with the same name follow this order). Empty when the platform reports none. UI thread only.
    /// </summary>
    IReadOnlyList<CameraScreen> GetScreens();
}

/// <summary>No monitors (e.g. a settings view model without a window in tests).</summary>
internal sealed class EmptyScreenCatalog : IScreenCatalog
{
    public static EmptyScreenCatalog Instance { get; } = new();

    public IReadOnlyList<CameraScreen> GetScreens() => [];
}

/// <summary>
/// The monitors Avalonia reports for a window (<see cref="TopLevel.Screens"/>; usable before the window is shown).
/// </summary>
/// <remarks>
/// Names: on Windows the name and the device path of the physical monitor come from Windows itself
/// (<see cref="MonitorNameNative"/>: the friendly name such as
/// "DELL U2719D", else the GDI device name <c>\\.\DISPLAY1</c>), because Avalonia 12.1.4 reports an empty
/// <c>DisplayName</c> there; Avalonia's name is used when that fails. Other platforms use Avalonia's <c>DisplayName</c>
/// (macOS: <c>NSScreen.localizedName</c>, e.g. "Built-in Retina Display" in the system language, identical monitors
/// numbered "DELL U2723QE (1)", "(2)"). On macOS the device path is the display's persistent UUID
/// (<see cref="MacDisplayUuid"/>, T-038), because those names are not stable: the numbering of identical monitors may
/// change at the next start or reconnect. A monitor without any name is still listed (empty name): the window can be
/// placed on it, but the settings cannot store it as the chosen monitor. Device paths that two monitors share are
/// dropped (<see cref="WithoutSharedDevicePaths"/>).
/// </remarks>
internal sealed class AvaloniaScreenCatalog : IScreenCatalog
{
    /// <summary>Avalonia.Native's handle of a screen: the <c>CGDirectDisplayID</c> as the handle value.</summary>
    internal const string MacDisplayHandleDescriptor = "CGDirectDisplayID";

    private readonly TopLevel _topLevel;
    private readonly Func<uint, string?> _macDisplayPath;

    /// <param name="topLevel">The window whose screens are listed.</param>
    /// <param name="macDisplayPath">
    /// The device path of a macOS display by its <c>CGDirectDisplayID</c>; default: <see cref="MacDisplayUuid"/> (only
    /// asked on macOS). Tests supply a fake.
    /// </param>
    public AvaloniaScreenCatalog(TopLevel topLevel, Func<uint, string?>? macDisplayPath = null)
    {
        _topLevel = topLevel ?? throw new ArgumentNullException(nameof(topLevel));
        _macDisplayPath = macDisplayPath ?? MacDisplayUuidOrNone;
    }

    public IReadOnlyList<CameraScreen> GetScreens()
    {
        if (_topLevel.Screens is not { } screens)
        {
            return [];
        }

        List<CameraScreen> result = [];
        foreach (Screen screen in screens.All)
        {
            if (Map(screen) is { } mapped)
            {
                result.Add(mapped);
            }
        }

        return Order(WithoutSharedDevicePaths(result));
    }

    /// <summary>Left to right, then top to bottom (stable for equal positions).</summary>
    internal static IReadOnlyList<CameraScreen> Order(IEnumerable<CameraScreen> screens) =>
        [.. screens.OrderBy(static screen => screen.Bounds.X).ThenBy(static screen => screen.Bounds.Y)];

    /// <summary>
    /// <paramref name="screens"/> where a device path that more than one screen has (compared like
    /// <see cref="CameraMonitorId.MatchesDevicePath"/>, ignoring case) is removed from all of them: such a path tells
    /// nobody apart, and a saved monitor would always resolve to the first of them. Those screens fall back to their
    /// names (on macOS still told apart by the "(1)", "(2)" macOS adds). Other screens are returned as they are.
    /// </summary>
    internal static IReadOnlyList<CameraScreen> WithoutSharedDevicePaths(IReadOnlyList<CameraScreen> screens)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> shared = new(StringComparer.OrdinalIgnoreCase);
        foreach (CameraScreen screen in screens)
        {
            if (screen.DevicePath is { } path && !seen.Add(path))
            {
                shared.Add(path);
            }
        }

        return shared.Count == 0
            ? screens
            : [.. screens.Select(screen => screen.DevicePath is { } path && shared.Contains(path) ? screen with { DevicePath = null } : screen)];
    }

    /// <summary>
    /// The device path of a macOS screen from Avalonia.Native's platform handle (<see cref="MacDisplayHandleDescriptor"/>)
    /// through <paramref name="pathOfDisplay"/>; <see langword="null"/> for any other or missing handle, display id 0
    /// (<c>kCGNullDirectDisplay</c>) or an id that is no <c>uint</c>.
    /// </summary>
    internal static string? MacDevicePathOf(IPlatformHandle? handle, Func<uint, string?> pathOfDisplay)
    {
        ArgumentNullException.ThrowIfNull(pathOfDisplay);
        return handle is { HandleDescriptor: MacDisplayHandleDescriptor, Handle: var value } && value > 0 && value <= uint.MaxValue
            ? pathOfDisplay((uint)value)
            : null;
    }

    private CameraScreen? Map(Screen screen)
    {
        PixelBounds bounds = ToBounds(screen.Bounds);
        if (bounds.IsEmpty || !double.IsFinite(screen.Scaling) || screen.Scaling <= 0)
        {
            return null;
        }

        (string name, string? devicePath) = IdentityOf(screen);
        return new CameraScreen(name, bounds, ToBounds(screen.WorkingArea), screen.Scaling, screen.IsPrimary, devicePath);
    }

    /// <summary>The name and (Windows, macOS) the device path of the physical monitor (T-035 Q8, T-038).</summary>
    private (string Name, string? DevicePath) IdentityOf(Screen screen)
    {
        if (OperatingSystem.IsWindows() && screen.TryGetPlatformHandle() is { Handle: var handle and not 0 })
        {
            try
            {
                if (MonitorNameNative.GetIdentity(handle) is { } identity)
                {
                    return (identity.Name, identity.DevicePath);
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                // Not a desktop Windows (no display configuration API): Avalonia's name below.
            }
        }

        string name = screen.DisplayName ?? string.Empty;
        if (OperatingSystem.IsMacOS())
        {
            string? path = MacDevicePathOf(screen.TryGetPlatformHandle(), _macDisplayPath);
            return (name, CameraMonitorId.IsValidDevicePath(path) ? path : null);
        }

        return (name, null);
    }

    /// <summary><see cref="MacDisplayUuid.Get"/> on macOS; <see langword="null"/> elsewhere or when ColorSync is missing.</summary>
    private static string? MacDisplayUuidOrNone(uint displayId)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }

        try
        {
            return MacDisplayUuid.Get(displayId);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // No ColorSync (not expected on macOS 14+): the monitor is identified by its name as before.
            return null;
        }
    }

    private static PixelBounds ToBounds(PixelRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
}
