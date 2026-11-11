using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;

using FlaUI.Core.AutomationElements;

using Microsoft.Win32;

using LayerPeek.App.E2E.Tests.Infrastructure;
using LayerPeek.Tests.Shared;

using Wait = LayerPeek.App.E2E.Tests.Infrastructure.Wait;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// The offered update (T-058) on the real exe, replacing the manual checks of T-125 (T-128), with no network at all: the
/// update feed of the test mode (Q10 a) is a local folder or a server on the loopback, never GitHub.
/// <list type="bullet">
/// <item><b>Velopack installation:</b> the Debug build of this worktree packed by <c>vpk</c> (the pinned local tool) twice as
/// the portable package of a test package id (<see cref="PackId"/>, never <c>LayerPeek</c>), as the version of this worktree
/// and the next patch version; the older one extracted into the test's temporary folder (a portable Velopack installation:
/// <c>Update.exe</c>, <c>current\LayerPeek.exe</c>, <c>sq.version</c>; no installer, no Start menu or autostart shortcut, no
/// Uninstall entry, no hook of the app runs), the newer one in a local folder feed. The app finds the new version, shows the
/// menu item "New version X.Y.Z available" and the toast banner; a click on either downloads it, quits the app and
/// Velopack's <c>Update.exe</c> replaces <c>current</c>. The test mode never lets the updater start the app again (it would
/// start without the test mode, <c>VelopackUpdateEngine.RestartsAfterUpdate</c>); the test starts the updated copy itself
/// and checks that it runs and finds nothing newer.</item>
/// <item><b><c>.zip</c> / development build:</b> the Debug exe asks the GitHub REST answer of <see cref="FakeGitHubReleases"/>;
/// the menu item and the banner open the release page, which the test mode records instead of starting a browser.</item>
/// </list>
/// The installation test runs only while LayerPeek is not installed on this machine (like <see cref="VelopackInstallerTests"/>),
/// so that nothing can ever be mistaken for the user's installation; it first runs together with T-124. System changes
/// (trait <see cref="SystemChanges.Trait"/>): those of every toast test (<see cref="ToastNotificationTests"/>), journaled and
/// restored also after a failure or a killed run. Velopack may log into <c>%LOCALAPPDATA%\velopack</c>: a log file the test
/// created there is deleted again, lines added to an existing one stay (like T-017, accepted 2026-10-24).
/// </summary>
public sealed class UpdateTests(E2EFixture fixture)
{
    /// <summary>The package id of the test packages: its own temporary folders, logs and feed files, never the app's.</summary>
    private const string PackId = "LayerPeekE2E";

    /// <summary>The title of the test packages (the name of any shortcut Velopack would create for them).</summary>
    private const string PackTitle = "LayerPeek Update E2E";

    private const string Runtime = "win-x64";

    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Download (a local copy of about 170 MB), quit and Velopack's replacement of <c>current</c>.</summary>
    private static readonly TimeSpan UpdateTimeout = TimeSpan.FromMinutes(3);

    private static readonly SemaphoreSlim PackGate = new(1, 1);

    private static Packages? s_packages;

    /// <summary>The version of this worktree (<c>Directory.Build.props</c>): the installed older version.</summary>
    private static string Running { get; } = XDocument.Load(Path.Combine(TestWorktree.Root, "Directory.Build.props")).Descendants("Version").Single().Value.Trim();

    /// <summary>The next patch version: the update in the feed (above the version the assemblies of both packages report).</summary>
    private static string Next { get; } = NextPatch(Running);

