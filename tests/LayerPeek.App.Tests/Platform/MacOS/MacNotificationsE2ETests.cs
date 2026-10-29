#if DEBUG
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Composition;
using LayerPeek.App.SingleInstance;
using LayerPeek.Simulator.Printers;
using LayerPeek.Tests.Shared;

namespace LayerPeek.App.Tests.Platform.MacOS;

/// <summary>
/// End-to-end test of the real macOS notifications (T-020), on demand only (<c>LAYERPEEK_E2E=1</c> on macOS; a normal
/// <c>dotnet test</c> builds no bundle and never makes macOS ask for permission). It builds the Debug app as
/// <c>LayerPeek E2E.app</c> with the bundle identifier <see cref="AppEnvironment.TestBundleIdentifier"/> (production + <c>.E2E</c>),
/// signed ad hoc, in <c>artifacts/</c> of the worktree (never in /Applications; macOS refuses notifications of an app in the temp
/// folder), runs it in the isolated test mode with
/// notifications against a simulated printer and checks through the status mode of the bundle that a notification was
/// delivered, that quitting removes it and that the cleanup mode removes it after a killed run. The production bundle, its
/// notifications and its permission are never used.
/// <para>
/// Permission: only the user can allow the notifications of the test bundle (macOS asks with the first notification of the
/// test app). Not allowed yet, the test asks (the dialog of macOS appears), waits a while for the answer and otherwise skips
/// with the steps (docs and task T-020). Turned off, it skips. A click on a notification cannot be automated without
/// Accessibility rights (a change of the user's privacy settings), so the click itself stays a manual check; the test checks
/// the path from the click to the opened address through the instance command a click becomes.
/// </para>
/// <para>
/// Left in the system: the entry "LayerPeek E2E" in System Settings &gt; Notifications (macOS offers no way to remove it, see
/// T-020). Everything else the run creates goes in <c>finally</c>: the notifications, the registration of the bundle in
/// LaunchServices (<c>lsregister -u</c>), its Preferences domain and the bundle itself.
/// </para>
/// </summary>
[SupportedOSPlatform("macos")]
public sealed partial class MacNotificationsE2ETests
{
    /// <summary>Set to 1 to run the end-to-end tests (as for the Windows E2E tests).</summary>
    public const string RunVariable = "LAYERPEEK_E2E";

    private const string PrinterId = "notify";

    private const string BundleName = "LayerPeek E2E.app";

    /// <summary>
    /// macOS asks with a notification of its own ("LayerPeek E2E" would like to send notifications, Options &gt; Allow); while
    /// that question waits, the permission already reads as denied.
    /// </summary>
    private const string DeniedReason =
        "The notifications of 'LayerPeek E2E' are not allowed (yet): allow them in the notification of macOS that asks for it "
        + "(Options > Allow) or in System Settings > Notifications > LayerPeek E2E, then run the test again.";

    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(45);

    /// <summary>How long a first run waits for the user to answer the question of macOS.</summary>
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromMinutes(2);

    private static readonly string LsRegister = "/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister";

