using LayerPeek.App.Platform.MacOS;

using Microsoft.Extensions.Logging.Abstractions;

namespace LayerPeek.App.Tests.Platform.MacOS;

/// <summary>
/// T-240: ⌘⇧[ and ⌘⇧] reach the windows as keys. Only a key down of the two physical keys with exactly ⌘⇧ is passed from
/// <c>performKeyEquivalent:</c> to <c>keyDown:</c>; the real key path (AppKit's Show Help menu on the Czech layout and
/// Avalonia.Native) is checked by <c>mac.keys-czech-layout</c> of <c>tools/macos-verify.ps1</c>.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("macos")]
public sealed class MacTabShortcutKeysTests
{
    // NSEventType, key codes (kVK_ANSI_*) and NSEventModifierFlags (attribute arguments cannot be nint).
    private const long KeyDown = 10;
    private const long KeyUp = 11;
    private const long LeftBracket = 33;
    private const long RightBracket = 30;
    private const long Slash = 44;
    private const long CapsLock = 1 << 16;
    private const long Shift = 1 << 17;
    private const long Control = 1 << 18;
    private const long Option = 1 << 19;
    private const long Command = 1 << 20;
    private const long NumericPad = 1 << 21;

    [Theory]
    [InlineData(KeyDown, LeftBracket, Command | Shift, true)]
    [InlineData(KeyDown, RightBracket, Command | Shift, true)]
    // The flags of the event as the keyboard sends them (device-dependent bits, Caps Lock) do not matter.
    [InlineData(KeyDown, LeftBracket, Command | Shift | CapsLock | NumericPad | 0x10A, true)]
    [InlineData(KeyDown, LeftBracket, Command, false)]
    [InlineData(KeyDown, LeftBracket, Shift, false)]
    [InlineData(KeyDown, LeftBracket, Command | Shift | Option, false)]
    [InlineData(KeyDown, LeftBracket, Command | Shift | Control, false)]
    [InlineData(KeyDown, Slash, Command | Shift, false)]
    [InlineData(KeyUp, LeftBracket, Command | Shift, false)]
    public void PassesOnlyAKeyDownOfTheBracketKeysWithCommandShift(long type, long keyCode, long flags, bool expected) =>
        Assert.Equal(expected, MacTabShortcutKeys.IsTabShortcutKeyDown((nint)type, (nint)keyCode, (nint)flags));

    [Fact]
    public void InstallWithoutTheViewClassOfAvaloniaChangesNothingAndDoesNotThrow()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "The Objective-C runtime exists only on macOS.");

        // The test process does not load libAvaloniaNative, so AvnView does not exist.
        MacTabShortcutKeys.Install(NullLogger.Instance);
        MacTabShortcutKeys.Install(NullLogger.Instance);
    }
}
