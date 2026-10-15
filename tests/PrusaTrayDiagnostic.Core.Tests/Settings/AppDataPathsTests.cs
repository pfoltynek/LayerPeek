using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.Core.Tests.Settings;

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
}
