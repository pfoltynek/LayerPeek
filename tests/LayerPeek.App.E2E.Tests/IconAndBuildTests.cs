using System.Drawing;

using LayerPeek.App.E2E.Tests.Infrastructure;
using LayerPeek.Tests.Shared;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// The app icon on the exe and on the windows (T-025), and the absence of every test switch from Release builds (T-043,
/// T-044): the Release assembly and, when it was published in this run (newer than the Release build), the Native AOT exe.
/// </summary>
public sealed class IconAndBuildTests(E2EFixture fixture)
{
    private static readonly string AppProject = Path.Combine(TestWorktree.Root, "src", "LayerPeek.App");

    private static readonly string IconFile = Path.Combine(AppProject, "Assets", "AppIcon", "app-icon.ico");

    /// <summary>Every Debug-only switch of the app; none may appear in a Release build.</summary>
    private static readonly string[] Switches =
    [
        AppSwitches.DataDirectoryVariable,
        AppSwitches.SuffixVariable,
        AppSwitches.ForwardOnlyVariable,
        AppSwitches.NativeLibraryProbeVariable,
        AppSwitches.SecretsFileName,
        AppSwitches.CrashTestMessage,
        AppSwitches.TestToastsVariable,
        AppSwitches.TestCredentialPrefixVariable,
        AppSwitches.TestCredentialPrefixRoot,
        AppSwitches.TestStartDelayVariable,
        AppSwitches.ClearToastsArgument,
        // The name of the test shortcut, with and without its extension (the app name Windows shows in test toasts).
        AppSwitches.TestShortcutFileName,
        Path.GetFileNameWithoutExtension(AppSwitches.TestShortcutFileName),
        AppSwitches.OpenedUrlsFileName,
        // T-068: the test app's toast activator and the test switches in its command line.
        AppSwitches.TestToastActivatorClsid,
        AppSwitches.TestEnvironmentArgument,
        // T-020: the macOS test bundle and its status mode.
        AppSwitches.NotificationStatusArgument,
        AppSwitches.TestBundleIdentifier,
    ];

    [Fact]
    public void ExeCarriesTheAppIcon()
    {
        E2EFixture.Require();
        nint[] large = new nint[1];
        Assert.True(Native.ExtractIconEx(E2EFixture.AppPath, 0, large, null, 1) >= 1 && large[0] != 0, "The exe has no icon.");
        try
        {
            AssertLooksLikeTheAppIcon(large[0], "exe-icon");
        }
        finally
        {
            Native.DestroyIcon(large[0]);
        }
    }

    [Fact]
    public async Task WindowsCarryTheAppIcon()
    {
        E2EFixture.Require();
        await using AppInstance app = await AppInstance.StartAsync(fixture, "icons", TestSettings.Json("en", []));
        nint settings = await app.OpenAsync("settings", AppWindowKind.Settings);
        nint diagnostics = await app.OpenAsync("diagnostics", AppWindowKind.Diagnostics);
        nint about = await app.OpenAsync("about", AppWindowKind.Other);

        foreach ((nint window, string name) in new[] { (settings, "settings"), (diagnostics, "diagnostics"), (about, "about") })
        {
            WindowChecks.AssertHasIcon(window, name);
            AssertLooksLikeTheAppIcon(Native.WindowIcon(window, big: true), $"window-icon-{name}");
        }
    }

    [Fact]
    public void ReleaseBuildsContainNoTestSwitches()
    {
        E2EFixture.Require();
        // The Release builds are checked once, in the pass against the Debug exe (its managed assembly is the reference that
        // the scan finds the switches at all); the Native AOT pass of the Debug configuration has no such assembly (T-073).
        Assert.SkipWhen(E2EFixture.IsNativeAot, "Checked in the pass against the Debug exe; the Native AOT exe has no managed assembly to compare with.");
        string debug = Path.ChangeExtension(E2EFixture.AppPath, ".dll");
        Assert.All(Switches, name => Assert.True(E2EFixture.BinaryContains(debug, name), $"The scan finds nothing: '{name}' is missing in the Debug build."));

        string release = Path.Combine(AppProject, "bin", "Release", "net11.0", "LayerPeek.dll");
        string published = Path.Combine(AppProject, "bin", "Release", "net11.0", "win-x64", "publish", "LayerPeek.exe");
        Assert.True(File.Exists(release), $"No Release build at '{release}' (tools/run-e2e.ps1 builds it).");
        // A published exe counts only when it is not older than the Release build (published by this run, -PublishAot); an
        // older one was built from other sources and proves nothing about these.
        bool freshPublish = File.Exists(published) && File.GetLastWriteTimeUtc(published) >= File.GetLastWriteTimeUtc(release);
        foreach (string binary in freshPublish ? [release, published] : new[] { release })
        {
            Assert.All(Switches, name => Assert.False(E2EFixture.BinaryContains(binary, name), $"'{binary}' contains the test switch '{name}'."));
        }
    }

    [Fact]
    public void BuildsCarryNoFormerProductName()
    {
        // T-074: the app was renamed from its former name before the first release; no identity of the old name (data folder,
        // AUMID, credential prefix, shortcut, pipe) may survive in the exe the tests run nor in the Release builds.
        E2EFixture.Require();
        // The binaries embed their PDB path: a worktree folder that still carries the former name (renamed by hand after the
        // merge) would be found there, not in the code.
        Assert.SkipWhen(
            FormerNames.Any(name => TestWorktree.Root.Contains(name, StringComparison.OrdinalIgnoreCase)),
            "The worktree folder still carries the former name (its PDB path is embedded in the binaries).");
        string release = Path.Combine(AppProject, "bin", "Release", "net11.0", "LayerPeek.dll");
        string published = Path.Combine(AppProject, "bin", "Release", "net11.0", "win-x64", "publish", "LayerPeek.exe");
        string[] binaries = [E2EFixture.AppPath, .. new[] { Path.ChangeExtension(E2EFixture.AppPath, ".dll"), release, published }.Where(File.Exists)];
        foreach (string binary in binaries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Assert.All(FormerNames, name => Assert.False(E2EFixture.BinaryContains(binary, name), $"'{binary}' contains the former name '{name}'."));
        }
    }

    /// <summary>The former product name in the spellings the code used (T-074).</summary>
    private static readonly string[] FormerNames = ["PrusaTrayDiagnostic", "Prusa Tray Diagnostic", "PRUSATRAYDIAGNOSTIC"];

    /// <summary>The icon matches the app icon file at the same size (mean colour difference below 10 %), saved for review.</summary>
    private static void AssertLooksLikeTheAppIcon(nint handle, string name)
    {
        using Icon shown = Icon.FromHandle(handle);
        using Bitmap actual = shown.ToBitmap();
        using Icon reference = new(IconFile, actual.Width, actual.Height);
        using Bitmap expected = reference.ToBitmap();
        Screenshots.Save(actual, name);
        Assert.Equal(expected.Size, actual.Size);

        double difference = 0;
        for (int y = 0; y < actual.Height; y++)
        {
            for (int x = 0; x < actual.Width; x++)
            {
                Color a = actual.GetPixel(x, y);
                Color b = expected.GetPixel(x, y);
                difference += (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) + Math.Abs(a.A - b.A)) / (4 * 255.0);
            }
        }

        difference /= actual.Width * actual.Height;
        Assert.True(difference < 0.1, $"{name}: {actual.Width}×{actual.Height} differs from the app icon by {difference:P1}.");
    }
}
