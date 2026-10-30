#if DEBUG
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Composition;
using LayerPeek.App.Notifications;
using LayerPeek.App.SingleInstance;
using LayerPeek.Tests.Shared;

namespace LayerPeek.App.Tests.Platform.MacOS;

/// <summary>
/// End-to-end test of the update of the macOS app through Velopack (T-058, decision Q10 a), on demand only (<c>LAYERPEEK_E2E=1</c>
/// on macOS) and entirely in a temporary folder (user decision 2026-10-30): the real Debug app in the test bundle
/// <c>LayerPeek E2E.app</c> (test identifier, never in /Applications), packed by <c>vpk</c> twice from one build as 0.3.1 and
/// 0.3.2 (ad hoc, hardened runtime; the JIT of the Debug app needs <c>allow-jit</c>, so the test signs with entitlements of its
/// own, the release check of T-108 is the Pester test of the release script), version 0.3.1 "installed" from its Portable.zip,
/// 0.3.2 served as a local folder feed through <see cref="AppEnvironment.TestUpdateSourceVariable"/>; no network at all. The app
/// runs in the isolated test mode as a child of the test host (no notification center, no Login Item), with <c>HOME</c> in the
/// temporary folder, so that Velopack (its log and package cache) and its updater <c>UpdateMac</c> write only there. The click
/// on the update notification is the instance command a click sends; then the app downloads, quits and <c>UpdateMac</c>
/// replaces the bundle in place. The test checks the version, the signature of every Mach-O file (<c>codesign --verify --strict</c>), the
/// hardened runtime, that no quarantine attribute appeared, and starts the updated app (the test mode never lets the updater
/// start it: <c>open -n</c> would start it outside the test mode). Everything is deleted afterwards, the LaunchServices
/// registrations of the temporary bundles included.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacUpdateE2ETests
{
    private const string Running = "0.3.1";
    private const string Next = "0.3.2";
    private const string BundleName = "LayerPeek E2E.app";
    private const string LsRegister = "/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    [Fact]
    public async Task TheInstalledAppUpdatesItselfFromALocalFeedAndStaysSignedWithoutQuarantine()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS() && Environment.GetEnvironmentVariable(MacNotificationsE2ETests.RunVariable) == "1", $"On demand only: {MacNotificationsE2ETests.RunVariable}=1 on macOS (packs the Debug app with vpk and updates it in a temporary folder).");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string run = Path.Combine(TestDirectories.ShortRoot, "u" + Guid.NewGuid().ToString("N")[..8]);
        string install = Path.Combine(run, "install");
        string data = Path.Combine(run, "data");
        string home = Path.Combine(run, "home");
        string feed = Path.Combine(run, "feed");
        string suffix = "e2e-" + Guid.NewGuid().ToString("N")[..8];
        string realLogs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs");
        string[] velopackLogsBefore = VelopackLogs(realLogs);
        // Velopack logs into ~/Library/Logs when it exists (otherwise into the system temp folder): here the temporary HOME.
        Directory.CreateDirectory(Path.Combine(home, "Library", "Logs"));
        Directory.CreateDirectory(Path.Combine(home, "Library", "Caches"));
        Directory.CreateDirectory(feed);
        try
        {
            // 0.3.1 and 0.3.2 of the same build; only the Info.plist version and the package version differ.
            string entitlements = Path.Combine(run, "e2e.entitlements");
            await File.WriteAllTextAsync(entitlements, TestEntitlements, cancellationToken);
            string portable = await PackAsync(run, Running, entitlements, cancellationToken);
            string nextOut = Path.GetDirectoryName(await PackAsync(run, Next, entitlements, cancellationToken))!;
            foreach (string name in new[] { $"LayerPeek-{Next}-osx-arm64-full.nupkg", "releases.osx-arm64.json" })
            {
                File.Copy(Path.Combine(nextOut, name), Path.Combine(feed, name));
            }

            await RequireToolAsync("/usr/bin/ditto", ["-x", "-k", portable, install], cancellationToken);
            string app = Assert.Single(Directory.EnumerateDirectories(install, "*.app"));
            Assert.Equal(Running, await PlistVersionAsync(app, cancellationToken));
            await AssertSignedAsync(app, cancellationToken);
            await WriteSettingsAsync(data, cancellationToken);
            Dictionary<string, string> variables = new()
            {
                [AppEnvironment.DataDirectoryVariable] = data,
                [InstanceTransports.SuffixVariable] = suffix,
                [AppEnvironment.TestUpdateSourceVariable] = feed,
                ["HOME"] = home,
            };

            // 1. 0.3.1 finds 0.3.2 in the feed (2 s after the start in the test mode).
            using (Process first = StartApp(app, variables))
            {
                try
                {
                    await UntilAsync(() => Log(data).Contains($"version {Next} is available", StringComparison.Ordinal), first, data, "the update check to find the new version", cancellationToken);

                    // 2. The click on the update notification: download, quit, UpdateMac applies.
                    Send(data, suffix, new InstanceCommand(InstanceCommandKind.Toast, ToastLaunchArgument.Update));
                    await WaitForExitAsync(first, data, "the app to quit for the update", cancellationToken);
                    Assert.Equal(0, first.ExitCode);
                    Assert.Contains($"Update {Next} downloaded; the app quits and the updater applies it.", Log(data), StringComparison.Ordinal);
                }
                finally
                {
                    Stop(first);
                }
            }

            await WaitUntilAsync(async () => await PlistVersionAsync(app, cancellationToken) == Next && !UpdaterRuns(), $"UpdateMac to replace the bundle; Velopack logs: {VelopackLogText(home)}", cancellationToken);

            // 3. The same path, the new version, still signed with the hardened runtime, no quarantine.
            Assert.Equal(app, Assert.Single(Directory.EnumerateDirectories(install, "*.app")));
            Assert.Contains($"<version>{Next}</version>", await File.ReadAllTextAsync(Path.Combine(app, "Contents", "MacOS", "sq.version"), cancellationToken), StringComparison.Ordinal);
            await AssertSignedAsync(app, cancellationToken);
            (int xattrCode, string attributes, _) = await RunToolAsync("/usr/bin/xattr", ["-lr", app], cancellationToken);
            Assert.Equal(0, xattrCode);
            Assert.DoesNotContain("com.apple.quarantine", attributes, StringComparison.Ordinal);
            // Velopack and UpdateMac wrote into the temporary HOME only.
            Assert.Equal(velopackLogsBefore, VelopackLogs(realLogs));
            TestContext.Current.SendDiagnosticMessage($"Velopack files in the temporary HOME: {string.Join(", ", Directory.EnumerateFiles(home, "*", SearchOption.AllDirectories).Select(file => Path.GetRelativePath(home, file)))}");

            // 4. The updated app starts again and quits normally. (Within a day it does not ask the feed again: it offers what the
            // last check found; its assemblies are the same build, so it still calls itself 0.3.1.)
            using Process second = StartApp(app, variables);
            try
            {
                const string Monitoring = "Starting printer monitoring";
                await UntilAsync(() => Occurrences(Log(data), Monitoring) == 2, second, data, "the updated app to start its services", cancellationToken);
                Send(data, suffix, new InstanceCommand(InstanceCommandKind.Quit));
                await WaitForExitAsync(second, data, "the updated app to quit", cancellationToken);
                Assert.Equal(0, second.ExitCode);
            }
            finally
            {
                Stop(second);
            }
        }
        finally
        {
            foreach (string path in await RegisteredPathsUnderAsync(run, cancellationToken))
            {
                _ = await RunToolAsync(LsRegister, ["-u", path], cancellationToken);
            }

            Assert.Empty(await RegisteredPathsUnderAsync(run, CancellationToken.None));
            TestDirectories.Delete(run);
        }
    }

    /// <summary>The entitlements of the test bundle: the JIT of the Debug app under the hardened runtime, and the runtime of DOTNET_ROOT.</summary>
    private const string TestEntitlements = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
            <key>com.apple.security.cs.allow-jit</key>
            <true/>
            <key>com.apple.security.cs.allow-unsigned-executable-memory</key>
            <true/>
            <key>com.apple.security.cs.disable-library-validation</key>
            <true/>
        </dict>
        </plist>
        """;

    /// <summary>The test bundle of the Debug app as <paramref name="version"/>, packed and signed by vpk; returns its Portable.zip.</summary>
    private static async Task<string> PackAsync(string run, string version, string entitlements, CancellationToken cancellationToken)
    {
        string source = Path.Combine(TestWorktree.Root, "src", "LayerPeek.App", "bin", "Debug", "net11.0");
        Assert.True(File.Exists(Path.Combine(source, "LayerPeek.dll")) && File.Exists(Path.Combine(source, "LayerPeek")), $"The Debug app is not built in {source}.");
        string bundle = Path.Combine(run, "bundle-" + version, BundleName);
        string macOS = Path.Combine(bundle, "Contents", "MacOS");
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file);
            if (relative.StartsWith("runtimes" + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !relative.StartsWith(Path.Combine("runtimes", "osx"), StringComparison.Ordinal))
            {
                continue;
            }

            string destination = Path.Combine(macOS, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }

        string plist = MacNotificationsE2ETests.TestBundle.TestInfoPlist(await File.ReadAllTextAsync(Path.Combine(TestWorktree.Root, "build", "macos", "Info.plist"), cancellationToken))
            .Replace("<string>0.0.0</string>", $"<string>{version}</string>", StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(bundle, "Contents", "Info.plist"), plist, cancellationToken);
        // vpk puts its manifest into Contents/Resources (linked from Contents/MacOS); the release bundle has the folder anyway.
        Directory.CreateDirectory(Path.Combine(bundle, "Contents", "Resources"));
        string output = Path.Combine(run, "out-" + version);
        string notes = Path.Combine(run, "notes-" + version + ".md");
        await File.WriteAllTextAsync(notes, $"- E2E {version}\n", cancellationToken);
        (int code, string stdout, string stderr) = await RunToolAsync(
            "dotnet",
            ["vpk", "--skip-updates", "-x", "pack", "--packId", "LayerPeek", "--packVersion", version, "--packDir", bundle, "--mainExe", "LayerPeek",
             "--packTitle", "LayerPeek E2E", "--channel", "osx-arm64", "-r", "osx-arm64", "--releaseNotes", notes, "--delta", "None", "--noInst",
             "--signAppIdentity", "-", "--signEntitlements", entitlements, "--outputDir", output],
            cancellationToken,
            start =>
            {
                start.WorkingDirectory = TestWorktree.Root;
                // vpk works in $TMPDIR/velopack: inside the temporary folder of the test, deleted with it.
                string temp = Path.Combine(run, "tmp");
                Directory.CreateDirectory(temp);
                start.Environment["TMPDIR"] = temp;
            });
        Assert.True(code == 0, $"vpk pack {version} ended with {code}: {stdout}{stderr}");
        return Path.Combine(output, "LayerPeek-osx-arm64-Portable.zip");
    }

    private static async Task AssertSignedAsync(string app, CancellationToken cancellationToken)
    {
        List<string> problems = await ProblemsAsync(app, cancellationToken);
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>Problems of the signature and the hardened runtime of the main executable and the updater.</summary>
    private static async Task<List<string>> ProblemsAsync(string app, CancellationToken cancellationToken)
    {
        List<string> problems = [];
        // Every Mach-O file of the bundle on its own: the Debug app keeps its managed .dll files in Contents/MacOS, which no
        // bundle signature seals (the Native AOT bundle of a release has only Mach-O files there; its deep check is the Pester
        // test of the release script).
        // The main executable stands for the bundle (its check would include the .dll files): it gets the flags check below, and
        // the kernel checks its pages when it starts.
        string main = Path.Combine(app, "Contents", "MacOS", "LayerPeek");
        foreach (string file in Directory.EnumerateFiles(Path.Combine(app, "Contents", "MacOS")).Where(file => file != main && IsMachO(file)))
        {
            (int verify, _, string verifyError) = await RunToolAsync("/usr/bin/codesign", ["--verify", "--strict", file], cancellationToken);
            if (verify != 0)
            {
                problems.Add($"codesign --verify {Path.GetFileName(file)}: {verifyError}");
            }
        }

        foreach (string file in new[] { "LayerPeek", "UpdateMac" })
        {
            (_, _, string details) = await RunToolAsync("/usr/bin/codesign", ["-d", "--verbose=4", Path.Combine(app, "Contents", "MacOS", file)], cancellationToken);
            if (!Regex.IsMatch(details, @"flags=0x1[0-9a-f]{4}\(.*runtime"))
            {
                problems.Add($"{file} has no hardened runtime: {details}");
            }
        }

        return problems;
    }

    private static bool IsMachO(string path)
    {
        Span<byte> magic = stackalloc byte[4];
        using FileStream stream = File.OpenRead(path);
        return stream.Read(magic) == 4 && (magic.SequenceEqual((ReadOnlySpan<byte>)[0xCF, 0xFA, 0xED, 0xFE]) || magic.SequenceEqual((ReadOnlySpan<byte>)[0xCA, 0xFE, 0xBA, 0xBE]));
    }

    private static async Task<string> PlistVersionAsync(string app, CancellationToken cancellationToken)
    {
        (_, string version, _) = await RunToolAsync("/usr/bin/plutil", ["-extract", "CFBundleShortVersionString", "raw", Path.Combine(app, "Contents", "Info.plist")], cancellationToken);
        return version.Trim();
    }

    private static Process StartApp(string app, IReadOnlyDictionary<string, string> variables)
    {
        ProcessStartInfo start = new(Path.Combine(app, "Contents", "MacOS", "LayerPeek")) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string name in start.Environment.Keys.Where(static name => name.StartsWith("LAYERPEEK_", StringComparison.Ordinal)).ToList())
        {
            start.Environment.Remove(name);
        }

        start.Environment["DOTNET_ROOT"] = DotnetRoot();
        foreach ((string name, string value) in variables)
        {
            start.Environment[name] = value;
        }

        Process process = Process.Start(start) ?? throw new InvalidOperationException("The test app did not start.");
        process.OutputDataReceived += static (_, _) => { };
        process.ErrorDataReceived += static (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static string DotnetRoot()
    {
        string? root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        return string.IsNullOrEmpty(root)
            ? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "..", "..", ".."))
            : root;
    }

    private static bool UpdaterRuns()
    {
        Process[] updaters = Process.GetProcessesByName("UpdateMac");
        foreach (Process updater in updaters)
        {
            updater.Dispose();
        }

        return updaters.Length > 0;
    }

    private static string[] VelopackLogs(string folder) =>
        Directory.Exists(folder) ? [.. Directory.EnumerateFiles(folder, "velopack*").Order(StringComparer.Ordinal)] : [];

    private static async Task WriteSettingsAsync(string data, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(data);
        JsonObject settings = new()
        {
            ["version"] = 2,
            ["language"] = "en",
            ["checkForUpdates"] = true,
            ["offerPrereleaseUpdates"] = true,
            // No notification: the app is no responsible process of its own here, and the test never asks macOS for a permission.
            ["notifications"] = new JsonObject { ["enabled"] = false },
            ["printers"] = new JsonArray(),
        };
        await File.WriteAllTextAsync(Path.Combine(data, "settings.json"), settings.ToJsonString(), cancellationToken);
    }

    private static void Send(string data, string suffix, InstanceCommand command)
    {
        using SingleInstanceGuard guard = new(
            InstanceTransports.ForCurrentPlatform("-" + suffix, AppEnvironment.ForTests(data)),
            NullLogger<SingleInstanceGuard>.Instance);
        InstanceRole role = guard.Start(command, static _ => { });
        Assert.True(role == InstanceRole.Forwarded, $"{role}. The log:{Environment.NewLine}{Log(data)}");
    }

    private static void Stop(Process app)
    {
        if (!app.HasExited)
        {
            app.Kill();
            app.WaitForExit(10_000);
        }
    }

    private static string Log(string data)
    {
        string logs = Path.Combine(data, AppEnvironment.TestLogFolderName);
        return Directory.Exists(logs)
            ? string.Join('\n', Directory.EnumerateFiles(logs).Order(StringComparer.Ordinal).Select(static file =>
            {
                using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using StreamReader reader = new(stream);
                return reader.ReadToEnd();
            }))
            : string.Empty;
    }

    private static int Occurrences(string text, string value)
    {
        int count = 0;
        for (int index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static async Task UntilAsync(Func<bool> condition, Process app, string data, string what, CancellationToken cancellationToken)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.False(app.HasExited, $"The test app ended while waiting for {what}. Its log:{Environment.NewLine}{Log(data)}");
            Assert.True(elapsed.Elapsed < Timeout, $"Waited {Timeout.TotalSeconds:0} s for {what}. The log:{Environment.NewLine}{Log(data)}");
            await Task.Delay(200, cancellationToken);
        }
    }

    private static async Task WaitForExitAsync(Process app, string data, string what, CancellationToken cancellationToken)
    {
        try
        {
            await app.WaitForExitAsync(cancellationToken).WaitAsync(Timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            Assert.Fail($"Waited {Timeout.TotalSeconds:0} s for {what}. The log:{Environment.NewLine}{Log(data)}");
        }
    }

    private static string VelopackLogText(string home) =>
        string.Join(Environment.NewLine, Directory.EnumerateFiles(home, "*.log", SearchOption.AllDirectories).Select(static file => file + ":" + Environment.NewLine + File.ReadAllText(file)));

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string what, CancellationToken cancellationToken)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        while (!await condition())
        {
            Assert.True(elapsed.Elapsed < Timeout, $"Waited {Timeout.TotalSeconds:0} s for {what}.");
            await Task.Delay(500, cancellationToken);
        }
    }

    /// <summary>The bundles LaunchServices knows below <paramref name="folder"/> (both spellings of the temporary folder).</summary>
    private static async Task<List<string>> RegisteredPathsUnderAsync(string folder, CancellationToken cancellationToken)
    {
        (_, string dump, _) = await RunToolAsync(LsRegister, ["-dump"], cancellationToken);
        string leaf = "/" + Path.GetFileName(folder) + "/";
        List<string> paths = [];
        foreach (string raw in dump.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("path:", StringComparison.Ordinal))
            {
                string path = Regex.Replace(line["path:".Length..].Trim(), @"\s+\(0x[0-9a-fA-F]+\)$", string.Empty);
                if (path.Contains(leaf, StringComparison.Ordinal) && !paths.Contains(path))
                {
                    paths.Add(path);
                }
            }
        }

        return paths;
    }

    private static async Task RequireToolAsync(string tool, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        (int code, string output, string error) = await RunToolAsync(tool, arguments, cancellationToken);
        Assert.True(code == 0, $"{Path.GetFileName(tool)} ended with {code}: {output}{error}");
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunToolAsync(string tool, IReadOnlyList<string> arguments, CancellationToken cancellationToken, Action<ProcessStartInfo>? configure = null)
    {
        ProcessStartInfo start = new(tool) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        configure?.Invoke(start);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"{tool} did not start.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromMinutes(3), cancellationToken);
        return (process.ExitCode, await output, await error);
    }
}
#endif
