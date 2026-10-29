#if DEBUG
using LayerPeek.App.Composition;
using LayerPeek.Tests.Shared;

namespace LayerPeek.App.Tests.Platform.MacOS;

/// <summary>
/// The Info.plist of the test bundle <c>LayerPeek E2E.app</c> of the macOS notification E2E test (T-020): the production plist
/// with the test identity only, never the production bundle identifier.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("macos")]
public sealed class TestBundleInfoPlistTests
{
    [Fact]
    public void TheTestBundleHasTheTestIdentityOnly()
    {
        string production = File.ReadAllText(Path.Combine(TestWorktree.Root, "build", "macos", "Info.plist"));

        string test = MacNotificationsE2ETests.TestBundle.TestInfoPlist(production);

        Assert.Contains($"<key>CFBundleIdentifier</key>\n\t<string>{AppEnvironment.TestBundleIdentifier}</string>", test.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.DoesNotContain($"<string>{AppEnvironment.ProductionBundleIdentifier}</string>", test, StringComparison.Ordinal);
        Assert.Contains("<key>CFBundleName</key>\n\t<string>LayerPeek E2E</string>", test.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("<key>CFBundleDisplayName</key>\n\t<string>LayerPeek E2E</string>", test.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.DoesNotContain("CFBundleIconFile", test, StringComparison.Ordinal);
        Assert.DoesNotContain("@VERSION@", test, StringComparison.Ordinal);
        // The rest of the identity of the app stays: the executable and the agent app without a Dock icon.
        Assert.Contains("<key>CFBundleExecutable</key>\n\t<string>LayerPeek</string>", test.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("<key>LSUIElement</key>", test, StringComparison.Ordinal);
    }
}
#endif
