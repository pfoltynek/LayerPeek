using LayerPeek.Core.Display;

namespace LayerPeek.Core.Tests.Display;

/// <summary>T-186: the default until a platform reports more (macOS before T-187, tests).</summary>
public sealed class AlwaysVisibleDisplayActivityTests
{
    [Fact]
    public void ScreenIsAlwaysVisibleAndNeverChanges()
    {
        AlwaysVisibleDisplayActivity display = AlwaysVisibleDisplayActivity.Instance;
        int changes = 0;
        EventHandler handler = (_, _) => changes++;

        display.ScreenVisibilityChanged += handler;
        display.ScreenVisibilityChanged -= handler;

        Assert.True(display.IsScreenVisible);
        Assert.Equal(0, changes);
    }
}
