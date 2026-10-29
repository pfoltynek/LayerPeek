#if DEBUG
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Composition;
using LayerPeek.App.SingleInstance;
using LayerPeek.Simulator.Printers;
using LayerPeek.Tests.Shared;

namespace LayerPeek.App.Tests.Platform.MacOS;

/// <summary>
/// End-to-end test of the start without an active display (T-096, T-099), on demand only (<c>LAYERPEEK_E2E=1</c> on macOS).
/// The real Debug app, in the test bundle <c>LayerPeek E2E.app</c> (so that what AppKit keeps goes to the test identifier, removed
/// afterwards) and the isolated test mode, is told by <see cref="AppEnvironment.TestNoDisplayVariable"/> that no display is
/// active for a while; the display is never put to sleep (run it with an awake display, e.g. under <c>caffeinate -d -u</c>).
/// Against a simulated printer on 127.0.0.1 it checks that monitoring runs during the wait, that the UI attaches afterwards,
/// that later starts during the wait are honoured once after it, and that a quit during the wait (test command or SIGTERM)
/// ends the process with 0. Notifications are not part of it (they need the start through LaunchServices and the permission
/// of the test bundle: <see cref="MacNotificationsE2ETests"/>).
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacDisplayWaitE2ETests
{
    private const string PrinterId = "wait";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task MonitoringRunsWhileWaitingAndTheUiAttachesAndHonoursLaterStarts()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS() && Environment.GetEnvironmentVariable(MacNotificationsE2ETests.RunVariable) == "1", $"On demand only: {MacNotificationsE2ETests.RunVariable}=1 on macOS (builds and runs the test bundle LayerPeek E2E.app).");
        await RunAsync(async (bundle, printer, data, suffix, cancellationToken) =>
        {
            using Process app = bundle.StartChild(Variables(data, suffix, noDisplay: TimeSpan.FromSeconds(20)));
            try
            {
                await UntilAsync(() => Log(data).Contains("Starting printer monitoring and notifications while waiting for an active display.", StringComparison.Ordinal), app, data, "the services to start while waiting", cancellationToken);
                int requests = printer.RequestCount;
                await UntilAsync(() => printer.RequestCount > requests, app, data, "the app to poll the printer while waiting", cancellationToken);
                Assert.DoesNotContain("starting the UI.", Log(data), StringComparison.Ordinal);

                // Two later starts during the wait: honoured once, after the wait (user decision 2026-10-29).
                Assert.Equal(InstanceRole.Forwarded, Send(data, suffix, InstanceCommand.Activate));
                Assert.Equal(InstanceRole.Forwarded, Send(data, suffix, InstanceCommand.Activate));
                await UntilAsync(() => Log(data).Contains("waits for the menu bar item", StringComparison.Ordinal), app, data, "the later start to be kept", cancellationToken);
                Assert.DoesNotContain("reached the menu bar app", Log(data), StringComparison.Ordinal);

                await UntilAsync(() => Log(data).Contains("The menu bar item attaches to the monitoring that already runs.", StringComparison.Ordinal), app, data, "the UI to attach", cancellationToken);
                await UntilAsync(() => Log(data).Contains("The instance command Activate reached the menu bar app.", StringComparison.Ordinal), app, data, "the later start to reach the UI", cancellationToken);
                string log = Log(data);
                Assert.True(log.IndexOf("starting the UI.", StringComparison.Ordinal) < log.IndexOf("The instance command Activate reached", StringComparison.Ordinal), "The activation reached the UI before it started.");
                // The same monitoring session: started once.
                Assert.Single(Occurrences(log, "Starting printer monitoring and notifications while waiting"));
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                Assert.Single(Occurrences(Log(data), "The instance command Activate reached the menu bar app."));

                Assert.Equal(InstanceRole.Forwarded, Send(data, suffix, new InstanceCommand(InstanceCommandKind.Quit)));
                await app.WaitForExitAsync(cancellationToken).WaitAsync(Timeout, cancellationToken);
                Assert.Equal(0, app.ExitCode);
            }
            finally
            {
                Stop(app);
            }
        });
    }

    [Fact]
    public async Task QuittingWhileWaitingEndsWithZero()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS() && Environment.GetEnvironmentVariable(MacNotificationsE2ETests.RunVariable) == "1", $"On demand only: {MacNotificationsE2ETests.RunVariable}=1 on macOS (builds and runs the test bundle LayerPeek E2E.app).");
        await RunAsync(async (bundle, printer, data, suffix, cancellationToken) =>
        {
            // The test command "quit" during the wait.
            using (Process app = bundle.StartChild(Variables(data, suffix, noDisplay: TimeSpan.FromSeconds(90))))
            {
                try
                {
                    await WaitForMonitoringAsync(app, printer, data, cancellationToken);
                    Assert.Equal(InstanceRole.Forwarded, Send(data, suffix, new InstanceCommand(InstanceCommandKind.Quit)));
                    await app.WaitForExitAsync(cancellationToken).WaitAsync(Timeout, cancellationToken);
                    Assert.Equal(0, app.ExitCode);
                    Assert.Contains("while waiting for an active display; the UI was not started.", Log(data), StringComparison.Ordinal);
                }
                finally
                {
                    Stop(app);
                }
            }

            // SIGTERM during the wait (logout, kill).
            using Process second = bundle.StartChild(Variables(data, suffix, noDisplay: TimeSpan.FromSeconds(90)));
            try
            {
                int before = Occurrences(Log(data), "Starting printer monitoring and notifications while waiting").Count;
                await UntilAsync(() => Occurrences(Log(data), "Starting printer monitoring and notifications while waiting").Count > before, second, data, "the services of the second run", cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                using (Process kill = Process.Start("/bin/kill", ["-TERM", second.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)]))
                {
                    await kill.WaitForExitAsync(cancellationToken);
                }

                await second.WaitForExitAsync(cancellationToken).WaitAsync(Timeout, cancellationToken);
                Assert.Equal(0, second.ExitCode);
                Assert.Equal(2, Occurrences(Log(data), "the UI was not started.").Count);
            }
            finally
            {
                Stop(second);
            }
        });
    }

    private delegate Task Body(MacNotificationsE2ETests.TestBundle bundle, SimulatedPrinter printer, string data, string suffix, CancellationToken cancellationToken);

    private static async Task RunAsync(Body body)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string run = Path.Combine(TestDirectories.ShortRoot, "w" + Guid.NewGuid().ToString("N")[..8]);
        string bundleFolder = Path.Combine(TestWorktree.Root, "artifacts", "e2e-macos-display-wait", Guid.NewGuid().ToString("N")[..8]);
        MacNotificationsE2ETests.TestBundle bundle = await MacNotificationsE2ETests.TestBundle.BuildAsync(bundleFolder, cancellationToken);
        try
        {
            await using SimulatedPrinter printer = await SimulatedPrinter.StartAsync(new SimulatedPrinterOptions { Scenario = PrinterScenario.Printing }, cancellationToken);
            string data = Path.Combine(run, "data");
            await WriteSettingsAsync(data, printer, cancellationToken);
            await body(bundle, printer, data, "e2e-" + Guid.NewGuid().ToString("N")[..8], cancellationToken);
        }
        finally
        {
            await bundle.DisposeAsync();
            TestDirectories.Delete(run);
            DeleteEmpty(Path.GetDirectoryName(bundleFolder)!);
            DeleteEmpty(Path.Combine(TestWorktree.Root, "artifacts"));
        }
    }

    private static Dictionary<string, string> Variables(string data, string suffix, TimeSpan noDisplay) => new()
    {
        [AppEnvironment.DataDirectoryVariable] = data,
        [InstanceTransports.SuffixVariable] = suffix,
        [AppEnvironment.TestNoDisplayVariable] = ((int)noDisplay.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    private static async Task WaitForMonitoringAsync(Process app, SimulatedPrinter printer, string data, CancellationToken cancellationToken)
    {
        await UntilAsync(() => Log(data).Contains("Starting printer monitoring and notifications while waiting for an active display.", StringComparison.Ordinal), app, data, "the services to start while waiting", cancellationToken);
        int requests = printer.RequestCount;
        await UntilAsync(() => printer.RequestCount > requests, app, data, "the app to poll the printer while waiting", cancellationToken);
    }

    private static InstanceRole Send(string data, string suffix, InstanceCommand command)
    {
        using SingleInstanceGuard guard = new(
            // The app adds the separator to the variable's value (InstanceTransports.NameSuffix).
            InstanceTransports.ForCurrentPlatform("-" + suffix, AppEnvironment.ForTests(data)),
            NullLogger<SingleInstanceGuard>.Instance);
        InstanceRole role = guard.Start(command, static _ => { });
        Assert.True(role == InstanceRole.Forwarded, $"{role}; files: {string.Join(", ", Directory.EnumerateFileSystemEntries(data).Select(Path.GetFileName))}. The log:{Environment.NewLine}{Log(data)}");
        return role;
    }

    private static void Stop(Process app)
    {
        if (!app.HasExited)
        {
            app.Kill();
            app.WaitForExit(10_000);
        }
    }

    private static async Task WriteSettingsAsync(string data, SimulatedPrinter printer, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(data);
        JsonObject settings = new()
        {
            ["version"] = 1,
            ["pollingIntervalSeconds"] = 2,
            ["language"] = "en",
            ["notifications"] = new JsonObject { ["enabled"] = false },
            ["printers"] = new JsonArray(new JsonObject
            {
                ["id"] = PrinterId,
                ["name"] = "LP wait " + Guid.NewGuid().ToString("N")[..8],
                ["connectionType"] = "PrusaLink",
                ["address"] = $"127.0.0.1:{printer.Port}",
                ["userName"] = printer.UserName,
            }),
        };
        await File.WriteAllTextAsync(Path.Combine(data, "settings.json"), settings.ToJsonString(), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(data, "test-secrets.txt"), $"printer/{PrinterId}\t{printer.Password}\n", cancellationToken);
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

    private static List<int> Occurrences(string text, string value)
    {
        List<int> found = [];
        for (int index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            found.Add(index);
        }

        return found;
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

    private static void DeleteEmpty(string folder)
    {
        if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
        {
            Directory.Delete(folder);
        }
    }
}
#endif
