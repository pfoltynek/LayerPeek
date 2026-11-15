using Avalonia.Input;

using LayerPeek.App.Windows;

namespace LayerPeek.App.Tests.Windows;

/// <summary>
/// T-100 / T-240: ⌘⇧] / ⌘⇧[ by the logical bracket or by the physical key at its US position. The key events are the
/// ones Avalonia 12.1.6 raised on macOS 27.2 (<c>mac.keys-czech-layout</c> of <c>tools/macos-verify.ps1</c>, 2026-11-15).
/// </summary>
public sealed class MacShortcutsTests
{
    private const KeyModifiers CommandShift = KeyModifiers.Meta | KeyModifiers.Shift;

    private static KeyEventArgs Key(Key key, PhysicalKey physical, KeyModifiers modifiers, string symbol) =>
        new() { Key = key, PhysicalKey = physical, KeyModifiers = modifiers, KeySymbol = symbol };

    [Theory]
    // US layout: the logical brackets ({ and } with ⇧).
    [InlineData(Avalonia.Input.Key.OemCloseBrackets, PhysicalKey.BracketRight, "}", 1)]
    [InlineData(Avalonia.Input.Key.OemOpenBrackets, PhysicalKey.BracketLeft, "{", -1)]
    // Czech layout: ⇧ types ( and / on these keys; Avalonia reports ( as the US key, / as OemQuestion.
    [InlineData(Avalonia.Input.Key.OemCloseBrackets, PhysicalKey.BracketRight, "(", 1)]
    [InlineData(Avalonia.Input.Key.OemQuestion, PhysicalKey.BracketLeft, "/", -1)]
    // The real ⌘? (the slash key) is no tab shortcut.
    [InlineData(Avalonia.Input.Key.OemQuestion, PhysicalKey.Slash, "?", 0)]
    public void CommandShiftMatchesTheBracketKeysOnEveryLayout(Key key, PhysicalKey physical, string symbol, int expected) =>
        Assert.Equal(expected, MacShortcuts.MainTabStep(Key(key, physical, CommandShift, symbol)));

    [Theory]
    [InlineData(KeyModifiers.Meta)]
    [InlineData(KeyModifiers.Shift)]
    [InlineData(KeyModifiers.Meta | KeyModifiers.Shift | KeyModifiers.Alt)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Shift)]
    public void OnlyExactlyCommandShiftSwitchesTheTab(KeyModifiers modifiers) =>
        Assert.Equal(0, MacShortcuts.MainTabStep(Key(Avalonia.Input.Key.OemQuestion, PhysicalKey.BracketLeft, modifiers, "/")));
}
