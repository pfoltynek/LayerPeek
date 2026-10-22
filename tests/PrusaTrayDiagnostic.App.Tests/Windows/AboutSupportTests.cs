using System.Runtime.InteropServices;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using PrusaTrayDiagnostic.App.Platform;
using PrusaTrayDiagnostic.App.Resources;
using PrusaTrayDiagnostic.App.Support;
using PrusaTrayDiagnostic.App.Tests.Support;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.App.Tests.Tray.Menu;
using PrusaTrayDiagnostic.App.Windows;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.App.Tests.Windows;

/// <summary>The support actions of the About window (T-048): save dialog, outcome in the status line, log folder.</summary>
public sealed class AboutSupportTests : IDisposable
{
    private static readonly SupportEnvironment Environment = new(
        "1.2.3", "Test OS", Architecture.X64, Architecture.X64, ".NET", NativeAot: false, "en-US", null, "7.1");

    private readonly CultureScope _culture = new("en-US");
    private readonly string _folder = TestDirectories.NewDirectoryPath();
    private readonly FakeFolders _folders = new();
    private readonly FakePicker _picker = new();
    private readonly FakeClipboard _clipboard = new();
    private readonly FakeTimeProvider _time = new();

    public AboutSupportTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        _culture.Dispose();
        TestDirectories.Delete(_folder);
    }

    [Fact]
    public async Task CancelledDialogCreatesNothingAndShowsNothing()
    {
        AboutViewModel about = Create();

        await about.SaveBundleCommand.ExecuteAsync(null);

        Assert.Equal(1, _picker.Calls);
        Assert.StartsWith("PrusaTrayDiagnostic-diagnostics-", _picker.SuggestedName, StringComparison.Ordinal);
        // Production: where the OS opens the dialog (only the test mode of T-044 sets its data folder).
        Assert.Null(_picker.StartDirectory);
        Assert.Empty(Directory.GetFiles(_folder));
        Assert.Null(about.SupportStatusText);
    }

    [Fact]
    public async Task SavedBundleIsReported()
    {
        AboutViewModel about = Create();
        _picker.Target = new FileTarget(Path.Combine(_folder, "bundle.zip"));

        await about.SaveBundleCommand.ExecuteAsync(null);

        Assert.True(File.Exists(_picker.Target.Path));
        Assert.Equal(Strings.SupportBundleSaved, about.SupportStatusText);
        Assert.False(about.SupportStatusIsError);
    }

    [Fact]
    public async Task FailedBundleIsReportedAsAnErrorAndRemoved()
    {
        AboutViewModel about = Create();
        _picker.Target = new FileTarget(Path.Combine(_folder, "bundle.zip")) { FailWrites = true };

        await about.SaveBundleCommand.ExecuteAsync(null);

        Assert.False(File.Exists(_picker.Target.Path));
        Assert.Equal(Strings.SupportBundleFailed, about.SupportStatusText);
        Assert.True(about.SupportStatusIsError);
    }

    [Fact]
    public async Task StatusFollowsTheLanguage()
    {
        AboutViewModel about = Create();
        _picker.Target = new FileTarget(Path.Combine(_folder, "bundle.zip"));
        await about.SaveBundleCommand.ExecuteAsync(null);

        using (new CultureScope("cs-CZ"))
        {
            about.RefreshTexts();

            Assert.Equal("Diagnostický balíček byl uložen.", about.SupportStatusText);
        }
    }

    [Fact]
    public async Task LogFolderOpensOrReportsAnError()
    {
        AboutViewModel about = Create();

        await about.OpenLogFolderCommand.ExecuteAsync(null);
        Assert.Equal([Path.Combine(_folder, "logs")], _folders.Opened);
        Assert.Null(about.SupportStatusText);

        _folders.Succeeds = false;
        await about.OpenLogFolderCommand.ExecuteAsync(null);
        Assert.Equal(Strings.SupportLogFolderFailed, about.SupportStatusText);
        Assert.True(about.SupportStatusIsError);
    }

    [Fact]
    public async Task CopiedInfoSaysCopiedForTwoSeconds()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "logs"));
        const string Record = "2026-10-22 14:03:12.345 +02:00 INF [pid 1] Tray: Started.";
        File.WriteAllText(Path.Combine(_folder, "logs", "PrusaTrayDiagnostic.log"), Record + "\n");
        AboutViewModel about = Create();

        await about.CopyInfoCommand.ExecuteAsync(null);

        string text = Assert.Single(_clipboard.Texts);
        Assert.StartsWith("Prusa Tray Diagnostic 1.2.3\n", text, StringComparison.Ordinal);
        Assert.EndsWith("Last log lines (1):\n" + Record + "\n", text, StringComparison.Ordinal);
        Assert.True(about.IsInfoCopied);
        Assert.True(about.CopyInfoCommand.CanExecute(null));
        _time.Advance(TimeSpan.FromSeconds(1));

        // Copied again: the feedback starts over.
        await about.CopyInfoCommand.ExecuteAsync(null);
        _time.Advance(TimeSpan.FromSeconds(1.5));
        Assert.True(about.IsInfoCopied);
        _time.Advance(TimeSpan.FromSeconds(0.5));
        for (int i = 0; i < 500 && about.IsInfoCopied; i++)
        {
            // The end of the feedback continues on the thread pool after the fake clock fired.
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.False(about.IsInfoCopied);
        Assert.Equal(2, _clipboard.Texts.Count);
        // Users get the usual clipboard history; only the test mode keeps copies out of it (T-064).
        Assert.False(_clipboard.KeptOutOfHistory);
        Assert.Null(about.SupportStatusText);
    }

    [Fact]
    public async Task ClipboardFailureIsReported()
    {
        AboutViewModel about = Create();
        _clipboard.Fails = true;

        await about.CopyInfoCommand.ExecuteAsync(null);

        Assert.False(about.IsInfoCopied);
        Assert.Equal(Strings.SupportCopyFailed, about.SupportStatusText);
        Assert.True(about.SupportStatusIsError);
    }

    [Fact]
    public async Task WithoutSupportTheButtonsDoNothing()
    {
        AboutViewModel about = new(new FakeUrlLauncher()) { BundlePicker = _picker };

        await about.SaveBundleCommand.ExecuteAsync(null);
        await about.OpenLogFolderCommand.ExecuteAsync(null);

        Assert.Equal(0, _picker.Calls);
        Assert.Null(about.SupportStatusText);
    }

    private AboutViewModel Create()
    {
        SupportActions support = new(
            new SupportLog(Path.Combine(_folder, "logs"), static _ => true),
            static _ => Task.FromResult(new AppSettings()),
            new FakePrinterMonitor(),
            _folders,
            static _ => Environment,
            TimeProvider.System,
            NullLogger<SupportActions>.Instance);
        return new AboutViewModel(new FakeUrlLauncher(), typeof(AboutViewModel).Assembly, support, _time) { BundlePicker = _picker, Clipboard = _clipboard };
    }

    private sealed class FakeClipboard : ISupportClipboard
    {
        public bool Fails { get; set; }

        public List<string> Texts { get; } = [];

        public bool KeptOutOfHistory { get; private set; }

        public Task SetTextAsync(string text, bool keepOutOfHistory = false)
        {
            KeptOutOfHistory = keepOutOfHistory;
            if (Fails)
            {
                throw new InvalidOperationException("The clipboard is busy.");
            }

            Texts.Add(text);
            return Task.CompletedTask;
        }
    }

    private sealed class FakePicker : IBundleFilePicker
    {
        public FileTarget? Target { get; set; }

        public int Calls { get; private set; }

        public string? SuggestedName { get; private set; }

        public string? StartDirectory { get; private set; }

        public Task<IBundleTarget?> PickAsync(string suggestedFileName, string? startDirectory = null)
        {
            Calls++;
            SuggestedName = suggestedFileName;
            StartDirectory = startDirectory;
            return Task.FromResult<IBundleTarget?>(Target);
        }
    }

    private sealed class FakeFolders : IFolderLauncher
    {
        public bool Succeeds { get; set; } = true;

        public List<string> Opened { get; } = [];

        public Task<bool> OpenAsync(string directory)
        {
            Opened.Add(directory);
            return Task.FromResult(Succeeds);
        }
    }
}
