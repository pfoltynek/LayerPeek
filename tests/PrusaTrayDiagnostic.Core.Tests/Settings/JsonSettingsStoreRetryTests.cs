using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;
using PrusaTrayDiagnostic.Tests.Shared;

namespace PrusaTrayDiagnostic.Core.Tests.Settings;

/// <summary>
/// <c>settings.json</c> held open by another process (antivirus, backup, an editor; user decision 2026-10-22): loads and
/// writes wait a few short, growing delays and try again; other errors are not retried. The clock is scripted, so nothing
/// really waits. The file is held with <see cref="FileShare.None"/>; on macOS and Linux .NET emulates that with an advisory
/// lock, which conflicts with reading but not with the atomic replace (a rename), so the write tests run on Windows only.
/// </summary>
public sealed class JsonSettingsStoreRetryTests : IDisposable
{
    private const string Address = "10.0.0.5";

    private static readonly PrinterModelCode Mk4S = new(1, 4, 1);

    private static readonly AppSettings Saved = AppSettings.Default with
    {
        Printers = [new PrinterDefinition { Id = "mk4", Name = "MK4", ConnectionType = ConnectionType.PrusaLink, Address = Address }],
    };

    private static readonly AppSettings Changed = AppSettings.Default with
    {
        Printers = [new PrinterDefinition { Id = "xl", Name = "XL", ConnectionType = ConnectionType.PrusaLink, Address = "10.0.0.6" }],
    };

    private readonly string _directory = TestDirectories.NewDirectoryPath();
    private readonly HeldFiles _held = new();

    private string FilePath => Path.Combine(_directory, JsonSettingsStore.FileName);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _held.Dispose();
        // SaveDoesNotRetryOtherErrors puts a file where the folder belongs; removed here even when that test fails.
        if (File.Exists(_directory))
        {
            File.Delete(_directory);
        }

