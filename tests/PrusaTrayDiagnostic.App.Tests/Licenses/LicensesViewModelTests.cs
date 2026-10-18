using PrusaTrayDiagnostic.App.Licenses;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.App.Tests.Tray.Menu;
using PrusaTrayDiagnostic.App.Windows;
using PrusaTrayDiagnostic.Core.Cameras;

namespace PrusaTrayDiagnostic.App.Tests.Licenses;

public sealed class LicensesViewModelTests : IDisposable
{
    private readonly CultureScope _culture = new("en-US");
    private readonly FakeUrlLauncher _launcher = new();

    public void Dispose() => _culture.Dispose();

    private LicensesViewModel Create(Func<string>? version = null, Func<string?>? licenseText = null) =>
        new(_launcher, version ?? (static () => "avcodec 63.1.102 (LGPL version 3 or later)"), licenseText ?? (static () => "GNU LESSER GENERAL PUBLIC LICENSE"));

    [Fact]
    public void ListsEveryComponentWithVersionLicenceAndProject()
    {
        LicensesViewModel viewModel = Create();

        Assert.Equal(ThirdPartyLicenses.All.Count, viewModel.Components.Count);
        LicenseItemViewModel avalonia = viewModel.Components[0];
        Assert.Equal("Avalonia", avalonia.Name);
        Assert.Equal("Version 12.1.4", avalonia.VersionText);
        Assert.Equal("Licence: MIT", avalonia.LicenseText);
        Assert.Equal("https://github.com/AvaloniaUI/Avalonia", avalonia.ProjectUrl);
    }

    [Fact]
    public void RuntimeVersionIsTheRunningOne()
    {
        LicenseItemViewModel runtime = Create().Components.Single(static c => c.Name.StartsWith(".NET runtime", StringComparison.Ordinal));

        Assert.Equal("Version " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, runtime.VersionText);
    }

    [Fact]
    public void FullTextIsLoadedOnlyWhenExpanded()
    {
        LicenseItemViewModel item = Create().Components[0];
        Assert.Null(item.FullText);

        item.IsExpanded = true;

        Assert.Contains("Permission is hereby granted", item.FullText, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectLinkOpensThroughTheLauncher()
    {
        LicenseItemViewModel item = Create().Components[0];

        item.OpenProjectCommand.Execute(null);

        Assert.Equal([new Uri("https://github.com/AvaloniaUI/Avalonia")], _launcher.Opened);
    }

    [Fact]
    public async Task FFmpegVersionAndLicenceAreLoadedInTheBackground()
    {
        int versionThread = 0;
        LicensesViewModel viewModel = Create(() =>
        {
            versionThread = Environment.CurrentManagedThreadId;
            return "avcodec 63.1.102 (LGPL version 3 or later)";
        });
        Assert.Equal("Detecting the FFmpeg libraries…", viewModel.FFmpegVersionText);

        await viewModel.LoadAsync();

        Assert.Equal("avcodec 63.1.102 (LGPL version 3 or later)", viewModel.FFmpegVersionText);
        Assert.Equal("GNU LESSER GENERAL PUBLIC LICENSE", viewModel.FFmpegFullText);
        Assert.NotEqual(0, versionThread);
    }

    [Fact]
    public async Task MissingFFmpegIsShownGracefully()
    {
        LicensesViewModel viewModel = Create(static () => throw new CameraFeedException("FFmpeg is not available."), static () => null);

        await viewModel.LoadAsync();

        Assert.Equal("The FFmpeg libraries were not found next to the application; the camera does not work.", viewModel.FFmpegVersionText);
        Assert.StartsWith("The FFmpeg licence text (FFmpeg-LICENSE.txt) was not found", viewModel.FFmpegFullText, StringComparison.Ordinal);
    }

    [Fact]
    public void FFmpegLinksOpenSourceBuildAndScripts()
    {
        LicensesViewModel viewModel = Create();

        viewModel.OpenFFmpegSourceCommand.Execute(null);
        viewModel.OpenFFmpegBuildCommand.Execute(null);
        viewModel.OpenFFmpegBuildScriptsCommand.Execute(null);

        Assert.Equal([ThirdPartyLicenses.FFmpeg.SourceUri, ThirdPartyLicenses.FFmpeg.BuildUri, ThirdPartyLicenses.FFmpeg.BuildScriptsUri], _launcher.Opened);
        Assert.Equal("Licence: LGPL-3.0-or-later", viewModel.FFmpegLicenseText);
        Assert.Contains("may be replaced", viewModel.FFmpegNote, StringComparison.Ordinal);
    }

    [Fact]
    public void ShippedFFmpegLicenceFileIsReadFromTheApplicationFolder()
    {
        string? text = LicensesViewModel.ReadShippedFFmpegLicense();

        // The FFmpeg DLLs and their licence flow into this test output through the App reference.
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, ThirdPartyLicenses.FFmpeg.LicenseFileName)))
        {
            Assert.False(string.IsNullOrWhiteSpace(text));
        }
        else
        {
            Assert.Null(text);
        }
    }

    [Fact]
    public void AboutOffersTheLicences()
    {
        AboutViewModel about = new(_launcher, typeof(AboutViewModel).Assembly);
        int requests = 0;
        about.LicensesRequested += (_, _) => requests++;

        Assert.Equal("Third-party licences…", about.ThirdPartyLicensesText);
        about.ShowLicensesCommand.Execute(null);

        Assert.Equal(1, requests);
        Assert.NotNull(about.CreateLicensesViewModel());
    }

    [Fact]
    public void CzechTexts()
    {
        using CultureScope czech = new("cs-CZ");

        LicensesViewModel viewModel = Create();

        Assert.Equal("Licence třetích stran", viewModel.Title);
        Assert.Equal("Verze 12.1.4", viewModel.Components[0].VersionText);
    }
}
