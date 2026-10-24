using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;

using LayerPeek.App.Installer;
using LayerPeek.Tests.Shared;

namespace LayerPeek.App.Tests.Installer;

/// <summary>
/// The start of the app inside a Velopack installation (T-017): Velopack runs first in <c>Main</c>, before the
/// single-instance guard, so that the installer hooks run and end without UI even while the app runs; outside an
/// installation it is not even created (no Velopack log in <c>%LOCALAPPDATA%\velopack</c> for development, test and
/// <c>.zip</c> starts).
/// </summary>
public sealed class VelopackStartupTests
{
    private const string InstalledExe = @"C:\Users\u\AppData\Local\LayerPeek\current\LayerPeek.exe";

    private static readonly HashSet<string> InstalledFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        @"C:\Users\u\AppData\Local\LayerPeek\Update.exe",
        @"C:\Users\u\AppData\Local\LayerPeek\current\sq.version",
    };

    [Fact]
    public void AnExeNextToTheVelopackManifestBelowUpdateExeIsInstalled()
    {
        Assert.True(VelopackInstallation.IsInstalled(InstalledExe, InstalledFiles.Contains));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"D:\Source\LayerPeek\src\LayerPeek.App\bin\Debug\net11.0\LayerPeek.exe")]
    [InlineData(@"C:\Tools\LayerPeek-0.1.0-win-x64\LayerPeek.exe")]
    [InlineData("LayerPeek.exe")]
    public void AnyOtherExeIsNotInstalled(string? executable)
    {
        Assert.False(VelopackInstallation.IsInstalled(executable, InstalledFiles.Contains));
    }

    [Fact]
    public void UpdateExeWithoutTheManifestIsNoInstallation()
    {
        HashSet<string> onlyUpdater = new(StringComparer.OrdinalIgnoreCase) { @"C:\Users\u\AppData\Local\LayerPeek\Update.exe" };

        Assert.False(VelopackInstallation.IsInstalled(InstalledExe, onlyUpdater.Contains));
    }

    [Fact]
    public void TheManifestWithoutUpdateExeIsNoInstallation()
    {
        HashSet<string> onlyManifest = new(StringComparer.OrdinalIgnoreCase) { @"C:\Users\u\AppData\Local\LayerPeek\current\sq.version" };

        Assert.False(VelopackInstallation.IsInstalled(InstalledExe, onlyManifest.Contains));
    }

    [Theory]
    [InlineData("--veloapp-install", "0.1.0")]
    [InlineData("--veloapp-uninstall", "0.1.0")]
    [InlineData("--VELOAPP-updated", "0.1.0")]
    [InlineData("--veloapp-obsolete")]
    public void VelopackHookArgumentsAreRecognized(params string[] arguments)
    {
        Assert.True(VelopackInstallation.IsHookStart(arguments));
    }

    [Theory]
    [InlineData]
    [InlineData("-ToastActivated")]
    [InlineData("--layerpeek-e2e-run=x", "--veloapp-install")]
    [InlineData("veloapp-install")]
    public void OtherArgumentsAreNoHook(params string[] arguments)
    {
        Assert.False(VelopackInstallation.IsHookStart(arguments));
    }

    [Fact]
    public void AnInstalledStartRunsVelopackAndContinues()
    {
        List<string[]> runs = [];

        int? exit = VelopackStartup.Run(["-ToastActivated"], InstalledExe, InstalledFiles.Contains, runs.Add, out _);

        Assert.Null(exit);
        Assert.Equal(["-ToastActivated"], Assert.Single(runs));
    }

    [Fact]
    public void AStartOutsideAnInstallationNeverCreatesVelopack()
    {
        List<string[]> runs = [];

        int? exit = VelopackStartup.Run([], @"D:\dev\LayerPeek.exe", InstalledFiles.Contains, runs.Add, out _);

        Assert.Null(exit);
        Assert.Empty(runs);
    }

    /// <summary>A failing Velopack never stops a normal start: the app goes on without it and logs the failure.</summary>
    [Fact]
    public void ANormalStartGoesOnWhenVelopackFails()
    {
        InvalidOperationException broken = new("locator failed");

        int? exit = VelopackStartup.Run([], InstalledExe, InstalledFiles.Contains, _ => throw broken, out Exception? failure);

        Assert.Null(exit);
        Assert.Same(broken, failure);
    }

    [Fact]
    public void AHookStartReportsAFailingVelopackToTheInstaller()
    {
        InvalidOperationException broken = new("locator failed");

        int? exit = VelopackStartup.Run(["--veloapp-install", "0.1.0"], InstalledExe, InstalledFiles.Contains, _ => throw broken, out Exception? failure);

        Assert.Equal(VelopackStartup.HookFailedExitCode, exit);
        Assert.Same(broken, failure);
    }

    [Fact]
    public void TheHookLogPassesInformationAndAboveOn()
    {
        List<(Microsoft.Extensions.Logging.LogLevel, string)> written = [];
        using HookLog log = new((level, message) => written.Add((level, message)));
        Microsoft.Extensions.Logging.ILogger logger = log.CreateLogger("x");

        logger.Log(Microsoft.Extensions.Logging.LogLevel.Debug, default, "debug", null, static (state, _) => state);
        logger.Log(Microsoft.Extensions.Logging.LogLevel.Information, default, "info", null, static (state, _) => state);
        logger.Log(Microsoft.Extensions.Logging.LogLevel.Warning, default, "warn", new IOException("denied"), static (state, _) => state);

        Assert.Equal(
            [(Microsoft.Extensions.Logging.LogLevel.Information, "info"), (Microsoft.Extensions.Logging.LogLevel.Warning, "warn IOException: denied")],
            written);
    }

    /// <summary>
    /// A hook start of an exe that is not installed (a development build started by hand with the arguments) ends at once:
    /// it neither starts the app nor removes the registrations of the installed app.
    /// </summary>
    [Fact]
    public void AHookStartOutsideAnInstallationEndsWithoutDoingAnything()
    {
        List<string[]> runs = [];

        int? exit = VelopackStartup.Run(["--veloapp-uninstall", "0.1.0"], @"D:\dev\LayerPeek.exe", InstalledFiles.Contains, runs.Add, out _);

        Assert.Equal(0, exit);
        Assert.Empty(runs);
    }

    /// <summary>
    /// T-017 (docs/velopack.md, point 1): <c>vpk</c> cannot check this in a Native AOT exe, so this test does. Before
    /// Velopack only the DLL search restriction of T-052 (one kernel call, it loads nothing) and its Debug-only probe run;
    /// everything else, the single-instance guard (T-043) above all, comes after it.
    /// </summary>
    [Fact]
    public void VelopackIsTheFirstThingMainRunsAfterTheDllSearchRestriction()
    {
        string source = File.ReadAllText(Path.Combine(TestWorktree.Root, "src", "LayerPeek.App", "Program.cs"));
        int main = source.IndexOf("public static int Main(string[] args)", StringComparison.Ordinal);
        Assert.True(main > 0, "Main not found.");
        string body = source[main..];
        int velopack = body.IndexOf("VelopackStartup.Run(args, out", StringComparison.Ordinal);
        Assert.True(velopack > 0, "Main does not call VelopackStartup.Run(args, out …).");

        foreach (string later in new[]
        {
            "SingleInstanceGuard", "AppLogging.Create", "AppEnvironment.FromProcess", "ClearToastsArgument",
            "ToastActivatorCommandLine", "StartToastActivator", "WindowsAppIdentity", "BuildAvaloniaApp(",
        })
        {
            int index = body.IndexOf(later, StringComparison.Ordinal);
            Assert.True(index > velopack, $"'{later}' must come after VelopackStartup.Run in Main.");
        }

        // Only the DLL search restriction and its probe may come before it.
        string before = Regex.Replace(body[..velopack], @"//[^\n]*", string.Empty);
        string[] allowed =
        [
            "Main", "OperatingSystem.IsWindows", "Environment.GetEnvironmentVariable", "NativeLibrarySearch.RunProbe",
            "NativeLibrarySearch.Restrict",
        ];
        foreach (Match call in Regex.Matches(before, @"[A-Za-z_][A-Za-z0-9_.]*(?=\()"))
        {
            Assert.True(allowed.Contains(call.Value, StringComparer.Ordinal), $"Main calls '{call.Value}' before Velopack.");
        }

        // Exactly this order (confirmed by the user 2026-10-24): the restriction of T-052, then Velopack right after it.
        const string restrict = "dllSearchError = NativeLibrarySearch.Restrict();";
        int restriction = before.IndexOf(restrict, StringComparison.Ordinal);
        Assert.True(restriction > 0, "Main does not restrict the DLL search before Velopack.");
        Assert.Equal(1, Regex.Count(before, Regex.Escape(restrict)));
        string between = before[(restriction + restrict.Length)..];
        Assert.Matches(@"^\s*\}\s*if\s*\($", between);
    }

    /// <summary>
    /// The settings of the Velopack start (T-017): the hooks of install and uninstall, and no update applied at startup,
    /// because applying ends every process of the installation, also a running primary instance; updates are applied only
    /// by the primary instance after it saved its state (<see cref="AppUpdates"/>).
    /// </summary>
    [Fact]
    public void VelopackNeverAppliesUpdatesAtStartup()
    {
        string source = File.ReadAllText(Path.Combine(TestWorktree.Root, "src", "LayerPeek.App", "Installer", "VelopackStartup.cs"));

        Assert.Contains(".SetAutoApplyOnStartup(false)", source, StringComparison.Ordinal);
        Assert.Contains(".OnAfterInstallFastCallback(", source, StringComparison.Ordinal);
        Assert.Contains(".OnBeforeUninstallFastCallback(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OnFirstRun", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HookFailuresAreReportedToVelopack()
    {
        // Velopack logs a throwing hook and goes on (the install or uninstall is never stopped by it).
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => VelopackStartup.ThrowOnFailures("uninstall", ["a failed", "b failed"]));

        Assert.Contains("uninstall", error.Message, StringComparison.Ordinal);
        Assert.Contains("a failed", error.Message, StringComparison.Ordinal);
        Assert.Contains("b failed", error.Message, StringComparison.Ordinal);
        VelopackStartup.ThrowOnFailures("install", []);
    }
}
