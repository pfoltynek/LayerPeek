using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using LayerPeek.App.Licenses;

namespace LayerPeek.App.Tests.Licenses;

/// <summary>Keeps the licence list in line with what the app really ships (T-014, user decision 2026-10-18).</summary>
public sealed partial class ThirdPartyLicensesTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LayerPeek.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root was not found.");
    }

    /// <summary>Packages of the App's restored dependency graph (direct and transitive), from its project.assets.json.</summary>
    private static List<(string Id, string Version)> AppPackages()
    {
        string assets = Path.Combine(RepositoryRoot, "src", "LayerPeek.App", "obj", "project.assets.json");
        Assert.True(File.Exists(assets), $"{assets} is missing: restore or build src/LayerPeek.App before running this test.");
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

    /// <summary>Package ids the App restores: its packages and the runtime packs downloaded for its runtime identifier.</summary>
    private static HashSet<string> AppPackageAndRuntimePackIds()
    {
        HashSet<string> ids = new(AppPackages().Select(static package => package.Id), StringComparer.OrdinalIgnoreCase);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot, "src", "LayerPeek.App", "obj", "project.assets.json")));
        foreach (JsonProperty framework in document.RootElement.GetProperty("project").GetProperty("frameworks").EnumerateObject())
        {
            if (framework.Value.TryGetProperty("downloadDependencies", out JsonElement downloads))
            {
                foreach (JsonElement download in downloads.EnumerateArray())
                {
                    ids.Add(download.GetProperty("name").GetString()!);
                }
            }
        }

        return ids;
    }

    /// <summary>Shared frameworks referenced by the source projects (the App gets them transitively), except the base one.</summary>
    private static SortedSet<string> SharedFrameworksOfTheSources()
    {
        SortedSet<string> frameworks = new(StringComparer.OrdinalIgnoreCase);
        foreach (string project in Directory.EnumerateDirectories(Path.Combine(RepositoryRoot, "src")))
        {
            string assets = Path.Combine(project, "obj", "project.assets.json");
            Assert.True(File.Exists(assets), $"{assets} is missing: restore the solution before running this test.");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(assets));
            foreach (JsonProperty framework in document.RootElement.GetProperty("project").GetProperty("frameworks").EnumerateObject())
            {
                if (framework.Value.TryGetProperty("frameworkReferences", out JsonElement references))
                {
                    foreach (JsonProperty reference in references.EnumerateObject())
                    {
                        frameworks.Add(reference.Name);
                    }
                }
            }
        }

        frameworks.Remove("Microsoft.NETCore.App");
        return frameworks;
    }

    [Fact]
    public void EverySharedFrameworkCompiledIntoTheAppHasALicenceEntry()
    {
        SortedSet<string> frameworks = SharedFrameworksOfTheSources();

        // T-028: the PrusaLink web proxy runs on Kestrel from the ASP.NET Core shared framework.
        Assert.Contains("Microsoft.AspNetCore.App", frameworks);
        foreach (string framework in frameworks)
        {
            Assert.True(
                Find(framework + ".Runtime.win-x64") is not null && Find(framework + ".Runtime.osx-arm64") is not null,
                $"Add a licence entry for the shared framework {framework} (its runtime packs) in Licenses/ThirdPartyLicenses.cs.");
        }
    }

    [Fact]
    public void NoEntryListsAPackageTheAppNoLongerUses()
    {
        HashSet<string> restored = AppPackageAndRuntimePackIds();
        List<string> stale = [];
        foreach (PackageRef package in ThirdPartyLicenses.All.SelectMany(static component => component.Packages))
        {
            // Entries for the other operating system name the same package with another runtime identifier.
            string[] candidates = [package.Id, package.Id.Replace("osx-arm64", "win-x64", StringComparison.Ordinal), package.Id.Replace("win-x64", "osx-arm64", StringComparison.Ordinal)];
            if (!candidates.Any(restored.Contains))
            {
                stale.Add(package.Id);
            }
        }

        Assert.True(stale.Count == 0, "Remove licence entries of packages the app no longer ships from Licenses/ThirdPartyLicenses.cs: " + string.Join(", ", stale));
    }

    [Fact]
    public void TranslatedComponentNamesKeepTheProductNameInEveryLanguage()
    {
        // User decision 2026-10-19: descriptions are translated, product names stay; licence texts stay English.
        List<ThirdPartyComponent> described = [.. ThirdPartyLicenses.All.Where(static c => c.DisplayNameKey is not null)];
        Assert.Equal(3, described.Count);
        foreach (ThirdPartyComponent component in described)
        {
            foreach (string code in Core.Settings.AppLanguages.Supported)
            {
                string? text = Resources.Strings.ResourceManager.GetString(component.DisplayNameKey!, System.Globalization.CultureInfo.GetCultureInfo(code));
                Assert.False(string.IsNullOrWhiteSpace(text), $"{component.DisplayNameKey} ({code})");
                Assert.Contains(component.Name, text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void ShownVersionIsTheVersionOfEveryPackageOfTheComponent()
    {
        foreach (ThirdPartyComponent component in ThirdPartyLicenses.All.Where(static c => c.Version is not null))
        {
            Assert.All(component.Packages, package => Assert.Equal(component.Version, package.Version));
            LicenseItemViewModel item = new(component, new Tests.Tray.Menu.FakeUrlLauncher());
            Assert.EndsWith(component.Version!, item.VersionText, StringComparison.Ordinal);
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
            // Whitespace as in the original files (Velopack's licence aligns its words with double spaces).
            string text = System.Text.RegularExpressions.Regex.Replace(ThirdPartyLicenses.ReadText(component), @"\s+", " ");
            Assert.Contains("Permission is hereby granted, free of charge", text, StringComparison.Ordinal);
        }

        ThirdPartyComponent angle = Assert.Single(ThirdPartyLicenses.All, static c => c.License == "BSD-3-Clause");
        Assert.Contains("The ANGLE Project Authors", ThirdPartyLicenses.ReadText(angle), StringComparison.Ordinal);
    }

    [Fact]
    public void MissingTextResourceIsReported()
    {
        ThirdPartyComponent broken = ThirdPartyLicenses.All[0] with { TextResources = ["LayerPeek.Licenses.missing.txt"] };

        Assert.Throws<InvalidOperationException>(() => ThirdPartyLicenses.ReadText(broken));
    }

    [Fact]
    public void FFmpegBuildLinkIsThePinnedBuild()
    {
        string targets = File.ReadAllText(Path.Combine(RepositoryRoot, "build", "FFmpeg.targets"));
        Match pinned = PinnedUrl().Match(targets);

        Assert.True(pinned.Success);
        Assert.Equal(new Uri(pinned.Groups[1].Value), ThirdPartyLicenses.FFmpeg.WindowsBuildUri);
        Assert.Equal("LGPL-3.0-or-later", ThirdPartyLicenses.FFmpeg.License);
    }

    [Fact]
    public void FFmpegMacOSBuildLinkIsThePinnedSourceTarball()
    {
        string targets = File.ReadAllText(Path.Combine(RepositoryRoot, "build", "FFmpeg.targets"));
        Match pinned = PinnedMacSourceUrl().Match(targets);

        Assert.True(pinned.Success);
        Assert.Equal(new Uri(pinned.Groups[1].Value), ThirdPartyLicenses.FFmpeg.MacOSBuildUri);
        // The same FFmpeg release as the Windows build (n9.0.2 of BtbN).
        Assert.Contains("ffmpeg-9.0.2.", ThirdPartyLicenses.FFmpeg.MacOSBuildUri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains("n9.0.2-", ThirdPartyLicenses.FFmpeg.WindowsBuildUri.AbsoluteUri, StringComparison.Ordinal);
        Assert.EndsWith("/build/macos/build-ffmpeg.sh", ThirdPartyLicenses.FFmpeg.MacOSBuildScriptsUri.AbsoluteUri, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(RepositoryRoot, "build", "macos", "build-ffmpeg.sh")));
    }

    [Fact]
    public void FFmpegMacOSBuildIsLgplOnlyWithoutHardwareDecoding()
    {
        // T-026 (user decisions 2026-10-29): LGPL only, version 3 like the BtbN build, software decoding only.
        string script = File.ReadAllText(Path.Combine(RepositoryRoot, "build", "macos", "build-ffmpeg.sh"));
        Match flags = ConfigureFlags().Match(script);
        Assert.True(flags.Success);
        string configure = flags.Groups[1].Value;

        Assert.DoesNotContain("--enable-gpl", configure, StringComparison.Ordinal);
        Assert.DoesNotContain("--enable-nonfree", configure, StringComparison.Ordinal);
        Assert.DoesNotContain("--enable-videotoolbox", configure, StringComparison.Ordinal);
        Assert.Contains("--enable-version3", configure, StringComparison.Ordinal);
        Assert.Contains("--disable-autodetect", configure, StringComparison.Ordinal);
        Assert.Contains("--enable-shared", configure, StringComparison.Ordinal);
    }

    [Fact]
    public void FFmpegMacOSBuildTargetsTheMinimumMacOSVersionOfTheBundle()
    {
        string script = File.ReadAllText(Path.Combine(RepositoryRoot, "build", "macos", "build-ffmpeg.sh"));
        // The DOCTYPE names Apple's DTD: never fetched.
        using System.Xml.XmlReader reader = System.Xml.XmlReader.Create(
            Path.Combine(RepositoryRoot, "build", "macos", "Info.plist"),
            new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Ignore, XmlResolver = null });
        XElement plist = XElement.Load(reader);
        string minimum = plist.Element("dict")!.Elements("key").Single(static key => key.Value == "LSMinimumSystemVersion")
            .ElementsAfterSelf().First().Value;

        Match pinned = MacMinimumVersion().Match(script);

        Assert.True(pinned.Success);
        Assert.Equal(minimum, pinned.Groups[1].Value);
    }

    [GeneratedRegex("<LayerPeekFFmpegUrl>([^<]+)</LayerPeekFFmpegUrl>")]
    private static partial Regex PinnedUrl();

    [GeneratedRegex("<LayerPeekFFmpegMacSourceUrl>([^<]+)</LayerPeekFFmpegMacSourceUrl>")]
    private static partial Regex PinnedMacSourceUrl();

    [GeneratedRegex(@"^CONFIGURE_FLAGS=\((.*?)^\)$", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex ConfigureFlags();

    [GeneratedRegex("""^MIN_MACOS="([0-9.]+)"$""", RegexOptions.Multiline)]
    private static partial Regex MacMinimumVersion();
}
