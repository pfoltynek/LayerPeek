namespace LayerPeek.Core.Cameras;

/// <summary>A rectangle in screen pixels (virtual desktop coordinates; may be negative left of or above the primary monitor).</summary>
public readonly record struct PixelBounds(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    /// <summary>No area (zero or negative width or height).</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>
/// A connected monitor as seen by the UI layer (T-035), without any UI types: e.g. an Avalonia <c>Screen</c> mapped by the
/// App. <paramref name="Bounds"/> and <paramref name="WorkingArea"/> are in pixels; <paramref name="Scaling"/> converts
/// device-independent units to pixels on this monitor (1.0 = 100 %, 1.5 = 150 %).
/// </summary>
/// <param name="DisplayName">Name reported by the OS (may be empty when the platform reports none).</param>
/// <param name="Bounds">The whole monitor in pixels; must not be empty.</param>
/// <param name="WorkingArea">The monitor without task bar / menu bar / dock in pixels; empty when the platform does not report it.</param>
/// <param name="Scaling">Positive finite number.</param>
/// <param name="IsPrimary">The primary monitor.</param>
/// <param name="DevicePath">
/// A path that identifies this physical monitor on its connector (Windows: <c>monitorDevicePath</c> of the display
/// configuration); <see langword="null"/> when the platform has none. Tells two identical monitors apart (T-035 Q8).
/// </param>
public sealed record CameraScreen(string DisplayName, PixelBounds Bounds, PixelBounds WorkingArea, double Scaling, bool IsPrimary, string? DevicePath = null)
{
    public string DisplayName { get; } = DisplayName ?? throw new ArgumentNullException(nameof(DisplayName));

    public PixelBounds Bounds { get; } = Bounds.IsEmpty
        ? throw new ArgumentOutOfRangeException(nameof(Bounds), Bounds, "A screen must have an area.")
        : Bounds;

    public double Scaling { get; } = double.IsFinite(Scaling) && Scaling > 0
        ? Scaling
        : throw new ArgumentOutOfRangeException(nameof(Scaling), Scaling, "Scaling must be a positive finite number.");

    /// <summary><see cref="WorkingArea"/>, or <see cref="Bounds"/> when the working area is empty.</summary>
    public PixelBounds UsableArea => WorkingArea.IsEmpty ? Bounds : WorkingArea;
}

/// <summary>Monitor chosen for a camera window.</summary>
/// <param name="Screen">The monitor to use.</param>
/// <param name="PreferredMonitorMissing">A monitor was chosen in the settings but is not connected; <paramref name="Screen"/> is the primary one.</param>
public sealed record CameraScreenSelection(CameraScreen Screen, bool PreferredMonitorMissing);

/// <summary>Where a camera window opens from its defaults.</summary>
/// <param name="Screen">Monitor the window is placed on.</param>
/// <param name="PreferredMonitorMissing">The chosen monitor is not connected; <paramref name="Screen"/> is the primary one.</param>
/// <param name="Bounds">
/// Outer window rectangle in pixels (client area plus the frame passed to the computation); the whole monitor in full screen.
/// </param>
/// <param name="ClientWidth">Client width in device-independent units (the window's <c>Width</c>), after clamping.</param>
/// <param name="ClientHeight">Client height in device-independent units (the window's <c>Height</c>), after clamping.</param>
public sealed record CameraWindowTarget(CameraScreen Screen, bool PreferredMonitorMissing, PixelBounds Bounds, double ClientWidth, double ClientHeight);
