using System.ComponentModel;
using System.Diagnostics;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using PrusaTrayDiagnostic.App.Platform;
using PrusaTrayDiagnostic.App.Tests.Settings;
using PrusaTrayDiagnostic.App.Tray.Menu;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Web;
using PrusaTrayDiagnostic.PrusaLink.WebProxy;

namespace PrusaTrayDiagnostic.App.Tests.Tray.Menu;

public sealed class PrinterWebOpenerTests : IAsyncDisposable
{
    private static readonly Uri PrinterUri = new("http://192.168.1.10/");

    private readonly FakeUrlLauncher _launcher = new();
    private readonly FakeWebGateway _gateway = new();
    private readonly ListLogger<PrinterWebOpener> _logger = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => _gateway.DisposeAsync();

    [Fact]
    public async Task PrusaLinkOpensTheProxyAddress()
    {
        bool opened = await CreateOpener().OpenAsync(PrinterUri, PrusaLink(userName: "admin"), Ct);

        Assert.True(opened);
        Assert.Equal([FakeWebGateway.ProxyAddress], _launcher.OpenedSecret);
        Assert.Empty(_launcher.Opened);
        Assert.Equal("admin", Assert.Single(_gateway.Opened).UserName);
    }

    [Fact]
    public async Task GatewayIsAskedOnEveryClick()
    {
        PrinterWebOpener opener = CreateOpener();

        await opener.OpenAsync(PrinterUri, PrusaLink(), Ct);
        await opener.OpenAsync(PrinterUri, PrusaLink(), Ct);

        Assert.Equal(2, _gateway.Opened.Count);
        Assert.Equal([FakeWebGateway.ProxyAddress, FakeWebGateway.ProxyAddress], _launcher.OpenedSecret);
    }

