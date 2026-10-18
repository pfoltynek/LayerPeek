using System.ComponentModel;
using System.Diagnostics;

using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.Platform;
using PrusaTrayDiagnostic.App.Tests.Settings;
using PrusaTrayDiagnostic.App.Tray.Menu;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Secrets;

namespace PrusaTrayDiagnostic.App.Tests.Tray.Menu;

public sealed class PrinterWebOpenerTests
{
    private const string Password = "S3cret p@ss:word";

    private static readonly Uri PrinterUri = new("http://192.168.1.10/");

    private readonly FakeUrlLauncher _launcher = new();
    private readonly FakeSecretStore _secrets = new();
    private readonly ListLogger<PrinterWebOpener> _logger = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PrusaLinkWithStoredPasswordOpensLoggedIn()
    {
        _secrets.Seed("mk4", Password);

        bool opened = await CreateOpener().OpenAsync(PrinterUri, PrusaLink(userName: "admin"), Ct);

        Assert.True(opened);
        Assert.Equal([(PrinterUri, "admin", Password)], _launcher.OpenedWithCredentials);
        Assert.Empty(_launcher.Opened);
    }

    [Fact]
    public async Task PasswordIsReadAtClickTime()
    {
        PrinterWebOpener opener = CreateOpener();
        await opener.OpenAsync(PrinterUri, PrusaLink(), Ct);

        _secrets.Seed("mk4", Password);
        await opener.OpenAsync(PrinterUri, PrusaLink(), Ct);

        Assert.Equal([PrinterUri], _launcher.Opened);
        Assert.Equal([(PrinterUri, PrinterUserName.Default, Password)], _launcher.OpenedWithCredentials);
        Assert.Equal(["get " + SecretKeys.ForPrinter("mk4"), "get " + SecretKeys.ForPrinter("mk4")], _secrets.Operations);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task PrusaLinkWithoutPasswordOpensPlainAddress(string? password)
    {
        if (password is not null)
        {
            _secrets.Seed("mk4", password);
        }

        bool opened = await CreateOpener().OpenAsync(PrinterUri, PrusaLink(), Ct);

        Assert.True(opened);
        Assert.Equal([PrinterUri], _launcher.Opened);
        Assert.Empty(_launcher.OpenedWithCredentials);
    }

    [Fact]
    public async Task SecretStoreFailureOpensPlainAddress()
    {
        _secrets.FailGet.Add(SecretKeys.ForPrinter("mk4"));

        bool opened = await CreateOpener().OpenAsync(PrinterUri, PrusaLink(), Ct);

        Assert.True(opened);
        Assert.Equal([PrinterUri], _launcher.Opened);
        Assert.Empty(_launcher.OpenedWithCredentials);
        (LogLevel level, _, Exception? exception) = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Warning, level);
        Assert.IsType<SecretStoreException>(exception);
    }

    [Fact]
    public async Task UnexpectedSecretStoreErrorOpensPlainAddress()
    {
        PrinterWebOpener opener = new(new ThrowingSecretStore(new InvalidOperationException("broken")), _launcher, _logger);

        bool opened = await opener.OpenAsync(PrinterUri, PrusaLink(), Ct);

        Assert.True(opened);
        Assert.Equal([PrinterUri], _launcher.Opened);
        Assert.Empty(_launcher.OpenedWithCredentials);
    }

    [Fact]
    public async Task PrusaConnectNeverReadsSecrets()
    {
        _secrets.Seed("xl", Password);
        PrinterDefinition printer = new() { Id = "xl", Name = "XL", ConnectionType = ConnectionType.PrusaConnect, Address = "c0ffee" };

        bool opened = await CreateOpener().OpenAsync(PrinterWebAddress.PrusaConnectHome, printer, Ct);

        Assert.True(opened);
        Assert.Equal([PrinterWebAddress.PrusaConnectHome], _launcher.Opened);
        Assert.Empty(_launcher.OpenedWithCredentials);
        Assert.Empty(_secrets.Operations);
    }

    [Fact]
    public async Task UnknownPrinterOpensPlainAddress()
    {
        bool opened = await CreateOpener().OpenAsync(PrinterUri, printer: null, Ct);

        Assert.True(opened);
        Assert.Equal([PrinterUri], _launcher.Opened);
        Assert.Empty(_secrets.Operations);
    }

