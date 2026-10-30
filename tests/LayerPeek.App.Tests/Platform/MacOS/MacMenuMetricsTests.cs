using LayerPeek.App.Platform.MacOS;

namespace LayerPeek.App.Tests.Platform.MacOS;

/// <summary>
/// T-116: the menu font size and the display scaling for the printer icons of the menu bar menu (read only). The test
/// process does not load AppKit itself, so the values are usually unknown here; in the app Avalonia.Native has loaded
/// AppKit (a Retina Mac with macOS 27.2: 14 pt, 2.0; manual check in T-115).
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("macos")]
public sealed class MacMenuMetricsTests
{
    [Fact]
    public void ReadsNothingOrPlausibleValuesWithoutThrowing()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "AppKit exists only on macOS.");

        if (MacMenuMetrics.MenuFontPointSize() is { } font)
        {
            Assert.InRange(font, 6, 72);
        }

        if (MacMenuMetrics.HighestBackingScaleFactor() is { } scale)
        {
            Assert.InRange(scale, 1, 4);
        }
    }
}
