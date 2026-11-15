using LayerPeek.App.Platform.MacOS;

using Microsoft.Extensions.Logging.Abstractions;

namespace LayerPeek.App.Tests.Platform.MacOS;

/// <summary>
/// T-241: the Window menu outside the menu bar, from which the Dock menu lists the windows. The real Dock menu is checked
/// by <c>mac.dock-windows</c> of <c>tools/macos-verify.ps1</c>.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("macos")]
public sealed class MacWindowsMenuTests
{
    [Fact]
    public void InstallWithoutNSAppChangesNothingAndDoesNotThrow()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "The Objective-C runtime exists only on macOS.");

        // The test process has no NSApp (no Avalonia.Native, no AppKit app).
        Assert.Equal(0, MacAppActivation.RunningApplication());
        MacWindowsMenu.Install(NullLogger.Instance);
        MacWindowsMenu.Install(NullLogger.Instance);
        Assert.Equal(0, MacAppActivation.RunningApplication());
    }
}
