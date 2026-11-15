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
        Assert.Equal(5, described.Count);
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

        // T-223: the Inter font (SIL Open Font License 1.1, with the copyright of the Inter authors).
        string inter = ThirdPartyLicenses.ReadText(Assert.Single(ThirdPartyLicenses.All, static c => c.License == "OFL-1.1"));
        Assert.Contains("SIL OPEN FONT LICENSE Version 1.1", inter, StringComparison.Ordinal);
        Assert.Contains("The Inter Project Authors", inter, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingTextResourceIsReported()
    {
        ThirdPartyComponent broken = ThirdPartyLicenses.All[0] with { TextResources = ["LayerPeek.Licenses.missing.txt"] };

        Assert.Throws<InvalidOperationException>(() => ThirdPartyLicenses.ReadText(broken));
    }

    [Fact]
    public void FFmpegSourceLinkIsThePinnedSourceTarballOfBothPlatforms()
    {
        // T-202: both platforms build the same official tarball (LayerPeekFFmpegSourceUrl); no BtbN build any more.
        string targets = File.ReadAllText(Path.Combine(RepositoryRoot, "build", "FFmpeg.targets"));
        Match pinned = PinnedSourceUrl().Match(targets);

        Assert.True(pinned.Success);
        Assert.Equal(new Uri(pinned.Groups[1].Value), ThirdPartyLicenses.FFmpeg.SourceTarballUri);
        Assert.Equal("LGPL-3.0-or-later", ThirdPartyLicenses.FFmpeg.License);
        Assert.Contains("ffmpeg-9.0.2.", ThirdPartyLicenses.FFmpeg.SourceTarballUri.AbsoluteUri, StringComparison.Ordinal);
        Assert.DoesNotContain("BtbN", targets, StringComparison.Ordinal);
        Assert.DoesNotContain("BtbN", File.ReadAllText(Path.Combine(RepositoryRoot, "src", "LayerPeek.App", "Licenses", "ThirdPartyLicenses.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void FFmpegBuildScriptLinksAreTheScriptsOfBothPlatforms()
    {
        Assert.EndsWith("/build/macos/build-ffmpeg.sh", ThirdPartyLicenses.FFmpeg.MacOSBuildScriptsUri.AbsoluteUri, StringComparison.Ordinal);
        Assert.EndsWith("/build/windows/build-ffmpeg.sh", ThirdPartyLicenses.FFmpeg.WindowsBuildScriptsUri.AbsoluteUri, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(RepositoryRoot, "build", "macos", "build-ffmpeg.sh")));
        Assert.True(File.Exists(Path.Combine(RepositoryRoot, "build", "windows", "build-ffmpeg.sh")));
    }

    [Theory]
    [InlineData("0.2.0", true, "https://github.com/pfoltynek/LayerPeek/blob/v0.2.0/build/macos/build-ffmpeg.sh")]
    [InlineData("1.10.3", true, "https://github.com/pfoltynek/LayerPeek/blob/v1.10.3/build/macos/build-ffmpeg.sh")]
    [InlineData("2.0.0-beta.1", true, "https://github.com/pfoltynek/LayerPeek/blob/v2.0.0-beta.1/build/macos/build-ffmpeg.sh")]
    [InlineData("1.0.0", false, "https://github.com/pfoltynek/LayerPeek/blob/v1.0.0/build/windows/build-ffmpeg.sh")]
    public void FFmpegBuildScriptLinkPointsToTheReleaseTagOfTheVersion(string version, bool macOS, string expected)
    {
        // User decision 2026-10-29: the release tag vX.Y.Z of the app version, never a branch (blob/main).
        Assert.Equal(new Uri(expected), ThirdPartyLicenses.FFmpeg.BuildScriptUri(version, macOS));
    }

    [Theory]
    [InlineData("1.0.0", false, "https://github.com/pfoltynek/LayerPeek/releases/download/v1.0.0/ffmpeg-source-1.0.0-windows.tar.xz")]
    [InlineData("1.0.0", true, "https://github.com/pfoltynek/LayerPeek/releases/download/v1.0.0/ffmpeg-source-1.0.0-macos.tar.xz")]
    [InlineData("2.0.0-beta.1", true, "https://github.com/pfoltynek/LayerPeek/releases/download/v2.0.0-beta.1/ffmpeg-source-2.0.0-beta.1-macos.tar.xz")]
    public void FFmpegSourceArchiveLinkPointsToTheReleaseOfTheVersion(string version, bool macOS, string expected)
    {
        // T-199: the file release-build.ps1 attaches to the release vX.Y.Z (tag format of scripts/release.ps1).
        Assert.Equal(new Uri(expected), ThirdPartyLicenses.FFmpeg.SourceArchiveUri(version, macOS));
    }

    [Fact]
    public void FFmpegSourceArchiveHasTheNameOfTheReleaseScripts()
    {
        // The app links the file by name; Get-FFmpegSourceFileName of the release tools writes it.
        string tools = File.ReadAllText(Path.Combine(RepositoryRoot, "scripts", "ReleaseTools", "ReleaseTools.psm1"));

        Assert.Contains("\"ffmpeg-source-$Version-$Os.tar.xz\"", tools, StringComparison.Ordinal);
        Assert.Equal("ffmpeg-source-1.0.0-windows.tar.xz", ThirdPartyLicenses.FFmpeg.SourceArchiveFileName("1.0.0", macOS: false));
        Assert.Equal("ffmpeg-source-1.0.0-macos.tar.xz", ThirdPartyLicenses.FFmpeg.SourceArchiveFileName("1.0.0", macOS: true));
    }

    [Fact]
    public void FFmpegBuildScriptLinksUseTheApplicationVersion()
    {
        string version = LayerPeek.App.Windows.AboutViewModel.ProductVersion(typeof(ThirdPartyLicenses).Assembly);
        XElement props = XElement.Load(Path.Combine(RepositoryRoot, "Directory.Build.props"));
        string pinned = props.Descendants("Version").Single().Value;

        Assert.Equal(pinned, version);
        Assert.Equal(ThirdPartyLicenses.FFmpeg.BuildScriptUri(version, macOS: true), ThirdPartyLicenses.FFmpeg.MacOSBuildScriptsUri);
        Assert.Equal(ThirdPartyLicenses.FFmpeg.BuildScriptUri(version, macOS: false), ThirdPartyLicenses.FFmpeg.WindowsBuildScriptsUri);
        Assert.Contains($"/blob/v{pinned}/", ThirdPartyLicenses.FFmpeg.MacOSBuildScriptsUri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains($"/blob/v{pinned}/", ThirdPartyLicenses.FFmpeg.WindowsBuildScriptsUri.AbsoluteUri, StringComparison.Ordinal);
        Assert.DoesNotContain("/blob/main/", ThirdPartyLicenses.FFmpeg.MacOSBuildScriptsUri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public void FFmpegMacOSBuildIsLgplOnlyWithVideoToolboxAsItsOnlyHardwareDecoder()
    {
        // T-026 (user decisions 2026-10-29): LGPL only, version 3. T-203 (user decision 2026-11-12):
        // hardware decoding of H.264 through VideoToolbox, a system framework, and nothing else.
        string script = File.ReadAllText(Path.Combine(RepositoryRoot, "build", "macos", "build-ffmpeg.sh"));
        Match flags = ConfigureFlags().Match(script);
        Assert.True(flags.Success);
        string configure = flags.Groups[1].Value;

        Assert.DoesNotContain("--enable-gpl", configure, StringComparison.Ordinal);
        Assert.DoesNotContain("--enable-nonfree", configure, StringComparison.Ordinal);
        Assert.Contains("--enable-videotoolbox --enable-hwaccel=h264_videotoolbox", configure, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(configure, "--enable-hwaccel"));
        // T-147: recording only remuxes (--enable-muxer=mp4), it never encodes.
        Assert.DoesNotContain("--enable-encoder", configure, StringComparison.Ordinal);
        Assert.Contains("--enable-version3", configure, StringComparison.Ordinal);
        Assert.Contains("--disable-autodetect", configure, StringComparison.Ordinal);
        Assert.Contains("--enable-shared", configure, StringComparison.Ordinal);
    }

    [Fact]
    public void FFmpegWindowsBuildIsLgplOnlyWithD3D11VAAsItsOnlyHardwareDecoder()
    {
        // T-202 (user decisions 2026-11-12): LGPL 3, no external library, hardware decoding of H.264 through D3D11VA only
        // (no DXVA2, no D3D12VA); the GCC runtime and winpthreads linked statically.
        string configure = WindowsConfigureFlags();

        Assert.DoesNotContain("--enable-gpl", configure, StringComparison.Ordinal);
        Assert.DoesNotContain("--enable-nonfree", configure, StringComparison.Ordinal);
        Assert.DoesNotContain("--enable-lib", configure, StringComparison.Ordinal);
        Assert.Contains("--enable-d3d11va --enable-hwaccel=h264_d3d11va,h264_d3d11va2", configure, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(configure, "--enable-hwaccel"));
        Assert.DoesNotContain("dxva2", configure.Replace("d3d11va2", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("d3d12va", configure, StringComparison.Ordinal);
        Assert.DoesNotContain("--enable-encoder", configure, StringComparison.Ordinal);
        Assert.Contains("--enable-version3", configure, StringComparison.Ordinal);
        Assert.Contains("--disable-autodetect", configure, StringComparison.Ordinal);
        Assert.Contains("--enable-shared", configure, StringComparison.Ordinal);
        Assert.Contains("--enable-w32threads --disable-pthreads", configure, StringComparison.Ordinal);
        Assert.Contains("--extra-ldflags=-static-libgcc", configure, StringComparison.Ordinal);
        Assert.Contains("--extra-libs=-l:libwinpthread.a", configure, StringComparison.Ordinal);
        // ASCII only: bash of MSYS2 and the byte-exact copy in the release source archive (ReleaseTools Pester tests).
        Assert.All(File.ReadAllBytes(Path.Combine(RepositoryRoot, "build", "windows", "build-ffmpeg.sh")), static b => Assert.True(b < 0x80));
    }

    [Fact]
    public void FFmpegBuildsOfBothPlatformsHaveTheSameFeatures()
    {
        // T-202: the same container, protocol, parser and decoder set on both OS; only the platform lines differ
        // (architecture, toolchain, threads, the hardware decoder of the OS).
        static string[] Common(string flags) =>
        [
            .. flags.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Where(static line => !line.StartsWith('#'))
                .Where(static line => !line.Contains("hwaccel", StringComparison.Ordinal) && !line.StartsWith("--arch", StringComparison.Ordinal)
                    && !line.StartsWith("--target-os", StringComparison.Ordinal) && !line.StartsWith("--install-name-dir", StringComparison.Ordinal)
                    && !line.Contains("--extra-", StringComparison.Ordinal) && !line.Contains("threads", StringComparison.Ordinal)),
        ];

        string mac = ConfigureFlags().Match(File.ReadAllText(Path.Combine(RepositoryRoot, "build", "macos", "build-ffmpeg.sh"))).Groups[1].Value;

        Assert.Equal(Common(mac), Common(WindowsConfigureFlags()));
        Assert.Contains("--enable-demuxer=rtsp,h264", Common(mac));
    }

    [Fact]
    public void MinGWRuntimeNoticesAreListedForTheStaticallyLinkedRuntime()
    {
        // T-202: the MinGW-w64 runtime (crt: ZPL 2.1 and the notices of COPYING.MinGW-w64-runtime.txt) and winpthreads
        // (MIT, parts BSD-3-Clause) are linked into the Windows FFmpeg DLLs; their notices must ship in binary form.
        ThirdPartyComponent mingw = Assert.Single(ThirdPartyLicenses.All, static c => c.Name == "MinGW-w64 runtime");
        string text = ThirdPartyLicenses.ReadText(mingw);

        Assert.Contains("statically", text, StringComparison.Ordinal);
        Assert.Contains("linked against the MinGW-w64 runtime", text, StringComparison.Ordinal);
        Assert.Contains("Copyright (c) 2011 mingw-w64 project", text, StringComparison.Ordinal);
        Assert.Contains("Lockless Inc.", text, StringComparison.Ordinal);
        Assert.Empty(mingw.Packages);
        Assert.Equal(nameof(Resources.Strings.LicensesComponentMinGW), mingw.DisplayNameKey);
    }

    [Fact]
    public void MinGWRuntimeVersionIsTheOneThatBuiltTheDlls()
    {
        // T-202 review (N1): the licences window names the version of the MinGW-w64 runtime in the Windows FFmpeg DLLs. The
        // build records the packages it used (build/windows/build-ffmpeg.sh, "Packages:" in BUILD.txt, copied next to the
        // tests as FFmpeg-BUILD.txt); after an MSYS2 update that rebuilt FFmpeg, this fails until the entry is updated.
        string build = Path.Combine(AppContext.BaseDirectory, "FFmpeg-BUILD.txt");
        Assert.SkipUnless(File.Exists(build) && File.ReadAllText(build).Contains("Packages:", StringComparison.Ordinal),
            "No FFmpeg-BUILD.txt of the Windows FFmpeg build next to the tests (macOS, or built with LayerPeekFFmpegEnabled=false).");

        string packages = File.ReadAllLines(build).Single(static line => line.StartsWith("Packages:", StringComparison.Ordinal));
        Match crt = CrtPackage().Match(packages);
        Assert.True(crt.Success, $"No mingw-w64 crt package in '{packages}' (MSYS2 without pacman?).");
        ThirdPartyComponent mingw = Assert.Single(ThirdPartyLicenses.All, static c => c.Name == "MinGW-w64 runtime");

        Assert.Equal(crt.Groups[1].Value, mingw.Version);
        Assert.Contains($"mingw-w64-x86_64-winpthreads {crt.Groups[1].Value}", packages, StringComparison.Ordinal);
    }

    private static string WindowsConfigureFlags()
    {
        Match flags = ConfigureFlags().Match(File.ReadAllText(Path.Combine(RepositoryRoot, "build", "windows", "build-ffmpeg.sh")));
        Assert.True(flags.Success);
        return flags.Groups[1].Value;
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

    // "mingw-w64-x86_64-crt 14.0.0.r598.ge3d18511f-1": the release version before the git suffix.
    [GeneratedRegex(@"mingw-w64-[a-z0-9_]+-crt (\d+\.\d+\.\d+)")]
    private static partial Regex CrtPackage();

    [GeneratedRegex("<LayerPeekFFmpegSourceUrl>([^<]+)</LayerPeekFFmpegSourceUrl>")]
    private static partial Regex PinnedSourceUrl();

    [GeneratedRegex(@"^CONFIGURE_FLAGS=\((.*?)^\)$", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex ConfigureFlags();

    [GeneratedRegex("""^MIN_MACOS="([0-9.]+)"$""", RegexOptions.Multiline)]
    private static partial Regex MacMinimumVersion();
}