    [Fact]
    public async Task NotificationsOfTheTestBundleAreDeliveredAndRemovedOnQuitAndAfterACrash()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS() && Environment.GetEnvironmentVariable(RunVariable) == "1", $"On demand only: {RunVariable}=1 on macOS (builds and runs the test bundle LayerPeek E2E.app).");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string run = Path.Combine(TestDirectories.ShortRoot, "n" + Guid.NewGuid().ToString("N")[..8]);
        // The bundle outside the temp folder: macOS refuses notifications of an app under /private/var/folders ("Failed to find
        // or validate client" in usernoted, UNErrorCodeNotificationsNotAllowed, found in T-020). artifacts/ is ignored by git.
        string bundleFolder = Path.Combine(TestWorktree.Root, "artifacts", "e2e-macos-notifications", Guid.NewGuid().ToString("N")[..8]);
        TestBundle bundle = await TestBundle.BuildAsync(bundleFolder, cancellationToken);
        Process? app = null;
        try
        {
            Status before = await bundle.StatusAsync(cancellationToken);
            Assert.SkipWhen(before.Permission == "Denied", DeniedReason);

            await using SimulatedPrinter printer = await SimulatedPrinter.StartAsync(new SimulatedPrinterOptions { Scenario = PrinterScenario.Printing }, cancellationToken);
            string data = Path.Combine(run, "data");
            await WriteSettingsAsync(data, printer, cancellationToken);
            string suffix = "e2e-" + Guid.NewGuid().ToString("N")[..8];

            // Run 1: a notification, then Quit removes it.
            app = await bundle.StartAppAsync(data, suffix, cancellationToken);
            await WaitForFirstPollAsync(printer, app, data, cancellationToken);
            printer.Simulation.SetScenario(PrinterScenario.Paused);
            Status delivered = await WaitForDeliveryAsync(bundle, before.Permission != "Allowed", cancellationToken);
            if (delivered.Delivered == 0)
            {
                Assert.Skip($"No notification of 'LayerPeek E2E' was delivered; permission: {delivered.Permission}. {DeniedReason} What the app logged about notifications:{Environment.NewLine}{NotificationLog(data)}");
            }

            string log = ReadLog(data);
            Assert.Contains("was handed to macOS", log, StringComparison.Ordinal);
            Assert.Contains($"Notifications of {AppEnvironment.TestBundleIdentifier} are allowed in macOS.", log, StringComparison.Ordinal);
            Assert.DoesNotContain($"Notifications of {AppEnvironment.ProductionBundleIdentifier} ", log, StringComparison.Ordinal);

            // The path of a click (the delegate turns it into this instance command): the printer's web is opened.
            Assert.Equal(InstanceRole.Forwarded, SendCommand(data, suffix, new InstanceCommand(InstanceCommandKind.Toast, LayerPeek.App.Notifications.ToastLaunchArgument.ForPrinter(PrinterId))));
            await Wait.UntilAsync(() => OpenedUrls(data).Count > 0, TimeSpan.FromSeconds(15), "the printer's web opened by the click command", cancellationToken);

            Assert.Equal(InstanceRole.Forwarded, SendCommand(data, suffix, new InstanceCommand(InstanceCommandKind.Quit)));
            await app.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            Assert.Equal(0, (await bundle.StatusAsync(cancellationToken)).Delivered);
            Assert.Contains("were removed from the notification center", ReadLog(data), StringComparison.Ordinal);
            app.Dispose();
            app = null;

            // Run 2: a notification, the app is killed, the cleanup mode of the bundle removes it.
            app = await bundle.StartAppAsync(data, suffix, cancellationToken);
            await WaitForFirstPollAsync(printer, app, data, cancellationToken);
            printer.Simulation.SetScenario(PrinterScenario.Printing);
            Assert.True((await WaitForDeliveryAsync(bundle, waitForAnswer: false, cancellationToken)).Delivered > 0, "The notification of the second run was not delivered.");
            app.Kill();
            await app.WaitForExitAsync(cancellationToken);
            app.Dispose();
            app = null;
            Assert.True((await bundle.StatusAsync(cancellationToken)).Delivered > 0, "The notification of the killed run is already gone.");

            (int exitCode, string output, string error) = await bundle.RunAsync([AppEnvironment.ClearToastsArgument], cancellationToken);
            Assert.True(exitCode == 0, $"The cleanup mode ended with {exitCode}: {output}{error}");
            Assert.Equal(string.Empty, error);
            Assert.Equal(0, (await bundle.StatusAsync(cancellationToken)).Delivered);
        }
        finally
        {
            if (app is { HasExited: false })
            {
                app.Kill();
                await app.WaitForExitAsync(CancellationToken.None);
            }

            app?.Dispose();
            await bundle.DisposeAsync();
            TestDirectories.Delete(run);
            DeleteEmpty(Path.GetDirectoryName(bundleFolder)!);
            DeleteEmpty(Path.Combine(TestWorktree.Root, "artifacts"));
        }
    }

    private static void DeleteEmpty(string folder)
    {
        if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
        {
            Directory.Delete(folder);
        }
    }

    /// <summary>Monitoring took its first status (the baseline, no notification); the next change notifies.</summary>
    private static async Task WaitForFirstPollAsync(SimulatedPrinter printer, Process app, string data, CancellationToken cancellationToken)
    {
        int requests = printer.RequestCount;
        await Wait.UntilAsync(() => app.HasExited || printer.RequestCount > requests, TimeSpan.FromSeconds(30), "the app to poll the printer", cancellationToken);
        // While every display sleeps the app waits for one before its UI and monitoring start (T-096) and this wait times out:
        // wake the display (caffeinate -d -u).
        Assert.False(app.HasExited, $"The test app ended. Its log:{Environment.NewLine}{ReadLog(data)}");
        // One poll is 5 s: the next one after the switch sees the new state.
        await Task.Delay(TimeSpan.FromSeconds(7), cancellationToken);
    }

    private static async Task<Status> WaitForDeliveryAsync(TestBundle bundle, bool waitForAnswer, CancellationToken cancellationToken)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        TimeSpan timeout = waitForAnswer ? AnswerTimeout : DeliveryTimeout;
        Status status;
        do
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            status = await bundle.StatusAsync(cancellationToken);
        }
        // Asked for the first time: the permission reads as denied until the user answers, so only the time ends the wait.
        while (status.Delivered == 0 && (waitForAnswer || status.Permission != "Denied") && elapsed.Elapsed < timeout);

        return status;
    }

    /// <summary>A test command through the instance channel of the test app, like a later start (T-043).</summary>
    private static InstanceRole SendCommand(string data, string suffix, InstanceCommand command)
    {
        using SingleInstanceGuard guard = new(
            // The app adds the separator to the variable's value (InstanceTransports.NameSuffix).
            InstanceTransports.ForCurrentPlatform("-" + suffix, AppEnvironment.ForTests(data)),
            NullLogger<SingleInstanceGuard>.Instance);
        return guard.Start(command, static _ => { });
    }

    private static async Task WriteSettingsAsync(string data, SimulatedPrinter printer, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(data);
        JsonObject settings = new()
        {
            ["version"] = 1,
            ["pollingIntervalSeconds"] = 5,
            ["language"] = "en",
            // Silent: the test run plays no sound (as the Windows E2E tests, T-068).
            ["notifications"] = new JsonObject { ["enabled"] = true, ["remainingTimeMinutes"] = 0, ["sound"] = "Off" },
            ["printers"] = new JsonArray(new JsonObject
            {
                ["id"] = PrinterId,
                ["name"] = "LP E2E " + Guid.NewGuid().ToString("N")[..8],
                ["connectionType"] = "PrusaLink",
                ["address"] = $"127.0.0.1:{printer.Port}",
                ["userName"] = printer.UserName,
            }),
        };
        await File.WriteAllTextAsync(Path.Combine(data, "settings.json"), settings.ToJsonString(), cancellationToken);
        // The in-memory password store of the test mode (T-044) reads its passwords from this file.
        await File.WriteAllTextAsync(Path.Combine(data, "test-secrets.txt"), $"printer/{PrinterId}\t{printer.Password}\n", cancellationToken);
    }

    private static string ReadLog(string data)
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

    private static string NotificationLog(string data) =>
        string.Join(Environment.NewLine, ReadLog(data).Split('\n').Where(static line => line.Contains("otification", StringComparison.Ordinal) || line.Contains("macOS", StringComparison.Ordinal)).Select(static line => "  " + line.TrimEnd()));

    private static List<string> OpenedUrls(string data)
    {
        string file = Path.Combine(data, "opened-urls.txt");
        return File.Exists(file) ? [.. File.ReadAllLines(file).Where(static line => line.Length > 0)] : [];
    }

    /// <summary>What the status mode of the test bundle printed.</summary>
    internal sealed record Status(string Permission, int Delivered);

    [GeneratedRegex(@"^permission=(?<permission>\w+) delivered=(?<delivered>\d+)$", RegexOptions.Multiline)]
    private static partial Regex StatusLine();

    /// <summary>
    /// <c>LayerPeek E2E.app</c>: the Debug app of this worktree with the test bundle identifier, signed ad hoc. Disposing removes
    /// its notifications, its LaunchServices registration, its Preferences domain and the bundle.
    /// </summary>
    internal sealed class TestBundle : IAsyncDisposable
    {
        private TestBundle(string path) => Path = path;

        public string Path { get; }

        public string Executable => System.IO.Path.Combine(Path, "Contents", "MacOS", "LayerPeek");

        public static async Task<TestBundle> BuildAsync(string run, CancellationToken cancellationToken)
        {
            string source = System.IO.Path.Combine(TestWorktree.Root, "src", "LayerPeek.App", "bin", "Debug", "net11.0");
            Assert.True(File.Exists(System.IO.Path.Combine(source, "LayerPeek.dll")) && File.Exists(System.IO.Path.Combine(source, "LayerPeek")), $"The Debug app is not built in {source}.");
            TestBundle bundle = new(System.IO.Path.Combine(run, BundleName));
            string macOS = System.IO.Path.Combine(bundle.Path, "Contents", "MacOS");
            Copy(source, macOS);
            string plist = System.IO.Path.Combine(bundle.Path, "Contents", "Info.plist");
            await File.WriteAllTextAsync(plist, TestInfoPlist(await File.ReadAllTextAsync(System.IO.Path.Combine(TestWorktree.Root, "build", "macos", "Info.plist"), cancellationToken)), cancellationToken);

            // Never the production identity, checked on what macOS will read.
            (int code, string identifier, _) = await RunToolAsync("/usr/bin/plutil", ["-extract", "CFBundleIdentifier", "raw", plist], cancellationToken);
            Assert.Equal(0, code);
            Assert.Equal(AppEnvironment.TestBundleIdentifier, identifier.Trim());

            // Every file of Contents/MacOS is sealed on its own (a nested file without a signature fails the bundle's), then the bundle.
            foreach (string[] chunk in Directory.EnumerateFiles(macOS, "*", SearchOption.AllDirectories).Where(file => file != bundle.Executable).Chunk(40))
            {
                await RequireToolAsync("/usr/bin/codesign", ["--force", "--sign", "-", "--timestamp=none", .. chunk], cancellationToken);
            }

            await RequireToolAsync("/usr/bin/codesign", ["--force", "--sign", "-", "--timestamp=none", bundle.Path], cancellationToken);
            await RequireToolAsync("/usr/bin/codesign", ["--verify", "--strict", bundle.Path], cancellationToken);
            // The notification center finds the app by its identifier through LaunchServices: another registration of the test
            // identifier (a test bundle of a crashed run) makes it refuse this one ("Failed to find or validate client",
            // UNErrorCodeNotificationsNotAllowed). Only test bundles carry the identifier, so those registrations go.
            foreach (string stale in await RegisteredPathsAsync(cancellationToken))
            {
                _ = await RunToolAsync(LsRegister, ["-u", stale], cancellationToken);
            }

            // Unregistered again when the bundle is disposed.
            await RequireToolAsync(LsRegister, ["-f", bundle.Path], cancellationToken);
            return bundle;
        }

        /// <summary>
        /// Starts the app of the bundle through LaunchServices (<c>open -n</c>), like a user would: started as a child of the
        /// test host, the app is not its own responsible process and macOS refuses its notifications
        /// (<c>UNErrorCodeNotificationsNotAllowed</c>) without ever asking the user.
        /// </summary>
        public async Task<Process> StartAppAsync(string data, string suffix, CancellationToken cancellationToken)
        {
            string root = DotnetRoot();
            List<string> arguments = ["-n", "-F", "-g", "--stdout", System.IO.Path.Combine(data, "stdout.txt"), "--stderr", System.IO.Path.Combine(data, "stderr.txt")];
            foreach ((string name, string value) in new[]
            {
                (AppEnvironment.DataDirectoryVariable, data),
                (InstanceTransports.SuffixVariable, suffix),
                (AppEnvironment.TestToastsVariable, "1"),
                ("DOTNET_ROOT", root),
            })
            {
                arguments.Add("--env");
                arguments.Add($"{name}={value}");
            }

            arguments.Add(Path);
            HashSet<int> before = [.. AppProcesses()];
            await RequireToolAsync("/usr/bin/open", arguments, cancellationToken);
            int started = 0;
            await Wait.UntilAsync(() => (started = AppProcesses().FirstOrDefault(id => !before.Contains(id))) != 0, TimeSpan.FromSeconds(20), "the test app to start", cancellationToken);
            return Process.GetProcessById(started);
        }

        /// <summary>
        /// Starts the bundle's executable as a child of the test host in the test mode with <paramref name="variables"/>
        /// (T-099: its exit code is known; no notifications, which need the start through LaunchServices). Other test
        /// switches of the test host are not passed on. Its output is read and discarded.
        /// </summary>
        public Process StartChild(IReadOnlyDictionary<string, string> variables)
        {
            ArgumentNullException.ThrowIfNull(variables);
            ProcessStartInfo start = new(Executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string name in start.Environment.Keys.Where(static name => name.StartsWith("LAYERPEEK_", StringComparison.Ordinal)).ToList())
            {
                start.Environment.Remove(name);
            }

            SetDotnetRoot(start);
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

        /// <summary>The processes of this bundle's executable.</summary>
        private List<int> AppProcesses()
        {
            ProcessStartInfo start = new("/bin/ps") { UseShellExecute = false, RedirectStandardOutput = true };
            foreach (string argument in (ReadOnlySpan<string>)["-axww", "-o", "pid=,command="])
            {
                start.ArgumentList.Add(argument);
            }

            using Process ps = Process.Start(start) ?? throw new InvalidOperationException("ps did not start.");
            string output = ps.StandardOutput.ReadToEnd();
            ps.WaitForExit();
            List<int> ids = [];
            foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int space = line.IndexOf(' ', StringComparison.Ordinal);
                if (space <= 0)
                {
                    continue;
                }

                string command = line[(space + 1)..];
                // LaunchServices starts the real path: /var is /private/var on macOS.
                if ((command.StartsWith(Executable, StringComparison.Ordinal) || command.StartsWith("/private" + Executable, StringComparison.Ordinal)) && int.TryParse(line[..space], NumberStyles.None, CultureInfo.InvariantCulture, out int id))
                {
                    ids.Add(id);
                }
            }

            return ids;
        }

        public async Task<Status> StatusAsync(CancellationToken cancellationToken)
        {
            (int code, string output, string error) = await RunAsync([AppEnvironment.NotificationStatusArgument], cancellationToken);
            Match match = StatusLine().Match(output);
            Assert.True(code == 0 && match.Success, $"The status mode ended with {code}: {output}{error}");
            return new Status(match.Groups["permission"].Value, int.Parse(match.Groups["delivered"].Value, CultureInfo.InvariantCulture));
        }

        public Task<(int ExitCode, string Output, string Error)> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
            RunToolAsync(Executable, arguments, cancellationToken, SetDotnetRoot);

        public async ValueTask DisposeAsync()
        {
            if (!Directory.Exists(Path))
            {
                return;
            }

            // A test app this run started and lost track of (a failed start, a failed assertion) never outlives the run.
            foreach (int id in AppProcesses())
            {
                try
                {
                    using Process left = Process.GetProcessById(id);
                    left.Kill();
                    await left.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception)
                {
                    // Already gone.
                }
            }

            // The notifications of the test bundle (also after a failed assertion), then what macOS keeps for the bundle.
            _ = await RunAsync([AppEnvironment.ClearToastsArgument], CancellationToken.None);
            _ = await RunToolAsync(LsRegister, ["-u", Path], CancellationToken.None);
            if ((await RunToolAsync("/usr/bin/defaults", ["read", AppEnvironment.TestBundleIdentifier], CancellationToken.None)).ExitCode == 0)
            {
                _ = await RunToolAsync("/usr/bin/defaults", ["delete", AppEnvironment.TestBundleIdentifier], CancellationToken.None);
            }

            string library = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library");
            // `defaults delete` leaves an empty plist behind; these places belong to the test bundle only.
            DeleteFile(System.IO.Path.Combine(library, "Preferences", AppEnvironment.TestBundleIdentifier + ".plist"));
            foreach (string folder in new[] { "Saved Application State/" + AppEnvironment.TestBundleIdentifier + ".savedState", "Caches/" + AppEnvironment.TestBundleIdentifier, "HTTPStorages/" + AppEnvironment.TestBundleIdentifier })
            {
                string path = System.IO.Path.Combine(library, folder);
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }

            Directory.Delete(System.IO.Path.GetDirectoryName(Path)!, recursive: true);
        }

        /// <summary>The bundles LaunchServices knows with the test bundle identifier.</summary>
        internal static async Task<List<string>> RegisteredPathsAsync(CancellationToken cancellationToken)
        {
            (_, string dump, _) = await RunToolAsync(LsRegister, ["-dump"], cancellationToken);
            return RegisteredPaths(dump, AppEnvironment.TestBundleIdentifier);
        }

        /// <summary>The <c>path:</c> of every record of <c>lsregister -dump</c> whose <c>identifier:</c> is <paramref name="identifier"/>.</summary>
        internal static List<string> RegisteredPaths(string dump, string identifier)
        {
            List<string> paths = [];
            string? path = null;
            foreach (string raw in dump.Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("----", StringComparison.Ordinal))
                {
                    path = null;
                }
                else if (line.StartsWith("path:", StringComparison.Ordinal))
                {
                    path = Regex.Replace(line["path:".Length..].Trim(), @"\s+\(0x[0-9a-fA-F]+\)$", string.Empty);
                }
                else if (path is not null && line.StartsWith("identifier:", StringComparison.Ordinal) && line["identifier:".Length..].Trim() == identifier && !paths.Contains(path))
                {
                    paths.Add(path);
                }
            }

            return paths;
        }

        /// <summary>The Info.plist of LayerPeek.app with the test identity: identifier + <c>.E2E</c>, name "LayerPeek E2E", no icon.</summary>
        internal static string TestInfoPlist(string production)
        {
            string text = production
                .Replace($"<string>{AppEnvironment.ProductionBundleIdentifier}</string>", $"<string>{AppEnvironment.TestBundleIdentifier}</string>", StringComparison.Ordinal)
                .Replace("@VERSION@", "0.0.0", StringComparison.Ordinal);
            text = Regex.Replace(text, @"(<key>CFBundle(Display)?Name</key>\s*<string>)LayerPeek(</string>)", "$1LayerPeek E2E$3");
            return Regex.Replace(text, @"\s*<key>CFBundleIconFile</key>\s*<string>[^<]*</string>", string.Empty);
        }

        private static void Copy(string source, string target)
        {
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string relative = System.IO.Path.GetRelativePath(source, file);
                // The native libraries of other platforms are not needed (and not worth signing).
                if (relative.StartsWith("runtimes" + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal) && !relative.StartsWith(System.IO.Path.Combine("runtimes", "osx"), StringComparison.Ordinal))
                {
                    continue;
                }

                string destination = System.IO.Path.Combine(target, relative);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
        }

        private static void DeleteFile(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static async Task RequireToolAsync(string tool, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            (int code, string output, string error) = await RunToolAsync(tool, arguments, cancellationToken);
            Assert.True(code == 0, $"{System.IO.Path.GetFileName(tool)} ended with {code}: {output}{error}");
        }

        /// <summary>The runtime of this test host for the framework-dependent app (an install outside the default location).</summary>
        private static void SetDotnetRoot(ProcessStartInfo start) => start.Environment["DOTNET_ROOT"] = DotnetRoot();

        private static string DotnetRoot()
        {
            string? root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            // <root>/shared/Microsoft.NETCore.App/<version>/System.Private.CoreLib.dll
            return string.IsNullOrEmpty(root)
                ? System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!, "..", "..", ".."))
                : root;
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
            await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromMinutes(2), cancellationToken);
            return (process.ExitCode, await output, await error);
        }
    }

    /// <summary>Polling with a timeout.</summary>
    private static class Wait
    {
        public static async Task UntilAsync(Func<bool> condition, TimeSpan timeout, string what, CancellationToken cancellationToken)
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            while (!condition())
            {
                if (elapsed.Elapsed > timeout)
                {
                    throw new TimeoutException($"Waited {timeout.TotalSeconds:0} s for {what}.");
                }

                await Task.Delay(200, cancellationToken);
            }
        }
    }
}
#endif
