using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.Core.Cameras;

namespace PrusaTrayDiagnostic.App.Tests.Cameras;

/// <summary>Monitors chosen by the test (headless Avalonia has a single one); ordered like the real catalog.</summary>
internal sealed class FakeScreenCatalog(params CameraScreen[] screens) : IScreenCatalog
{
    /// <summary>Taskbar height of <see cref="Screen"/>: the work area is the monitor without it.</summary>
    public const int Taskbar = 48;

    public List<CameraScreen> Screens { get; } = [.. screens];

    public IReadOnlyList<CameraScreen> GetScreens() => AvaloniaScreenCatalog.Order(Screens);

    /// <summary>A monitor at (<paramref name="x"/>, <paramref name="y"/>) with a taskbar of <see cref="Taskbar"/> pixels at the bottom.</summary>
    public static CameraScreen Screen(string name, int x, int y, int width, int height, double scaling = 1.0, bool primary = false) =>
        new(name, new PixelBounds(x, y, width, height), new PixelBounds(x, y, width, height - Taskbar), scaling, primary);

    /// <summary>Primary 2560 × 1440 at 100 %, a 1920 × 1080 at 150 % to its left and a rotated 1440 × 2560 to its right.</summary>
    public static FakeScreenCatalog ThreeMonitors() => new(
        Screen("DELL U2719D", 0, 0, 2560, 1440, primary: true),
        Screen("LG HDR 4K", -1920, 200, 1920, 1080, scaling: 1.5),
        Screen("DELL U2719D", 2560, 0, 1440, 2560));
}
