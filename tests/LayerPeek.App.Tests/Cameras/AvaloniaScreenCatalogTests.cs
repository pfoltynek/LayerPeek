using Avalonia.Platform;

using LayerPeek.App.Cameras;
using LayerPeek.Core.Cameras;

namespace LayerPeek.App.Tests.Cameras;

/// <summary>
/// T-038: how the screen catalog identifies macOS monitors (device path = display UUID from Avalonia.Native's
/// <c>CGDirectDisplayID</c> handle) and that a device path several screens share is dropped.
/// </summary>
public sealed class AvaloniaScreenCatalogTests
{
    private const string UuidA = "A8825834-5FF5-44BB-B419-A855792BA90D";
    private const string UuidB = "37D8832A-2D66-02CA-B9F7-8F30A301B230";

    [Fact]
    public void MacScreenHandleGivesThePathOfItsDisplay()
    {
        List<uint> asked = [];

        string? path = AvaloniaScreenCatalog.MacDevicePathOf(
            new PlatformHandle(new IntPtr(69734208), AvaloniaScreenCatalog.MacDisplayHandleDescriptor),
            id =>
            {
                asked.Add(id);
                return UuidA;
            });

        Assert.Equal(UuidA, path);
        Assert.Equal([69734208u], asked);
    }

    [Fact]
    public void MacDisplayIdTakesTheWholeUnsignedRange()
    {
        string? path = AvaloniaScreenCatalog.MacDevicePathOf(
            new PlatformHandle(new IntPtr(uint.MaxValue), AvaloniaScreenCatalog.MacDisplayHandleDescriptor),
            id => id == uint.MaxValue ? UuidB : null);

        Assert.Equal(UuidB, path);
    }

    public static TheoryData<string, long> NoMacDisplays => new()
    {
        { "HWND", 1234 },
        { "cgdirectdisplayid", 1234 },
        { AvaloniaScreenCatalog.MacDisplayHandleDescriptor, 0 },
        { AvaloniaScreenCatalog.MacDisplayHandleDescriptor, -5 },
        { AvaloniaScreenCatalog.MacDisplayHandleDescriptor, (long)uint.MaxValue + 1 },
    };

    [Theory]
    [MemberData(nameof(NoMacDisplays))]
    public void OtherHandlesAndInvalidDisplayIdsHaveNoPath(string descriptor, long value)
    {
        bool asked = false;

        string? path = AvaloniaScreenCatalog.MacDevicePathOf(new PlatformHandle(new IntPtr(value), descriptor), _ =>
        {
            asked = true;
            return UuidA;
        });

        Assert.Null(path);
        Assert.False(asked);
    }

    [Fact]
    public void ScreenWithoutAHandleHasNoPath() =>
        Assert.Null(AvaloniaScreenCatalog.MacDevicePathOf(null, _ => UuidA));

    [Fact]
    public void UniquePathsAreKeptAsTheyAre()
    {
        CameraScreen[] screens =
        [
            Screen("Built-in Retina Display", 0, UuidA),
            Screen("DELL U2723QE", 1512, UuidB),
            Screen("LG HDR 4K", 4072, devicePath: null),
        ];

        IReadOnlyList<CameraScreen> result = AvaloniaScreenCatalog.WithoutSharedDevicePaths(screens);

        Assert.Same(screens, result);
    }

    [Fact]
    public void PathThatSeveralScreensShareIsDroppedFromAllOfThem()
    {
        // Two identical monitors macOS could not tell apart (no serial number): their names ("(1)", "(2)") stay.
        CameraScreen builtIn = Screen("Built-in Retina Display", 0, UuidA);
        CameraScreen first = Screen("DELL U2723QE (1)", 1512, UuidB);
        CameraScreen second = Screen("DELL U2723QE (2)", 4072, UuidB.ToLowerInvariant());

        IReadOnlyList<CameraScreen> result = AvaloniaScreenCatalog.WithoutSharedDevicePaths([builtIn, first, second]);

        Assert.Equal([builtIn, first with { DevicePath = null }, second with { DevicePath = null }], result);
        Assert.Same(builtIn, result[0]);
    }

    [Fact]
    public void ScreensWithoutPathsStayWithoutPaths()
    {
        CameraScreen[] screens = [Screen("DELL U2719D", 0, devicePath: null), Screen("DELL U2719D", 2560, devicePath: null)];

        Assert.Same(screens, AvaloniaScreenCatalog.WithoutSharedDevicePaths(screens));
    }

    // ---- Why macOS needs the path: its names are not stable (rules of CameraWindowDefaultPlacement) ----

    [Fact]
    public void ChosenTwinIsFoundWhenMacOSNumbersTheIdenticalMonitorsTheOtherWayRound()
    {
        // Chosen: the right one of two identical monitors, then "DELL U2723QE (2)".
        CameraScreen[] before = [Screen("Built-in Retina Display", 0, UuidA), Screen("DELL U2723QE (1)", 1512, "11111111-1111-1111-1111-111111111111"), Screen("DELL U2723QE (2)", 4072, UuidB)];
        CameraMonitorId chosen = CameraMonitorId.FromScreen(before[2], before);
        // After the next start macOS numbers them the other way round.
        CameraScreen[] after = [before[0], Renamed(before[1], "DELL U2723QE (2)"), Renamed(before[2], "DELL U2723QE (1)")];

        CameraScreenSelection? selection = CameraWindowDefaultPlacement.SelectScreen(chosen, after);

        Assert.Equal(new CameraScreenSelection(after[2], PreferredMonitorMissing: false), selection);
    }

    [Fact]
    public void ChosenTwinSwitchedOffIsNotReplacedByTheOtherOne()
    {
        CameraScreen[] before = [Screen("Built-in Retina Display", 0, UuidA), Screen("DELL U2723QE (1)", 1512, "11111111-1111-1111-1111-111111111111"), Screen("DELL U2723QE (2)", 4072, UuidB)];
        CameraMonitorId chosen = CameraMonitorId.FromScreen(before[2], before);
        // The chosen one is off; macOS drops the number of the remaining one.
        CameraScreen[] after = [before[0], Renamed(before[1], "DELL U2723QE")];

        CameraScreenSelection? selection = CameraWindowDefaultPlacement.SelectScreen(chosen, after);

        Assert.Equal(new CameraScreenSelection(after[0], PreferredMonitorMissing: true), selection);
        Assert.False(CameraWindowDefaultPlacement.IsConnected(chosen, after));
    }

    [Fact]
    public void BuiltInDisplayIsFoundAfterTheSystemLanguageChanged()
    {
        CameraScreen[] before = [Screen("Vestavěný displej Retina", 0, UuidA), Screen("DELL U2723QE", 1512, UuidB)];
        CameraMonitorId chosen = CameraMonitorId.FromScreen(before[0], before);
        CameraScreen[] after = [Renamed(before[0], "Built-in Retina Display"), before[1]];

        Assert.Equal(new CameraScreenSelection(after[0], PreferredMonitorMissing: false), CameraWindowDefaultPlacement.SelectScreen(chosen, after));
    }

    private static CameraScreen Renamed(CameraScreen screen, string name) =>
        new(name, screen.Bounds, screen.WorkingArea, screen.Scaling, screen.IsPrimary, screen.DevicePath);

    private static CameraScreen Screen(string name, int x, string? devicePath) =>
        new(name, new PixelBounds(x, 0, 1512, 982), new PixelBounds(x, 37, 1512, 880), 1.0, IsPrimary: x == 0, devicePath);
}