    [Theory]
    [InlineData(PrinterWebFallback.PasswordMissing)]
    [InlineData(PrinterWebFallback.PasswordUnreadable)]
    [InlineData(PrinterWebFallback.PasswordRejected)]
    [InlineData(PrinterWebFallback.AccessDenied)]
    [InlineData(PrinterWebFallback.UnsupportedAuthentication)]
    [InlineData(PrinterWebFallback.NotReachable)]
    [InlineData(PrinterWebFallback.UnexpectedResponse)]
    [InlineData(PrinterWebFallback.ProxyUnavailable)]
    public async Task FallbackOpensThePlainAddressOfTheGateway(PrinterWebFallback reason)
    {
        Uri plain = new("http://192.168.1.10:8080/prusa/");
        _gateway.Open = (_, _) => Task.FromResult(PrinterWebAccess.Plain(plain, reason));

        bool opened = await CreateOpener().OpenAsync(PrinterUri, PrusaLink(), Ct);

        Assert.True(opened);
        Assert.Equal([plain], _launcher.Opened);
        Assert.Empty(_launcher.OpenedSecret);
        // Only the reason (enum name) is logged, never an address.
        (LogLevel level, string message, Exception? exception) = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Information, level);
        Assert.Contains(reason.ToString(), message, StringComparison.Ordinal);
        Assert.DoesNotContain("192.168.1.10", message, StringComparison.Ordinal);
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(PrinterWebFallback.NotSupported)]
    [InlineData(PrinterWebFallback.InvalidAddress)]
    public async Task FallbackWithoutAddressOpensTheEntryAddress(PrinterWebFallback reason)
    {
        _gateway.Open = (_, _) => Task.FromResult(PrinterWebAccess.Plain(null, reason));

        bool opened = await CreateOpener().OpenAsync(PrinterUri, PrusaLink(), Ct);

        Assert.True(opened);
        Assert.Equal([PrinterUri], _launcher.Opened);
        Assert.Empty(_launcher.OpenedSecret);
    }

    [Fact]
    public async Task PrusaConnectOpensTheEntryAddressWithoutReadingSecrets()
    {
        // The real gateway: it must not read any secret, start a proxy or touch the network for Prusa Connect.
        FakeSecretStore secrets = new();
        secrets.Seed("xl", "pw");
        await using PrusaLinkWebGateway gateway = new(new NoHttpClientFactory(), secrets, TimeProvider.System, NullLoggerFactory.Instance);
        PrinterDefinition printer = new() { Id = "xl", Name = "XL", ConnectionType = ConnectionType.PrusaConnect, Address = "c0ffee" };

        bool opened = await new PrinterWebOpener(gateway, _launcher, _logger).OpenAsync(PrinterWebAddress.PrusaConnectHome, printer, Ct);

        Assert.True(opened);
        Assert.Equal([PrinterWebAddress.PrusaConnectHome], _launcher.Opened);
        Assert.Empty(_launcher.OpenedSecret);
        Assert.Empty(secrets.Operations);
        // Not a failure worth logging: Prusa Connect simply has no proxy.
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task UnknownPrinterOpensPlainAddressWithoutTheGateway()
    {
        bool opened = await CreateOpener().OpenAsync(PrinterUri, printer: null, Ct);

        Assert.True(opened);
        Assert.Equal([PrinterUri], _launcher.Opened);
        Assert.Empty(_gateway.Opened);
    }

    [Fact]
    public async Task UnexpectedGatewayErrorOpensPlainAddressAndLogsNoExceptionText()
    {
        _gateway.Open = static (_, _) => throw new InvalidOperationException($"broken {FakeWebGateway.ProxyAddress}");

        bool opened = await CreateOpener().OpenAsync(PrinterUri, PrusaLink(), Ct);

        Assert.True(opened);
        Assert.Equal([PrinterUri], _launcher.Opened);
        (LogLevel level, string message, Exception? exception) = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains(nameof(InvalidOperationException), message, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeWebGateway.Token, message, StringComparison.Ordinal);
        Assert.Null(exception);
    }

    [Fact]
    public async Task DisposedGatewayOpensNothing()
    {
        // The app is quitting.
        _gateway.Open = static (_, _) => throw new ObjectDisposedException(nameof(IPrinterWebGateway));

        bool opened = await CreateOpener().OpenAsync(PrinterUri, PrusaLink(), Ct);

        Assert.False(opened);
        Assert.Empty(_launcher.Opened);
        Assert.Empty(_launcher.OpenedSecret);
    }

    [Fact]
    public async Task CancellationWhileTheGatewayChecksOpensNothing()
    {
        using CancellationTokenSource cts = new();
        _gateway.Open = (_, token) =>
        {
            cts.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(PrinterWebAccess.Proxied(FakeWebGateway.ProxyAddress));
        };

        bool opened = await CreateOpener().OpenAsync(PrinterUri, PrusaLink(), cts.Token);

        Assert.False(opened);
        Assert.Empty(_launcher.Opened);
        Assert.Empty(_launcher.OpenedSecret);
    }

    [Fact]
    public async Task CancellationAfterTheGatewayAnsweredOpensNothing()
    {
        using CancellationTokenSource cts = new();
        _gateway.Open = (_, _) =>
        {
            cts.Cancel();
            return Task.FromResult(PrinterWebAccess.Plain(PrinterUri, PrinterWebFallback.NotReachable));
        };

        bool opened = await CreateOpener().OpenAsync(PrinterUri, PrusaLink(), cts.Token);

        Assert.False(opened);
        Assert.Empty(_launcher.Opened);
    }

    [Fact]
    public async Task ABlockingGatewayDoesNotBlockTheCaller()
    {
        // The real gateway may block on the OS credential store before its first await; the click is on the UI thread.
        using ManualResetEventSlim release = new();
        _gateway.Open = (_, _) =>
        {
            release.Wait(TestContext.Current.CancellationToken);
            return Task.FromResult(PrinterWebAccess.Proxied(FakeWebGateway.ProxyAddress));
        };

        Task<bool> open = CreateOpener().OpenAsync(PrinterUri, PrusaLink(), Ct);

        Assert.False(open.IsCompleted);
        release.Set();
        Assert.True(await open);
    }

    [Fact]
    public async Task ProxyTokenIsNeverLoggedWhenTheBrowserFailsToStart()
    {
        ListLogger<ProcessUrlLauncher> launcherLog = new();
        string? started = null;
        ProcessUrlLauncher launcher = new(launcherLog, UrlLaunchPlatform.Windows, info =>
        {
            started ??= info.FileName;
            // Process.Start puts the file name (the proxy address with its token) into its exception message.
            throw new Win32Exception(2, $"An error occurred trying to start process '{info.FileName}'.");
        });
        PrinterWebOpener opener = new(_gateway, launcher, _logger);

        bool opened = await opener.OpenAsync(PrinterUri, PrusaLink(), Ct);

        Assert.False(opened);
        Assert.Equal(FakeWebGateway.ProxyAddress.AbsoluteUri, started);
        List<(LogLevel Level, string Message, Exception? Exception)> entries = [.. _logger.Entries, .. launcherLog.Entries];
        Assert.NotEmpty(entries);
        AssertNoToken(entries);
    }

    [Fact]
    public async Task ProxyTokenIsNeverLoggedWhenTheBrowserStarts()
    {
        ListLogger<ProcessUrlLauncher> launcherLog = new();
        List<ProcessStartInfo> started = [];
        ProcessUrlLauncher launcher = new(launcherLog, UrlLaunchPlatform.MacOS, info =>
        {
            started.Add(info);
            return null;
        });

        bool opened = await new PrinterWebOpener(_gateway, launcher, _logger).OpenAsync(PrinterUri, PrusaLink(), Ct);

        Assert.True(opened);
        Assert.Equal([FakeWebGateway.ProxyAddress.AbsoluteUri], Assert.Single(started).ArgumentList);
        AssertNoToken([.. _logger.Entries, .. launcherLog.Entries]);
    }

    private static void AssertNoToken(IEnumerable<(LogLevel Level, string Message, Exception? Exception)> entries)
    {
        foreach ((_, string message, Exception? exception) in entries)
        {
            foreach (string text in new[] { message, exception?.ToString() ?? string.Empty })
            {
                Assert.DoesNotContain(FakeWebGateway.Token, text, StringComparison.Ordinal);
            }
        }
    }

    private PrinterWebOpener CreateOpener() => new(_gateway, _launcher, _logger);

    private static PrinterDefinition PrusaLink(string userName = PrinterUserName.Default) =>
        new() { Id = "mk4", Name = "MK4", ConnectionType = ConnectionType.PrusaLink, Address = "192.168.1.10", UserName = userName };

    /// <summary>Fails the test if anything tries to reach the network.</summary>
    private sealed class NoHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("No network in this test.");
    }
}
