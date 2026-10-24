using LayerPeek.Core.Images;
using LayerPeek.Core.IO;
using LayerPeek.Tests.Shared;

namespace LayerPeek.Core.Tests.Images;

/// <summary>
/// A stored printer image held open by another process (user decision 2026-10-22): reading, replacing and deleting it wait
/// a few short, growing delays (<see cref="FileRetry"/>) and try again. The clock is scripted, so nothing really waits.
/// Replacing and deleting conflict with an open file only on Windows; those tests run there only.
/// </summary>
public sealed class FilePrinterImageStoreRetryTests : IDisposable
{
    private const string WindowsOnly = "Only Windows refuses to replace or delete a file another process holds open.";

    private readonly string _settingsDirectory = TestDirectories.NewDirectoryPath();
    private readonly HeldFiles _held = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _held.Dispose();
        TestDirectories.Delete(_settingsDirectory);
    }

    [Fact]
    public async Task SaveWaitsWhileTheImageIsHeldOpenAndSucceedsOnceReleased()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        string path = await SaveInitialAsync();
        _held.Hold(path);
        ScriptedDelayTimeProvider time = new() { OnDelay = index => _held.ReleaseWhen(index == 2) };
        FilePrinterImageStore store = new(_settingsDirectory, time);
        byte[] second = ImageTestData.Png(200);

        await store.SaveAsync("mk4", second, Ct);

        Assert.Equal(FileRetry.Delays.Take(2), time.Delays);
        Assert.Equal(second, await File.ReadAllBytesAsync(path, Ct));
        AssertNoTemporaryFiles(store);
    }

    [Fact]
    public async Task SaveGivesUpAfterTheRetriesAndKeepsThePreviousImage()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        string path = await SaveInitialAsync();
        byte[] before = await File.ReadAllBytesAsync(path, Ct);
        _held.Hold(path);
        ScriptedDelayTimeProvider time = new();
        FilePrinterImageStore store = new(_settingsDirectory, time);

        Exception error = await Assert.ThrowsAnyAsync<Exception>(() => store.SaveAsync("mk4", ImageTestData.Png(200), Ct));

        Assert.True(error is IOException or UnauthorizedAccessException, error.ToString());
        Assert.Equal(FileRetry.Delays, time.Delays);
        _held.ReleaseAll();
        Assert.Equal(before, await File.ReadAllBytesAsync(path, Ct));
        AssertNoTemporaryFiles(store);
    }

    [Fact]
    public async Task ReadWaitsWhileTheImageIsHeldOpen()
    {
        string path = await SaveInitialAsync();
        byte[] expected = await File.ReadAllBytesAsync(path, Ct);
        _held.Hold(path);
        ScriptedDelayTimeProvider time = new() { OnDelay = index => _held.ReleaseWhen(index == 1) };
        FilePrinterImageStore store = new(_settingsDirectory, time);

        byte[]? image = await store.ReadAsync("mk4", Ct);

        Assert.Equal(expected, image);
        Assert.Equal(FileRetry.Delays.Take(1), time.Delays);
    }

    [Fact]
    public async Task ReadStopsWaitingWhenCancelled()
    {
        string path = await SaveInitialAsync();
        _held.Hold(path);
        using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        ScriptedDelayTimeProvider time = new()
        {
            OnDelay = _ =>
            {
                cancel.Cancel();
                return false;
            },
        };
        FilePrinterImageStore store = new(_settingsDirectory, time);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ReadAsync("mk4", cancel.Token));

        Assert.Single(time.Delays);
    }

    [Fact]
    public async Task DeleteWaitsWhileTheImageIsHeldOpen()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        string path = await SaveInitialAsync();
        _held.Hold(path);
        ScriptedDelayTimeProvider time = new() { OnDelay = index => _held.ReleaseWhen(index == 1) };
        FilePrinterImageStore store = new(_settingsDirectory, time);

        bool deleted = await store.DeleteAsync("mk4", Ct);

        Assert.True(deleted);
        Assert.False(File.Exists(path));
        Assert.Equal(FileRetry.Delays.Take(1), time.Delays);
    }

    [Fact]
    public async Task MissingImageDoesNotWait()
    {
        ScriptedDelayTimeProvider time = new();
        FilePrinterImageStore store = new(_settingsDirectory, time);

        Assert.Null(await store.ReadAsync("mk4", Ct));
        Assert.False(await store.DeleteAsync("mk4", Ct));
        Assert.Empty(time.Delays);
    }

    /// <returns>Path of the stored image of printer "mk4".</returns>
    private async Task<string> SaveInitialAsync()
    {
        FilePrinterImageStore store = new(_settingsDirectory);
        await store.SaveAsync("mk4", ImageTestData.Png(100), Ct);
        return store.GetFilePath("mk4");
    }

    private static void AssertNoTemporaryFiles(FilePrinterImageStore store) =>
        Assert.Empty(Directory.GetFiles(store.DirectoryPath, "*.tmp"));
}
