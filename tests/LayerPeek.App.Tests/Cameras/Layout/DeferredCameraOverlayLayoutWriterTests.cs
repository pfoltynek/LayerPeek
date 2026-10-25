using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Cameras.Layout;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Tests.Cameras.Layout;

/// <summary>
/// The app's layout writer (T-091): the settings store is created on the first save, handlers subscribed before it get the
/// announcement, and a store that cannot be created fails the save visibly. Nothing on disk.
/// </summary>
public sealed class DeferredCameraOverlayLayoutWriterTests
{
    private static readonly CameraOverlayLayout Moved = CameraOverlayLayout.Default.MoveTo(OverlayWidgetIds.Status, OverlayAnchor.BottomCenter, 0);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StoreIsCreatedOnTheFirstSaveAndHandlersSubscribedBeforeGetTheAnnouncement()
    {
        int created = 0;
        OverlayStore store = new();
        DeferredCameraOverlayLayoutWriter writer = new(() =>
        {
            created++;
            return store;
        }, NullLogger<CameraOverlayLayoutWriter>.Instance);
        List<CameraOverlayLayoutSavedEventArgs> before = [];
        List<CameraOverlayLayoutSavedEventArgs> removed = [];
        writer.LayoutSaved += (_, e) => before.Add(e);
        void Removed(object? sender, CameraOverlayLayoutSavedEventArgs e) => removed.Add(e);
        writer.LayoutSaved += Removed;
        writer.LayoutSaved -= Removed;
        Assert.Equal(0, created);

        Assert.True(await writer.SaveAsync(" mk4 ", Moved, Ct));
        List<CameraOverlayLayoutSavedEventArgs> after = [];
        writer.LayoutSaved += (_, e) => after.Add(e);
        Assert.True(await writer.SaveAsync("mk4", CameraOverlayLayout.Default, Ct));

        Assert.Equal(1, created);
        Assert.Equal([("mk4", Moved), ("mk4", CameraOverlayLayout.Default)], store.Saves);
        Assert.Equal(["mk4", "mk4"], before.Select(static e => e.PrinterId));
        Assert.Same(CameraOverlayLayout.Default, Assert.Single(after).Layout);
        Assert.Empty(removed);
    }

    [Fact]
    public async Task NothingWrittenIsNotAnnounced()
    {
        DeferredCameraOverlayLayoutWriter writer = new(() => new OverlayStore { Result = false }, NullLogger<CameraOverlayLayoutWriter>.Instance);
        int announced = 0;
        writer.LayoutSaved += (_, _) => announced++;

        Assert.False(await writer.SaveAsync("mk4", Moved, Ct));
        Assert.Equal(0, announced);
    }

    [Fact]
    public async Task StoreThatCannotBeCreatedFailsTheSave()
    {
        DeferredCameraOverlayLayoutWriter writer = new(static () => throw new InvalidOperationException("No per-user data folder."), NullLogger<CameraOverlayLayoutWriter>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.SaveAsync("mk4", Moved, Ct));
    }

    private sealed class OverlayStore : ISettingsStore
    {
        public bool Result { get; init; } = true;

        public List<(string PrinterId, CameraOverlayLayout Layout)> Saves { get; } = [];

        public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> SetCameraOverlayLayoutAsync(string printerId, CameraOverlayLayout layout, CancellationToken cancellationToken)
        {
            Saves.Add((printerId, layout));
            return Task.FromResult(Result);
        }
    }
}
