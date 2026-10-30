using LayerPeek.App.Installer;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Updates;

namespace LayerPeek.App.Tests.Installer;

/// <summary>
/// The Velopack update wiring of T-017 as the update checker of an installation (T-058): a failed check throws for the update
/// service to log, pre-releases are asked for as told, and an update is applied only after the app quit normally (its state
/// saved), because Velopack ends every process of the installation.
/// </summary>
public sealed class AppUpdatesTests
{
    private readonly FakeUpdateEngine _engine = new();
    private readonly ListLogger<AppUpdates> _logger = new();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheCheckAsksForPrereleasesAsTold(bool prereleases)
    {
        _engine.Next = new AvailableUpdate("0.2.0", "notes");

        UpdateRelease? release = await Updates().CheckAsync(prereleases, TestContext.Current.CancellationToken);

        Assert.Equal(ReleaseVersion.Parse("0.2.0"), release?.Version);
        Assert.Equal([prereleases], _engine.Checks);
    }

    [Fact]
    public async Task TheReleasePageIsTheTagOnGitHub()
    {
        _engine.Next = new AvailableUpdate("0.4.0-beta.2", null);

        UpdateRelease? release = await Updates().CheckAsync(true, TestContext.Current.CancellationToken);

        Assert.Equal(new Uri("https://github.com/pfoltynek/LayerPeek/releases/tag/v0.4.0-beta.2"), release?.ReleasePage);
        Assert.True(release?.IsPrerelease);
    }

    [Fact]
    public async Task NoUpdateIsNull()
    {
        Assert.Null(await Updates().CheckAsync(true, TestContext.Current.CancellationToken));
    }

    public static TheoryData<Exception> CheckFailures => new()
    {
        new HttpRequestException("Response status code does not indicate success: 404 (Not Found).", null, System.Net.HttpStatusCode.NotFound),
        new HttpRequestException("403 rate limit", null, System.Net.HttpStatusCode.Forbidden),
        new TaskCanceledException("timeout"),
        new System.Text.Json.JsonException("broken feed"),
        new InvalidOperationException("anything"),
    };

    [Theory]
    [MemberData(nameof(CheckFailures))]
    public async Task AFailedCheckThrowsForTheServiceToLog(Exception failure)
    {
        _engine.CheckFailure = failure;

        Exception thrown = await Assert.ThrowsAnyAsync<Exception>(() => Updates().CheckAsync(true, TestContext.Current.CancellationToken));
        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task AFeedVersionThatIsNoSemanticVersionFailsTheCheck()
    {
        _engine.Next = new AvailableUpdate("0.2", null);

        await Assert.ThrowsAsync<FormatException>(() => Updates().CheckAsync(true, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ApplyingDownloadsSchedulesTheUpdaterAndThenQuits()
    {
        _engine.Next = new AvailableUpdate("0.2.0", null);
        AppUpdates updates = Updates();
        UpdateRelease release = (await updates.CheckAsync(false, TestContext.Current.CancellationToken))!;
        List<string> steps = [];
        _engine.Steps = steps;

        bool applied = await updates.ApplyAsync(release, includePrereleases: false, () =>
        {
            steps.Add("quit");
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);

        Assert.True(applied);
        // The updater waits for this process to exit: the quit saves the state first (T-017, docs/velopack.md point 5).
        Assert.Equal(["download 0.2.0", "apply on exit 0.2.0", "quit"], steps);
        // The update of the check, no second check.
        Assert.Single(_engine.Checks);
    }

    [Fact]
    public async Task AReleaseRememberedFromAnEarlierRunIsCheckedAgainFirst()
    {
        _engine.Next = new AvailableUpdate("0.2.0", null);
        List<string> steps = [];
        _engine.Steps = steps;

        bool applied = await Updates().ApplyAsync(Release("0.2.0"), includePrereleases: true, () => Task.CompletedTask, TestContext.Current.CancellationToken);

        Assert.True(applied);
        Assert.Equal([true], _engine.Checks);
        Assert.Equal(["download 0.2.0", "apply on exit 0.2.0"], steps);
    }

    [Fact]
    public async Task AReleaseNoLongerInTheFeedIsNotApplied()
    {
        _engine.Next = new AvailableUpdate("0.3.0", null);
        bool quit = false;

        bool applied = await Updates().ApplyAsync(Release("0.2.0"), includePrereleases: true, () =>
        {
            quit = true;
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);

        Assert.False(applied);
        Assert.False(quit);
        Assert.Empty(_engine.Scheduled);
    }

    [Fact]
    public async Task AFailedDownloadNeitherSchedulesNorQuits()
    {
        _engine.Next = new AvailableUpdate("0.2.0", null);
        _engine.DownloadFailure = new HttpRequestException("gone");
        bool quit = false;

        bool applied = await Updates().ApplyAsync(Release("0.2.0"), true, () =>
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
        _engine.Next = new AvailableUpdate("0.2.0", null);
        _engine.DownloadFailure = new OperationCanceledException(cancel.Token);
        AppUpdates updates = Updates();
        UpdateRelease release = (await updates.CheckAsync(true, TestContext.Current.CancellationToken))!;
        bool quit = false;

        bool applied = await updates.ApplyAsync(release, true, () =>
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
        _engine.Next = new AvailableUpdate("0.2.0", null);
        _engine.ApplyFailure = new IOException("Update.exe missing");
        bool quit = false;

        bool applied = await Updates().ApplyAsync(Release("0.2.0"), true, () =>
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

    private static UpdateRelease Release(string version) =>
        new(ReleaseVersion.Parse(version), UpdateRelease.PageFor(ReleaseVersion.Parse(version)), IsPrerelease: false);

    private AppUpdates Updates() => new(_engine, _logger);

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
