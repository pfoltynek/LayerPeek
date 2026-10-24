using LayerPeek.Core.Cameras;

namespace LayerPeek.Core.Tests.Cameras;

/// <summary>Placement from the defaults on fake monitors (pixels); no real screen is involved.</summary>
public class CameraWindowDefaultPlacementTests
{
    // Primary, 100 %, taskbar 40 px at the bottom.
    private static readonly CameraScreen Primary = new("DISPLAY1", new PixelBounds(0, 0, 1920, 1080), new PixelBounds(0, 0, 1920, 1040), 1.0, IsPrimary: true);

    // Right of the primary, 150 %.
    private static readonly CameraScreen Right = new("DISPLAY2", new PixelBounds(1920, 0, 2560, 1440), new PixelBounds(1920, 0, 2560, 1400), 1.5, IsPrimary: false);

    // Left of the primary and a bit higher (negative origin), 125 %.
    private static readonly CameraScreen Left = new("DISPLAY3", new PixelBounds(-1280, -200, 1280, 1024), new PixelBounds(-1280, -200, 1280, 984), 1.25, IsPrimary: false);

    // Above the primary, 200 %.
    private static readonly CameraScreen Above = new("DISPLAY4", new PixelBounds(0, -2160, 3840, 2160), new PixelBounds(0, -2160, 3840, 2160), 2.0, IsPrimary: false);

    [Fact]
    public void MarginIsSixteenDip() => Assert.Equal(16, CameraWindowDefaultPlacement.MarginDip);

    [Fact]
    public void DefaultsOpenCenteredOnPrimary()
    {
        CameraWindowTarget target = Compute(CameraWindowDefaults.Default, CameraWindowMode.Normal, Primary, Right);

        Assert.Same(Primary, target.Screen);
        Assert.False(target.PreferredMonitorMissing);
        Assert.Equal(new PixelBounds(480, 250, 960, 540), target.Bounds);
        Assert.Equal(960, target.ClientWidth);
        Assert.Equal(540, target.ClientHeight);
    }

    public static TheoryData<string, CameraWindowCorner, PixelBounds> Corners => new()
    {
        // 100 %: margin 16 px, 960×540 px.
        { "DISPLAY1", CameraWindowCorner.TopLeft, new PixelBounds(16, 16, 960, 540) },
        { "DISPLAY1", CameraWindowCorner.TopRight, new PixelBounds(944, 16, 960, 540) },
        { "DISPLAY1", CameraWindowCorner.BottomLeft, new PixelBounds(16, 484, 960, 540) },
        { "DISPLAY1", CameraWindowCorner.BottomRight, new PixelBounds(944, 484, 960, 540) },
        { "DISPLAY1", CameraWindowCorner.Center, new PixelBounds(480, 250, 960, 540) },
        // 150 %: margin 24 px, 1440×810 px.
        { "DISPLAY2", CameraWindowCorner.TopLeft, new PixelBounds(1944, 24, 1440, 810) },
        { "DISPLAY2", CameraWindowCorner.TopRight, new PixelBounds(3016, 24, 1440, 810) },
        { "DISPLAY2", CameraWindowCorner.BottomLeft, new PixelBounds(1944, 566, 1440, 810) },
        { "DISPLAY2", CameraWindowCorner.BottomRight, new PixelBounds(3016, 566, 1440, 810) },
        { "DISPLAY2", CameraWindowCorner.Center, new PixelBounds(2480, 295, 1440, 810) },
        // 125 %, negative origin: margin 20 px, 1200×675 px.
        { "DISPLAY3", CameraWindowCorner.TopLeft, new PixelBounds(-1260, -180, 1200, 675) },
        { "DISPLAY3", CameraWindowCorner.TopRight, new PixelBounds(-1220, -180, 1200, 675) },
        { "DISPLAY3", CameraWindowCorner.BottomLeft, new PixelBounds(-1260, 89, 1200, 675) },
        { "DISPLAY3", CameraWindowCorner.BottomRight, new PixelBounds(-1220, 89, 1200, 675) },
        { "DISPLAY3", CameraWindowCorner.Center, new PixelBounds(-1240, -46, 1200, 675) },
        // 200 %, above the primary: margin 32 px, 1920×1080 px.
        { "DISPLAY4", CameraWindowCorner.TopLeft, new PixelBounds(32, -2128, 1920, 1080) },
        { "DISPLAY4", CameraWindowCorner.TopRight, new PixelBounds(1888, -2128, 1920, 1080) },
        { "DISPLAY4", CameraWindowCorner.BottomLeft, new PixelBounds(32, -1112, 1920, 1080) },
        { "DISPLAY4", CameraWindowCorner.BottomRight, new PixelBounds(1888, -1112, 1920, 1080) },
        { "DISPLAY4", CameraWindowCorner.Center, new PixelBounds(960, -1620, 1920, 1080) },
    };

    [Theory]
    [MemberData(nameof(Corners))]
    public void CornerIsPlacedWithScaledMarginOnChosenMonitor(string monitor, CameraWindowCorner corner, PixelBounds expected)
    {
        CameraScreen screen = AllScreens().Single(screen => screen.DisplayName == monitor);
        CameraWindowDefaults defaults = new()
        {
            Monitor = CameraMonitorId.FromScreen(screen),
            Normal = new CameraWindowLayout(new CameraWindowSize(960, 540), corner),
        };

        CameraWindowTarget target = Compute(defaults, CameraWindowMode.Normal, AllScreens());

        Assert.Same(screen, target.Screen);
        Assert.False(target.PreferredMonitorMissing);
        Assert.Equal(expected, target.Bounds);
        Assert.Equal(960, target.ClientWidth);
        Assert.Equal(540, target.ClientHeight);
    }

