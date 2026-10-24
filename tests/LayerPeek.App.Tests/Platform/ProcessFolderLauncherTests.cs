using System.Diagnostics;

using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Platform;

namespace LayerPeek.App.Tests.Platform;

/// <summary>"Open log folder" (T-048): the file manager by absolute path, the folder as one argument.</summary>
public sealed class ProcessFolderLauncherTests : IDisposable
{
    private readonly string _folder = TestDirectories.NewDirectoryPath();

    public void Dispose() => TestDirectories.Delete(_folder);

    [Fact]
    public void WindowsStartsExplorerFromTheWindowsFolder()
    {
        ProcessStartInfo info = ProcessFolderLauncher.CreateStartInfo(@"C:\Users\me\AppData\Local\LayerPeek\logs", UrlLaunchPlatform.Windows, @"C:\Windows");

        Assert.Equal(@"C:\Windows" + Path.DirectorySeparatorChar + "explorer.exe", info.FileName);
        Assert.False(info.UseShellExecute);
        Assert.Equal([@"C:\Users\me\AppData\Local\LayerPeek\logs"], info.ArgumentList);
    }

    [Fact]
    public void UnknownWindowsFolderNeverFallsBackToARelativeExplorer() =>
        Assert.Throws<InvalidOperationException>(() => ProcessFolderLauncher.CreateStartInfo(@"C:\logs", UrlLaunchPlatform.Windows, windowsDirectory: string.Empty));

    [Fact]
    public void MacOsStartsOpenByAbsolutePath()
    {
        ProcessStartInfo info = ProcessFolderLauncher.CreateStartInfo("/Users/me/Library/Logs/LayerPeek", UrlLaunchPlatform.MacOS);

        Assert.Equal("/usr/bin/open", info.FileName);
        Assert.False(info.UseShellExecute);
        Assert.Equal(["/Users/me/Library/Logs/LayerPeek"], info.ArgumentList);
    }

    [Fact]
    public async Task MissingFolderIsCreatedBeforeItOpens()
    {
        List<ProcessStartInfo> started = [];
        ProcessFolderLauncher launcher = new(NullLogger.Instance, UrlLaunchPlatform.MacOS, info =>
        {
            started.Add(info);
            return null;
        });

        Assert.True(await launcher.OpenAsync(_folder));

        Assert.True(Directory.Exists(_folder));
        Assert.Equal(_folder, Assert.Single(Assert.Single(started).ArgumentList));
    }

    [Fact]
    public async Task FailureToStartIsReportedNotThrown()
    {
        ProcessFolderLauncher launcher = new(NullLogger.Instance, UrlLaunchPlatform.MacOS, static _ => throw new System.ComponentModel.Win32Exception(2));

        Assert.False(await launcher.OpenAsync(_folder));
    }

    [Fact]
    public async Task RelativeFolderIsRejected()
    {
        ProcessFolderLauncher launcher = new(NullLogger.Instance, UrlLaunchPlatform.MacOS, static _ => null);

        await Assert.ThrowsAsync<ArgumentException>(() => launcher.OpenAsync("logs"));
    }
}
