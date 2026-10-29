using LayerPeek.App.Platform;
using LayerPeek.App.Tests.Platform;
using LayerPeek.App.Windows;

namespace LayerPeek.App.Tests.Windows;

/// <summary>
/// The window counter behind the Dock icon of macOS (T-102, user decision 2026-10-29): only open windows that are not
/// always on top count; the platform hears only the transitions.
/// </summary>
public sealed class AppPresenceTests
{
    private readonly RecordingAppPresence _platform = new();
    private readonly AppPresence _presence;

    public AppPresenceTests() => _presence = new AppPresence(_platform);

    [Fact]
    public void NothingOpenIsHiddenWithoutAnyCall()
    {
        Assert.False(_presence.IsVisible);
        Assert.Equal(0, _presence.Count);
        Assert.Empty(_platform.Calls);
    }

    [Fact]
    public void FirstWindowShowsAndLastClosedHidesWithoutKeepingTheAppActive()
    {
        object settings = new();
        object camera = new();

        _presence.Set(settings, topmost: false);
        _presence.Set(camera, topmost: false);
        Assert.Equal(2, _presence.Count);
        _presence.Remove(settings);
        Assert.True(_presence.IsVisible);
        _presence.Remove(camera);

        Assert.False(_presence.IsVisible);
        Assert.Equal(["show", "hide"], _platform.Calls);
    }

    [Fact]
    public void TopmostWindowsNeverShowTheApp()
    {
        object mini = new();

        _presence.Set(mini, topmost: true);
        _presence.Remove(mini);

        Assert.False(_presence.IsVisible);
        Assert.Empty(_platform.Calls);
    }

    [Fact]
    public void ModeSwitchesOfTheOnlyWindowFollowItsTopmost()
    {
        object camera = new();

        _presence.Set(camera, topmost: false);
        // Normal → mini preview (on top): the app leaves the Dock but stays active for the preview.
        _presence.Set(camera, topmost: true);
        Assert.False(_presence.IsVisible);
        // Mini preview → full screen (never on top).
        _presence.Set(camera, topmost: false);
        // Unchanged: nothing reported.
        _presence.Set(camera, topmost: false);
        _presence.Remove(camera);

        Assert.Equal(["show", "hide keep-active", "show", "hide"], _platform.Calls);
    }

    [Fact]
    public void ClosingTheLastCountedWindowNextToATopmostOneKeepsTheAppActive()
    {
        object settings = new();
        object mini = new();
        _presence.Set(mini, topmost: true);
        _presence.Set(settings, topmost: false);

        _presence.Remove(settings);
        _presence.Remove(mini);

        Assert.Equal(["show", "hide keep-active"], _platform.Calls);
    }

    [Fact]
    public void TopmostWindowTurnedOffCountsAndUnknownOrRepeatedRemovalsAreIgnored()
    {
        object normal = new();
        _presence.Set(normal, topmost: true);
        _presence.Set(normal, topmost: false);

        _presence.Remove(new object());
        _presence.Remove(normal);
        _presence.Remove(normal);

        Assert.Equal(["show", "hide"], _platform.Calls);
    }

    [Fact]
    public void NoAppPresenceDoesNothing()
    {
        AppPresence presence = new(NoAppPresence.Instance);
        object window = new();

        presence.Set(window, topmost: false);

        Assert.True(presence.IsVisible);
    }
}
