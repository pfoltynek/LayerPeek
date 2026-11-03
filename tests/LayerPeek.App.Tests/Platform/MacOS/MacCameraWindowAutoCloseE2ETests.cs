#if DEBUG
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Composition;
using LayerPeek.App.SingleInstance;
using LayerPeek.Simulator;
using LayerPeek.Simulator.Camera;
using LayerPeek.Simulator.Printers;
using LayerPeek.Tests.Shared;

namespace LayerPeek.App.Tests.Platform.MacOS;

/// <summary>
/// End-to-end test of the automatic close and reopen of the camera window on macOS (T-151), on demand only
/// (<c>LAYERPEEK_E2E=1</c> on macOS, with an awake display). The real Debug app in the test bundle <c>LayerPeek E2E.app</c>
/// and the isolated test mode, with the delays shortened by <see cref="AppEnvironment.TestCameraCloseAfterVariable"/> and
/// <see cref="AppEnvironment.TestCameraOpenAfterVariable"/>, against a simulated printer and RTSP camera on 127.0.0.1: the
/// window opened by the test command closes (its stream ends) when the printer goes offline, and opens again (its stream
/// starts) once the printer is back, without making LayerPeek the active app (user decision 2026-11-03 #1: Finder stays in
/// front). The Dock icon and ⌘Tab of the reopened window are checked by hand (T-127).
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacCameraWindowAutoCloseE2ETests
{
    private const string PrinterId = "auto";
    private const string Finder = "com.apple.finder";
    private const string LoginWindow = "com.apple.loginwindow";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task OfflineClosesTheCameraWindowAndOnlineOpensItAgainWithoutActivatingTheApp()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS() && Environment.GetEnvironmentVariable(MacNotificationsE2ETests.RunVariable) == "1", $"On demand only: {MacNotificationsE2ETests.RunVariable}=1 on macOS (builds and runs the test bundle LayerPeek E2E.app).");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string run = Path.Combine(TestDirectories.ShortRoot, "c" + Guid.NewGuid().ToString("N")[..8]);
        string bundleFolder = Path.Combine(TestWorktree.Root, "artifacts", "e2e-macos-camera-auto-close", Guid.NewGuid().ToString("N")[..8]);
        MacNotificationsE2ETests.TestBundle bundle = await MacNotificationsE2ETests.TestBundle.BuildAsync(bundleFolder, cancellationToken);
        try
        {
            await using SimulatedPrinter printer = await SimulatedPrinter.StartAsync(new SimulatedPrinterOptions { Scenario = PrinterScenario.Idle }, cancellationToken);
            await using RtspCamera camera = new(new RtspCameraOptions { Clip = H264Clip.Load(SimulatorHost.DefaultClipPath) });
            string data = Path.Combine(run, "data");
            await WriteSettingsAsync(data, printer, camera, cancellationToken);
            string suffix = "e2e-" + Guid.NewGuid().ToString("N")[..8];
            using Process app = bundle.StartChild(new Dictionary<string, string>
            {
                [AppEnvironment.DataDirectoryVariable] = data,
                [InstanceTransports.SuffixVariable] = suffix,
                [AppEnvironment.TestCameraCloseAfterVariable] = "3000",
                [AppEnvironment.TestCameraOpenAfterVariable] = "2000",
            });
            try
            {
                int requests = printer.RequestCount;
                await UntilAsync(() => printer.RequestCount > requests, app, data, "the app to poll the printer", cancellationToken);

                // "Show camera" of the tray menu (the test command): the window opens and streams.
                await UntilAsync(() => TrySend(data, suffix, new InstanceCommand(InstanceCommandKind.Camera, PrinterId)), app, data, "the instance server", cancellationToken);
                await UntilAsync(() => Log(data).Contains("The instance command Camera reached the menu bar app.", StringComparison.Ordinal), app, data, "the camera command to reach the UI", cancellationToken);
                await UntilAsync(() => camera.ActiveSessions > 0, app, data, "the camera window to stream", cancellationToken);

                printer.Simulation.SetOffline(true);
                await UntilAsync(() => Log(data).Contains($"Closing the camera window of printer {PrinterId}: the printer is offline.", StringComparison.Ordinal), app, data, "the automatic close", cancellationToken);
                await UntilAsync(() => camera.ActiveSessions == 0, app, data, "the stream of the closed window to end", cancellationToken);
                await UntilAsync(() => File.Exists(Path.Combine(data, "camera-windows.json")) && File.ReadAllText(Path.Combine(data, "camera-windows.json")).Contains("\"autoClosed\"", StringComparison.Ordinal), app, data, "the flag to be saved", cancellationToken);

                // Another app in front; the reopened window must leave it there. A locked screen (the login window in
                // front) has no app to keep in front: the rest is checked, the focus is reported as not checked.
                bool locked = FrontBundleIdentifier() == LoginWindow;
                if (!locked)
                {
                    await RunAsync("/usr/bin/open", ["-a", "Finder"], cancellationToken);
                    await UntilAsync(() => FrontBundleIdentifier() == Finder, app, data, "Finder to come to the front", cancellationToken);
                }

                int connections = camera.Connections;

                printer.Simulation.SetOffline(false);
                await UntilAsync(() => Log(data).Contains($"Opening the camera window of printer {PrinterId} again: the printer is online.", StringComparison.Ordinal), app, data, "the automatic open", cancellationToken);
                await UntilAsync(() => camera.Connections > connections && camera.ActiveSessions > 0, app, data, "the reopened window to stream", cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

                Assert.Equal(locked ? LoginWindow : Finder, FrontBundleIdentifier());
                Assert.DoesNotContain("could not open again", Log(data), StringComparison.Ordinal);
                Assert.DoesNotContain("\"autoClosed\"", await File.ReadAllTextAsync(Path.Combine(data, "camera-windows.json"), cancellationToken), StringComparison.Ordinal);

                Assert.True(TrySend(data, suffix, new InstanceCommand(InstanceCommandKind.Quit)));
                await app.WaitForExitAsync(cancellationToken).WaitAsync(Timeout, cancellationToken);
                Assert.Equal(0, app.ExitCode);
                if (locked)
                {
                    Assert.Skip("Close and reopen passed; the focus was not checked: the screen is locked (the login window is in front).");
                }
            }
            finally
            {
                if (!app.HasExited)
                {
                    app.Kill();
                    await app.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
                }
            }
        }
        finally
        {
            await bundle.DisposeAsync();
            TestDirectories.Delete(run);
            DeleteEmpty(Path.GetDirectoryName(bundleFolder)!);
            DeleteEmpty(Path.Combine(TestWorktree.Root, "artifacts"));
        }
    }

    private static bool TrySend(string data, string suffix, InstanceCommand command)
    {
        using SingleInstanceGuard guard = new(
            // The app adds the separator to the variable's value (InstanceTransports.NameSuffix).
            InstanceTransports.ForCurrentPlatform("-" + suffix, AppEnvironment.ForTests(data)),
            NullLogger<SingleInstanceGuard>.Instance);
        return guard.Start(command, static _ => { }) == InstanceRole.Forwarded;
    }

    private static async Task WriteSettingsAsync(string data, SimulatedPrinter printer, RtspCamera camera, CancellationToken cancellationToken)
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
                ["name"] = "LP auto " + Guid.NewGuid().ToString("N")[..8],
                ["connectionType"] = "PrusaLink",
                ["address"] = $"127.0.0.1:{printer.Port}",
                ["userName"] = printer.UserName,
                ["camera"] = new JsonObject { ["enabled"] = true, ["streamUrl"] = camera.StreamUri.ToString() },
            }),
        };
        await File.WriteAllTextAsync(Path.Combine(data, "settings.json"), settings.ToJsonString(), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(data, "test-secrets.txt"), $"printer/{PrinterId}\t{printer.Password}\n", cancellationToken);
    }

    /// <summary>The bundle identifier of the frontmost app (<c>lsappinfo</c>), or <see langword="null"/>.</summary>
    private static string? FrontBundleIdentifier()
    {
        string front = Output("/usr/bin/lsappinfo", ["front"]).Trim();
        if (front.Length == 0)
        {
            return null;
        }

        // bundleID="com.apple.finder" (or "CFBundleIdentifier"="com.apple.finder" in older versions)
        string info = Output("/usr/bin/lsappinfo", ["info", "-only", "bundleid", front]);
        System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(info, "(?:bundleID|\"CFBundleIdentifier\")=\"([^\"]+)\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string Output(string tool, IEnumerable<string> arguments)
    {
        ProcessStartInfo start = new(tool, arguments) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"{tool} did not start.");
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(10_000);
        return output;
    }

    private static async Task RunAsync(string tool, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        using Process process = Process.Start(new ProcessStartInfo(tool, arguments) { UseShellExecute = false }) ?? throw new InvalidOperationException($"{tool} did not start.");
        await process.WaitForExitAsync(cancellationToken).WaitAsync(Timeout, cancellationToken);
        Assert.Equal(0, process.ExitCode);
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
