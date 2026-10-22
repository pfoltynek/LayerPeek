using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.Logging;
using PrusaTrayDiagnostic.App.Platform;
using PrusaTrayDiagnostic.App.Support;
using PrusaTrayDiagnostic.App.Tests.Logging;
using PrusaTrayDiagnostic.App.Tests.Settings;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.Secrets;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.App.Tests.Support;

/// <summary>The diagnostic bundle (T-048): logs, info.txt and redacted settings.json, and never a secret.</summary>
public sealed class SupportBundleTests : IDisposable
{
    private const string PrinterPassword = "Pw7Secret!";
    private const string ApiKey = "Ak3Secret";
    private const string CameraUser = "camuser";
    private const string CameraPassword = "CamPass9";
    private const string CameraToken = "Tok9Secret";

    private static readonly SupportEnvironment Environment = new(
        "1.2.3", "Test OS", Architecture.X64, Architecture.X64, ".NET", NativeAot: false, "en-US", null, "7.1");

    private readonly string _folder = TestDirectories.NewDirectoryPath();
    private readonly List<IDisposable> _owned = [];

    private string DataDirectory => Path.Combine(_folder, "data");

    private string LogDirectory => Path.Combine(_folder, "logs");

    public void Dispose()
    {
        foreach (IDisposable owned in Enumerable.Reverse(_owned))
        {
            owned.Dispose();
        }

        TestDirectories.Delete(_folder);
    }

    [Fact]
    public async Task BundleHoldsLogsInfoAndSettingsButNoSecret()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        // A settings file with credentials and a token in the camera address, as a user might have typed it by hand.
        Directory.CreateDirectory(DataDirectory);
        string streamUrl = $"rtsp://{CameraUser}:{CameraPassword}@192.168.0.50/live?token={CameraToken}";
        await File.WriteAllTextAsync(
            Path.Combine(DataDirectory, JsonSettingsStore.FileName),
            "{ \"version\": 1, \"printers\": [ { \"id\": \"mk4\", \"name\": \"Workshop MK4\", \"connectionType\": \"PrusaLink\", "
            + "\"address\": \"192.168.0.226\", \"userName\": \"workshop\", \"camera\": { \"enabled\": true, \"streamUrl\": \"" + streamUrl + "\" } } ] }",
            ct);
        using JsonSettingsStore store = new(DataDirectory);
        AppSettings settings = (await store.LoadAsync(ct)).Settings;
        FakeSecretStore secrets = new();
        await secrets.SetAsync(SecretKeys.ForPrinter("mk4"), PrinterPassword, ct);
        await secrets.SetAsync("prusa-connect", ApiKey, ct);
        (SupportActions support, ILoggerFactory logging) = CreateSupport(settings);
        // Something the app logged before (camera addresses are logged redacted).
        logging.CreateLogger("Camera").Write(LogLevel.Warning, $"Camera stream failed: {UriRedaction.Redact(new Uri(streamUrl))}");
        FileTarget target = new(Path.Combine(_folder, "bundle.zip"));

        Assert.True(await support.SaveBundleAsync(target));

