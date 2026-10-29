using LayerPeek.App.Composition;
using LayerPeek.App.Platform.MacOS;

namespace LayerPeek.App.Tests.Platform.MacOS;

/// <summary>
/// The Login Item of the test mode (T-019, user decision 2026-10-29): a file in the test data folder, never SMAppService.
/// Runs on every OS (plain files).
/// </summary>
public sealed class StandInLoginItemTests : IDisposable
{
    private readonly string _data = TestDirectories.NewDirectoryPath();

    public void Dispose() => TestDirectories.Delete(_data);

    [Fact]
    public void RegisteringWritesOnlyTheFileInTheTestDataFolder()
    {
        string path = AppEnvironment.TestLoginItemPathIn(_data);
        StandInLoginItem item = new(path, "/Volumes/x/LayerPeek");
        Assert.Equal(LoginItemStatus.NotRegistered, item.Status());

        item.Register();

        Assert.Equal(LoginItemStatus.Enabled, item.Status());
        Assert.Equal(Path.Combine(_data, "LoginItems", "LayerPeek.loginitem"), path);
        Assert.Equal("/Volumes/x/LayerPeek\n", File.ReadAllText(path));
        Assert.Equal([Path.Combine(_data, "LoginItems")], Directory.GetFileSystemEntries(_data));

        item.Unregister();
        item.Unregister();

        Assert.Equal(LoginItemStatus.NotRegistered, item.Status());
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void OutsideTheTestModeThereIsNoStandIn()
    {
        Assert.Null(AppEnvironment.Production.TestLoginItemPath());
    }

    [Fact]
    public void ARelativePathIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new StandInLoginItem("LoginItems/LayerPeek.loginitem", "LayerPeek"));
    }
}
