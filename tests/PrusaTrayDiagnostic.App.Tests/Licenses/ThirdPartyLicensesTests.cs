using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using PrusaTrayDiagnostic.App.Licenses;

namespace PrusaTrayDiagnostic.App.Tests.Licenses;

/// <summary>Keeps the licence list in line with what the app really ships (T-014, user decision 2026-10-18).</summary>
public sealed partial class ThirdPartyLicensesTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PrusaTrayDiagnostic.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root was not found.");
    }

    /// <summary>Packages of the App's restored dependency graph (direct and transitive), from its project.assets.json.</summary>
    private static List<(string Id, string Version)> AppPackages()
    {
        string assets = Path.Combine(RepositoryRoot, "src", "PrusaTrayDiagnostic.App", "obj", "project.assets.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(assets));
        List<(string, string)> packages = [];
        foreach (JsonProperty library in document.RootElement.GetProperty("libraries").EnumerateObject())
        {
            if (library.Value.GetProperty("type").GetString() == "package")
            {
                string[] parts = library.Name.Split('/');
                packages.Add((parts[0], parts[1]));
            }
        }

        return packages;
    }

    private static PackageRef? Find(string id) =>
        ThirdPartyLicenses.All.SelectMany(static component => component.Packages)
            .FirstOrDefault(package => string.Equals(package.Id, id, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void EveryShippedPackageHasALicenceEntryWithItsVersion()
    {
        List<(string Id, string Version)> packages = AppPackages();
        Assert.NotEmpty(packages);

        List<string> missing = [];
        foreach ((string id, string version) in packages)
        {
            if (ThirdPartyLicenses.BuildOnlyPackages.Contains(id, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            PackageRef? entry = Find(id);
            if (entry is null || (entry.Version is not null && !string.Equals(entry.Version, version, StringComparison.OrdinalIgnoreCase)))
            {
                missing.Add($"{id} {version} (listed: {entry?.Version ?? "no"})");
            }
        }

        Assert.True(missing.Count == 0, "Add or update licence entries in Licenses/ThirdPartyLicenses.cs: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryCentralPackageUsedByTheAppHasALicenceEntry()
    {
        // Directory.Packages.props: the groups that the app ships (not the test packages).
        XDocument props = XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Packages.props"));
        List<(string Id, string Version)> central = [.. props.Descendants("ItemGroup")
            .Where(static group => (string?)group.Attribute("Label") is not "Tests")
            .Elements("PackageVersion")
            .Select(static package => ((string)package.Attribute("Include")!, (string)package.Attribute("Version")!))];
        HashSet<string> shipped = new(AppPackages().Select(static package => package.Id), StringComparer.OrdinalIgnoreCase);
        Assert.NotEmpty(central);

        foreach ((string id, string version) in central.Where(package => shipped.Contains(package.Id)))
        {
            PackageRef? entry = Find(id);
            Assert.True(entry is not null && entry.Version == version, $"No licence entry for {id} {version}.");
        }
    }

    [Fact]
    public void EveryLicenceTextIsEmbedded()
    {
        foreach (ThirdPartyComponent component in ThirdPartyLicenses.All)
        {
            Assert.NotEmpty(component.TextResources);
            string text = ThirdPartyLicenses.ReadText(component);
            Assert.False(string.IsNullOrWhiteSpace(text), component.Name);
            Assert.True(component.ProjectUri.Scheme == Uri.UriSchemeHttps, component.Name);
        }
    }

    [Fact]
    public void LicencesComeFromTheEntriesNotGuesses()
    {
        // The MIT entries carry the MIT text; the others their own licence file.
        foreach (ThirdPartyComponent component in ThirdPartyLicenses.All.Where(static c => c.License == "MIT"))
        {
            Assert.Contains("Permission is hereby granted, free of charge", ThirdPartyLicenses.ReadText(component), StringComparison.Ordinal);
        }

        ThirdPartyComponent angle = Assert.Single(ThirdPartyLicenses.All, static c => c.License == "BSD-3-Clause");
        Assert.Contains("The ANGLE Project Authors", ThirdPartyLicenses.ReadText(angle), StringComparison.Ordinal);
    }

    [Fact]
    public void MissingTextResourceIsReported()
    {
        ThirdPartyComponent broken = ThirdPartyLicenses.All[0] with { TextResources = ["PrusaTrayDiagnostic.Licenses.missing.txt"] };

        Assert.Throws<InvalidOperationException>(() => ThirdPartyLicenses.ReadText(broken));
    }

    [Fact]
    public void FFmpegBuildLinkIsThePinnedBuild()
    {
        string targets = File.ReadAllText(Path.Combine(RepositoryRoot, "build", "FFmpeg.targets"));
        Match pinned = PinnedUrl().Match(targets);

        Assert.True(pinned.Success);
        Assert.Equal(new Uri(pinned.Groups[1].Value), ThirdPartyLicenses.FFmpeg.BuildUri);
        Assert.Equal("LGPL-3.0-or-later", ThirdPartyLicenses.FFmpeg.License);
    }

    [GeneratedRegex("<PtdFFmpegUrl>([^<]+)</PtdFFmpegUrl>")]
    private static partial Regex PinnedUrl();
}
