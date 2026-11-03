using System.Net;
using System.Text;
using System.Text.Json.Nodes;

using LayerPeek.App.E2E.Tests.Infrastructure;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// The helpers of the tests of the test notification and the update (T-128) without the app or the desktop: they run in
/// every <c>dotnet test</c>. The fake GitHub answers on the loopback only.
/// </summary>
public sealed class NotificationAndUpdateInfrastructureTests
{
    [Fact]
    public void TheToastHistoryIsParsedOnlyWhenTheCountMatches()
    {
        string xml = "<toast launch=\"layerpeek:settings\"><visual><binding template=\"ToastGeneric\"><text>Test notification</text><text>Body</text></binding></visual><audio silent=\"true\"/></toast>";
        string line = "TOAST " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"layerpeek-test-notification\t\t{xml}"));

        HistoryToast toast = Assert.Single(ToastHistory.Parse($"noise\r\n{line}\r\nCOUNT 1\r\n")!);
        Assert.Equal("layerpeek-test-notification", toast.Tag);
        Assert.Equal(string.Empty, toast.Group);
        Assert.True(toast.IsSilent);
        Assert.Equal("layerpeek:settings", toast.Launch);
        Assert.Equal(["Test notification", "Body"], toast.Texts);

        Assert.Empty(ToastHistory.Parse("COUNT 0")!);
        Assert.Null(ToastHistory.Parse($"{line}\r\nCOUNT 2"));
        Assert.Null(ToastHistory.Parse(line));
    }

    [Fact]
    public void AToastWithoutAudioPlaysTheDefaultSound()
    {
        Assert.False(new HistoryToast("t", string.Empty, "<toast><visual><binding template=\"ToastGeneric\"><text>a</text></binding></visual></toast>").IsSilent);
        Assert.False(new HistoryToast("t", string.Empty, "<toast><audio src=\"ms-winsoundevent:Notification.Default\"/></toast>").IsSilent);
    }

    [Theory]
    [InlineData("0.3.1", "0.3.2")]
    [InlineData("1.0.9", "1.0.10")]
    [InlineData("0.4.0-beta.1", "0.4.1")]
    [InlineData("2.1", "2.1.1")]
    public void TheUpdateIsTheNextPatchVersion(string running, string next) => Assert.Equal(next, UpdateTests.NextPatch(running));

    [Fact]
    public void TheUpdateCopyIsOnlyAnUpdateFolderInAReservedFolderOfTheseTests()
    {
        string reserved = Path.Combine(Path.GetTempPath(), "LayerPeek.App.E2E.Tests.0a1b2c3d");
        string folder = Path.Combine(reserved, "update-1234abcd");
        Assert.Equal(folder, UpdateCopyKind.Checked(folder));
        Assert.Equal(folder, UpdateCopyKind.Checked(folder + Path.DirectorySeparatorChar));

        Assert.Throws<InvalidOperationException>(() => UpdateCopyKind.Checked(null));
        Assert.Throws<InvalidOperationException>(() => UpdateCopyKind.Checked("update-1234abcd"));
        Assert.Throws<InvalidOperationException>(() => UpdateCopyKind.Checked(Path.Combine(reserved, "update-")));
        Assert.Throws<InvalidOperationException>(() => UpdateCopyKind.Checked(Path.Combine(reserved, "run-1234abcd")));
        Assert.Throws<InvalidOperationException>(() => UpdateCopyKind.Checked(Path.Combine(Path.GetTempPath(), "update-1234abcd")));
        Assert.Throws<InvalidOperationException>(() => UpdateCopyKind.Checked(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LayerPeek")));
        Assert.Throws<InvalidOperationException>(() => UpdateCopyKind.Checked(Path.Combine(reserved, "update-1234abcd", "install")));
    }

    [Fact]
    public void TheUpdateCopyIsRestoredOnceItsFolderTrayEntriesAndNewLogsAreGone()
    {
        UpdateCopyKind kind = new();
        UpdateCopyState original = new(false, [], ["velopack.log"], true);
        Assert.True(kind.Matches(original, new UpdateCopyState(false, [], ["velopack.log"], true)));
        Assert.True(kind.Matches(original, new UpdateCopyState(false, [], [], true)));
        Assert.False(kind.Matches(original, new UpdateCopyState(true, [], ["velopack.log"], true)));
        Assert.False(kind.Matches(original, new UpdateCopyState(false, ["123"], ["velopack.log"], true)));
        Assert.False(kind.Matches(original, new UpdateCopyState(false, [], ["velopack.log", "velopack_LayerPeekE2E.log"], true)));
        Assert.False(kind.Matches(original with { VelopackFolderExists = false, VelopackLogs = [] }, new UpdateCopyState(false, [], [], true)));
    }

    [Fact]
    public void TheUpdateCopyIsAKindOfTheToastTests()
    {
        UpdateCopyKind kind = new();
        Assert.Equal("UpdateCopy", kind.Name);
        Assert.Equal(SystemChangeTraits.Toasts, kind.Trait);
        Assert.True(kind.ChangesSystem);
    }

    [Fact]
    public async Task TheFakeGitHubListsTheReleaseOnlyOncePublished()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using FakeGitHubReleases github = FakeGitHubReleases.Start();
        Assert.True(IPAddress.IsLoopback(IPAddress.Parse(github.BaseUri.Host)));
        using HttpClient http = new(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(10) };
        Uri releases = new(github.BaseUri, FakeGitHubReleases.ReleasesPath.TrimStart('/') + "?per_page=30");

        Assert.Equal("[]", await http.GetStringAsync(releases, cancellationToken));

        github.Publish("0.3.2");
        JsonArray list = JsonNode.Parse(await http.GetStringAsync(releases, cancellationToken))!.AsArray();
        JsonNode release = Assert.Single(list)!;
        Assert.Equal("v0.3.2", release["tag_name"]!.GetValue<string>());
        Assert.False(release["draft"]!.GetValue<bool>());
        Assert.False(release["prerelease"]!.GetValue<bool>());
        Assert.Contains(release["assets"]!.AsArray(), static asset => asset!["name"]!.GetValue<string>() == "SHA256SUMS-windows.txt" && asset["state"]!.GetValue<string>() == "uploaded");

        using HttpResponseMessage other = await http.GetAsync(new Uri(github.BaseUri, "repos/someone/else/releases"), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.Equal(3, github.Requests.Count);
        Assert.StartsWith($"GET {FakeGitHubReleases.ReleasesPath}?per_page=30 HTTP/1.1", github.Requests[0], StringComparison.Ordinal);
    }

    [Fact]
    public void TheTestAppIdOfTheTestsIsTheAppsOwn()
    {
        // T-133: the app id lives once in the app (AppEnvironment.TestAppUserModelId) and once here (the tests do not
        // reference the app); raising it is the recovery when Windows keeps the test app's notifications off.
        string source = File.ReadAllText(Path.Combine(LayerPeek.Tests.Shared.TestWorktree.Root, "src", "LayerPeek.App", "Composition", "AppEnvironment.cs"));
        Assert.Contains($"internal const string TestAppUserModelId = \"{AppSwitches.AppUserModelId}\";", source, StringComparison.Ordinal);
        Assert.True(AppSwitches.IsTestAppUserModelId(AppSwitches.AppUserModelId));
    }

    [Fact]
    public void TheRecoveryOfTheTestAppsNotificationsIsDocumentedWithTheCurrentTestAppId()
    {
        // T-133: the pre-flight points to docs/troubleshooting.md, which names the current test app id and the two constants.
        string doc = File.ReadAllText(Path.Combine(LayerPeek.Tests.Shared.TestWorktree.Root, "docs", "troubleshooting.md"));
        Assert.Contains($"`{AppSwitches.AppUserModelId}`", doc, StringComparison.Ordinal);
        Assert.Contains("AppEnvironment.TestAppUserModelId", doc, StringComparison.Ordinal);
        Assert.Contains("AppSwitches.AppUserModelId", doc, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("LayerPeek.E2E", true)]
    [InlineData("LayerPeek.E2E.2", true)]
    [InlineData("LayerPeek.E2E.10", true)]
    [InlineData("LayerPeek", false)]
    [InlineData("LayerPeek.E2Ex", false)]
    [InlineData("Other.LayerPeek.E2E", false)]
    [InlineData("LayerPeek.E2E.2 x", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyTheAppIdsOfTheTestAppAreTheTestApps(string? appUserModelId, bool expected) =>
        Assert.Equal(expected, AppSwitches.IsTestAppUserModelId(appUserModelId));

    [Fact]
    public void AnAppIdWithABackslashIsNeverTheTestApps() =>
        Assert.False(AppSwitches.IsTestAppUserModelId(AppSwitches.TestAppUserModelIdRoot + ".2" + Path.DirectorySeparatorChar + "x"));
}
