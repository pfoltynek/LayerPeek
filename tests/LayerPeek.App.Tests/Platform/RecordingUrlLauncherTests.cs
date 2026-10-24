#if DEBUG
using LayerPeek.App.Platform;
using LayerPeek.App.Tests.Tray;

namespace LayerPeek.App.Tests.Platform;

/// <summary>
/// The test mode never starts a browser (T-067, user decision 2026-10-22): the addresses go, one complete line each, to
/// <c>opened-urls.txt</c> in the test folder, and the proxy address with its token never reaches the log (T-028).
/// </summary>
public sealed class RecordingUrlLauncherTests : IDisposable
{
    private const string Token = "Zm9vYmFyU2VjcmV0VG9rZW4xMjM0NTY";

    private static readonly Uri ProxyAddress = new($"http://127.0.0.1:50123/{Token}/");

    private readonly string _folder = TestDirectories.NewDirectoryPath();
    private readonly ListLogger<RecordingUrlLauncher> _logger = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string FilePath => Path.Combine(_folder, "opened-urls.txt");

    public void Dispose() => TestDirectories.Delete(_folder);

    [Fact]
    public async Task AddressesAreAppendedOneLineEachInOrder()
    {
        RecordingUrlLauncher launcher = new(_folder, _logger);

        Assert.True(await launcher.OpenAsync(new Uri("http://192.168.1.10/")));
        Assert.True(await launcher.OpenSecretAsync(ProxyAddress, Ct));
        Assert.True(await launcher.OpenAsync(new Uri("https://printer.local:8443/a b/")));

        Assert.Equal(FilePath, launcher.FilePath);
        Assert.Equal(
            "http://192.168.1.10/\n" + ProxyAddress.AbsoluteUri + "\nhttps://printer.local:8443/a%20b/\n",
            await File.ReadAllTextAsync(FilePath, Ct));
    }

    [Fact]
    public async Task TheProxyAddressNeverReachesTheLog()
    {
        RecordingUrlLauncher launcher = new(_folder, _logger);

        Assert.True(await launcher.OpenSecretAsync(ProxyAddress, Ct));

        Assert.NotEmpty(_logger.Entries);
        Assert.All(_logger.Entries, entry =>
        {
            Assert.DoesNotContain(Token, entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(":50123", entry.Message, StringComparison.Ordinal);
            Assert.Null(entry.Exception);
        });
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("ftp://printer/")]
    [InlineData("http://user:pw@printer/")]
    public async Task UnsafeAddressesAreRefusedAndNotRecorded(string uri)
    {
        RecordingUrlLauncher launcher = new(_folder, _logger);

        Assert.False(await launcher.OpenAsync(new Uri(uri)));
        Assert.False(await launcher.OpenSecretAsync(new Uri(uri), Ct));

        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public async Task ACancelledSecretOpenRecordsNothing()
    {
        RecordingUrlLauncher launcher = new(_folder, _logger);

        Assert.False(await launcher.OpenSecretAsync(ProxyAddress, new CancellationToken(canceled: true)));

        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public async Task AReaderAlwaysSeesCompleteLines()
    {
        RecordingUrlLauncher launcher = new(_folder, _logger);
        Uri[] addresses = [.. Enumerable.Range(0, 200).Select(static i => new Uri($"http://127.0.0.1:{50000 + i}/{Token}{i}/"))];

        // Writers in parallel, a reader meanwhile (as a test reads the file while the app runs).
        Task writing = Task.WhenAll(addresses.Select(address => Task.Run(() => launcher.OpenSecretAsync(address, Ct), Ct)));
        while (!writing.IsCompleted)
        {
            if (File.Exists(FilePath))
            {
                string content = await ReadSharedAsync();
                string[] lines = content.Split('\n');
                // Every line before the last newline is a whole address.
                Assert.All(lines[..^1], line => Assert.Contains(line, addresses.Select(static address => address.AbsoluteUri)));
            }

            await Task.Delay(1, Ct);
        }

        await writing;
        string[] written = (await ReadSharedAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(addresses.Select(static address => address.AbsoluteUri).Order(StringComparer.Ordinal), written.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AnUnwritableFileIsReportedAsNotOpened()
    {
        // A folder in place of the file.
        Directory.CreateDirectory(FilePath);
        RecordingUrlLauncher launcher = new(_folder, _logger);

        Assert.False(await launcher.OpenSecretAsync(ProxyAddress, Ct));

        Assert.Contains(_logger.Entries, entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Error);
        Assert.All(_logger.Entries, entry => Assert.DoesNotContain(Token, entry.Message, StringComparison.Ordinal));
    }

    private async Task<string> ReadSharedAsync()
    {
        await using FileStream stream = new(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using StreamReader reader = new(stream);
        return await reader.ReadToEndAsync(Ct);
    }
}
#endif