    [Fact]
    public void MiniPreviewUsesItsOwnSizeAndCorner()
    {
        CameraWindowDefaults defaults = new()
        {
            Monitor = CameraMonitorId.FromScreen(Right),
            Mini = new CameraWindowLayout(new CameraWindowSize(320, 180), CameraWindowCorner.BottomRight),
        };

        CameraWindowTarget target = Compute(defaults, CameraWindowMode.Mini, Primary, Right);

        // 150 %: 480×270 px, margin 24.
        Assert.Same(Right, target.Screen);
        Assert.Equal(new PixelBounds(1920 + 2560 - 24 - 480, 1400 - 24 - 270, 480, 270), target.Bounds);
        Assert.Equal(320, target.ClientWidth);
        Assert.Equal(180, target.ClientHeight);
    }

    [Fact]
    public void FullScreenCoversChosenMonitor()
    {
        CameraWindowDefaults defaults = new() { Monitor = CameraMonitorId.FromScreen(Right) };

        CameraWindowTarget target = Compute(defaults, CameraWindowMode.FullScreen, Primary, Right, Left);

        Assert.Same(Right, target.Screen);
        Assert.Equal(Right.Bounds, target.Bounds);
        Assert.Equal(2560 / 1.5, target.ClientWidth, 6);
        Assert.Equal(1440 / 1.5, target.ClientHeight, 6);
    }

    [Fact]
    public void FullScreenFallsBackToPrimaryWhenMonitorIsMissing()
    {
        CameraWindowDefaults defaults = new() { Monitor = new CameraMonitorId("DISPLAY9", 1920, 1080) };

        CameraWindowTarget target = Compute(defaults, CameraWindowMode.FullScreen, Right, Primary);

        Assert.Same(Primary, target.Screen);
        Assert.True(target.PreferredMonitorMissing);
        Assert.Equal(Primary.Bounds, target.Bounds);
    }

    [Fact]
    public void MissingMonitorFallsBackToPrimary()
    {
        CameraWindowDefaults defaults = new() { Monitor = new CameraMonitorId("DISPLAY9", 2560, 1440) };

        CameraWindowTarget target = Compute(defaults, CameraWindowMode.Normal, Right, Primary, Left);

        Assert.Same(Primary, target.Screen);
        Assert.True(target.PreferredMonitorMissing);
        Assert.Equal(new PixelBounds(480, 250, 960, 540), target.Bounds);
    }

    [Fact]
    public void SameNameWithOtherResolutionIsUsed()
    {
        // Q4 (2026-10-19): a monitor whose resolution changed (or that was rotated) is still the chosen one.
        CameraScreenSelection selection = CameraWindowDefaultPlacement.SelectScreen(new CameraMonitorId("DISPLAY2", 1440, 2560), [Primary, Right])!;

        Assert.Same(Right, selection.Screen);
        Assert.False(selection.PreferredMonitorMissing);
    }

    [Fact]
    public void SameNameWithOtherResolutionIsPlacedOnThatMonitor()
    {
        CameraWindowDefaults defaults = new()
        {
            Monitor = new CameraMonitorId("DISPLAY2", 1920, 1080),
            Normal = new CameraWindowLayout(new CameraWindowSize(960, 540), CameraWindowCorner.TopLeft),
        };

        CameraWindowTarget target = Compute(defaults, CameraWindowMode.Normal, Primary, Right);

        Assert.Same(Right, target.Screen);
        Assert.False(target.PreferredMonitorMissing);
        Assert.Equal(new PixelBounds(1944, 24, 1440, 810), target.Bounds);
    }

    [Fact]
    public void SameNameMonitorsPreferExactResolution()
    {
        CameraScreen twinSmall = new("DELL", new PixelBounds(1920, 0, 1920, 1080), new PixelBounds(1920, 0, 1920, 1040), 1.0, IsPrimary: false);
        CameraScreen twinLarge = new("DELL", new PixelBounds(3840, 0, 2560, 1440), new PixelBounds(3840, 0, 2560, 1400), 1.0, IsPrimary: false);

        CameraScreenSelection selection = CameraWindowDefaultPlacement.SelectScreen(new CameraMonitorId("DELL", 2560, 1440), [Primary, twinSmall, twinLarge])!;

        Assert.Same(twinLarge, selection.Screen);
        Assert.False(selection.PreferredMonitorMissing);
    }

    [Fact]
    public void SameNameMonitorsWithoutExactResolutionUseTheFirstByOrder()
    {
        CameraScreen twinSmall = new("DELL", new PixelBounds(1920, 0, 1920, 1080), new PixelBounds(1920, 0, 1920, 1040), 1.0, IsPrimary: false);
        CameraScreen twinLarge = new("DELL", new PixelBounds(3840, 0, 2560, 1440), new PixelBounds(3840, 0, 2560, 1400), 1.0, IsPrimary: false);

        CameraScreenSelection selection = CameraWindowDefaultPlacement.SelectScreen(new CameraMonitorId("DELL", 3840, 2160), [Primary, twinLarge, twinSmall])!;

        Assert.Same(twinLarge, selection.Screen);
        Assert.False(selection.PreferredMonitorMissing);
    }

