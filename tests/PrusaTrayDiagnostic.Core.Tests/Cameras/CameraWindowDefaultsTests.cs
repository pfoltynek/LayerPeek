using PrusaTrayDiagnostic.Core.Cameras;

namespace PrusaTrayDiagnostic.Core.Tests.Cameras;

public class CameraWindowDefaultsTests
{
    [Fact]
    public void DefaultsReproduceTodaysBehaviour()
    {
        CameraWindowDefaults defaults = CameraWindowDefaults.Default;

        Assert.True(defaults.RememberLastPlacement);
        Assert.Equal(CameraWindowStartMode.Last, defaults.StartMode);
        Assert.Null(defaults.Monitor);
        Assert.Equal(new CameraWindowSize(960, 540), defaults.Normal.Size);
        Assert.Equal(CameraWindowCorner.Center, defaults.Normal.Corner);
        Assert.Equal(new CameraWindowSize(320, 180), defaults.Mini.Size);
        Assert.Equal(CameraWindowCorner.BottomRight, defaults.Mini.Corner);
        Assert.Equal(CameraWindowDefaults.Default, new CameraWindowDefaults());
    }

    [Fact]
    public void CameraSettingsHaveDefaultWindow()
    {
        Assert.Same(CameraWindowDefaults.Default, CameraSettings.Disabled.Window);
        Assert.Same(CameraWindowDefaults.Default, new CameraSettings(enabled: true, new Uri("rtsp://192.168.0.227/live")).Window);
        Assert.Same(CameraWindowDefaults.Default, CameraSettings.FromInvalidStreamUrl("http://a/live").Window);
    }

    [Fact]
    public void CameraSettingsEqualityIncludesWindow()
    {
        CameraSettings camera = new(enabled: true, new Uri("rtsp://192.168.0.227/live"));
        CameraSettings changed = camera with { Window = new CameraWindowDefaults { RememberLastPlacement = false } };
        CameraSettings same = camera with { Window = new CameraWindowDefaults { RememberLastPlacement = false } };

        Assert.NotEqual(camera, changed);
        Assert.Equal(changed, same);
        Assert.Equal(changed.GetHashCode(), same.GetHashCode());
    }

    [Fact]
    public void CameraSettingsRejectNullWindow() =>
        Assert.Throws<ArgumentNullException>(() => CameraSettings.Disabled with { Window = null! });

    [Fact]
    public void CameraSettingsToStringShowsWindowButHidesInvalidAddress()
    {
        string text = CameraSettings.FromInvalidStreamUrl("http://secret-host/live").ToString();

        Assert.Contains("Window = ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-host", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CameraWindowSize.MinWidth, CameraWindowSize.MinHeight)]
    [InlineData(640, 360)]
    [InlineData(1000, 1000)]
    [InlineData(CameraWindowSize.MaxSize, CameraWindowSize.MaxSize)]
    public void ValidSizeIsAccepted(int width, int height)
    {
        Assert.True(CameraWindowSize.IsValid(width, height));
        CameraWindowSize size = new(width, height);
        Assert.Equal(width, size.Width);
        Assert.Equal(height, size.Height);
    }

