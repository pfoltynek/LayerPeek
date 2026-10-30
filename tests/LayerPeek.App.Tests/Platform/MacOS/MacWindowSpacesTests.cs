using LayerPeek.App.Platform.MacOS;
using LayerPeek.Core.Cameras;

namespace LayerPeek.App.Tests.Platform.MacOS;

/// <summary>
/// T-038 question 1 (user decision 2026-10-30, option b): the mini preview that is always on top joins every Space and
/// shows above apps in full screen; leaving that state restores the native flags. Logic only, with a fake window.
/// </summary>
public sealed class MacWindowSpacesTests
{
    // What Avalonia 12.1.4 gives a normal window, plus flags this change must keep.
    private const nuint Managed = 1 << 2;
    private const nuint ParticipatesInCycle = 1 << 5;
    private const nuint AvaloniaNormal = MacWindowSpaces.FullScreenPrimary;

    [Theory]
    [InlineData(CameraWindowMode.Mini, true, true)]
    [InlineData(CameraWindowMode.Mini, false, false)]
    [InlineData(CameraWindowMode.Normal, true, false)]
    [InlineData(CameraWindowMode.Normal, false, false)]
    [InlineData(CameraWindowMode.FullScreen, true, false)]
    [InlineData(CameraWindowMode.FullScreen, false, false)]
    public void OnlyTheMiniPreviewOnTopJoinsEverySpace(CameraWindowMode mode, bool topmost, bool expected) =>
        Assert.Equal(expected, MacWindowSpaces.JoinsAllSpaces(mode, topmost));

    [Fact]
    public void TheNormalWindowOfAvaloniaBecomesAnAuxiliaryWindowOnEverySpace() =>
        Assert.Equal(MacWindowSpaces.CanJoinAllSpaces | MacWindowSpaces.FullScreenAuxiliary, MacWindowSpaces.Joined(AvaloniaNormal));

    [Theory]
    [InlineData(1u << 1)] // MoveToActiveSpace
    [InlineData(1u << 7)] // FullScreenPrimary
    [InlineData(1u << 9)] // FullScreenNone
    [InlineData(1u << 11)] // FullScreenAllowsTiling
    public void FlagsAppKitRefusesTogetherAreDropped(uint conflicting)
    {
        nuint joined = MacWindowSpaces.Joined(conflicting);

        Assert.Equal((nuint)0, joined & conflicting);
        Assert.Equal(MacWindowSpaces.CanJoinAllSpaces | MacWindowSpaces.FullScreenAuxiliary, joined);
    }

    [Fact]
    public void OtherFlagsAreKept() =>
        Assert.Equal(
            Managed | ParticipatesInCycle | MacWindowSpaces.CanJoinAllSpaces | MacWindowSpaces.FullScreenAuxiliary,
            MacWindowSpaces.Joined(Managed | ParticipatesInCycle | AvaloniaNormal));

    [Fact]
    public void JoiningRemembersThePreviousFlagsAndLeavingRestoresThem()
    {
        FakeWindow window = new(Managed | AvaloniaNormal);
        MacWindowSpaces spaces = new(window);

        spaces.Apply(true);
        Assert.True(spaces.IsOnAllSpaces);
        Assert.Equal(Managed | MacWindowSpaces.CanJoinAllSpaces | MacWindowSpaces.FullScreenAuxiliary, window.Behavior);

        spaces.Apply(false);
        Assert.False(spaces.IsOnAllSpaces);
        Assert.Equal(Managed | AvaloniaNormal, window.Behavior);
        Assert.Equal(2, window.Writes.Count);
    }

    [Fact]
    public void ApplyingTheSameStateAgainWritesNothing()
    {
        FakeWindow window = new(AvaloniaNormal);
        MacWindowSpaces spaces = new(window);

        spaces.Apply(false);
        Assert.Empty(window.Writes);

        spaces.Apply(true);
        spaces.Apply(true);
        Assert.Single(window.Writes);
        Assert.Equal(1, window.Reads);

        spaces.Apply(false);
        spaces.Apply(false);
        Assert.Equal(2, window.Writes.Count);
    }

    [Fact]
    public void JoiningAgainReadsTheFlagsTheWindowHasThen()
    {
        FakeWindow window = new(AvaloniaNormal);
        MacWindowSpaces spaces = new(window);
        spaces.Apply(true);
        spaces.Apply(false);

        // Avalonia (or AppKit) changed the flags of the normal window meanwhile.
        window.Behavior = AvaloniaNormal | Managed;
        spaces.Apply(true);
        spaces.Apply(false);

        Assert.Equal(AvaloniaNormal | Managed, window.Behavior);
    }

    [Fact]
    public void UnreadableFlagsAreNeverWritten()
    {
        FakeWindow window = new(null);
        MacWindowSpaces spaces = new(window);

        spaces.Apply(true);
        spaces.Apply(false);

        Assert.False(spaces.IsOnAllSpaces);
        Assert.Empty(window.Writes);
    }

    [Fact]
    public void AWindowIsRequired() => Assert.Throws<ArgumentNullException>(() => new MacWindowSpaces(null!));

    private sealed class FakeWindow(nuint? behavior) : IWindowCollectionBehavior
    {
        public nuint? Behavior { get; set; } = behavior;

        public int Reads { get; private set; }

        public List<nuint> Writes { get; } = [];

        public nuint? Read()
        {
            Reads++;
            return Behavior;
        }

        public void Write(nuint value)
        {
            Writes.Add(value);
            Behavior = value;
        }
    }
}
