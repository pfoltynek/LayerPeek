using System.Xml.Linq;

using LayerPeek.App.Composition;
using LayerPeek.App.Platform.MacOS;
using LayerPeek.Tests.Shared;

namespace LayerPeek.App.Tests.Platform.MacOS;

/// <summary>
/// build/macos/Info.plist, the template of LayerPeek.app (T-019): the bundle identifier the app knows, no Dock icon
/// (LSUIElement), the executable the bundle checks look for, and only the version placeholder of the release script.
/// </summary>
public sealed class AppBundleInfoPlistTests
{
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
}
