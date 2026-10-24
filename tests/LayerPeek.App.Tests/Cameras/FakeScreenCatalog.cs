using LayerPeek.App.Cameras;
using LayerPeek.Core.Cameras;

namespace LayerPeek.App.Tests.Cameras;

/// <summary>Monitors chosen by the test (headless Avalonia has a single one); ordered like the real catalog.</summary>
internal sealed class FakeScreenCatalog(params CameraScreen[] screens) : IScreenCatalog
{
    /// <summary>Taskbar height of <see cref="Screen"/>: the work area is the monitor without it.</summary>
    public const int Taskbar = 48;

    /// <summary>Windows device paths of two identical DELL monitors (on different connectors).</summary>
    public const string DellPathA = @"\\?\DISPLAY#DEL415A#5&224edec0&0&UID160000#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    /// <inheritdoc cref="DellPathA"/>
    public const string DellPathB = @"\\?\DISPLAY#DEL415A#5&224edec0&0&UID160002#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    public List<CameraScreen> Screens { get; } = [.. screens];

    /// <summary>How often the monitors were read.</summary>
    public int Reads { get; private set; }

    public IReadOnlyList<CameraScreen> GetScreens()
    {
        Reads++;
        return AvaloniaScreenCatalog.Order(Screens);
    }

    /// <summary>A monitor at (<paramref name="x"/>, <paramref name="y"/>) with a taskbar of <see cref="Taskbar"/> pixels at the bottom.</summary>
    public static CameraScreen Screen(string name, int x, int y, int width, int height, double scaling = 1.0, bool primary = false, string? devicePath = null) =>
        new(name, new PixelBounds(x, y, width, height), new PixelBounds(x, y, width, height - Taskbar), scaling, primary, devicePath);

    /// <summary>
    /// Primary 2560 × 1440 at 100 %, a 1920 × 1080 at 150 % to its left (no device path, like other platforms) and a
    /// rotated 1440 × 2560 of the same model as the primary to its right (both DELLs with device paths, like Windows).
    /// </summary>
    public static FakeScreenCatalog ThreeMonitors() => new(
        Screen("DELL U2719D", 0, 0, 2560, 1440, primary: true, devicePath: DellPathA),
        Screen("LG HDR 4K", -1920, 200, 1920, 1080, scaling: 1.5),
        Screen("DELL U2719D", 2560, 0, 1440, 2560, devicePath: DellPathB));
}