    [Fact]
    public async Task CancellationWhileReadingThePasswordOpensNothing()
    {
        using CancellationTokenSource cts = new();
        ThrowingSecretStore secrets = new(null) { BeforeGet = cts.Cancel };
        PrinterWebOpener opener = new(secrets, _launcher, _logger);

        bool opened = await opener.OpenAsync(PrinterUri, PrusaLink(), cts.Token);

        Assert.False(opened);
        Assert.Empty(_launcher.Opened);
        Assert.Empty(_launcher.OpenedWithCredentials);
    }

    [Fact]
    public async Task NeitherPasswordNorAddressWithCredentialsIsLogged()
    {
        ListLogger<ProcessUrlLauncher> launcherLog = new();
        string? started = null;
        ProcessUrlLauncher launcher = new(launcherLog, UrlLaunchPlatform.Windows, info =>
        {
            started ??= info.FileName;
            // Process.Start puts the file name (the address with the password) into its exception message.
            throw new Win32Exception(2, $"An error occurred trying to start process '{info.FileName}'.");
        });
        _secrets.Seed("mk4", Password);
        PrinterWebOpener opener = new(_secrets, launcher, _logger);

        bool opened = await opener.OpenAsync(PrinterUri, PrusaLink(), Ct);
        _secrets.FailGet.Add(SecretKeys.ForPrinter("mk4"));
        await opener.OpenAsync(PrinterUri, PrusaLink(), Ct);

        Assert.False(opened);
        string address = Assert.IsType<string>(started);
        Assert.Contains(Uri.EscapeDataString(Password), address, StringComparison.Ordinal);
        List<(LogLevel Level, string Message, Exception? Exception)> entries = [.. _logger.Entries, .. launcherLog.Entries];
        Assert.NotEmpty(entries);
        foreach ((_, string message, Exception? exception) in entries)
        {
            foreach (string text in new[] { message, exception?.ToString() ?? string.Empty })
            {
                Assert.DoesNotContain(Password, text, StringComparison.Ordinal);
                Assert.DoesNotContain(Uri.EscapeDataString(Password), text, StringComparison.Ordinal);
                Assert.DoesNotContain("maker:", text, StringComparison.Ordinal);
                Assert.DoesNotContain("@192.168.1.10", text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task SuccessfulLoginLogsOnlyTheAddressWithoutCredentials()
    {
        ListLogger<ProcessUrlLauncher> launcherLog = new();
        List<ProcessStartInfo> started = [];
        ProcessUrlLauncher launcher = new(launcherLog, UrlLaunchPlatform.MacOS, info =>
        {
            started.Add(info);
            return null;
        });
        _secrets.Seed("mk4", Password);

        bool opened = await new PrinterWebOpener(_secrets, launcher, _logger).OpenAsync(PrinterUri, PrusaLink(), Ct);

        Assert.True(opened);
        Assert.Single(started);
        foreach ((_, string message, _) in _logger.Entries.Concat(launcherLog.Entries))
        {
            Assert.DoesNotContain(Password, message, StringComparison.Ordinal);
            Assert.DoesNotContain(Uri.EscapeDataString(Password), message, StringComparison.Ordinal);
            Assert.DoesNotContain("maker:", message, StringComparison.Ordinal);
        }
    }

    private PrinterWebOpener CreateOpener() => new(_secrets, _launcher, _logger);

    private static PrinterDefinition PrusaLink(string userName = PrinterUserName.Default) =>
        new() { Id = "mk4", Name = "MK4", ConnectionType = ConnectionType.PrusaLink, Address = "192.168.1.10", UserName = userName };

    /// <summary>Fails every read with <paramref name="failure"/>; returns the test password when it is <see langword="null"/>.</summary>
    private sealed class ThrowingSecretStore(Exception? failure) : ISecretStore
    {
        public Action? BeforeGet { get; init; }

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken)
        {
            BeforeGet?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return failure is null ? Task.FromResult<string?>(Password) : throw failure;
        }

        public Task SetAsync(string key, string secret, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
