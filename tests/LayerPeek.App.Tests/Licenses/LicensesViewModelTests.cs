using System.Runtime.InteropServices;

using LayerPeek.App.Licenses;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tests.Tray.Menu;
using LayerPeek.App.Windows;
using LayerPeek.Core.Cameras;

namespace LayerPeek.App.Tests.Licenses;

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
        bool shipped = File.Exists(Path.Combine(AppContext.BaseDirectory, ThirdPartyLicenses.FFmpeg.LicenseFileName));

        // On Windows x64 the build always brings FFmpeg (build/FFmpeg.targets): the licence must be next to the DLLs.
        // Elsewhere FFmpeg is not provided yet (macOS: T-026), so a missing licence is skipped, not failed.
        if (!(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64))
        {
            Assert.SkipUnless(shipped, "FFmpeg is only provided for win-x64 so far.");
        }

        Assert.True(shipped, $"{ThirdPartyLicenses.FFmpeg.LicenseFileName} is missing next to the application.");
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Contains("LESSER GENERAL PUBLIC LICENSE", text, StringComparison.OrdinalIgnoreCase);
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

    // ---- Language switch (T-029) ----

    [Fact]
    public async Task RefreshTextsSwitchesTheWindowAndEveryComponentWithoutReadingFFmpegAgain()
    {
        int versionReads = 0;
        LicensesViewModel viewModel = Create(() =>
        {
            versionReads++;
            return "avcodec 63.1.102 (LGPL version 3 or later)";
        });
        await viewModel.LoadAsync();
        LicenseItemViewModel avalonia = viewModel.Components[0];
        List<string?> changes = [];
        viewModel.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        List<string?> itemChanges = [];
        avalonia.PropertyChanged += (_, e) => itemChanges.Add(e.PropertyName);

        using (new CultureScope("de-DE"))
        {
            viewModel.RefreshTexts();
        }

        Assert.Equal("Lizenzen von Drittanbietern", viewModel.Title);
        Assert.Equal("Lizenz: LGPL-3.0-or-later", viewModel.FFmpegLicenseText);
        Assert.Equal("Projektseite", viewModel.ProjectText);
        Assert.Equal("Schließen", viewModel.CloseText);
        Assert.StartsWith("FFmpeg ist unter der GNU LGPL", viewModel.FFmpegNote, StringComparison.Ordinal);
        // The loaded version is kept (it is not a text of the app); only the texts around it change.
        Assert.Equal("avcodec 63.1.102 (LGPL version 3 or later)", viewModel.FFmpegVersionText);
        Assert.Equal(1, versionReads);
        Assert.Equal("Version 12.1.4", avalonia.VersionText);
        Assert.Equal("Lizenz: MIT", avalonia.LicenseText);
        Assert.Equal([string.Empty], changes);
        Assert.Contains(nameof(LicenseItemViewModel.LicenseText), itemChanges);
    }

    [Fact]
    public async Task RefreshTextsTranslatesTheMissingFFmpegNotices()
    {
        LicensesViewModel viewModel = Create(static () => throw new CameraFeedException("FFmpeg is not available."), static () => null);
        Assert.Equal("Detecting the FFmpeg libraries…", viewModel.FFmpegVersionText);
        await viewModel.LoadAsync();

        using (new CultureScope("de-DE"))
        {
            viewModel.RefreshTexts();

            Assert.StartsWith("Die FFmpeg-Bibliotheken wurden", viewModel.FFmpegVersionText, StringComparison.Ordinal);
            Assert.StartsWith("Der Lizenztext von FFmpeg", viewModel.FFmpegFullText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RefreshTextsBeforeLoadingTranslatesTheDetectingText()
    {
        LicensesViewModel viewModel = Create();

        using (new CultureScope("pl-PL"))
        {
            viewModel.RefreshTexts();

            Assert.Equal("Wykrywanie bibliotek FFmpeg…", viewModel.FFmpegVersionText);
            Assert.Null(viewModel.FFmpegFullText);
        }
    }

    [Fact]
    public void ComponentDescriptionsAreTranslatedAndProductNamesStay()
    {
        LicensesViewModel viewModel = Create();
        Assert.Contains(viewModel.Components, static c => c.Name == ".NET runtime (Native AOT, compiled into the application)");
        Assert.Contains(viewModel.Components, static c => c.Name == "Tmds.DBus.Protocol (Linux only)");
        List<string?> changes = [];
        LicenseItemViewModel tmds = viewModel.Components.Single(static c => c.Name.StartsWith("Tmds.DBus.Protocol", StringComparison.Ordinal));
        tmds.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        using (new CultureScope("de-DE"))
        {
            viewModel.RefreshTexts();
        }

        Assert.Contains(viewModel.Components, static c => c.Name == ".NET-Laufzeit (Native AOT, in die Anwendung kompiliert)");
        Assert.Contains(viewModel.Components, static c => c.Name.StartsWith("Gemeinsames Framework ASP.NET Core (Webserver Kestrel", StringComparison.Ordinal));
        Assert.Equal("Tmds.DBus.Protocol (nur Linux)", tmds.Name);
        Assert.Contains(nameof(LicenseItemViewModel.Name), changes);
        // Product names without a description are never translated.
        Assert.Equal("Avalonia", viewModel.Components[0].Name);
    }

    [Fact]
    public void AboutRefreshAlsoSwitchesItsLicencesWindow()
    {
        AboutViewModel about = new(_launcher, typeof(AboutViewModel).Assembly);
        LicensesViewModel licences = about.CreateLicensesViewModel();

        using (new CultureScope("fr-FR"))
        {
            about.RefreshTexts();
        }

        Assert.Equal("Licences tierces…", about.ThirdPartyLicensesText);
        Assert.Equal("Licences tierces", licences.Title);
        Assert.Equal("Licence\u00A0: MIT", licences.Components[0].LicenseText);
    }
}
