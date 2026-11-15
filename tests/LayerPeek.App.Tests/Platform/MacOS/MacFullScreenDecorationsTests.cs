using LayerPeek.App.Platform.MacOS;

namespace LayerPeek.App.Tests.Platform.MacOS;

/// <summary>
/// T-237: after AppKit left full screen by itself, Avalonia.Native 12.1.6 ignores the decorations until it enters full
/// screen again, so they are applied natively meanwhile. Logic only, with a fake window.
/// </summary>
public sealed class MacFullScreenDecorationsTests
{
    // What Avalonia.Native gives a normal window that can be resized and minimized, and the mini preview.
    private const nuint AvaloniaFull = MacFullScreenDecorations.Titled | MacFullScreenDecorations.Closable | MacFullScreenDecorations.Miniaturizable | MacFullScreenDecorations.Resizable;
    private const nuint AvaloniaNone = MacFullScreenDecorations.FullSizeContentView | MacFullScreenDecorations.Miniaturizable;
    private const nuint FullScreen = 1 << 14;

    private static (MacFullScreenDecorations Decorations, FakeDecorations Fake) Create()
    {
        FakeDecorations fake = new();
        return (new MacFullScreenDecorations(fake), fake);
    }

    [Fact]
    public void AnExitOfAppKitMakesTheDecorationsNative()
    {
        (MacFullScreenDecorations decorations, FakeDecorations fake) = Create();
        decorations.OnWindowStateChanged(wasFullScreen: false, isFullScreen: true, byApp: true);

        decorations.OnWindowStateChanged(wasFullScreen: true, isFullScreen: false, byApp: false);
        decorations.OnDecorationsChanged(framed: false, resizable: true, miniaturizable: true);

        Assert.True(decorations.IsIgnoredByAvalonia);
        Assert.Equal([(false, true, true)], fake.Applied);
    }

    [Fact]
    public void AnExitOfTheAppLeavesThemToAvalonia()
    {
        (MacFullScreenDecorations decorations, FakeDecorations fake) = Create();
        decorations.OnWindowStateChanged(wasFullScreen: false, isFullScreen: true, byApp: true);

        decorations.OnWindowStateChanged(wasFullScreen: true, isFullScreen: false, byApp: true);
        decorations.OnDecorationsChanged(framed: false, resizable: true, miniaturizable: true);

        Assert.False(decorations.IsIgnoredByAvalonia);
        Assert.Empty(fake.Applied);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EnteringFullScreenAgainEndsIt(bool byApp)
    {
        (MacFullScreenDecorations decorations, FakeDecorations fake) = Create();
        decorations.OnWindowStateChanged(wasFullScreen: true, isFullScreen: false, byApp: false);

        decorations.OnWindowStateChanged(wasFullScreen: false, isFullScreen: true, byApp);
        decorations.OnDecorationsChanged(framed: true, resizable: true, miniaturizable: true);

        Assert.False(decorations.IsIgnoredByAvalonia);
        Assert.Empty(fake.Applied);
    }

    [Fact]
    public void OtherStateChangesKeepItAsItIs()
    {
        (MacFullScreenDecorations decorations, _) = Create();

        // Minimized and restored by the user, never in full screen.
        decorations.OnWindowStateChanged(wasFullScreen: false, isFullScreen: false, byApp: false);
        Assert.False(decorations.IsIgnoredByAvalonia);

        decorations.OnWindowStateChanged(wasFullScreen: true, isFullScreen: false, byApp: false);
        decorations.OnWindowStateChanged(wasFullScreen: false, isFullScreen: false, byApp: true);
        Assert.True(decorations.IsIgnoredByAvalonia);
    }

    // Avalonia's masks as numbers (attributes take no nuint constants): 0xF titled, closable, miniaturizable, resizable;
    // 0x8004 full size content view, miniaturizable.
    [Theory]
    [InlineData(0xFu, false, true, true, 0x8004u)]
    [InlineData(0x8004u, true, true, true, 0xFu)]
    [InlineData(0x8004u, true, false, true, 0x7u)]
    [InlineData(0xFu, false, true, false, 0x8000u)]
    [InlineData(0x10Fu, false, true, true, 0x8004u)] // textured background
    public void TheStyleMaskIsTheOneOfAvalonia(uint current, bool framed, bool resizable, bool miniaturizable, uint expected) =>
        Assert.Equal(expected, MacFullScreenDecorations.StyleMask(current, framed, resizable, miniaturizable));

    [Fact]
    public void TheMasksOfAvaloniaAreThoseNumbers()
    {
        Assert.Equal((nuint)0xF, AvaloniaFull);
        Assert.Equal((nuint)0x8004, AvaloniaNone);
    }

    [Fact]
    public void OtherFlagsOfTheStyleMaskStay() =>
        Assert.Equal(AvaloniaNone | FullScreen, MacFullScreenDecorations.StyleMask(AvaloniaFull | FullScreen, framed: false, resizable: true, miniaturizable: true));

    private sealed class FakeDecorations : IWindowDecorations
    {
        public List<(bool Framed, bool Resizable, bool Miniaturizable)> Applied { get; } = [];

        public void Apply(bool framed, bool resizable, bool miniaturizable) => Applied.Add((framed, resizable, miniaturizable));
    }
}
