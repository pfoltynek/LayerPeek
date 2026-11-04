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

    [Fact]
    public void DefaultCacheDirectoryIsTheStandardCacheFolderOfTheOs()
    {
        string directory = AppDataPaths.GetDefaultCacheDirectory();

        Assert.True(Path.IsPathFullyQualified(directory));
        if (OperatingSystem.IsWindows())
        {
            // Local, not roaming (T-154: images, never synchronized with the profile).
            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppDataPaths.AppFolderName, "cache"), directory);
        }
        else if (OperatingSystem.IsMacOS())
        {
            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Caches", AppDataPaths.AppFolderName), directory);
        }

        Assert.False(directory.StartsWith(AppDataPaths.GetDefaultDirectory(), StringComparison.OrdinalIgnoreCase));
    }
}
