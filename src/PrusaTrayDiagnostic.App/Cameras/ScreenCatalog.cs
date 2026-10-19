using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

using PrusaTrayDiagnostic.App.Platform.Windows;
using PrusaTrayDiagnostic.Core.Cameras;

namespace PrusaTrayDiagnostic.App.Cameras;

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
/// Names: on Windows the name comes from Windows itself (<see cref="MonitorNameNative"/>: the friendly name such as
/// "DELL U2719D", else the GDI device name <c>\\.\DISPLAY1</c>), because Avalonia 12.1.4 reports an empty
/// <c>DisplayName</c> there; Avalonia's name is used when that fails. Other platforms use Avalonia's <c>DisplayName</c>
/// (macOS: the localized monitor name, not verified). A monitor without any name is still listed (empty name): the
/// window can be placed on it, but the settings cannot store it as the chosen monitor.
/// </remarks>
internal sealed class AvaloniaScreenCatalog(TopLevel topLevel) : IScreenCatalog
{
    private readonly TopLevel _topLevel = topLevel ?? throw new ArgumentNullException(nameof(topLevel));

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

        return Order(result);
    }

    /// <summary>Left to right, then top to bottom (stable for equal positions).</summary>
    internal static IReadOnlyList<CameraScreen> Order(IEnumerable<CameraScreen> screens) =>
        [.. screens.OrderBy(static screen => screen.Bounds.X).ThenBy(static screen => screen.Bounds.Y)];

    private static CameraScreen? Map(Screen screen)
    {
        PixelBounds bounds = ToBounds(screen.Bounds);
        if (bounds.IsEmpty || !double.IsFinite(screen.Scaling) || screen.Scaling <= 0)
        {
            return null;
        }

        return new CameraScreen(NameOf(screen), bounds, ToBounds(screen.WorkingArea), screen.Scaling, screen.IsPrimary);
    }

    private static string NameOf(Screen screen)
    {
        if (OperatingSystem.IsWindows() && screen.TryGetPlatformHandle() is { Handle: var handle and not 0 })
        {
            try
            {
                if (MonitorNameNative.GetName(handle) is { } name)
                {
                    return name;
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                // Not a desktop Windows (no display configuration API): Avalonia's name below.
            }
        }

        return screen.DisplayName ?? string.Empty;
    }

    private static PixelBounds ToBounds(PixelRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
}
