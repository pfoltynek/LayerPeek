using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using LayerPeek.App.Composition;
using LayerPeek.App.Platform.MacOS;
using LayerPeek.Core.Settings;
using LayerPeek.Tests.Shared;

namespace LayerPeek.App.Tests.Platform.MacOS;

/// <summary>
/// build/macos/Info.plist, the template of LayerPeek.app (T-019): the bundle identifier the app knows, no Dock icon
/// (LSUIElement), the executable the bundle checks look for, and only the version placeholder of the release script.
/// T-097: the Local Network text, in English there and translated in build/macos/Localizations/&lt;lang&gt;.lproj/InfoPlist.strings
/// for every app language (the list the resx translation-completeness test uses); not in the resx, so checked here.
/// </summary>
public sealed partial class AppBundleInfoPlistTests
{
    private const string LocalNetworkKey = "NSLocalNetworkUsageDescription";

    private static readonly string Localizations = Path.Combine(TestWorktree.Root, "build", "macos", "Localizations");

    private static readonly Dictionary<string, XElement> Entries = Read();

    private static Dictionary<string, XElement> Read()
    {
        XElement dict = XDocument.Load(Path.Combine(TestWorktree.Root, "build", "macos", "Info.plist")).Root!.Element("dict")!;
        List<XElement> items = [.. dict.Elements()];
        Dictionary<string, XElement> entries = new(StringComparer.Ordinal);
        for (int i = 0; i + 1 < items.Count; i += 2)
        {
            Assert.Equal("key", items[i].Name.LocalName);
            entries.Add(items[i].Value, items[i + 1]);
        }

        return entries;
    }

    [Fact]
    public void TheBundleIdentifierIsTheOneOfTheApp()
    {
        Assert.Equal(AppEnvironment.ProductionBundleIdentifier, Entries["CFBundleIdentifier"].Value);
    }

    [Fact]
    public void TheAppHasNoDockIcon()
    {
        Assert.Equal("true", Entries["LSUIElement"].Name.LocalName);
    }

    [Fact]
    public void TheExecutableIsTheOneTheBundleChecksLookFor()
    {
        Assert.Equal(MacAppBundle.ExecutableName, Entries["CFBundleExecutable"].Value);
        Assert.Equal("APPL", Entries["CFBundlePackageType"].Value);
        Assert.Equal("@VERSION@", Entries["CFBundleShortVersionString"].Value);
        Assert.Equal("@VERSION@", Entries["CFBundleVersion"].Value);
    }

    [Fact]
    public void TheLocalNetworkPermissionIsExplainedInEnglish()
    {
        Assert.Equal("string", Entries[LocalNetworkKey].Name.LocalName);
        Assert.Contains("local network", Entries[LocalNetworkKey].Value, StringComparison.Ordinal);
        Assert.Equal("en", Entries["CFBundleDevelopmentRegion"].Value);
    }

    [Fact]
    public void TheBundleListsTheAppLanguages()
    {
        Assert.Equal(AppLanguages.Supported, [.. Entries["CFBundleLocalizations"].Elements("string").Select(static e => e.Value)]);
    }

    [Fact]
    public void EveryAppLanguageExplainsTheLocalNetworkPermission()
    {
        foreach (string language in AppLanguages.Supported)
        {
            string path = Path.Combine(Localizations, language + ".lproj", "InfoPlist.strings");
            Assert.True(File.Exists(path), path);
            // UTF-8 without a byte order mark; an invalid byte throws.
            byte[] bytes = File.ReadAllBytes(path);
            Assert.False(bytes is [0xEF, 0xBB, 0xBF, ..], path);
            Dictionary<string, string> strings = ReadStrings(new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes));

            Assert.Equal([LocalNetworkKey], strings.Keys);
            Assert.False(string.IsNullOrWhiteSpace(strings[LocalNetworkKey]), path);
            if (language == "en")
            {
                Assert.Equal(Entries[LocalNetworkKey].Value, strings[LocalNetworkKey]);
            }
            else
            {
                Assert.NotEqual(Entries[LocalNetworkKey].Value, strings[LocalNetworkKey]);
            }
        }

        // Nothing for a language the app does not have.
        Assert.Equal(
            [.. AppLanguages.Supported.Select(static l => l + ".lproj").Order(StringComparer.Ordinal)],
            [.. Directory.GetFileSystemEntries(Localizations).Select(Path.GetFileName).Order(StringComparer.Ordinal)]);
    }

    /// <summary>The entries of an old-style .strings text: "key" = "value"; with comments; anything else fails the test.</summary>
    private static Dictionary<string, string> ReadStrings(string text)
    {
        Dictionary<string, string> entries = new(StringComparer.Ordinal);
        string rest = CommentsAndSpace().Replace(text, string.Empty, 1);
        while (rest.Length > 0)
        {
            Match entry = StringsEntry().Match(rest);
            Assert.True(entry.Success, "Not a .strings entry: " + rest);
            entries.Add(Unescape(entry.Groups["key"].Value), Unescape(entry.Groups["value"].Value));
            rest = CommentsAndSpace().Replace(rest[entry.Length..], string.Empty, 1);
        }

        return entries;
    }

    private static string Unescape(string value) => Escape().Replace(value, static m => m.Groups[1].Value switch
    {
        "n" => "\n",
        "t" => "\t",
        "\"" => "\"",
        "\\" => "\\",
        _ => throw new InvalidDataException("Unknown escape \\" + m.Groups[1].Value),
    });

    [GeneratedRegex(@"\A(?:\s+|/\*[\s\S]*?\*/|//[^\n]*(?:\n|\z))*")]
    private static partial Regex CommentsAndSpace();

    [GeneratedRegex(@"\A""(?<key>(?:[^""\\]|\\.)*)""\s*=\s*""(?<value>(?:[^""\\]|\\.)*)""\s*;")]
    private static partial Regex StringsEntry();

    [GeneratedRegex(@"\\(.)")]
    private static partial Regex Escape();
}
