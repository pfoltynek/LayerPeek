using LayerPeek.App.Platform.MacOS;

namespace LayerPeek.App.Tests.Platform.MacOS;

/// <summary>T-096: CoreGraphics counts the active displays (read only; the number depends on the machine and whether its display sleeps).</summary>
[System.Runtime.Versioning.SupportedOSPlatform("macos")]
public sealed class MacActiveDisplaysTests
{
    [Fact]
    public void CountsTheActiveDisplaysWithoutThrowing()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "CoreGraphics exists only on macOS.");

        bool known = new MacActiveDisplays().TryCount(out uint count, out int error);

        if (known)
        {
            Assert.Equal(0, error);
            Assert.InRange(count, 0u, 64u);
        }
        else
        {
            // E.g. without a connection to the window server (an SSH session): an error code, never a count.
            Assert.NotEqual(0, error);
            Assert.Equal(0u, count);
        }
    }
}
