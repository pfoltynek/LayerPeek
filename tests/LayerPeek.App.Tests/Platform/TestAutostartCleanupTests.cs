#if DEBUG
using LayerPeek.App.Composition;
using LayerPeek.App.Platform;

namespace LayerPeek.App.Tests.Platform;

/// <summary>
/// The Debug-only cleanup mode <c>--layerpeek-e2e-clear-autostart</c> (T-019, user decision 2026-10-28): it removes the
/// autostart stand-ins of a crashed test run from the test data folder and nothing else; without the test data folder it
/// refuses.
/// </summary>
public sealed class TestAutostartCleanupTests : IDisposable
{
    private readonly string _data = TestDirectories.NewDirectoryPath();

    public void Dispose() => TestDirectories.Delete(_data);

    [Fact]
    public void RemovesOnlyTheStandInsOfTheTestDataFolder()
    {
        string loginItem = AppEnvironment.TestLoginItemPathIn(_data);
        string startup = AppEnvironment.ForTests(_data).AutostartShortcutPath();
        string other = Path.Combine(_data, "settings.json");
        foreach (string file in new[] { loginItem, startup, other })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "x");
        }

        StringWriter error = new();
        int exit = TestAutostartCleanup.RunFromCommandLine(name => name == AppEnvironment.DataDirectoryVariable ? _data : null, error);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, error.ToString());
        Assert.False(File.Exists(loginItem));
        Assert.False(File.Exists(startup));
        Assert.True(File.Exists(other));
        Assert.Equal([loginItem, startup], TestAutostartCleanup.StandIns(_data));
    }

    [Fact]
    public void NothingToRemoveIsFine()
    {
        Assert.Equal(0, TestAutostartCleanup.RunFromCommandLine(name => name == AppEnvironment.DataDirectoryVariable ? _data : null, new StringWriter()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative/folder")]
    public void WithoutTheTestDataFolderItRefuses(string? folder)
    {
        StringWriter error = new();

        Assert.Equal(1, TestAutostartCleanup.RunFromCommandLine(name => name == AppEnvironment.DataDirectoryVariable ? folder : null, error));
        Assert.Contains(AppEnvironment.DataDirectoryVariable, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheSwitchIsATestArgument()
    {
        Assert.Equal("--layerpeek-e2e-clear-autostart", AppEnvironment.ClearAutostartArgument);
        Assert.StartsWith(AppEnvironment.TestArgumentPrefix, AppEnvironment.ClearAutostartArgument, StringComparison.Ordinal);
        Assert.True(AppEnvironment.IsTestStart([AppEnvironment.ClearAutostartArgument], static _ => null));
    }
}
#endif