        TestDirectories.Delete(_directory);
    }

    [Fact]
    public async Task SaveWaitsWhileTheFileIsHeldOpenAndSucceedsOnceReleased()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to replace a file another process holds open.");
        await SaveInitialAsync();
        _held.Hold(FilePath);
        ScriptedDelayTimeProvider time = new() { OnDelay = index => _held.ReleaseWhen(index == 2) };
        using JsonSettingsStore store = new(_directory, time);

        await store.SaveAsync(Changed, Ct);

        Assert.Equal(JsonSettingsStore.RetryDelays.Take(2), time.Delays);
        Assert.Equal("xl", Assert.Single((await store.LoadAsync(Ct)).Settings.Printers).Id);
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task SaveGivesUpAfterTheRetriesAndKeepsThePreviousFile()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to replace a file another process holds open.");
        await SaveInitialAsync();
        byte[] before = await File.ReadAllBytesAsync(FilePath, Ct);
        _held.Hold(FilePath);
        ScriptedDelayTimeProvider time = new();
        using JsonSettingsStore store = new(_directory, time);

        Exception error = await Assert.ThrowsAnyAsync<Exception>(() => store.SaveAsync(Changed, Ct));

        Assert.True(error is IOException or UnauthorizedAccessException, error.ToString());
        Assert.Equal(JsonSettingsStore.RetryDelays, time.Delays);
        _held.ReleaseAll();
        Assert.Equal(before, await File.ReadAllBytesAsync(FilePath, Ct));
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task SaveStopsWaitingWhenCancelled()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to replace a file another process holds open.");
        await SaveInitialAsync();
        _held.Hold(FilePath);
        using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        ScriptedDelayTimeProvider time = new()
        {
            OnDelay = _ =>
            {
                cancel.Cancel();
                return false;
            },
        };
        using JsonSettingsStore store = new(_directory, time);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(Changed, cancel.Token));

        Assert.Single(time.Delays);
        _held.ReleaseAll();
        Assert.Equal("mk4", Assert.Single((await store.LoadAsync(Ct)).Settings.Printers).Id);
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task SaveDoesNotRetryOtherErrors()
    {
        // The settings folder is a file: creating the folder fails at once, whatever is retried.
        Directory.CreateDirectory(Path.GetDirectoryName(_directory)!);
        await File.WriteAllTextAsync(_directory, "not a folder", Ct);
        ScriptedDelayTimeProvider time = new();
        using JsonSettingsStore store = new(_directory, time);

        await Assert.ThrowsAnyAsync<IOException>(() => store.SaveAsync(Changed, Ct));

        Assert.Empty(time.Delays);
    }

    [Fact]
    public async Task LoadWaitsWhileTheFileIsHeldOpen()
    {
        await SaveInitialAsync();
        _held.Hold(FilePath);
        ScriptedDelayTimeProvider time = new() { OnDelay = index => _held.ReleaseWhen(index == 1) };
        using JsonSettingsStore store = new(_directory, time);

        SettingsLoadResult result = await store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Equal("mk4", Assert.Single(result.Settings.Printers).Id);
        Assert.Equal(JsonSettingsStore.RetryDelays.Take(1), time.Delays);
    }

    [Fact]
    public async Task LoadReportsAFileHeldOpenAfterTheRetriesAsUnreadable()
    {
        await SaveInitialAsync();
        _held.Hold(FilePath);
        ScriptedDelayTimeProvider time = new();
        using JsonSettingsStore store = new(_directory, time);

        SettingsLoadResult result = await store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.Unreadable, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
        Assert.Equal(JsonSettingsStore.RetryDelays, time.Delays);
    }

    [Fact]
    public async Task LoadStopsWaitingWhenCancelled()
    {
        await SaveInitialAsync();
        _held.Hold(FilePath);
        using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        ScriptedDelayTimeProvider time = new()
        {
            OnDelay = _ =>
            {
                cancel.Cancel();
                return false;
            },
        };
        using JsonSettingsStore store = new(_directory, time);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.LoadAsync(cancel.Token));

        Assert.Single(time.Delays);
    }

    [Fact]
    public async Task LoadOfAMissingFileDoesNotWait()
    {
        ScriptedDelayTimeProvider time = new();
        using JsonSettingsStore store = new(_directory, time);

        SettingsLoadResult result = await store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.NotFound, result.Status);
        Assert.Empty(time.Delays);
    }

    [Fact]
    public async Task DetectedModelWaitsWhileTheFileIsHeldOpen()
    {
        await SaveInitialAsync();
        _held.Hold(FilePath);
        ScriptedDelayTimeProvider time = new() { OnDelay = index => _held.ReleaseWhen(index == 1) };
        using JsonSettingsStore store = new(_directory, time);

        bool written = await store.SetDetectedModelAsync("mk4", Mk4S, Address, ConnectionType.PrusaLink, Ct);

        Assert.True(written);
        Assert.Equal(Mk4S, Assert.Single((await store.LoadAsync(Ct)).Settings.Printers).DetectedModel);
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task DetectedModelThatCannotBeSavedAfterTheRetriesFails()
    {
        // Returning "nothing to write" would hide the lost model; the caller logs the failure.
        await SaveInitialAsync();
        _held.Hold(FilePath);
        ScriptedDelayTimeProvider time = new();
        using JsonSettingsStore store = new(_directory, time);

        await Assert.ThrowsAnyAsync<IOException>(() => store.SetDetectedModelAsync("mk4", Mk4S, Address, ConnectionType.PrusaLink, Ct));

        Assert.Equal(JsonSettingsStore.RetryDelays, time.Delays);
        _held.ReleaseAll();
        Assert.Null(Assert.Single((await store.LoadAsync(Ct)).Settings.Printers).DetectedModel);
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task CameraCredentialsAreRemovedOnceTheFileIsReleased()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            FilePath,
            """{ "printers": [ { "id": "mk4", "camera": { "enabled": true, "streamUrl": "rtsp://user:secret@10.0.0.7/live" } } ] }""",
            Ct);
        _held.Hold(FilePath);
        ScriptedDelayTimeProvider time = new() { OnDelay = index => _held.ReleaseWhen(index == 1) };
        using JsonSettingsStore store = new(_directory, time);

        IReadOnlyList<string> changed = await store.RemoveCameraCredentialsAsync(Ct);

        Assert.Equal(["mk4"], changed);
        Assert.DoesNotContain("secret", await File.ReadAllTextAsync(FilePath, Ct), StringComparison.Ordinal);
        AssertNoTemporaryFiles();
    }

    private async Task SaveInitialAsync()
    {
        using JsonSettingsStore store = new(_directory);
        await store.SaveAsync(Saved, Ct);
    }

    private void AssertNoTemporaryFiles() =>
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
}
