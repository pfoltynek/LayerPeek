using System.ComponentModel;
using System.Diagnostics;

using Microsoft.Extensions.Logging;

using LayerPeek.App.Platform;
using LayerPeek.App.Tests.Tray;

namespace LayerPeek.App.Tests.Platform;

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
    [InlineData(UrlLaunchPlatform.MacOS, "/usr/bin/open")]
    [InlineData(UrlLaunchPlatform.Linux, "/usr/bin/xdg-open")]
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

    [Fact]
    public void MacOSStartsOpenByAbsolutePathWithAddressAsOnlyArgument()
    {
        // Never looked up through PATH: a planted "open" would receive the proxy address with its token (L4).
        ProcessStartInfo info = ProcessUrlLauncher.CreateStartInfo(ProxyAddress.AbsoluteUri, UrlLaunchPlatform.MacOS);

        Assert.Equal("/usr/bin/open", info.FileName);
        Assert.False(info.UseShellExecute);
        Assert.Equal([ProxyAddress.AbsoluteUri], info.ArgumentList);
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
    public async Task WindowsOpensSecretAddressThroughShell()
    {
        bool opened = await Create(UrlLaunchPlatform.Windows).OpenSecretAsync(ProxyAddress, Ct);

        Assert.True(opened);
        ProcessStartInfo info = Assert.Single(_started);
        Assert.Equal(ProxyAddress.AbsoluteUri, info.FileName);
        Assert.True(info.UseShellExecute);
        Assert.Empty(info.ArgumentList);
        Assert.Empty(_logger.Entries);
    }

    [Theory]
    [InlineData(UrlLaunchPlatform.MacOS, "/usr/bin/open")]
    [InlineData(UrlLaunchPlatform.Linux, "/usr/bin/xdg-open")]
    public async Task UnixPassesSecretAddressAsSingleArgument(UrlLaunchPlatform platform, string tool)
    {
        bool opened = await Create(platform).OpenSecretAsync(ProxyAddress, Ct);

        Assert.True(opened);
        ProcessStartInfo info = Assert.Single(_started);
        Assert.Equal(tool, info.FileName);
        Assert.False(info.UseShellExecute);
        Assert.Equal([ProxyAddress.AbsoluteUri], info.ArgumentList);
        Assert.Equal(string.Empty, info.Arguments);
    }

    [Theory]
    [InlineData("http://user:pw@127.0.0.1:50123/token/")]
    [InlineData("http://@127.0.0.1:50123/token/")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("ftp://127.0.0.1/token/")]
    public async Task UnsafeSecretAddressIsNeverLaunchedNorLogged(string uri)
    {
        bool opened = await Create(UrlLaunchPlatform.Windows).OpenSecretAsync(new Uri(uri), Ct);

        Assert.False(opened);
        Assert.Empty(_started);
        (LogLevel level, string message, _) = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Warning, level);
        Assert.DoesNotContain("token", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedSecretLaunchLogsNeitherTheAddressNorTheException()
    {
        // Process.Start puts the file name (here the proxy address with its token) into its exception message.
        ProcessUrlLauncher launcher = new(_logger, UrlLaunchPlatform.Windows, info =>
            throw new Win32Exception(2, $"An error occurred trying to start process '{info.FileName}'."));

        bool opened = await launcher.OpenSecretAsync(ProxyAddress, Ct);

        Assert.False(opened);
        (LogLevel level, string message, Exception? exception) = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Error, level);
        Assert.Null(exception);
        Assert.Contains(nameof(Win32Exception), message, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, message, StringComparison.Ordinal);
        Assert.DoesNotContain(ProxyAddress.AbsoluteUri, message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelledSecretLaunchStartsNothing()
    {
        bool opened = await Create(UrlLaunchPlatform.Windows).OpenSecretAsync(ProxyAddress, new CancellationToken(canceled: true));

        Assert.False(opened);
        Assert.Empty(_started);
    }

    [Fact]
    public async Task SecretLaunchReleasesTheProcessHandle()
    {
        DisposeProbe probe = new();
        ProcessUrlLauncher launcher = new(_logger, UrlLaunchPlatform.Windows, _ => probe);

        Assert.True(await launcher.OpenSecretAsync(ProxyAddress, Ct));
        Assert.True(probe.Disposed);
    }

    [Fact]
    public async Task NullSecretAddressIsRejected()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Create(UrlLaunchPlatform.Windows).OpenSecretAsync(null!, Ct));
    }

    private const string Token = "Zm9vYmFyU2VjcmV0VG9rZW4xMjM0NTY";

    private static readonly Uri ProxyAddress = new($"http://127.0.0.1:50123/{Token}/");

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
