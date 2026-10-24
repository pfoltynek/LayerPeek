using System.Reflection;
using System.Reflection.Emit;
using System.Xml.Linq;

using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tests.Tray.Menu;
using LayerPeek.App.Windows;

namespace LayerPeek.App.Tests.Windows;

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
    public void VersionComesFromDirectoryBuildProps()
    {
        // <Version> in the root Directory.Build.props is the single version source (T-055): exe, About window, release tag.
        using CultureScope english = new("en-US");
        XDocument props = XDocument.Load(Path.Combine(RepositoryRoot(), "Directory.Build.props"));
        string expected = Assert.Single(props.Descendants("Version")).Value.Trim();

        Assert.Matches(@"^\d+\.\d+\.\d+$", expected);
        Assert.Equal(expected, AboutViewModel.ProductVersion(AppAssembly));
        Assert.Equal("Version " + expected, new AboutViewModel(_launcher, AppAssembly).VersionText);
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

        Assert.Equal("About LayerPeek", viewModel.Title);
        Assert.Equal("LayerPeek", viewModel.AppName);
        Assert.Equal("for Prusa printers", viewModel.Subtitle);
        Assert.Equal("Version " + AboutViewModel.ProductVersion(AppAssembly), viewModel.VersionText);
        Assert.Equal("This is not an official Prusa Research product.", viewModel.Disclaimer);
        Assert.Equal("License: GNU General Public License v3.0 only", viewModel.LicenseText);
        Assert.Equal("Copyright © Petr Foltýnek", viewModel.Copyright);
        Assert.Equal("Source code on GitHub", viewModel.RepositoryText);
    }

    [Fact]
    public void CzechTexts()
    {
        using CultureScope czech = new("cs-CZ");

        AboutViewModel viewModel = new(_launcher, AppAssembly);

        Assert.Equal("O aplikaci LayerPeek", viewModel.Title);
        Assert.Equal("LayerPeek", viewModel.AppName);
        Assert.Equal("pro tiskárny Prusa", viewModel.Subtitle);
        Assert.Equal("Verze " + AboutViewModel.ProductVersion(AppAssembly), viewModel.VersionText);
        Assert.Equal("Nejde o oficiální produkt společnosti Prusa Research.", viewModel.Disclaimer);
        Assert.Equal("Licence: GNU General Public License v3.0 only", viewModel.LicenseText);
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

        Assert.Equal("Über LayerPeek", viewModel.Title);
        Assert.Equal("für Prusa-Drucker", viewModel.Subtitle);
        Assert.Equal("Version " + AboutViewModel.ProductVersion(AppAssembly), viewModel.VersionText);
        Assert.Equal("Dies ist kein offizielles Produkt von Prusa Research.", viewModel.Disclaimer);
        Assert.Equal("Lizenz: GNU General Public License v3.0 only", viewModel.LicenseText);
        Assert.Equal("Quellcode auf GitHub", viewModel.RepositoryText);
        Assert.Equal("Schließen", viewModel.CloseText);
        Assert.Equal("Copyright © Petr Foltýnek", viewModel.Copyright);
        // "Everything changed" for the bindings.
        Assert.Equal([string.Empty], changes);
    }

    [Theory]
    [InlineData("en-US", "for Prusa printers")]
    [InlineData("cs-CZ", "pro tiskárny Prusa")]
    [InlineData("de-DE", "für Prusa-Drucker")]
    [InlineData("es-ES", "para impresoras Prusa")]
    [InlineData("fr-FR", "pour imprimantes Prusa")]
    [InlineData("it-IT", "per stampanti Prusa")]
    [InlineData("pl-PL", "do drukarek Prusa")]
    [InlineData("sk-SK", "pre tlačiarne Prusa")]
    public void ProductNameIsNeverTranslatedOnlyItsSubtitleIs(string culture, string subtitle)
    {
        // T-074: the product is LayerPeek in every language; "for Prusa printers" only describes compatibility.
        using CultureScope scope = new(culture);

        AboutViewModel viewModel = new(_launcher, AppAssembly);

        Assert.Equal("LayerPeek", viewModel.AppName);
        Assert.Equal(subtitle, viewModel.Subtitle);
        Assert.Contains("Prusa Research", viewModel.Disclaimer, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryLinkOpensGitHubThroughLauncher()
    {
        AboutViewModel viewModel = new(_launcher, AppAssembly);

        viewModel.OpenRepositoryCommand.Execute(null);

        Assert.Equal([new Uri("https://github.com/pfoltynek/LayerPeek")], _launcher.Opened);
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LayerPeek.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
