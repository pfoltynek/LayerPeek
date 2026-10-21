using System.Drawing;

using PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;
using PrusaTrayDiagnostic.Tests.Shared;

namespace PrusaTrayDiagnostic.App.E2E.Tests;

/// <summary>
/// The app icon on the exe and on the windows (T-025), and the absence of every test switch from Release builds (T-043,
/// T-044): the Release assembly and, when it was published in this run (newer than the Release build), the Native AOT exe.
/// </summary>
public sealed class IconAndBuildTests(E2EFixture fixture)
{
    private static readonly string AppProject = Path.Combine(TestWorktree.Root, "src", "PrusaTrayDiagnostic.App");

    private static readonly string IconFile = Path.Combine(AppProject, "Assets", "AppIcon", "app-icon.ico");

    /// <summary>Every Debug-only switch of the app; none may appear in a Release build.</summary>
    private static readonly string[] Switches =
    [
        AppSwitches.DataDirectoryVariable,
        AppSwitches.SuffixVariable,
        AppSwitches.ForwardOnlyVariable,
        AppSwitches.NativeLibraryProbeVariable,
        AppSwitches.SecretsFileName,
        AppSwitches.LogFileName,
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
        string debug = Path.ChangeExtension(E2EFixture.AppPath, ".dll");
        Assert.All(Switches, name => Assert.True(E2EFixture.BinaryContains(debug, name), $"The scan finds nothing: '{name}' is missing in the Debug build."));

        string release = Path.Combine(AppProject, "bin", "Release", "net11.0", "PrusaTrayDiagnostic.dll");
        string published = Path.Combine(AppProject, "bin", "Release", "net11.0", "win-x64", "publish", "PrusaTrayDiagnostic.exe");
        Assert.True(File.Exists(release), $"No Release build at '{release}' (tools/run-e2e.ps1 builds it).");
        // A published exe counts only when it is not older than the Release build (published by this run, -PublishAot); an
        // older one was built from other sources and proves nothing about these.
        bool freshPublish = File.Exists(published) && File.GetLastWriteTimeUtc(published) >= File.GetLastWriteTimeUtc(release);
        foreach (string binary in freshPublish ? [release, published] : new[] { release })
        {
            Assert.All(Switches, name => Assert.False(E2EFixture.BinaryContains(binary, name), $"'{binary}' contains the test switch '{name}'."));
        }
    }

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
