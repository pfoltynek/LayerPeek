using System.Runtime.InteropServices;

using Microsoft.Extensions.Logging.Abstractions;

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
        return new AboutViewModel(new FakeUrlLauncher(), support) { BundlePicker = _picker };
    }

    private sealed class FakePicker : IBundleFilePicker
    {
        public FileTarget? Target { get; set; }

        public int Calls { get; private set; }

        public string? SuggestedName { get; private set; }

        public Task<IBundleTarget?> PickAsync(string suggestedFileName)
        {
            Calls++;
            SuggestedName = suggestedFileName;
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
