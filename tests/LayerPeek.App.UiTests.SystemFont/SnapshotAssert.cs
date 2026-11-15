using Avalonia.Controls;

namespace LayerPeek.App.UiTests.Infrastructure;

/// <summary>
/// Stand-in for the snapshot assertions of LayerPeek.App.UiTests in the linked tests (T-214): nothing rendered with
/// San Francisco may be written anywhere (Apple's licence), so a snapshot comparison is skipped with the reason. The
/// snapshots themselves are checked with Inter by LayerPeek.App.UiTests.
/// </summary>
internal static class SnapshotAssert
{
    public const string SkipReason = "No snapshots with San Francisco (Apple's font must not end up in an image); LayerPeek.App.UiTests compares this snapshot with Inter.";

    public static void Matches(Window window, string name)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(name);
        Assert.Skip(SkipReason);
    }
}
