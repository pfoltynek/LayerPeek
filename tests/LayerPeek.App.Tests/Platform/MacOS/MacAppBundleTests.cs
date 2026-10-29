using LayerPeek.App.Platform.MacOS;

namespace LayerPeek.App.Tests.Platform.MacOS;

/// <summary>Where the running app is on macOS (T-019): in its LayerPeek.app, and whether a Login Item of it would last.</summary>
public sealed class MacAppBundleTests
{
    [Theory]
    [InlineData("/Applications/LayerPeek.app/Contents/MacOS/LayerPeek", "/Applications/LayerPeek.app")]
    [InlineData("/Users/u/Apps/LayerPeek 2.app/Contents/MacOS/LayerPeek", "/Users/u/Apps/LayerPeek 2.app")]
    [InlineData("/Volumes/LayerPeek/LayerPeek.app/Contents/MacOS/LayerPeek", "/Volumes/LayerPeek/LayerPeek.app")]
    [InlineData("/Users/u/src/LayerPeek/src/LayerPeek.App/bin/Debug/net11.0/LayerPeek", null)]
    [InlineData("/Applications/LayerPeek.app/Contents/MacOS/UpdateMac", null)]
    [InlineData("/Applications/LayerPeek.app/Contents/Resources/LayerPeek", null)]
    [InlineData("/Applications/LayerPeek/Contents/MacOS/LayerPeek", null)]
    [InlineData("LayerPeek.app/Contents/MacOS/LayerPeek", null)]
    [InlineData("/Users/u/tests/LayerPeek.App.Tests", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void TheBundleIsFoundOnlyAroundItsExecutable(string? executable, string? bundle)
    {
        Assert.Equal(bundle, MacAppBundle.BundleOf(executable));
    }

    [Theory]
    [InlineData("/Applications/LayerPeek.app", false, true)]
    [InlineData("/Users/u/Downloads/LayerPeek.app", false, true)]
    [InlineData("/private/var/folders/xy/abc/T/AppTranslocation/0D1B2C3D/d/LayerPeek.app", false, false)]
    [InlineData("/Volumes/LayerPeek/LayerPeek.app", true, false)]
    public void ALoginItemNeedsAPermanentPlace(string bundle, bool readOnly, bool permanent)
    {
        Assert.Equal(permanent, MacAppBundle.IsPermanentLocation(bundle, _ => readOnly));
    }
}

/// <summary>The native calls behind <see cref="MacAppBundle"/> and <see cref="MacAppActivation"/> (macOS only, T-019).</summary>
[System.Runtime.Versioning.SupportedOSPlatform("macos")]
public sealed class MacNativeCallTests
{
    public MacNativeCallTests()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Native calls of macOS.");
    }

    [Fact]
    public void AWritableFolderIsNotOnAReadOnlyVolume()
    {
        string folder = TestDirectories.NewDirectoryPath();
        Directory.CreateDirectory(folder);
        try
        {
            Assert.False(MacAppBundle.IsOnReadOnlyVolume(folder));
        }
        finally
        {
            TestDirectories.Delete(folder);
        }
    }

    /// <summary>
    /// Only EROFS counts: a folder this user may not write (EACCES, the system volume) or a missing one is not a read-only
    /// volume. The read-only volume itself (the mounted .dmg) is checked by hand (T-019 notes).
    /// </summary>
    [Fact]
    public void AMissingPermissionOrFolderIsNotAReadOnlyVolume()
    {
        Assert.False(MacAppBundle.IsOnReadOnlyVolume("/System/Library/CoreServices"));
        Assert.False(MacAppBundle.IsOnReadOnlyVolume(TestDirectories.NewDirectoryPath()));
    }

    /// <summary>
    /// Bringing the app to the front never creates an NSApp: in the test host (no Avalonia app on AppKit) it does nothing.
    /// </summary>
    [Fact]
    public void ActivatingWithoutAnAppDoesNothing()
    {
        MacAppActivation.Activate();
        new MacWindowShell().RequestAttention(0);
    }
}
