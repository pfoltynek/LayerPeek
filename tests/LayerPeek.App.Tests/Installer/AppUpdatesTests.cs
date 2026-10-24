using LayerPeek.App.Installer;
using LayerPeek.App.Tests.Tray;

namespace LayerPeek.App.Tests.Installer;

/// <summary>
/// The Velopack update wiring of T-017 (the menu item and the notification are T-058): every failure of a check counts as
/// "no update" (while the repository is private GitHub answers 404), pre-releases follow the setting, and an update is
/// applied only after the app quit normally (its state saved), because Velopack ends every process of the installation.
/// </summary>
public sealed class AppUpdatesTests
{
    private readonly FakeUpdateEngine _engine = new();
    private readonly ListLogger<AppUpdates> _logger = new();
    private bool _prereleases = true;

    [Fact]
    public void UpdatesExistOnlyInAVelopackInstallation()
    {
        Assert.True(Updates().IsAvailable);
        Assert.False(new AppUpdates(null, () => true, _logger).IsAvailable);
    }

    [Fact]
    public async Task OutsideAnInstallationThereIsNeverAnUpdate()
    {
        Assert.Null(await new AppUpdates(null, () => true, _logger).CheckAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheCheckAsksForPrereleasesAsTheSettingSays(bool prereleases)
    {
        _prereleases = prereleases;
        _engine.Next = new AvailableUpdate("0.2.0", "notes");

        AvailableUpdate? update = await Updates().CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal("0.2.0", update?.Version);
        Assert.Equal([prereleases], _engine.Checks);
    }

    [Fact]
    public async Task NoUpdateIsNull()
    {
        Assert.Null(await Updates().CheckAsync(TestContext.Current.CancellationToken));
    }

    public static TheoryData<Exception> CheckFailures => new()
    {
        new HttpRequestException("Response status code does not indicate success: 404 (Not Found).", null, System.Net.HttpStatusCode.NotFound),
        new HttpRequestException("403 rate limit", null, System.Net.HttpStatusCode.Forbidden),
        new TaskCanceledException("timeout"),
        new System.Text.Json.JsonException("broken feed"),
        new InvalidOperationException("anything"),
        new NotSupportedException("anything else"),
    };

    [Theory]
    [MemberData(nameof(CheckFailures))]
    public async Task EveryFailureOfTheCheckMeansNoUpdate(Exception failure)
    {
        _engine.CheckFailure = failure;

        Assert.Null(await Updates().CheckAsync(TestContext.Current.CancellationToken));
        Assert.Contains(_logger.Entries, entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Information && entry.Exception == failure);
    }

    [Fact]
    public async Task ACancelledCheckIsCancelled()
    {
        using CancellationTokenSource cancel = new();
        await cancel.CancelAsync();
        _engine.CheckFailure = new OperationCanceledException(cancel.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Updates().CheckAsync(cancel.Token));
    }

    [Fact]
    public async Task ApplyingDownloadsSchedulesTheUpdaterAndThenQuits()
    {
        AvailableUpdate update = new("0.2.0", null);
        List<string> steps = [];
        _engine.Steps = steps;

        bool applied = await Updates().ApplyAsync(update, () =>
        {
            steps.Add("quit");
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);

        Assert.True(applied);
        // The updater waits for this process to exit: the quit saves the state first (T-017, docs/velopack.md point 5).
        Assert.Equal(["download 0.2.0", "apply on exit 0.2.0", "quit"], steps);
    }

    [Fact]
    public async Task AFailedDownloadNeitherSchedulesNorQuits()
    {
        _engine.DownloadFailure = new HttpRequestException("gone");
        bool quit = false;

        bool applied = await Updates().ApplyAsync(new AvailableUpdate("0.2.0", null), () =>
        {
            quit = true;
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);

        Assert.False(applied);
        Assert.False(quit);
        Assert.Empty(_engine.Scheduled);
    }

    [Fact]
    public async Task ACancelledDownloadNeitherSchedulesNorQuits()
    {
        using CancellationTokenSource cancel = new();
        await cancel.CancelAsync();
        _engine.DownloadFailure = new OperationCanceledException(cancel.Token);
        bool quit = false;

        bool applied = await Updates().ApplyAsync(new AvailableUpdate("0.2.0", null), () =>
        {
            quit = true;
            return Task.CompletedTask;
        }, cancel.Token);

        Assert.False(applied);
        Assert.False(quit);
        Assert.Empty(_engine.Scheduled);
        Assert.Contains(_logger.Entries, entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Information && entry.Message.Contains("cancelled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFailedSchedulingDoesNotQuit()
    {
        _engine.ApplyFailure = new IOException("Update.exe missing");
        bool quit = false;

        bool applied = await Updates().ApplyAsync(new AvailableUpdate("0.2.0", null), () =>
        {
            quit = true;
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);

        Assert.False(applied);
        Assert.False(quit);
    }

    [Fact]
    public async Task OutsideAnInstallationNothingIsApplied()
    {
        bool quit = false;

        bool applied = await new AppUpdates(null, () => true, _logger).ApplyAsync(new AvailableUpdate("0.2.0", null), () =>
        {
            quit = true;
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);

        Assert.False(applied);
        Assert.False(quit);
    }

    [Fact]
    public void TheUpdatesComeFromTheGitHubReleasesOfTheProject()
    {
        Assert.Equal("https://github.com/pfoltynek/LayerPeek", AppUpdates.RepositoryUrl);
    }

    private AppUpdates Updates() => new(_engine, () => _prereleases, _logger);

    private sealed class FakeUpdateEngine : IUpdateEngine
    {
        public AvailableUpdate? Next { get; set; }

        public Exception? CheckFailure { get; set; }

        public Exception? DownloadFailure { get; set; }

        public Exception? ApplyFailure { get; set; }

        public List<bool> Checks { get; } = [];

        public List<AvailableUpdate> Scheduled { get; } = [];

        public List<string> Steps { get; set; } = [];

        public Task<AvailableUpdate?> CheckAsync(bool prereleases, CancellationToken cancellationToken)
        {
            Checks.Add(prereleases);
            return CheckFailure is not null ? Task.FromException<AvailableUpdate?>(CheckFailure) : Task.FromResult(Next);
        }

        public Task DownloadAsync(AvailableUpdate update, CancellationToken cancellationToken)
        {
            if (DownloadFailure is not null)
            {
                return Task.FromException(DownloadFailure);
            }

            Steps.Add($"download {update.Version}");
            return Task.CompletedTask;
        }

        public void ApplyOnExit(AvailableUpdate update)
        {
            if (ApplyFailure is not null)
            {
                throw ApplyFailure;
            }

            Steps.Add($"apply on exit {update.Version}");
            Scheduled.Add(update);
        }
    }
}
