using LayerPeek.Core.Settings;

namespace LayerPeek.Core.Tests.Settings;

public class AppDataPathsTests
{
    [Fact]
    public void DefaultDirectoryIsPlatformSpecificAppDataFolder()
    {
        string directory = AppDataPaths.GetDefaultDirectory();

        Assert.True(Path.IsPathFullyQualified(directory));
        Assert.Equal(AppDataPaths.AppFolderName, Path.GetFileName(directory));

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Path.GetDirectoryName(directory));
        }
        else if (OperatingSystem.IsMacOS())
        {
            string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support");
            Assert.Equal(expected, Path.GetDirectoryName(directory));
        }
    }

    [Fact]
    public void DefaultLogDirectoryIsTheStandardLogFolderOfTheOs()
    {
        string directory = AppDataPaths.GetDefaultLogDirectory();

        Assert.True(Path.IsPathFullyQualified(directory));
        if (OperatingSystem.IsWindows())
        {
            // Local, not roaming.
            string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppDataPaths.AppFolderName, "logs");
            Assert.Equal(expected, directory);
        }
        else if (OperatingSystem.IsMacOS())
        {
            string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", AppDataPaths.AppFolderName);
            Assert.Equal(expected, directory);
        }

        // Never next to settings.json.
        Assert.False(directory.StartsWith(AppDataPaths.GetDefaultDirectory(), StringComparison.OrdinalIgnoreCase));
    }
}
