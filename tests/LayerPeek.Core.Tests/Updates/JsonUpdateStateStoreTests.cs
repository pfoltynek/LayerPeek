using LayerPeek.Core.Tests.Monitoring;
using LayerPeek.Core.Updates;

namespace LayerPeek.Core.Tests.Updates;

/// <summary><c>update-state.json</c> (T-058): what the update check remembers between runs, never a reason to fail.</summary>
public sealed class JsonUpdateStateStoreTests : IDisposable
{
    private readonly string _folder = TestDirectories.NewDirectoryPath();
    private readonly ListLogger<JsonUpdateStateStore> _logger = new();

    public void Dispose() => TestDirectories.Delete(_folder);

    [Fact]
    public async Task NoFileIsAnEmptyState()
    {
        Assert.Equal(UpdateState.Empty, await Store().LoadAsync(TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public async Task TheStateRoundTrips()
    {
        ReleaseVersion version = ReleaseVersion.Parse("0.4.0-beta.1");
        UpdateState state = new(
            new DateTimeOffset(2026, 10, 30, 9, 15, 0, TimeSpan.Zero),
            new UpdateRelease(version, new Uri("https://github.com/pfoltynek/LayerPeek/releases/tag/v0.4.0-beta.1"), IsPrerelease: true),
            ReleaseVersion.Parse("0.3.2"));

        await Store().SaveAsync(state, TestContext.Current.CancellationToken);
        UpdateState loaded = await Store().LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(state.LastCheck, loaded.LastCheck);
        Assert.Equal(state.Latest, loaded.Latest);
        Assert.Equal(state.NotifiedVersion, loaded.NotifiedVersion);
        Assert.False(File.Exists(Path.Combine(_folder, JsonUpdateStateStore.FileName + ".tmp")));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "version": 2, "lastCheck": "2026-10-30T09:15:00+00:00" }""")]
    [InlineData("""{ "version": "one" }""")]
    public async Task ADamagedOrNewerFileIsAnEmptyState(string content)
    {
        Directory.CreateDirectory(_folder);
        await File.WriteAllTextAsync(Path.Combine(_folder, JsonUpdateStateStore.FileName), content, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateState.Empty, await Store().LoadAsync(TestContext.Current.CancellationToken));
        Assert.NotEmpty(_logger.Entries);
    }

    [Theory]
    [InlineData("http://github.com/pfoltynek/LayerPeek/releases/tag/v0.4.0")]
    [InlineData("https://example.com/releases/tag/v0.4.0")]
    [InlineData("https://user@github.com/pfoltynek/LayerPeek/releases/tag/v0.4.0")]
    [InlineData("https://github.com:8443/pfoltynek/LayerPeek/releases/tag/v0.4.0")]
    [InlineData("file:///etc/passwd")]
    public async Task OnlyAReleasePageOnGitHubIsReadBack(string page)
    {
        Directory.CreateDirectory(_folder);
        await File.WriteAllTextAsync(
            Path.Combine(_folder, JsonUpdateStateStore.FileName),
            $$"""{ "version": 1, "latest": { "version": "0.4.0", "page": "{{page}}", "prerelease": false }, "notifiedVersion": "0.4.0" }""",
            TestContext.Current.CancellationToken);

        UpdateState loaded = await Store().LoadAsync(TestContext.Current.CancellationToken);

        Assert.Null(loaded.Latest);
        Assert.Equal(ReleaseVersion.Parse("0.4.0"), loaded.NotifiedVersion);
    }

    [Fact]
    public async Task AnInvalidVersionReadsAsMissing()
    {
        Directory.CreateDirectory(_folder);
        await File.WriteAllTextAsync(
            Path.Combine(_folder, JsonUpdateStateStore.FileName),
            """{ "version": 1, "latest": { "version": "next", "page": "https://github.com/pfoltynek/LayerPeek/releases" }, "notifiedVersion": "0.4" }""",
            TestContext.Current.CancellationToken);

        UpdateState loaded = await Store().LoadAsync(TestContext.Current.CancellationToken);

        Assert.Null(loaded.Latest);
        Assert.Null(loaded.NotifiedVersion);
    }

    private JsonUpdateStateStore Store() => new(_folder, TimeProvider.System, _logger);
}