    [Theory]
    [InlineData("MenuItem")]
    [InlineData("Banner")]
    [Trait(SystemChanges.Trait, SystemChangeTraits.Toasts)]
    [Trait(SystemChanges.Trait, SystemChangeTraits.DoNotDisturb)]
    public async Task InstalledOlderVersionUpdatesFromALocalFeedAfterAClick(string click)
    {
        SystemChanges.Require();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Assert.SkipWhen(E2EFixture.IsNativeAot, "The update of a Velopack installation is tested once, in the pass against the Debug exe (it packs the Debug build).");
        Assert.SkipUnless(RuntimeInformation.OSArchitecture == Architecture.X64, $"The {Runtime} packages run on x64 Windows only.");
        Assert.SkipWhen(InstallerKind.IsInstalled(), $"LayerPeek is installed on this machine ({InstallerKind.InstallFolder}); the update test runs only without an installation (T-124 postpones it).");
        Assert.SkipWhen(KeyExists(InstallerKind.UninstallSubKey), $@"HKCU\{InstallerKind.UninstallSubKey} exists (a real installation); the update test runs only without one.");
        StringBuilder report = new();
        Packages packages = await PackOnceAsync(report, cancellationToken);
        string run = Path.Combine(E2EFixture.Root, UpdateCopyKind.FolderPrefix + Guid.NewGuid().ToString("N")[..8]);
        string install = Path.Combine(run, "install");
        string feed = Path.Combine(run, "feed");
        string temp = Path.Combine(run, "tmp");
        string exe = Path.Combine(install, "current", "LayerPeek.exe");
        // Journaled before the copy exists: its processes, folder, tray entries and Velopack's new logs go also after a crash.
        SystemChange<UpdateCopyState> copy = await SystemChanges.UpdateCopyAsync(run, cancellationToken);
        try
        {
            Directory.CreateDirectory(feed);
            Directory.CreateDirectory(temp);
            ZipFile.ExtractToDirectory(packages.RunningPortable, install);
            Assert.True(File.Exists(Path.Combine(install, "Update.exe")) && File.Exists(Path.Combine(install, ".portable")), "The portable package has no Update.exe or .portable.");
            Assert.Equal(Running, InstalledVersion(install));
            // Up to date at first: the feed lists only the installed version.
            File.Copy(packages.RunningFeed, Path.Combine(feed, FeedFileName));
            // Signed with the test key (T-181), which only the test source of a Debug build trusts.
            TestUpdateFeed.SignFile(Path.Combine(feed, FeedFileName), Runtime);
            report.AppendLine(CultureInfo.InvariantCulture, $"Installed {PackId} {Running} (portable) in {install}; feed {feed}");

            Dictionary<string, string> switches = new()
            {
                [AppSwitches.TestUpdateSourceVariable] = feed,
                // Velopack and its Update.exe work in the temporary folder of the process: inside the test's folder.
                ["TEMP"] = temp,
                ["TMP"] = temp,
            };
            ToastRunOptions options = new() { WithoutPrinter = true, Exe = exe, Switches = switches, EditSettings = WithUpdateChecks };
            await using (ToastNotificationTests.ToastRun toastRun = await ToastNotificationTests.ToastRun.StartAsync(fixture, "update-" + click.ToLowerInvariant(), "en", report, options: options))
            {
                AppInstance app = toastRun.App;
                await WaitForLogAsync(app, "Update check: no newer version than", "the first check (feed with the installed version only)");

                // The new version appears in the feed; the manual check of the settings window finds it.
                File.Copy(packages.NextPackage, Path.Combine(feed, Path.GetFileName(packages.NextPackage)));
                // The signature before the feed (as release-upload.ps1 uploads them): no check sees the new feed with the old one.
                TestUpdateFeed.SignFile(packages.NextFeed, Runtime, Path.Combine(feed, FeedFileName + ".sig"));
                File.Copy(packages.NextFeed, Path.Combine(feed, FeedFileName), overwrite: true);
                ToastMatch banner = await CheckNowAndWaitForBannerAsync(toastRun, report);

                if (click == "Banner")
                {
                    string activation = await Toasts.ActivateAsync(banner, () => app.Process.HasExited || Downloaded(app), TimeSpan.FromSeconds(60));
                    report.AppendLine("Banner activated by " + activation);
                }
                else
                {
                    ToastNotificationTests.MovePointerAway();
                    await ClickMenuItemAsync(app, report, AppStrings.Get("en", "MenuUpdateInstallHint"));
                }

                await WaitForExitAsync(app, "the app to download the update and quit");
                Assert.Equal(0, app.Process.ExitCode);
                Assert.Contains($"Update {Next} downloaded; the app quits and the updater applies it.", app.ReadLog(), StringComparison.Ordinal);
                // Neither the copy nor Velopack touched the production Start menu shortcut.
                toastRun.AssertProductionUnchanged();
            }

            await Wait.UntilAsync(
                () => InstalledVersion(install) == Next && UpdateCopyKind.ProcessesUnder(install).Count == 0,
                UpdateTimeout,
                () => $"Velopack's Update.exe to replace current (installed now: {InstalledVersion(install)}; processes: {string.Join(", ", UpdateCopyKind.ProcessesUnder(install))})");
            report.AppendLine(CultureInfo.InvariantCulture, $"current is {Next} now; Update.exe ended; no process of the installation runs (the test mode does not restart the app).");
            Assert.Empty(AppProcesses.FindMarked(E2EFixture.Root));
            // A portable installation gets no shortcut, also not when Velopack applies an update (vpk's default locations).
            Assert.Empty(VelopackShortcuts());

            // The updated copy starts (the test starts it: the test mode never lets the updater start it) and finds nothing newer.
            await using (AppInstance updated = await AppInstance.StartAsync(fixture, "update-" + click.ToLowerInvariant() + "-updated", Settings(), switches: switches, exe: exe))
            {
                await WaitForLogAsync(updated, "Update check: no newer version than", "the updated copy's check");
                Assert.DoesNotContain($"version {Next} is available", updated.ReadLog(), StringComparison.Ordinal);
                await updated.QuitAsync();
                Assert.Equal(0, updated.Process.ExitCode);
            }

            Assert.Equal(Next, InstalledVersion(install));
        }
        finally
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"Velopack logs in {UpdateCopyKind.VelopackLogFolder}: before {Describe(copy.Original.VelopackLogs)}, now {Describe(UpdateCopyKind.VelopackLogs())}; "
                + $"tray entries of the copy: {Describe(UpdateCopyKind.TrayEntriesUnder(run))} (removed with the copy)");
            Screenshots.SaveText(report.ToString(), "update-" + click.ToLowerInvariant() + "-report");
            await copy.DisposeAsync();
        }
    }

    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.Toasts)]
    [Trait(SystemChanges.Trait, SystemChangeTraits.DoNotDisturb)]
    public async Task ZipVersionOpensTheReleasePageFromTheBannerAndTheMenuItem()
    {
        SystemChanges.Require();
        StringBuilder report = new();
        string page = $"https://github.com/pfoltynek/LayerPeek/releases/tag/v{Next}";
        try
        {
            await using FakeGitHubReleases github = FakeGitHubReleases.Start();
            ToastRunOptions options = new()
            {
                WithoutPrinter = true,
                Switches = new Dictionary<string, string> { [AppSwitches.TestUpdateSourceVariable] = github.BaseUri.ToString() },
                EditSettings = WithUpdateChecks,
            };
            await using ToastNotificationTests.ToastRun run = await ToastNotificationTests.ToastRun.StartAsync(fixture, "update-zip", "en", report, options: options);
            await WaitForLogAsync(run.App, "Update check: no newer version than", "the first check (no release yet)");

            github.Publish(Next);
            ToastMatch banner = await CheckNowAndWaitForBannerAsync(run, report);
            string activation = await Toasts.ActivateAsync(banner, () => run.OpenedUrls().Count > 0, TimeSpan.FromSeconds(15));
            report.AppendLine("Banner activated by " + activation);
            Assert.Equal([page], await OpenedAsync(run, 1));

            ToastNotificationTests.MovePointerAway();
            await ClickMenuItemAsync(run.App, report, AppStrings.Get("en", "MenuUpdateOpenPageHint"));
            Assert.Equal([page, page], await OpenedAsync(run, 2));
            Assert.False(run.App.Process.HasExited, "The .zip version quit after a click on the update.");

            report.AppendLine(CultureInfo.InvariantCulture, $"Requests of the fake GitHub: {string.Join(" | ", github.Requests)}");
            Assert.All(github.Requests, static request => Assert.StartsWith($"GET {FakeGitHubReleases.ReleasesPath}?", request, StringComparison.Ordinal));
            await run.AssertDoNotDisturbStayedOffAsync(report);
            run.AssertProductionUnchanged();
        }
        finally
        {
            Screenshots.SaveText(report.ToString(), "update-zip-report");
        }
    }

    /// <summary>The settings of the update tests: automatic checks on, pre-releases offered (no printer).</summary>
    private static void WithUpdateChecks(JsonObject settings)
    {
        settings["checkForUpdates"] = true;
        settings["offerPrereleaseUpdates"] = true;
    }

    private static string Settings()
    {
        JsonObject settings = JsonNode.Parse(TestSettings.Json("en", []))!.AsObject();
        WithUpdateChecks(settings);
        return settings.ToJsonString();
    }

    /// <summary>
    /// "Check now" on the Startup and updates tab, the check finds <see cref="Next"/>, and the toast banner about it; the
    /// settings window stays open (closing it first could let the banner expire). The pointer rests on the banner.
    /// </summary>
    private static async Task<ToastMatch> CheckNowAndWaitForBannerAsync(ToastNotificationTests.ToastRun run, StringBuilder report)
    {
        nint settings = await run.App.OpenAsync("settings", AppWindowKind.Settings);
        AutomationElement window = run.App.Element(settings);
        await Ui.SelectAsync(window, "SettingsTabStartupUpdates");
        ToastNotificationTests.MovePointerAway();
        await Ui.InvokeAsync(window, "SettingsCheckForUpdatesNow");
        await WaitForLogAsync(run.App, $"Update check: version {Next} is available", "the manual check to find the new version");
        ToastTexts texts = new(AppStrings.Get("en", "NotificationUpdateTitle"), AppStrings.Format("en", "NotificationUpdateBody", Next));
        ToastMatch banner = await run.WaitForBannerAsync(texts);
        Rectangle bounds = banner.Anchor.BoundingRectangle;
        FlaUI.Core.Input.Mouse.Position = new Point(bounds.Left + (bounds.Width / 2), bounds.Top + (bounds.Height / 2));
        report.AppendLine("Update banner:").AppendLine(banner.Describe());
        Assert.True(banner.HasExactly(texts.PrinterName), $"The update toast's title is not '{texts.PrinterName}':{Environment.NewLine}{banner.Describe()}");
        Assert.True(banner.HasLine(texts.Body), $"The update toast's text is not '{texts.Body}':{Environment.NewLine}{banner.Describe()}");
        using (Bitmap capture = Toasts.Capture(await Toasts.SettledBoundsAsync(banner)))
        {
            Screenshots.Save(capture, "update-banner-" + run.App.Name);
        }

        Assert.Contains($"Notification shown for the new version {Next}.", run.App.ReadLog(), StringComparison.Ordinal);
        return banner;
    }

    /// <summary>Opens the tray menu, checks the update item (text, tooltip of the action) and clicks it.</summary>
    private static async Task ClickMenuItemAsync(AppInstance app, StringBuilder report, string hint)
    {
        string text = AppStrings.Format("en", "MenuUpdateAvailable", Next);
        TrayMenuWindow menu = await TrayMenu.OpenWithMouseAsync(app);
        try
        {
            AutomationElement item = menu.Item(text);
            string help = item.Properties.HelpText.ValueOrDefault ?? string.Empty;
            report.AppendLine(CultureInfo.InvariantCulture, $"Menu item '{text}', enabled={item.Properties.IsEnabled.ValueOrDefault}, help '{help}'");
            Screenshots.Save(menu.Handle, "update-menu-" + app.Name);
            Assert.True(item.Properties.IsEnabled.ValueOrDefault, $"The menu item '{text}' is disabled.");
            if (help.Length > 0)
            {
                Assert.Equal(hint, help);
            }

            item.Patterns.Invoke.Pattern.Invoke();
        }
        finally
        {
            if (menu.IsOpen && !app.Process.HasExited)
            {
                await TrayMenu.CloseAsync(menu);
            }
        }
    }

    /// <summary>The addresses the app opened, once there are <paramref name="count"/> (no more come within a moment).</summary>
    private static async Task<List<string>> OpenedAsync(ToastNotificationTests.ToastRun run, int count)
    {
        await Wait.UntilAsync(() => run.OpenedUrls().Count >= count, TimeSpan.FromSeconds(15), $"{count} opened address(es)");
        await Task.Delay(TimeSpan.FromSeconds(2));
        return run.OpenedUrls();
    }

    private static bool Downloaded(AppInstance app) => app.ReadLog().Contains($"Update {Next} downloaded", StringComparison.Ordinal);

    private static async Task WaitForLogAsync(AppInstance app, string text, string what) =>
        await Wait.UntilAsync(
            () => app.Process.HasExited
                ? throw new InvalidOperationException($"The app ended (exit code {app.Process.ExitCode}) while waiting for {what}.{Environment.NewLine}{app.ReadLog()}")
                : app.ReadLog().Contains(text, StringComparison.Ordinal),
            CheckTimeout,
            () => $"{what} ('{text}' in the log). Log:{Environment.NewLine}{app.ReadLog()}");

    private static async Task WaitForExitAsync(AppInstance app, string what)
    {
        try
        {
            await app.Process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(UpdateTimeout, TestContext.Current.CancellationToken);
        }
        catch (TimeoutException)
        {
            Assert.Fail($"Waited {UpdateTimeout.TotalSeconds:0} s for {what}. Log:{Environment.NewLine}{app.ReadLog()}");
        }
    }

    /// <summary>The two packages of this run, packed once for every case (under <see cref="E2EFixture.Root"/>, deleted with it).</summary>
    private static async Task<Packages> PackOnceAsync(StringBuilder report, CancellationToken cancellationToken)
    {
        await PackGate.WaitAsync(cancellationToken);
        try
        {
            if (s_packages is { } packed && File.Exists(packed.RunningPortable) && File.Exists(packed.NextPackage))
            {
                return packed;
            }

            (int helpCode, string helpOutput) = await RunAsync("dotnet", ["vpk", "--help"], TestWorktree.Root, null, TimeSpan.FromMinutes(1), cancellationToken);
            Assert.SkipWhen(helpCode != 0, $"The Velopack CLI (local tool vpk, .config/dotnet-tools.json) is not restored: run 'dotnet tool restore' once (it needs NuGet). {helpOutput.Trim()}");

            string root = Path.Combine(E2EFixture.Root, "velopack-packages");
            string stage = Path.Combine(root, "stage");
            string temp = Path.Combine(root, "tmp");
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            Directory.CreateDirectory(temp);
            CopyDebugBuild(Path.GetDirectoryName(E2EFixture.AppPath)!, stage);
            Stopwatch elapsed = Stopwatch.StartNew();
            string runningOut = await PackAsync(root, stage, temp, Running, cancellationToken);
            string nextOut = await PackAsync(root, stage, temp, Next, cancellationToken);
            report.AppendLine(CultureInfo.InvariantCulture, $"vpk packed {Running} and {Next} of the Debug build in {elapsed.Elapsed.TotalSeconds:0} s.");
            s_packages = new Packages(
                Path.Combine(runningOut, $"{PackId}-{Runtime}-Portable.zip"),
                Path.Combine(runningOut, FeedFileName),
                Path.Combine(nextOut, $"{PackId}-{Next}-{Runtime}-full.nupkg"),
                Path.Combine(nextOut, FeedFileName));
            foreach (string file in new[] { s_packages.RunningPortable, s_packages.RunningFeed, s_packages.NextPackage, s_packages.NextFeed })
            {
                Assert.True(File.Exists(file), $"vpk did not create {file}.");
            }

            return s_packages;
        }
        finally
        {
            PackGate.Release();
        }
    }

    /// <summary>The Debug build without the native libraries of other OSes (vpk packs one runtime).</summary>
    private static void CopyDebugBuild(string source, string destination)
    {
        string runtimes = "runtimes" + Path.DirectorySeparatorChar;
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file);
            if (relative.StartsWith(runtimes, StringComparison.OrdinalIgnoreCase) && !relative.StartsWith(Path.Combine("runtimes", Runtime) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    /// <summary>vpk pack of the stage as <paramref name="version"/>: the portable package and the feed, no installer; returns the output folder.</summary>
    private static async Task<string> PackAsync(string root, string stage, string temp, string version, CancellationToken cancellationToken)
    {
        string output = Path.Combine(root, "out-" + version);
        string notes = Path.Combine(root, $"notes-{version}.md");
        await File.WriteAllTextAsync(notes, $"- E2E {version} (T-128)\n", cancellationToken);
        (int code, string text) = await RunAsync(
            "dotnet",
            ["vpk", "--skip-updates", "-x", "pack", "--packId", PackId, "--packVersion", version, "--packDir", stage, "--mainExe", "LayerPeek.exe",
             "--packTitle", PackTitle, "--channel", Runtime, "-r", Runtime, "--releaseNotes", notes, "--delta", "None", "--noInst",
             "--outputDir", output],
            TestWorktree.Root,
            new Dictionary<string, string> { ["TEMP"] = temp, ["TMP"] = temp },
            TimeSpan.FromMinutes(5),
            cancellationToken);
        Assert.True(code == 0, $"vpk pack {version} ended with {code}:{Environment.NewLine}{text}");
        return output;
    }

    private const string FeedFileName = $"releases.{Runtime}.json";

    /// <summary>The version in <c>current\sq.version</c> of the installation, or <see langword="null"/>.</summary>
    private static string? InstalledVersion(string install)
    {
        string manifest = Path.Combine(install, "current", "sq.version");
        try
        {
            return File.Exists(manifest)
                ? XDocument.Load(manifest).Descendants().FirstOrDefault(static element => element.Name.LocalName == "version")?.Value.Trim()
                : null;
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException or UnauthorizedAccessException)
        {
            // Being replaced by Update.exe.
            return null;
        }
    }

    /// <summary>Shortcuts of the test packages on the desktop or in the Start menu (there must be none).</summary>
    private static List<string> VelopackShortcuts() =>
        [.. new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.Programs, Environment.SpecialFolder.Startup }
            .Select(static folder => Path.Combine(Environment.GetFolderPath(folder), PackTitle + ".lnk"))
            .Where(File.Exists)];

    private static string Describe(IReadOnlyList<string> files) => files.Count == 0 ? "none" : string.Join(", ", files);

    private static bool KeyExists(string subKey)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(subKey);
        return key is not null;
    }

    /// <summary><paramref name="version"/> (<c>X.Y.Z</c>, a pre-release suffix dropped) with the patch number one higher.</summary>
    internal static string NextPatch(string version)
    {
        string core = version.Split('-', '+')[0];
        Version parsed = System.Version.Parse(core);
        return string.Create(CultureInfo.InvariantCulture, $"{parsed.Major}.{parsed.Minor}.{Math.Max(parsed.Build, 0) + 1}");
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string tool, IReadOnlyList<string> arguments, string workingDirectory, IReadOnlyDictionary<string, string>? environment, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ProcessStartInfo start = new(tool) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = workingDirectory };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach ((string name, string value) in environment ?? new Dictionary<string, string>())
        {
            start.Environment[name] = value;
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"{tool} did not start.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{tool} {string.Join(' ', arguments)} did not end within {timeout.TotalMinutes:0} min.");
        }

        return (process.ExitCode, await output + await error);
    }

    /// <summary>The packed files: the older version's portable package and feed, the newer version's package and feed.</summary>
    private sealed record Packages(string RunningPortable, string RunningFeed, string NextPackage, string NextFeed);
}