        Dictionary<string, string> entries = ReadZip(target.Path);
        Assert.Equal(["info.txt", "logs/PrusaTrayDiagnostic.log", "settings.json"], entries.Keys.Order(StringComparer.Ordinal));
        Assert.Contains("Workshop MK4", entries["info.txt"], StringComparison.Ordinal);
        Assert.Contains("\"address\": \"192.168.0.226\"", entries["settings.json"], StringComparison.Ordinal);
        Assert.Contains("\"streamUrl\": \"rtsp://192.168.0.50/live\"", entries["settings.json"], StringComparison.Ordinal);
        // The log queue is written first: the bundle has the line of this save.
        Assert.Contains("Camera stream failed", entries["logs/PrusaTrayDiagnostic.log"], StringComparison.Ordinal);
        Assert.Contains("Saving a diagnostic bundle.", entries["logs/PrusaTrayDiagnostic.log"], StringComparison.Ordinal);
        foreach ((string name, string content) in entries)
        {
            foreach (string secret in new[] { PrinterPassword, ApiKey, CameraPassword, CameraUser, CameraToken })
            {
                Assert.False(content.Contains(secret, StringComparison.Ordinal), $"{name} contains '{secret}'.");
            }
        }
    }

    [Fact]
    public async Task FailedBundleIsDeleted()
    {
        (SupportActions support, _) = CreateSupport(new AppSettings());
        FileTarget target = new(Path.Combine(_folder, "bundle.zip")) { FailWrites = true };
        Directory.CreateDirectory(_folder);

        Assert.False(await support.SaveBundleAsync(target));

        Assert.True(target.Deleted);
        Assert.False(File.Exists(target.Path));
    }

    [Fact]
    public void WithoutALogFolderTheBundleHoldsInfoAndSettings()
    {
        using MemoryStream zip = new();

        SupportBundle.Write(zip, "info\n", "{}", Path.Combine(_folder, "missing"), CancellationToken.None);

        zip.Position = 0;
        Assert.Equal(["info.txt", "settings.json"], ReadZip(zip).Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void LogFileThatCannotBeReadIsNamedInTheInfo()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows locks a file against readers.");
        Directory.CreateDirectory(LogDirectory);
        File.WriteAllText(Path.Combine(LogDirectory, "PrusaTrayDiagnostic.log"), "readable\n");
        string locked = Path.Combine(LogDirectory, "PrusaTrayDiagnostic.1.log");
        using MemoryStream zip = new();

        using (new FileStream(locked, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            SupportBundle.Write(zip, "info\n", "{}", LogDirectory, CancellationToken.None);
        }

        zip.Position = 0;
        Dictionary<string, string> entries = ReadZip(zip);
        Assert.Equal("info\nLog file not readable: PrusaTrayDiagnostic.1.log\n", entries["info.txt"]);
        Assert.Equal("readable\n", entries["logs/PrusaTrayDiagnostic.log"]);
    }

    [Fact]
    public void DefaultFileNameIsEnglishWithTheTime() =>
        Assert.Equal(
            "PrusaTrayDiagnostic-diagnostics-20261022-140312.zip",
            SupportBundle.DefaultFileName(new DateTimeOffset(2026, 10, 22, 14, 3, 12, TimeSpan.FromHours(2))));

    private (SupportActions Support, ILoggerFactory Logging) CreateSupport(AppSettings settings)
    {
        LogFile file = Own(new LogFile(LogDirectory));
        FileLogProvider provider = Own(new FileLogProvider(file, new LogLevelSwitch(LogLevel.Information)));
        ILoggerFactory logging = Own(new LoggerFactory([provider]));
        SupportActions support = new(
            new SupportLog(LogDirectory, provider.Flush),
            _ => Task.FromResult(settings),
            new FakePrinterMonitor(),
            new NoFolders(),
            static _ => Environment,
            TimeProvider.System,
            logging.CreateLogger<SupportActions>());
        return (support, logging);
    }

    private T Own<T>(T disposable)
        where T : IDisposable
    {
        _owned.Add(disposable);
        return disposable;
    }

    private static Dictionary<string, string> ReadZip(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return ReadZip(stream);
    }

    private static Dictionary<string, string> ReadZip(Stream stream)
    {
        using ZipArchive archive = new(stream, ZipArchiveMode.Read, leaveOpen: true);
        Dictionary<string, string> entries = [];
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            using StreamReader reader = new(entry.Open(), Encoding.UTF8);
            entries[entry.FullName] = reader.ReadToEnd();
        }

        return entries;
    }

    private sealed class NoFolders : IFolderLauncher
    {
        public Task<bool> OpenAsync(string directory) => Task.FromResult(false);
    }
}

/// <summary>A bundle file on disk, optionally failing every write like a full disk.</summary>
internal sealed class FileTarget(string path) : IBundleTarget
{
    public string Path { get; } = path;

    public bool FailWrites { get; init; }

    public bool Deleted { get; private set; }

    public Task<Stream> OpenWriteAsync()
    {
        Stream file = File.Create(Path);
        return Task.FromResult(FailWrites ? new FailingStream(file) : file);
    }

    public Task DeleteAsync()
    {
        Deleted = true;
        File.Delete(Path);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>The first byte gets through, then every write fails.</summary>
    private sealed class FailingStream(Stream inner) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            inner.Write(buffer, offset, Math.Min(count, 1));
            throw new IOException("There is not enough space on the disk.");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
