#if DEBUG
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;

using LayerPeek.App.Composition;
using LayerPeek.App.Logging;
using LayerPeek.App.SingleInstance;
using LayerPeek.App.Tests.Tray;

namespace LayerPeek.App.Tests.SingleInstance;

/// <summary>
/// The real app exe started a second time (T-043): this test is the running instance (names of its own, Debug builds
/// only), the started exe must hand "activate" over and exit with 0. The started exe runs in the isolated test mode (its
/// own data folder, so its log goes there and never into the user's log folder, T-074). Should it ever become the primary
/// instance, the Debug-only forward-only switch makes it exit at once instead of starting the app, so nothing else (tray
/// icon, settings, Start menu shortcut) is touched. Windows only: there the names are kernel objects that vanish with the
/// processes, while the macOS lock file would go to the data folder.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SecondProcessTests
{
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(30);

    public SecondProcessTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The two-process test uses the Windows transport.");
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SecondStartOfTheExeForwardsActivateAndExitsWithZero()
    {
        // The name switches exist only in Debug builds of the app; a Release app would meet the production names.
        Assert.SkipUnless(
            typeof(InstanceTransports).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration == "Debug",
            "Needs a Debug build of the app (test-only instance names).");
        string exe = Path.Combine(AppContext.BaseDirectory, "LayerPeek.exe");
        Assert.SkipUnless(File.Exists(exe), "The app exe is not next to the tests.");

        string suffix = "test-" + Guid.NewGuid().ToString("N");
        string folder = TestDirectories.NewDirectoryPath();
        ProductionLogState productionLog = ProductionLogState.Now();
        ReceivedCommands received = new();
        SingleInstanceGuard running = new(InstanceTransports.ForCurrentPlatform("-" + suffix), new ListLogger<SingleInstanceGuard>());
        try
        {
            Assert.Equal(InstanceRole.Primary, running.Start(InstanceCommand.Activate, received.Add));

            using Process second = StartSecond(exe, suffix, folder);
            try
            {
                await second.WaitForExitAsync(Ct).WaitAsync(ExitTimeout, Ct);
            }
            finally
            {
                if (!second.HasExited)
                {
                    second.Kill(entireProcessTree: true);
                }
            }

            Assert.Equal(0, second.ExitCode);
            Assert.Equal(InstanceCommand.Activate, await received.NextAsync(TimeSpan.FromSeconds(10)));

            // The second start logged its handover into the test folder, and nothing into the user's log folder.
            string log = await File.ReadAllTextAsync(Path.Combine(folder, AppEnvironment.TestLogFolderName, LogFile.FileName), Ct);
            Assert.Contains($"[pid {second.Id}] LayerPeek.App.Program: Another instance is running", log, StringComparison.Ordinal);
            Assert.Equal(productionLog, ProductionLogState.Now());
        }
        finally
        {
            await running.DisposeAsync();
            TestDirectories.Delete(folder);
        }
    }

    private static Process StartSecond(string exe, string suffix, string dataDirectory)
    {
        ProcessStartInfo start = new(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        // The complete test mode (T-044): without the data folder this start would run in production mode (T-074).
        start.Environment[AppEnvironment.DataDirectoryVariable] = dataDirectory;
        start.Environment[InstanceTransports.SuffixVariable] = suffix;
        start.Environment[InstanceTransports.ForwardOnlyVariable] = "1";
        foreach (string variable in new[] { AppEnvironment.TestToastsVariable, AppEnvironment.TestCredentialPrefixVariable, AppEnvironment.TestStartDelayVariable })
        {
            start.Environment.Remove(variable);
        }

        return Process.Start(start) ?? throw new InvalidOperationException("The app did not start.");
    }
}
#endif
