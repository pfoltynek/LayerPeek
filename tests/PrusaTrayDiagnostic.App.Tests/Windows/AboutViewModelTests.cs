using System.Reflection;
using System.Reflection.Emit;

using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.App.Tests.Tray.Menu;
using PrusaTrayDiagnostic.App.Windows;

namespace PrusaTrayDiagnostic.App.Tests.Windows;

public sealed class AboutViewModelTests
{
    private static readonly Assembly AppAssembly = typeof(AboutViewModel).Assembly;

    private readonly FakeUrlLauncher _launcher = new();

    [Fact]
    public void VersionHasNoSourceRevisionSuffix()
    {
        string version = AboutViewModel.ProductVersion(AppAssembly);

        Assert.DoesNotContain("+", version, StringComparison.Ordinal);
        Assert.NotEmpty(version);
    }

    [Fact]
    public void CopyrightComesFromAssemblyMetadata()
    {
        // Directory.Build.props <Copyright> → AssemblyCopyrightAttribute.
        Assert.Equal("Copyright © Petr Foltýnek", AboutViewModel.ProductCopyright(AppAssembly));
        Assert.Equal("Copyright © Petr Foltýnek", new AboutViewModel(_launcher, AppAssembly).Copyright);
    }

    [Fact]
    public void CopyrightFallsBackWhenAssemblyHasNone()
    {
        AssemblyBuilder bare = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("NoCopyright"), AssemblyBuilderAccess.Run);

        Assert.Equal(AboutViewModel.DefaultCopyright, AboutViewModel.ProductCopyright(bare));
    }

    [Fact]
    public void EnglishTexts()
    {
        using CultureScope english = new("en-US");

        AboutViewModel viewModel = new(_launcher, AppAssembly);

        Assert.Equal("About Prusa Tray Diagnostic", viewModel.Title);
        Assert.Equal("Version " + AboutViewModel.ProductVersion(AppAssembly), viewModel.VersionText);
        Assert.Equal("This is not an official Prusa Research product.", viewModel.Disclaimer);
        Assert.Equal("License: Apache License 2.0", viewModel.LicenseText);
        Assert.Equal("Copyright © Petr Foltýnek", viewModel.Copyright);
        Assert.Equal("Source code on GitHub", viewModel.RepositoryText);
    }

    [Fact]
    public void CzechTexts()
    {
        using CultureScope czech = new("cs-CZ");

        AboutViewModel viewModel = new(_launcher, AppAssembly);

        Assert.Equal("O aplikaci Prusa Tray Diagnostic", viewModel.Title);
        Assert.Equal("Verze " + AboutViewModel.ProductVersion(AppAssembly), viewModel.VersionText);
        Assert.Equal("Nejde o oficiální produkt společnosti Prusa Research.", viewModel.Disclaimer);
        Assert.Equal("Licence: Apache License 2.0", viewModel.LicenseText);
        Assert.Equal("Zdrojový kód na GitHubu", viewModel.RepositoryText);
        Assert.Equal("Zavřít", viewModel.CloseText);
    }

    [Fact]
    public void RefreshTextsSwitchesEveryTextToTheNewLanguage()
    {
        using CultureScope english = new("en-US");
        AboutViewModel viewModel = new(_launcher, AppAssembly);
        List<string?> changes = [];
        viewModel.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        using (new CultureScope("de-DE"))
        {
            viewModel.RefreshTexts();
        }

        Assert.Equal("Über Prusa Tray Diagnostic", viewModel.Title);
        Assert.Equal("Version " + AboutViewModel.ProductVersion(AppAssembly), viewModel.VersionText);
        Assert.Equal("Dies ist kein offizielles Produkt von Prusa Research.", viewModel.Disclaimer);
        Assert.Equal("Lizenz: Apache License 2.0", viewModel.LicenseText);
        Assert.Equal("Quellcode auf GitHub", viewModel.RepositoryText);
        Assert.Equal("Schließen", viewModel.CloseText);
        Assert.Equal("Copyright © Petr Foltýnek", viewModel.Copyright);
        // "Everything changed" for the bindings.
        Assert.Equal([string.Empty], changes);
    }

    [Fact]
    public void RepositoryLinkOpensGitHubThroughLauncher()
    {
        AboutViewModel viewModel = new(_launcher, AppAssembly);

        viewModel.OpenRepositoryCommand.Execute(null);

        Assert.Equal([new Uri("https://github.com/pfoltynek/PrusaTrayDiagnostic")], _launcher.Opened);
    }
}