    [Fact]
    public void ConnectedMeansAScreenWithTheSameName()
    {
        Assert.True(CameraWindowDefaultPlacement.IsConnected(new CameraMonitorId("DISPLAY2", 2560, 1440), [Primary, Right]));
        Assert.True(CameraWindowDefaultPlacement.IsConnected(new CameraMonitorId("DISPLAY2", 1920, 1080), [Primary, Right]));
        Assert.False(CameraWindowDefaultPlacement.IsConnected(new CameraMonitorId("display2", 2560, 1440), [Primary, Right]));
        Assert.False(CameraWindowDefaultPlacement.IsConnected(new CameraMonitorId("DISPLAY7", 2560, 1440), [Primary, Right]));
        Assert.False(CameraWindowDefaultPlacement.IsConnected(new CameraMonitorId("DISPLAY2", 2560, 1440), []));
    }

    [Fact]
    public void MonitorIdMatchesNameAndResolutionSeparately()
    {
        CameraMonitorId rotated = new("DISPLAY2", 1440, 2560);

        Assert.True(rotated.MatchesName(Right));
        Assert.False(rotated.Matches(Right));
        Assert.True(CameraMonitorId.FromScreen(Right).Matches(Right));
        Assert.False(rotated.MatchesName(Primary));
    }

    [Fact]
    public void SameResolutionWithOtherNameIsMissing()
    {
        CameraScreenSelection selection = CameraWindowDefaultPlacement.SelectScreen(new CameraMonitorId("DISPLAY7", 2560, 1440), [Primary, Right])!;

        Assert.Same(Primary, selection.Screen);
        Assert.True(selection.PreferredMonitorMissing);
    }

    [Fact]
    public void MonitorNameIsCaseSensitive()
    {
        CameraScreenSelection selection = CameraWindowDefaultPlacement.SelectScreen(new CameraMonitorId("display2", 2560, 1440), [Primary, Right])!;

        Assert.True(selection.PreferredMonitorMissing);
    }

    [Fact]
    public void MatchingMonitorIsChosen()
    {
        CameraScreenSelection selection = CameraWindowDefaultPlacement.SelectScreen(new CameraMonitorId("DISPLAY3", 1280, 1024), [Primary, Right, Left])!;

        Assert.Same(Left, selection.Screen);
        Assert.False(selection.PreferredMonitorMissing);
    }

    [Fact]
    public void NoMonitorMeansPrimaryWithoutMissingFlag()
    {
        CameraScreenSelection selection = CameraWindowDefaultPlacement.SelectScreen(null, [Right, Left, Primary])!;

        Assert.Same(Primary, selection.Screen);
        Assert.False(selection.PreferredMonitorMissing);
    }

    [Fact]
    public void WithoutPrimaryFlagTheFirstScreenIsUsed()
    {
        CameraScreenSelection selection = CameraWindowDefaultPlacement.SelectScreen(new CameraMonitorId("DISPLAY9", 1, 1), [Right, Left])!;

        Assert.Same(Right, selection.Screen);
        Assert.True(selection.PreferredMonitorMissing);
    }

    [Fact]
    public void SingleScreenIsUsed()
    {
        CameraWindowTarget target = Compute(new CameraWindowDefaults { Monitor = new CameraMonitorId("DISPLAY2", 2560, 1440) }, CameraWindowMode.Normal, Primary);

        Assert.Same(Primary, target.Screen);
        Assert.True(target.PreferredMonitorMissing);
    }

    [Fact]
    public void NoScreensGiveNoTarget()
    {
        Assert.Null(CameraWindowDefaultPlacement.SelectScreen(null, []));
        Assert.Null(CameraWindowDefaultPlacement.Compute(CameraWindowDefaults.Default, CameraWindowMode.Normal, []));
    }

    [Theory]
    [InlineData(CameraWindowCorner.TopLeft)]
    [InlineData(CameraWindowCorner.BottomRight)]
    [InlineData(CameraWindowCorner.Center)]
    public void WindowLargerThanWorkAreaIsClampedToIt(CameraWindowCorner corner)
    {
        CameraWindowDefaults defaults = new() { Normal = new CameraWindowLayout(new CameraWindowSize(1920, 1080), corner) };

        CameraWindowTarget target = Compute(defaults, CameraWindowMode.Normal, Primary);

        Assert.Equal(Primary.WorkingArea, target.Bounds);
        Assert.Equal(1920, target.ClientWidth);
        Assert.Equal(1040, target.ClientHeight);
    }

    [Fact]
    public void ClampingConvertsBackToDipWithMonitorScaling()
    {
        CameraWindowDefaults defaults = new()
        {
            Monitor = CameraMonitorId.FromScreen(Right),
            Normal = new CameraWindowLayout(new CameraWindowSize(1920, 1080), CameraWindowCorner.TopRight),
        };

        CameraWindowTarget target = Compute(defaults, CameraWindowMode.Normal, Primary, Right);

        // 2880×1620 px does not fit 2560×1400 px.
        Assert.Equal(Right.WorkingArea, target.Bounds);
        Assert.Equal(2560 / 1.5, target.ClientWidth, 6);
        Assert.Equal(1400 / 1.5, target.ClientHeight, 6);
    }

    [Fact]
    public void WindowThatFitsOnlyWithoutMarginStaysInsideWorkArea()
    {
        // 16 px margin + 1910 px is wider than 1920 px: the window moves left so that it stays inside the work area.
        CameraWindowDefaults defaults = new() { Normal = new CameraWindowLayout(new CameraWindowSize(1910, 540), CameraWindowCorner.TopLeft) };

        CameraWindowTarget target = Compute(defaults, CameraWindowMode.Normal, Primary);

        Assert.Equal(new PixelBounds(10, 16, 1910, 540), target.Bounds);
    }

