using LayerPeek.App.Platform.MacOS;

using Microsoft.Extensions.Logging.Abstractions;

namespace LayerPeek.App.Tests.Platform.MacOS;

/// <summary>
/// T-141: ⌘. reaches the windows as a key. Only a key-down event with ⌘ is passed from <c>cancelOperation:</c> to
/// <c>keyDown:</c>; the real key path (AppKit and Avalonia.Native) is checked by <c>mac.keys</c> of
/// <c>tools/macos-verify.ps1</c>.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("macos")]
public sealed class MacCommandPeriodTests
{
    // NSEventType and NSEventModifierFlags (attribute arguments cannot be nint).
    private const long KeyDown = 10;
    private const long KeyUp = 11;
    private const long Command = 1 << 20;
    private const long Shift = 1 << 17;

    [Theory]
    [InlineData(KeyDown, Command, true)]
    [InlineData(KeyDown, Command | Shift, true)]
    [InlineData(KeyDown, 0, false)]
    [InlineData(KeyDown, Shift, false)]
    [InlineData(KeyUp, Command, false)]
    [InlineData(1, Command, false)]
    public void PassesOnlyAKeyDownWithCommand(long type, long flags, bool expected) =>
        Assert.Equal(expected, MacCommandPeriod.IsCommandKeyDown((nint)type, (nint)flags));

    [Fact]
    public void InstallWithoutTheViewClassOfAvaloniaChangesNothingAndDoesNotThrow()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "The Objective-C runtime exists only on macOS.");

        // The test process does not load libAvaloniaNative, so AvnView does not exist.
        MacCommandPeriod.Install(NullLogger.Instance);
        MacCommandPeriod.Install(NullLogger.Instance);
    }
}