    [Theory]
    [InlineData(CameraWindowSize.MinWidth - 1, 540)]
    [InlineData(960, CameraWindowSize.MinHeight - 1)]
    [InlineData(0, 0)]
    [InlineData(-960, 540)]
    [InlineData(CameraWindowSize.MaxSize + 1, 540)]
    [InlineData(960, CameraWindowSize.MaxSize + 1)]
    [InlineData(int.MaxValue, int.MinValue)]
    public void InvalidSizeIsRejected(int width, int height)
    {
        Assert.False(CameraWindowSize.IsValid(width, height));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraWindowSize(width, height));
    }

    [Fact]
    public void SizeLimitsMatchTheCameraWindow()
    {
        // CameraWindow.axaml: MinWidth=160, MinHeight=90; CameraWindowPlacement.MaxSize = 16384.
        Assert.Equal(160, CameraWindowSize.MinWidth);
        Assert.Equal(90, CameraWindowSize.MinHeight);
        Assert.Equal(16384, CameraWindowSize.MaxSize);
    }

    [Fact]
    public void PresetsAreTheDecidedSizes()
    {
        Assert.Equal(
            [new CameraWindowSize(640, 360), new CameraWindowSize(960, 540), new CameraWindowSize(1280, 720), new CameraWindowSize(1920, 1080)],
            CameraWindowSize.Presets);
    }

    [Fact]
    public void SizeHasValueEquality()
    {
        Assert.Equal(new CameraWindowSize(1000, 333), new CameraWindowSize(1000, 333));
        Assert.NotEqual(new CameraWindowSize(1000, 333), new CameraWindowSize(333, 1000));
    }

    [Fact]
    public void LayoutRejectsNullSizeAndUnknownCorner()
    {
        Assert.Throws<ArgumentNullException>(() => new CameraWindowLayout(null!, CameraWindowCorner.Center));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraWindowLayout(new CameraWindowSize(960, 540), (CameraWindowCorner)42));
    }

    [Fact]
    public void DefaultsRejectInvalidValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraWindowDefaults { StartMode = (CameraWindowStartMode)42 });
        Assert.Throws<ArgumentNullException>(() => new CameraWindowDefaults { Normal = null! });
        Assert.Throws<ArgumentNullException>(() => new CameraWindowDefaults { Mini = null! });
    }

    [Fact]
    public void DefaultsHaveValueEquality()
    {
        CameraWindowDefaults a = Customized();
        CameraWindowDefaults b = Customized();

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, a with { Monitor = null });
    }

    [Theory]
    [InlineData("DISPLAY1", 1920, 1080)]
    [InlineData(@"\\.\DISPLAY2", 2560, 1440)]
    [InlineData("Dell U2720Q", CameraMonitorId.MaxResolution, 1)]
    public void ValidMonitorIdIsAccepted(string name, int width, int height)
    {
        Assert.True(CameraMonitorId.IsValid(name, width, height));
        CameraMonitorId monitor = new(name, width, height);
        Assert.Equal(name, monitor.DisplayName);
        Assert.Equal(width, monitor.Width);
        Assert.Equal(height, monitor.Height);
    }

    [Theory]
    [InlineData("", 1920, 1080)]
    [InlineData("   ", 1920, 1080)]
    [InlineData("DISPLAY\n1", 1920, 1080)]
    [InlineData("DISPLAY1", 0, 1080)]
    [InlineData("DISPLAY1", 1920, -1)]
    [InlineData("DISPLAY1", CameraMonitorId.MaxResolution + 1, 1080)]
    public void InvalidMonitorIdIsRejected(string name, int width, int height)
    {
        Assert.False(CameraMonitorId.IsValid(name, width, height));
        Assert.ThrowsAny<ArgumentException>(() => new CameraMonitorId(name, width, height));
    }

    [Fact]
    public void TooLongMonitorNameIsRejected()
    {
        string name = new('x', CameraMonitorId.MaxDisplayNameLength + 1);

        Assert.False(CameraMonitorId.IsValid(name, 1920, 1080));
        Assert.True(CameraMonitorId.IsValid(name[..^1], 1920, 1080));
    }

    [Fact]
    public void NullMonitorNameIsRejected()
    {
        Assert.False(CameraMonitorId.IsValid(null, 1920, 1080));
        Assert.Throws<ArgumentNullException>(() => new CameraMonitorId(null!, 1920, 1080));
    }

    [Fact]
    public void MonitorIdIsCreatedFromScreen()
    {
        CameraScreen screen = new("DISPLAY2", new PixelBounds(-2560, 0, 2560, 1440), new PixelBounds(-2560, 0, 2560, 1400), 1.5, IsPrimary: false);

        Assert.Equal(new CameraMonitorId("DISPLAY2", 2560, 1440), CameraMonitorId.FromScreen(screen));
    }

    [Fact]
    public void UnchangedDefaultsKeepRememberedState()
    {
        Assert.False(CameraWindowDefaults.RequiresForgettingRememberedState(Customized(), Customized()));
        Assert.False(CameraWindowDefaults.RequiresForgettingRememberedState(CameraWindowDefaults.Default, new CameraWindowDefaults()));
    }

    [Fact]
    public void RememberToggleAloneKeepsRememberedState()
    {
        CameraWindowDefaults saved = Customized();

        Assert.False(CameraWindowDefaults.RequiresForgettingRememberedState(saved, saved with { RememberLastPlacement = !saved.RememberLastPlacement }));
    }

    public static TheoryData<string> ChangedValues => ["StartMode", "Monitor", "NoMonitor", "NormalSize", "NormalCorner", "MiniSize", "MiniCorner"];

    [Theory]
    [MemberData(nameof(ChangedValues))]
    public void ChangedDefaultsForgetRememberedState(string value)
    {
        CameraWindowDefaults saved = Customized();
        CameraWindowDefaults updated = value switch
        {
            "StartMode" => saved with { StartMode = CameraWindowStartMode.FullScreen },
            "Monitor" => saved with { Monitor = new CameraMonitorId("DISPLAY2", 2560, 1440) },
            "NoMonitor" => saved with { Monitor = null },
            "NormalSize" => saved with { Normal = saved.Normal with { Size = new CameraWindowSize(1920, 1080) } },
            "NormalCorner" => saved with { Normal = saved.Normal with { Corner = CameraWindowCorner.TopRight } },
            "MiniSize" => saved with { Mini = saved.Mini with { Size = new CameraWindowSize(400, 225) } },
            "MiniCorner" => saved with { Mini = saved.Mini with { Corner = CameraWindowCorner.Center } },
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };

        Assert.True(CameraWindowDefaults.RequiresForgettingRememberedState(saved, updated));
    }

    private static CameraWindowDefaults Customized() => new()
    {
        RememberLastPlacement = false,
        StartMode = CameraWindowStartMode.Mini,
        Monitor = new CameraMonitorId("DISPLAY1", 1920, 1080),
        Normal = new CameraWindowLayout(new CameraWindowSize(1280, 720), CameraWindowCorner.TopLeft),
        Mini = new CameraWindowLayout(new CameraWindowSize(480, 270), CameraWindowCorner.BottomLeft),
    };
}