    [Fact]
    public void FrameIsAddedAroundTheClientArea()
    {
        CameraWindowDefaults defaults = new() { Normal = new CameraWindowLayout(new CameraWindowSize(960, 540), CameraWindowCorner.BottomRight) };

        CameraWindowTarget target = CameraWindowDefaultPlacement.Compute(defaults, CameraWindowMode.Normal, [Primary], frameWidth: 16, frameHeight: 39)!;

        Assert.Equal(new PixelBounds(1920 - 16 - 976, 1040 - 16 - 579, 976, 579), target.Bounds);
        Assert.Equal(960, target.ClientWidth);
        Assert.Equal(540, target.ClientHeight);
    }

    [Fact]
    public void FrameIsScaledAndCountedWhenClamping()
    {
        CameraWindowDefaults defaults = new()
        {
            Monitor = CameraMonitorId.FromScreen(Right),
            Normal = new CameraWindowLayout(new CameraWindowSize(1920, 1080), CameraWindowCorner.Center),
        };

        CameraWindowTarget target = CameraWindowDefaultPlacement.Compute(defaults, CameraWindowMode.Normal, [Primary, Right], frameWidth: 0, frameHeight: 32)!;

        // Frame 48 px at 150 %; the client area gets the rest of the 1400 px.
        Assert.Equal(Right.WorkingArea, target.Bounds);
        Assert.Equal((1400 - 48) / 1.5, target.ClientHeight, 6);
    }

