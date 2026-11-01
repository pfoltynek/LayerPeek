using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Cameras;
using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Tests.Cameras;

/// <summary>
/// The app's writer of the pin (T-117), like the layout writer: the settings store is created on the first save, handlers
/// subscribed before it get the announcement, the write and its announcement hold the write gate shared with the settings
/// window, and a store that cannot be created fails the save visibly. Nothing on disk.
/// </summary>
public sealed class DeferredCameraWindowTopmostWriterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StoreIsCreatedOnTheFirstSaveAndHandlersSubscribedBeforeGetTheAnnouncement()
    {
        int created = 0;
        TopmostStore store = new();
        DeferredCameraWindowTopmostWriter writer = new(() =>
        {
            created++;
            return store;
        }, NullLogger<CameraWindowTopmostWriter>.Instance);
        List<CameraWindowTopmostSavedEventArgs> before = [];
        List<CameraWindowTopmostSavedEventArgs> removed = [];
        writer.TopmostSaved += (_, e) => before.Add(e);
        void Removed(object? sender, CameraWindowTopmostSavedEventArgs e) => removed.Add(e);
        writer.TopmostSaved += Removed;
        writer.TopmostSaved -= Removed;
        Assert.Equal(0, created);

        Assert.True(await writer.SaveAsync(" mk4 ", CameraWindowMode.Normal, true, Ct));
        List<CameraWindowTopmostSavedEventArgs> after = [];
        writer.TopmostSaved += (_, e) => after.Add(e);
        Assert.True(await writer.SaveAsync("mk4", CameraWindowMode.Mini, false, Ct));

        Assert.Equal(1, created);
        Assert.Equal([("mk4", CameraWindowMode.Normal, true), ("mk4", CameraWindowMode.Mini, false)], store.Saves);
        Assert.Equal([("mk4", CameraWindowMode.Normal, true), ("mk4", CameraWindowMode.Mini, false)], before.Select(static e => (e.PrinterId, e.Mode, e.Topmost)));
        Assert.Equal(CameraWindowMode.Mini, Assert.Single(after).Mode);
        Assert.Empty(removed);
    }

    [Fact]
    public async Task SaveAndAnnouncementWaitForTheWriteGateSharedWithTheSettingsWindow()
    {
        TopmostStore store = new();
        using SettingsWriteGate gate = new();
        DeferredCameraWindowTopmostWriter writer = new(() => store, NullLogger<CameraWindowTopmostWriter>.Instance, gate);
        int announced = 0;
        writer.TopmostSaved += (_, _) => announced++;
        await gate.EnterAsync(Ct);

        Task<bool> saving = writer.SaveAsync("mk4", CameraWindowMode.Normal, true, Ct);
        Assert.False(saving.IsCompleted);
        Assert.Empty(store.Saves);
        Assert.Equal(0, announced);
        gate.Exit();

        Assert.True(await saving.WaitAsync(TestTimeouts.Wait, Ct));
        Assert.Equal(1, announced);
        // Released after the save.
        Assert.True(gate.EnterAsync(Ct).IsCompletedSuccessfully);
        gate.Exit();
    }

    [Fact]
    public async Task NothingWrittenIsNotAnnounced()
    {
        TopmostStore store = new() { Result = false };
        DeferredCameraWindowTopmostWriter writer = new(() => store, NullLogger<CameraWindowTopmostWriter>.Instance);
        int announced = 0;
        writer.TopmostSaved += (_, _) => announced++;

        Assert.False(await writer.SaveAsync("mk4", CameraWindowMode.Normal, true, Ct));
        Assert.Equal(0, announced);
    }

    [Fact]
    public async Task StoreThatCannotBeCreatedFailsTheSaveAndReleasesTheGate()
    {
        using SettingsWriteGate gate = new();
        DeferredCameraWindowTopmostWriter writer = new(() => throw new InvalidOperationException("no data folder"), NullLogger<CameraWindowTopmostWriter>.Instance, gate);

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.SaveAsync("mk4", CameraWindowMode.Normal, true, Ct));

        Assert.True(gate.EnterAsync(Ct).IsCompletedSuccessfully);
        gate.Exit();
    }

    private sealed class TopmostStore : ISettingsStore
    {
        public List<(string PrinterId, CameraWindowMode Mode, bool Topmost)> Saves { get; } = [];

        public bool Result { get; init; } = true;

        public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SettingsLoadResult { Settings = AppSettings.Default, Status = SettingsLoadStatus.NotFound });

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<CameraWindowTopmostStoreResult> SetCameraWindowTopmostAsync(string printerId, CameraWindowMode mode, bool topmost, CancellationToken cancellationToken)
        {
            Saves.Add((printerId, mode, topmost));
            return Task.FromResult(Result ? CameraWindowTopmostStoreResult.Written : CameraWindowTopmostStoreResult.LeftAlone);
        }
    }
}
