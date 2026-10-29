using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using LayerPeek.App.Platform.MacOS;

namespace LayerPeek.App.Tests.Platform.MacOS;

/// <summary>
/// T-038: the persistent display UUID that identifies a monitor of the camera window on macOS. The ColorSync calls only
/// read; the main display's UUID depends on the machine (and is missing without a connection to the window server).
/// </summary>
[SupportedOSPlatform("macos")]
public sealed partial class MacDisplayUuidTests
{
    [Theory]
    [InlineData("a8825834-5ff5-44bb-b419-a855792ba90d", "A8825834-5FF5-44BB-B419-A855792BA90D")]
    [InlineData("A8825834-5FF5-44BB-B419-A855792BA90D", "A8825834-5FF5-44BB-B419-A855792BA90D")]
    public void TheUuidTextIsUpperCase(string text, string expected) =>
        Assert.Equal(expected, MacDisplayUuid.Normalize(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("A8825834-5FF5-44BB-B419-A855792BA90D-1")]
    [InlineData("{A8825834-5FF5-44BB-B419-A855792BA90D}")]
    [InlineData("A88258345FF544BBB419A855792BA90D")]
    [InlineData("not a uuid")]
    public void AnythingButAUuidIsNoDevicePath(string? text) =>
        Assert.Null(MacDisplayUuid.Normalize(text));

    [Fact]
    public void TheNullDisplayHasNoUuid()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "ColorSync exists only on macOS.");

        // ColorSync itself answers kCGNullDirectDisplay with the main display's UUID.
        Assert.Null(MacDisplayUuid.Get(0));
    }

    [Theory]
    [InlineData(1234u)]
    [InlineData(0xFFFFFFFFu)]
    public void AnUnknownDisplayHasNoUuid(uint displayId)
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "ColorSync exists only on macOS.");

        Assert.Null(MacDisplayUuid.Get(displayId));
    }

    [Fact]
    public void TheMainDisplayHasTheSameUuidEveryTime()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "ColorSync exists only on macOS.");
        uint main = CGMainDisplayID();
        string? first = MacDisplayUuid.Get(main);
        Assert.SkipWhen(main == 0 || first is null, "No main display (e.g. no connection to the window server).");

        Assert.Equal(first, MacDisplayUuid.Normalize(first));
        Assert.Equal(first, MacDisplayUuid.Get(main));
    }

    // CGDirectDisplayID CGMainDisplayID(void);
    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial uint CGMainDisplayID();
}