    [Fact]
    public void FrameIsIgnoredInFullScreen()
    {
        CameraWindowTarget target = CameraWindowDefaultPlacement.Compute(CameraWindowDefaults.Default, CameraWindowMode.FullScreen, [Primary], frameWidth: 16, frameHeight: 39)!;

        Assert.Equal(Primary.Bounds, target.Bounds);
        Assert.Equal(1920, target.ClientWidth);
        Assert.Equal(1080, target.ClientHeight);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, double.NaN)]
    [InlineData(double.PositiveInfinity, 0)]
    public void InvalidFrameIsRejected(double width, double height) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CameraWindowDefaultPlacement.Compute(CameraWindowDefaults.Default, CameraWindowMode.Normal, [Primary], width, height));

    [Fact]
    public void EmptyWorkAreaFallsBackToScreenBounds()
    {
        CameraScreen screen = new("DISPLAY1", new PixelBounds(0, 0, 1920, 1080), default, 1.0, IsPrimary: true);

        CameraWindowTarget target = Compute(CameraWindowDefaults.Default, CameraWindowMode.Normal, screen);

        Assert.Equal(new PixelBounds(480, 270, 960, 540), target.Bounds);
    }

    [Fact]
    public void PlaceUsesTheGivenScreen()
    {
        CameraWindowLayout layout = new(new CameraWindowSize(320, 180), CameraWindowCorner.TopLeft);

        CameraWindowTarget target = CameraWindowDefaultPlacement.Place(layout, Left);

        Assert.Same(Left, target.Screen);
        Assert.False(target.PreferredMonitorMissing);
        Assert.Equal(new PixelBounds(-1260, -180, 400, 225), target.Bounds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidScalingIsRejected(double scaling) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraScreen("DISPLAY1", new PixelBounds(0, 0, 1920, 1080), new PixelBounds(0, 0, 1920, 1040), scaling, IsPrimary: true));

    [Fact]
    public void EmptyScreenBoundsAreRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraScreen("DISPLAY1", new PixelBounds(0, 0, 0, 1080), default, 1.0, IsPrimary: true));

    [Fact]
    public void NullScreenNameIsRejected() =>
        Assert.Throws<ArgumentNullException>(() => new CameraScreen(null!, Primary.Bounds, Primary.WorkingArea, 1.0, IsPrimary: true));

    [Fact]
    public void UnknownModeIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CameraWindowDefaultPlacement.Compute(CameraWindowDefaults.Default, (CameraWindowMode)42, [Primary]));

    [Fact]
    public void PixelBoundsExposeRightAndBottom()
    {
        PixelBounds bounds = new(-1280, -200, 1280, 1024);

        Assert.Equal(0, bounds.Right);
        Assert.Equal(824, bounds.Bottom);
        Assert.False(bounds.IsEmpty);
        Assert.True(default(PixelBounds).IsEmpty);
    }

    // ---- Device path (T-035 Q8): two identical monitors told apart ----

    private const string PathA = @"\\?\DISPLAY#DEL415A#5&224edec0&0&UID160000#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private const string PathB = @"\\?\DISPLAY#DEL415A#5&224edec0&0&UID160002#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private const string PathC = @"\\?\DISPLAY#DEL415A#5&224edec0&0&UID160003#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private const string PathMoved = @"\\?\DISPLAY#DEL415A#5&224edec0&0&UID160004#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    private static readonly CameraScreen DellA = new("DELL U2719D", new PixelBounds(0, 0, 2560, 1440), new PixelBounds(0, 0, 2560, 1392), 1.0, IsPrimary: true, DevicePath: PathA);
    private static readonly CameraScreen DellB = new("DELL U2719D", new PixelBounds(2560, 0, 2560, 1440), new PixelBounds(2560, 0, 2560, 1392), 1.0, IsPrimary: false, DevicePath: PathB);
    private static readonly CameraScreen OtherPrimary = new("Other", new PixelBounds(-1920, 0, 1920, 1080), new PixelBounds(-1920, 0, 1920, 1040), 1.0, IsPrimary: true, DevicePath: @"\\?\DISPLAY#OTH0001#1&0&UID1#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}");

    [Fact]
    public void IdenticalMonitorsAreToldApartByTheirDevicePath()
    {
        CameraMonitorId second = CameraMonitorId.FromScreen(DellB);

        Assert.Equal(PathB, second.DevicePath);
        Assert.NotEqual(CameraMonitorId.FromScreen(DellA), second);
        Assert.Same(DellB, CameraWindowDefaultPlacement.SelectScreen(second, [DellA, DellB])!.Screen);
        Assert.Same(DellA, CameraWindowDefaultPlacement.SelectScreen(CameraMonitorId.FromScreen(DellA), [DellA, DellB])!.Screen);
    }

    [Fact]
    public void DevicePathWinsOverNameAndResolution()
    {
        // The monitor with the path was rotated, its twin still has the stored resolution.
        CameraScreen rotatedB = new("DELL U2719D", new PixelBounds(2560, 0, 1440, 2560), new PixelBounds(2560, 0, 1440, 2512), 1.0, IsPrimary: false, DevicePath: PathB);

        CameraScreenSelection selection = CameraWindowDefaultPlacement.SelectScreen(new CameraMonitorId("DELL U2719D", 2560, 1440, PathB), [DellA, rotatedB])!;

        Assert.Same(rotatedB, selection.Screen);
        Assert.False(selection.PreferredMonitorMissing);
    }

    [Fact]
    public void MonitorOnAnotherConnectorIsFoundByNameAndResolution()
    {
        // T-071 Q5 (2026-10-23, option a): the stored path is gone, exactly one connected monitor has the same name and
        // resolution and it is no known twin: the same monitor plugged into another connector.
        CameraScreen moved = new("DELL U2719D", new PixelBounds(2560, 0, 1920, 1080), new PixelBounds(2560, 0, 1920, 1040), 1.0, IsPrimary: false, DevicePath: PathMoved);
        CameraMonitorId stored = new("DELL U2719D", 1920, 1080, PathB);

        CameraScreenSelection selection = CameraWindowDefaultPlacement.SelectScreen(stored, [DellA, moved])!;

        Assert.Same(moved, selection.Screen);
        Assert.False(selection.PreferredMonitorMissing);
        Assert.True(CameraWindowDefaultPlacement.IsConnected(stored, [DellA, moved]));
    }

    [Fact]
    public void SwitchedOffMonitorIsNotReplacedByItsKnownTwin()
    {
        // T-071 Q4 + Q5: two identical monitors, the chosen one is switched off; its twin was known when it was chosen.
        CameraMonitorId stored = CameraMonitorId.FromScreen(DellB, [DellA, DellB]);
        CameraScreen twin = DellA with { IsPrimary = false };

        CameraScreenSelection selection = CameraWindowDefaultPlacement.SelectScreen(stored, [OtherPrimary, twin])!;

        Assert.Equal([PathA], stored.KnownTwinPaths);
        Assert.Same(OtherPrimary, selection.Screen);
        Assert.True(selection.PreferredMonitorMissing);
        Assert.False(CameraWindowDefaultPlacement.IsConnected(stored, [OtherPrimary, twin]));
        Assert.True(CameraWindowDefaultPlacement.IsConnected(stored, [OtherPrimary, twin, DellB]));
    }

    [Fact]
    public void KnownTwinIsComparedWithoutCase()
    {
        CameraMonitorId stored = new("DELL U2719D", 2560, 1440, PathB, [PathA.ToUpperInvariant()]);

        Assert.True(stored.IsKnownTwin(DellA));
        Assert.False(stored.IsKnownTwin(DellB));
        Assert.False(stored.IsKnownTwin(DellA with { DevicePath = null }));
        Assert.False(CameraWindowDefaultPlacement.IsConnected(stored, [DellA]));
    }

    [Fact]
    public void TwinPluggedInAfterTheChoiceIsNotKnown()
    {
        // Documented limitation: a twin connected only after the choice is unknown, so with the chosen monitor switched
        // off it is taken for the chosen monitor on another connector.
        CameraMonitorId stored = CameraMonitorId.FromScreen(DellB, [DellB]);
        CameraScreen laterTwin = DellA with { IsPrimary = false };

        CameraScreenSelection selection = CameraWindowDefaultPlacement.SelectScreen(stored, [OtherPrimary, laterTwin])!;

        Assert.Empty(stored.KnownTwinPaths);
        Assert.Same(laterTwin, selection.Screen);
        Assert.False(selection.PreferredMonitorMissing);
    }

    [Fact]
    public void TwoCandidatesWithTheSameNameAndResolutionAreNotConnected()
    {
        CameraScreen moved = DellB with { DevicePath = PathMoved };
        CameraMonitorId stored = new("DELL U2719D", 2560, 1440, PathC);

        CameraScreenSelection selection = CameraWindowDefaultPlacement.SelectScreen(stored, [DellA, moved])!;

        Assert.Same(DellA, selection.Screen);
        Assert.True(selection.PreferredMonitorMissing);
        Assert.False(CameraWindowDefaultPlacement.IsConnected(stored, [DellA, moved]));
    }

    [Fact]
    public void KnownTwinCountsAsACandidate()
    {
        // The known twin and an unknown monitor with the same name and resolution: two candidates, so neither is used.
        CameraScreen moved = DellB with { DevicePath = PathMoved };
        CameraMonitorId stored = new("DELL U2719D", 2560, 1440, PathC, [PathA]);

        Assert.False(CameraWindowDefaultPlacement.IsConnected(stored, [DellA, moved]));
        Assert.True(CameraWindowDefaultPlacement.IsConnected(stored, [moved]));
    }

    [Fact]
    public void CandidateWithOtherResolutionIsNotUsed()
    {
        // Unlike older settings without a path, the fallback needs the same name and resolution.
        CameraScreen rotated = new("DELL U2719D", new PixelBounds(2560, 0, 1440, 2560), new PixelBounds(2560, 0, 1440, 2512), 1.0, IsPrimary: false, DevicePath: PathMoved);
        CameraMonitorId stored = new("DELL U2719D", 2560, 1440, PathB);

        Assert.False(CameraWindowDefaultPlacement.IsConnected(stored, [OtherPrimary, rotated]));
    }

    [Fact]
    public void CandidateWithOtherNameIsNotUsed()
    {
        CameraMonitorId stored = new("DELL U2719D", 1920, 1080, PathB);

        Assert.False(CameraWindowDefaultPlacement.IsConnected(stored, [OtherPrimary]));
    }

    [Fact]
    public void SavedDevicePathFallsBackToTheOnlyMatchingScreenWithoutPaths()
    {
        // Screens without device paths (a settings file from Windows on another platform): the fallback still applies.
        CameraMonitorId stored = CameraMonitorId.FromScreen(DellB);
        CameraScreen noPath = DellB with { DevicePath = null };

        Assert.True(CameraWindowDefaultPlacement.IsConnected(stored, [noPath]));
        Assert.False(CameraWindowDefaultPlacement.IsConnected(stored, [noPath, DellA with { DevicePath = null }]));
    }

    // ---- Relocate (user decision 2026-10-23): the saved path follows the monitor to its new connector ----

    [Fact]
    public void RelocateGivesTheNewPathWhenTheFallbackMatches()
    {
        CameraScreen moved = DellB with { DevicePath = PathMoved };
        CameraMonitorId stored = new("DELL U2719D", 2560, 1440, PathB, [PathC]);

        CameraMonitorId? relocated = CameraWindowDefaultPlacement.Relocate(stored, [OtherPrimary, moved]);

        Assert.Equal(new CameraMonitorId("DELL U2719D", 2560, 1440, PathMoved, [PathC]), relocated);
    }

    [Fact]
    public void RelocateKeepsAMonitorFoundByItsPath()
    {
        CameraMonitorId stored = CameraMonitorId.FromScreen(DellB, [DellA, DellB]);

        Assert.Null(CameraWindowDefaultPlacement.Relocate(stored, [DellA, DellB]));
        Assert.Null(CameraWindowDefaultPlacement.Relocate(stored, [DellA, DellB with { DevicePath = PathB.ToUpperInvariant() }]));
    }

    [Fact]
    public void RelocateDoesNothingWithoutAnUnambiguousCandidate()
    {
        CameraMonitorId stored = CameraMonitorId.FromScreen(DellB, [DellA, DellB]);

        // Switched off, only its known twin is left.
        Assert.Null(CameraWindowDefaultPlacement.Relocate(stored, [OtherPrimary, DellA]));
        // Two candidates.
        Assert.Null(CameraWindowDefaultPlacement.Relocate(new CameraMonitorId("DELL U2719D", 2560, 1440, PathC), [DellA, DellB]));
        // Nothing similar is connected.
        Assert.Null(CameraWindowDefaultPlacement.Relocate(stored, [OtherPrimary]));
        Assert.Null(CameraWindowDefaultPlacement.Relocate(stored, []));
    }

    [Fact]
    public void RelocateNeedsAStorablePathOfTheCandidate()
    {
        CameraMonitorId stored = new("DELL U2719D", 2560, 1440, PathC);

        Assert.Null(CameraWindowDefaultPlacement.Relocate(stored, [DellB with { DevicePath = null }]));
        Assert.Null(CameraWindowDefaultPlacement.Relocate(stored, [DellB with { DevicePath = "bad\u0001path" }]));
    }

    [Fact]
    public void RelocateStoresThePathOfOlderSettingsOnlyWhenTheNameIsUnique()
    {
        CameraMonitorId old = new("DELL U2719D", 2560, 1440);

        // The only monitor with that name, in the saved resolution.
        Assert.Equal(new CameraMonitorId("DELL U2719D", 2560, 1440, PathB), CameraWindowDefaultPlacement.Relocate(old, [OtherPrimary, DellB]));
        // Two monitors with that name (even in other resolutions): which one was meant is unknown.
        CameraScreen rotatedTwin = new("DELL U2719D", new PixelBounds(5120, 0, 1440, 2560), new PixelBounds(5120, 0, 1440, 2512), 1.0, IsPrimary: false, DevicePath: PathA);
        Assert.Null(CameraWindowDefaultPlacement.Relocate(old, [DellB, rotatedTwin]));
        // Unique name in another resolution: matched by the name rule, but not unambiguous enough to store.
        Assert.Null(CameraWindowDefaultPlacement.Relocate(old, [OtherPrimary, rotatedTwin]));
        // No path to store.
        Assert.Null(CameraWindowDefaultPlacement.Relocate(old, [DellB with { DevicePath = null }]));
    }

    // ---- Known twins (T-071 Q5) ----

    [Fact]
    public void FromScreenRemembersTheOtherConnectedMonitorsWithTheSameName()
    {
        CameraScreen rotatedTwin = new("DELL U2719D", new PixelBounds(5120, 0, 1440, 2560), new PixelBounds(5120, 0, 1440, 2512), 1.0, IsPrimary: false, DevicePath: PathC);

        CameraMonitorId id = CameraMonitorId.FromScreen(DellB, [OtherPrimary, DellA, DellB, rotatedTwin]);

        // Every other monitor with the same name (any resolution), in list order; the chosen one and other names not.
        Assert.Equal([PathA, PathC], id.KnownTwinPaths);
        Assert.Equal(PathB, id.DevicePath);
    }

    [Fact]
    public void FromScreenLeavesOutTwinsWithoutAUsablePath()
    {
        CameraScreen sameAsChosen = DellA with { DevicePath = PathB.ToLowerInvariant() };
        CameraScreen duplicate = new("DELL U2719D", new PixelBounds(5120, 0, 2560, 1440), new PixelBounds(5120, 0, 2560, 1392), 1.0, IsPrimary: false, DevicePath: PathA);

        CameraMonitorId id = CameraMonitorId.FromScreen(DellB, [DellA with { DevicePath = null }, DellA with { DevicePath = "bad\u0001path" }, sameAsChosen, DellA, duplicate, DellB]);

        Assert.Equal([PathA], id.KnownTwinPaths);
    }

    [Fact]
    public void FromScreenWithoutAPathRemembersNoTwins()
    {
        CameraMonitorId id = CameraMonitorId.FromScreen(DellB with { DevicePath = null }, [DellA, DellB with { DevicePath = null }]);

        Assert.Null(id.DevicePath);
        Assert.Empty(id.KnownTwinPaths);
    }

    [Fact]
    public void KnownTwinsAreCapped()
    {
        CameraScreen[] screens = [DellB, .. Enumerable.Range(0, CameraMonitorId.MaxKnownTwinPaths + 3).Select(i => DellA with { DevicePath = $"twin-{i}" })];

        CameraMonitorId id = CameraMonitorId.FromScreen(DellB, screens);

        Assert.Equal(CameraMonitorId.MaxKnownTwinPaths, id.KnownTwinPaths.Count);
        Assert.Equal("twin-0", id.KnownTwinPaths[0]);
    }

    [Fact]
    public void KnownTwinsAreValidated()
    {
        Assert.Throws<ArgumentException>(() => new CameraMonitorId("DELL U2719D", 2560, 1440, PathB, ["bad\u0001path"]));
        Assert.Throws<ArgumentException>(() => new CameraMonitorId("DELL U2719D", 2560, 1440, PathB, [PathA, PathA.ToLowerInvariant()]));
        Assert.Throws<ArgumentException>(() => new CameraMonitorId("DELL U2719D", 2560, 1440, PathB, [PathB.ToUpperInvariant()]));
        Assert.Throws<ArgumentException>(() => new CameraMonitorId("DELL U2719D", 2560, 1440, devicePath: null, [PathA]));
        Assert.Throws<ArgumentException>(() => new CameraMonitorId("DELL U2719D", 2560, 1440, PathB, [.. Enumerable.Range(0, CameraMonitorId.MaxKnownTwinPaths + 1).Select(i => $"twin-{i}")]));
        Assert.Throws<ArgumentNullException>(() => new CameraMonitorId("DELL U2719D", 2560, 1440, PathB, [null!]));
        Assert.Empty(new CameraMonitorId("DELL U2719D", 2560, 1440, PathB).KnownTwinPaths);
        Assert.Empty(new CameraMonitorId("DELL U2719D", 2560, 1440, devicePath: null, []).KnownTwinPaths);
    }

    [Fact]
    public void KnownTwinsAreCopiedAndPartOfTheValue()
    {
        List<string> twins = [PathA];
        CameraMonitorId id = new("DELL U2719D", 2560, 1440, PathB, twins);
        twins.Add(PathC);

        Assert.Equal([PathA], id.KnownTwinPaths);
        Assert.Equal(new CameraMonitorId("DELL U2719D", 2560, 1440, PathB, [PathA]), id);
        Assert.Equal(new CameraMonitorId("DELL U2719D", 2560, 1440, PathB, [PathA]).GetHashCode(), id.GetHashCode());
        Assert.NotEqual(new CameraMonitorId("DELL U2719D", 2560, 1440, PathB), id);
        Assert.NotEqual(new CameraMonitorId("DELL U2719D", 2560, 1440, PathB, [PathC]), id);
        Assert.True(id.HasSameIdentity(new CameraMonitorId("DELL U2719D", 2560, 1440, PathB)));
        Assert.False(id.HasSameIdentity(new CameraMonitorId("DELL U2719D", 2560, 1440, PathC, [PathA])));
        Assert.False(id.HasSameIdentity(null));
    }

    [Fact]
    public void TwinsAreRefreshedWhenTheMonitorIsFoundByItsPath()
    {
        // User decision 2026-10-23: the stored twins plus the connected monitors with the same name, stored ones first.
        CameraScreen rotatedTwin = new("DELL U2719D", new PixelBounds(5120, 0, 1440, 2560), new PixelBounds(5120, 0, 1440, 2512), 1.0, IsPrimary: false, DevicePath: PathC);
        CameraMonitorId stored = new("DELL U2719D", 2560, 1440, PathB, ["old-twin", PathA.ToLowerInvariant()]);

        CameraMonitorId refreshed = stored.WithTwinsFrom([OtherPrimary, DellA, DellB, rotatedTwin]);

        Assert.Equal(["old-twin", PathA.ToLowerInvariant(), PathC], refreshed.KnownTwinPaths);
        Assert.Equal(PathB, refreshed.DevicePath);
    }

    [Fact]
    public void TwinsAreNotRefreshedWithoutTheMonitorConnectedByItsPath()
    {
        CameraMonitorId stored = new("DELL U2719D", 2560, 1440, PathB, ["old-twin"]);
        CameraMonitorId old = new("DELL U2719D", 2560, 1440);

        // Not connected, connected only through the fallback (another connector), older settings without a path.
        Assert.Same(stored, stored.WithTwinsFrom([OtherPrimary, DellA]));
        Assert.Same(stored, stored.WithTwinsFrom([DellA with { DevicePath = PathMoved }]));
        Assert.Same(old, old.WithTwinsFrom([DellA, DellB]));
        // Nothing new.
        CameraMonitorId known = new("DELL U2719D", 2560, 1440, PathB, [PathA]);
        Assert.Same(known, known.WithTwinsFrom([DellA, DellB, DellA with { DevicePath = PathA.ToUpperInvariant() }]));
    }

    [Fact]
    public void RefreshedTwinsAreCappedAndKeepTheStoredOnes()
    {
        string[] stored = [.. Enumerable.Range(0, CameraMonitorId.MaxKnownTwinPaths - 1).Select(static i => $"stored-{i}")];
        CameraMonitorId monitor = new("DELL U2719D", 2560, 1440, PathB, stored);

        CameraMonitorId refreshed = monitor.WithTwinsFrom([DellB, DellA, DellA with { DevicePath = PathC }, DellA with { DevicePath = "bad\u0001path" }]);

        Assert.Equal([.. stored, PathA], refreshed.KnownTwinPaths);
    }

    [Fact]
    public void WithDevicePathKeepsTheRest()
    {
        CameraMonitorId id = new("DELL U2719D", 2560, 1440, PathB, [PathA]);

        Assert.Equal(new CameraMonitorId("DELL U2719D", 2560, 1440, PathMoved, [PathA]), id.WithDevicePath(PathMoved));
        Assert.Throws<ArgumentException>(() => id.WithDevicePath(PathA));
        Assert.Throws<ArgumentException>(() => id.WithDevicePath(" "));
    }

    [Fact]
    public void DevicePathAloneMeansConnected()
    {
        // Renamed by a driver update, same connector.
        CameraScreen renamed = new("DELL U2719D (DisplayPort)", DellB.Bounds, DellB.WorkingArea, 1.0, IsPrimary: false, DevicePath: PathB);
        CameraMonitorId stored = CameraMonitorId.FromScreen(DellB);

        Assert.True(CameraWindowDefaultPlacement.IsConnected(stored, [renamed]));
        Assert.Same(renamed, CameraWindowDefaultPlacement.SelectScreen(stored, [DellA, renamed])!.Screen);
    }

    [Fact]
    public void OldMonitorWithoutDevicePathStillMatchesByNameAndResolution()
    {
        CameraMonitorId old = new("DELL U2719D", 2560, 1440);

        Assert.False(old.MatchesDevicePath(DellB));
        Assert.Same(DellA, CameraWindowDefaultPlacement.SelectScreen(old, [DellA, DellB])!.Screen);
        // Older settings without a path: the name alone still means connected (any resolution).
        Assert.True(CameraWindowDefaultPlacement.IsConnected(new CameraMonitorId("DELL U2719D", 1920, 1080), [DellB]));
        Assert.Same(DellB, CameraWindowDefaultPlacement.SelectScreen(new CameraMonitorId("DELL U2719D", 1920, 1080), [DellB])!.Screen);
    }

    [Fact]
    public void ScreenWithoutDevicePathNeverMatchesByPath()
    {
        CameraScreen noPath = DellB with { DevicePath = null };

        Assert.False(new CameraMonitorId("DELL U2719D", 2560, 1440, PathB).MatchesDevicePath(noPath));
        Assert.Null(CameraMonitorId.FromScreen(noPath).DevicePath);
        Assert.True(new CameraMonitorId("X", 1, 1, PathB.ToUpperInvariant()).MatchesDevicePath(DellB));
    }

    [Fact]
    public void UnstorableDevicePathOfAScreenIsLeftOut()
    {
        CameraScreen odd = DellB with { DevicePath = "bad\u0001path" };

        Assert.Null(CameraMonitorId.FromScreen(odd).DevicePath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a\u0007b")]
    public void InvalidDevicePathIsRejected(string path)
    {
        Assert.False(CameraMonitorId.IsValidDevicePath(path));
        Assert.Throws<ArgumentException>(() => new CameraMonitorId("DELL U2719D", 2560, 1440, path));
    }

    [Fact]
    public void DevicePathLengthIsLimited()
    {
        Assert.True(CameraMonitorId.IsValidDevicePath(new string('p', CameraMonitorId.MaxDevicePathLength)));
        Assert.False(CameraMonitorId.IsValidDevicePath(new string('p', CameraMonitorId.MaxDevicePathLength + 1)));
        Assert.False(CameraMonitorId.IsValidDevicePath(null));
    }

    private static CameraScreen[] AllScreens() => [Primary, Right, Left, Above];

    private static CameraWindowTarget Compute(CameraWindowDefaults defaults, CameraWindowMode mode, params CameraScreen[] screens) =>
        CameraWindowDefaultPlacement.Compute(defaults, mode, screens) ?? throw new InvalidOperationException("No target.");
}
