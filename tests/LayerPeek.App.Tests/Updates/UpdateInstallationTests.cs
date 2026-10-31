using LayerPeek.App.Composition;
using LayerPeek.App.Installer;

namespace LayerPeek.App.Tests.Updates;

/// <summary>
/// T-058: which copies update themselves through Velopack (Windows since T-017, macOS: every <c>.app</c> of <c>vpk</c> with
/// <c>UpdateMac</c> and <c>sq.version</c>), which processes ask GitHub at all, and the update feed of the end-to-end test
/// (Q10 a: only the loopback or a local folder, only in the test mode).
/// </summary>
public sealed class UpdateInstallationTests
{
    private const string MacApp = "/Applications/LayerPeek.app/Contents/MacOS/LayerPeek";

    /// <summary>System.IO.Path of Windows turns the macOS paths of these tests into \ paths; the code runs only on macOS.</summary>
    private const string MacPathsOnly = "System.IO.Path parses macOS paths only on macOS.";

    [Fact]
    public void AMacAppOfVelopackHasUpdateMacAndTheManifestNextToTheExecutable()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), MacPathsOnly);

        HashSet<string> files = ["/Applications/LayerPeek.app/Contents/MacOS/UpdateMac", "/Applications/LayerPeek.app/Contents/MacOS/sq.version"];

        Assert.True(VelopackInstallation.IsInstalledMac(MacApp, files.Contains));
        // Any folder and any name of the bundle: the .dmg, the .tar.gz and Portable.zip alike.
        Assert.True(VelopackInstallation.IsInstalledMac("/Users/u/Downloads/Copy of LayerPeek.app/Contents/MacOS/LayerPeek", static path => path.EndsWith("UpdateMac", StringComparison.Ordinal) || path.EndsWith("sq.version", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(MacApp, false, true)]
    [InlineData(MacApp, true, false)]
    [InlineData("/Users/u/src/LayerPeek/src/LayerPeek.App/bin/Debug/net11.0/LayerPeek", true, true)]
    [InlineData("/Applications/LayerPeek.app/Contents/MacOS/LayerPeek.App.Tests", true, true)]
    [InlineData("LayerPeek.app/Contents/MacOS/LayerPeek", true, true)]
    [InlineData(null, true, true)]
    public void TheAppOf030AndDevelopmentBuildsAreNoInstallation(string? executable, bool hasUpdater, bool hasManifest)
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), MacPathsOnly);

        bool Exists(string path) =>
            (hasUpdater && path.EndsWith("/UpdateMac", StringComparison.Ordinal)) || (hasManifest && path.EndsWith("/sq.version", StringComparison.Ordinal));

        Assert.False(VelopackInstallation.IsInstalledMac(executable, Exists));
    }

    [Fact]
    public void OnMacOSVelopackRunsOnlyInAnInstallation()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), MacPathsOnly);

        int runs = 0;
        HashSet<string> files = ["/Applications/LayerPeek.app/Contents/MacOS/UpdateMac", "/Applications/LayerPeek.app/Contents/MacOS/sq.version"];

        Assert.Null(VelopackStartup.Run([], MacApp, files.Contains, _ => runs++, out Exception? failure, VelopackInstallation.IsInstalledMac));
        Assert.Null(failure);
        Assert.Equal(1, runs);

        Assert.Null(VelopackStartup.Run([], MacApp, static _ => false, _ => runs++, out _, VelopackInstallation.IsInstalledMac));
        Assert.Equal(1, runs);
    }

    [Theory]
    [InlineData("/Applications/LayerPeek.app/Contents/MacOS/LayerPeek", true)]
    [InlineData("/c/Users/u/AppData/Local/LayerPeek/current/LayerPeek.exe", true)]
    [InlineData("/Users/u/src/LayerPeek/src/LayerPeek.App/bin/Debug/net11.0/LayerPeek", true)]
    [InlineData("/Users/u/src/LayerPeek/tests/bin/LayerPeek.App.Tests", false)]
    [InlineData("/c/src/tests/bin/LayerPeek.App.UiTests.exe", false)]
    [InlineData("/usr/local/share/dotnet/dotnet", false)]
    [InlineData("/x/LayerPeek.dll", false)]
    [InlineData(null, false)]
    public void OnlyTheAppItselfAsksGitHub(string? executable, bool asks)
    {
        Assert.Equal(asks, AppServices.IsAppExecutable(executable));
    }

    [Theory]
    [InlineData("http://127.0.0.1:41234/")]
    [InlineData("http://127.0.0.1:41234/feed/")]
    [InlineData("http://[::1]:41234/")]
    public void TheTestFeedMayBeOnTheLoopback(string value)
    {
        UpdateSource? source = UpdateSource.TryCreateForTest(value, static _ => false);

        Assert.Equal(new Uri(value), source?.Address);
        Assert.Null(source?.Folder);
    }

    [Theory]
    [InlineData("http://localhost:41234/")]
    [InlineData("http://192.168.1.10:41234/")]
    [InlineData("https://127.0.0.1:41234/")]
    [InlineData("https://github.com/pfoltynek/LayerPeek")]
    [InlineData("http://user:secret@127.0.0.1:41234/")]
    [InlineData("ftp://127.0.0.1/")]
    [InlineData("relative/folder")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingElseIsRefused(string? value)
    {
        Assert.Null(UpdateSource.TryCreateForTest(value, static _ => true));
    }

    [Fact]
    public void TheTestFeedMayBeAnExistingLocalFolder()
    {
        string folder = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "layerpeek-feed"));

        Assert.Equal(folder, UpdateSource.TryCreateForTest(folder, static _ => true)?.Folder);
        Assert.Null(UpdateSource.TryCreateForTest(folder, static _ => false));
    }

#if DEBUG
    [Fact]
    public void TheTestModeReadsItsFeedAndRefusesAnyOther()
    {
        string data = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "layerpeek-e2e-data"));
        Dictionary<string, string?> variables = new()
        {
            [AppEnvironment.DataDirectoryVariable] = data,
            [AppEnvironment.TestUpdateSourceVariable] = "http://127.0.0.1:41234/",
        };

        AppEnvironment environment = AppEnvironment.FromVariables(name => variables.GetValueOrDefault(name));
        Assert.Equal(new Uri("http://127.0.0.1:41234/"), environment.TestUpdateSource?.Address);

        variables[AppEnvironment.TestUpdateSourceVariable] = "https://github.com/pfoltynek/LayerPeek";
        Assert.Throws<ArgumentException>(() => AppEnvironment.FromVariables(name => variables.GetValueOrDefault(name)));
    }

    [Fact]
    public void WithoutTheTestModeTheFeedIsIgnored()
    {
        Dictionary<string, string?> variables = new() { [AppEnvironment.TestUpdateSourceVariable] = "http://127.0.0.1:41234/" };

        AppEnvironment environment = AppEnvironment.FromVariables(name => variables.GetValueOrDefault(name));

        Assert.False(environment.IsIsolated);
        Assert.Null(environment.TestUpdateSource);
        Assert.Contains(AppEnvironment.TestUpdateSourceVariable, environment.IgnoredTestSwitches);
    }
#endif
}
