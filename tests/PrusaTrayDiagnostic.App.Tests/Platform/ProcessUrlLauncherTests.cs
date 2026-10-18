using System.ComponentModel;
using System.Diagnostics;

using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.Platform;
using PrusaTrayDiagnostic.App.Tests.Tray;

namespace PrusaTrayDiagnostic.App.Tests.Platform;

public sealed class ProcessUrlLauncherTests
{
    private readonly ListLogger<ProcessUrlLauncher> _logger = new();
    private readonly List<ProcessStartInfo> _started = [];

    [Fact]
    public async Task WindowsOpensAddressThroughShell()
    {
        bool opened = await Create(UrlLaunchPlatform.Windows).OpenAsync(new Uri("http://192.168.1.10/"));

        Assert.True(opened);
        ProcessStartInfo info = Assert.Single(_started);
        Assert.Equal("http://192.168.1.10/", info.FileName);
        Assert.True(info.UseShellExecute);
        Assert.Empty(info.ArgumentList);
    }

    [Theory]
    [InlineData(UrlLaunchPlatform.MacOS, "open")]
    [InlineData(UrlLaunchPlatform.Linux, "xdg-open")]
    public async Task UnixPassesAddressAsSingleArgument(UrlLaunchPlatform platform, string tool)
    {
        bool opened = await Create(platform).OpenAsync(new Uri("https://printer.local:8443/a b/"));

        Assert.True(opened);
        ProcessStartInfo info = Assert.Single(_started);
        Assert.Equal(tool, info.FileName);
        Assert.False(info.UseShellExecute);
        Assert.Equal(["https://printer.local:8443/a%20b/"], info.ArgumentList);
        Assert.Equal(string.Empty, info.Arguments);
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("ftp://printer/")]
    [InlineData("http://user:pw@printer/")]
    public async Task UnsafeAddressIsNeverLaunched(string uri)
    {
        bool opened = await Create(UrlLaunchPlatform.Windows).OpenAsync(new Uri(uri));

        Assert.False(opened);
        Assert.Empty(_started);
        Assert.Equal(LogLevel.Warning, Assert.Single(_logger.Entries).Level);
    }

    [Fact]
    public async Task FailedLaunchIsLoggedAndReported()
    {
        ProcessUrlLauncher launcher = new(_logger, UrlLaunchPlatform.Windows, _ => throw new Win32Exception(2, "No browser"));

        bool opened = await launcher.OpenAsync(new Uri("http://printer/"));

        Assert.False(opened);
        (LogLevel level, _, Exception? exception) = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Error, level);
        Assert.IsType<Win32Exception>(exception);
    }

    [Fact]
    public async Task StartedProcessHandleIsReleased()
    {
        DisposeProbe probe = new();
        ProcessUrlLauncher launcher = new(_logger, UrlLaunchPlatform.Windows, _ => probe);

        await launcher.OpenAsync(new Uri("http://printer/"));

        Assert.True(probe.Disposed);
    }

    [Fact]
    public async Task NullAddressIsRejected()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Create(UrlLaunchPlatform.Windows).OpenAsync(null!));
    }

    [Fact]
    public async Task WindowsOpensAddressWithCredentialsThroughShell()
    {
        bool opened = await Create(UrlLaunchPlatform.Windows)
            .OpenWithCredentialsAsync(new Uri("http://192.168.1.10/"), "maker", "p@ss w", Ct);

        Assert.True(opened);
        ProcessStartInfo info = Assert.Single(_started);
        Assert.Equal("http://maker:p%40ss%20w@192.168.1.10/", info.FileName);
        Assert.True(info.UseShellExecute);
        Assert.Empty(info.ArgumentList);
    }

    [Theory]
    [InlineData(UrlLaunchPlatform.MacOS, "open")]
    [InlineData(UrlLaunchPlatform.Linux, "xdg-open")]
    public async Task UnixPassesAddressWithCredentialsAsSingleArgument(UrlLaunchPlatform platform, string tool)
    {
        // Non-ASCII stays percent-encoded (System.Uri.ToString would turn it back into raw characters and spaces).
        bool opened = await Create(platform)
            .OpenWithCredentialsAsync(new Uri("https://printer.local:8443/"), "maker", "kůň \"; rm -rf ~", Ct);

        Assert.True(opened);
        ProcessStartInfo info = Assert.Single(_started);
        Assert.Equal(tool, info.FileName);
        Assert.False(info.UseShellExecute);
        Assert.Equal(["https://maker:k%C5%AF%C5%88%20%22%3B%20rm%20-rf%20~@printer.local:8443/"], info.ArgumentList);
        Assert.Equal(string.Empty, info.Arguments);
    }

    [Theory]
    [InlineData("http://user:other@printer/")]
    [InlineData("http://user@printer/")]
    [InlineData("http://@printer/")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("ftp://printer/")]
    public async Task CredentialsAreNeverAddedToAnUnsafeAddress(string uri)
    {
        bool opened = await Create(UrlLaunchPlatform.Windows).OpenWithCredentialsAsync(new Uri(uri), "maker", "TopSecret1", Ct);

        Assert.False(opened);
        Assert.Empty(_started);
        (LogLevel level, string message, _) = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Warning, level);
        Assert.DoesNotContain("TopSecret1", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedLaunchWithCredentialsLogsNeitherPasswordNorAddress()
    {
        // Process.Start puts the file name (here the address with the password) into its exception message.
        ProcessUrlLauncher launcher = new(_logger, UrlLaunchPlatform.Windows, info =>
            throw new Win32Exception(2, $"An error occurred trying to start process '{info.FileName}'."));

        bool opened = await launcher.OpenWithCredentialsAsync(new Uri("http://printer/"), "maker", "TopSecret1", Ct);

        Assert.False(opened);
        (LogLevel level, string message, Exception? exception) = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Error, level);
        Assert.Null(exception);
        Assert.Contains("printer", message, StringComparison.Ordinal);
        Assert.DoesNotContain("TopSecret1", message, StringComparison.Ordinal);
        Assert.DoesNotContain("maker:", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelledLaunchWithCredentialsStartsNothing()
    {
        bool opened = await Create(UrlLaunchPlatform.Windows)
            .OpenWithCredentialsAsync(new Uri("http://printer/"), "maker", "pw", new CancellationToken(canceled: true));

        Assert.False(opened);
        Assert.Empty(_started);
    }

    [Theory]
    [InlineData("", "pw")]
    [InlineData("maker", "")]
    public async Task EmptyCredentialsAreRejected(string userName, string password)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Create(UrlLaunchPlatform.Windows).OpenWithCredentialsAsync(new Uri("http://printer/"), userName, password, Ct));
        Assert.Empty(_started);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ProcessUrlLauncher Create(UrlLaunchPlatform platform) =>
        new(_logger, platform, info =>
        {
            lock (_started)
            {
                _started.Add(info);
            }

            return null;
        });

    private sealed class DisposeProbe : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
