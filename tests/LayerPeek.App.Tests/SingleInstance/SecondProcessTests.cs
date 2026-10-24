#if DEBUG
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;

using LayerPeek.App.SingleInstance;
using LayerPeek.App.Tests.Tray;

namespace LayerPeek.App.Tests.SingleInstance;

/// <summary>
/// The real app exe started a second time (T-043): this test is the running instance (names of its own, Debug builds
/// only), the started exe must hand "activate" over and exit with 0. Should it ever become the primary instance, the
/// Debug-only forward-only switch makes it exit at once instead of starting the app, so nothing else (tray icon, settings,
/// Start menu shortcut) is touched. Windows only: there the names are kernel objects that vanish with the processes,
/// while the macOS lock file would go to the real data folder.
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
        ReceivedCommands received = new();
        SingleInstanceGuard running = new(InstanceTransports.ForCurrentPlatform("-" + suffix), new ListLogger<SingleInstanceGuard>());
        try
        {
            Assert.Equal(InstanceRole.Primary, running.Start(InstanceCommand.Activate, received.Add));

            using Process second = StartSecond(exe, suffix);
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
        }
        finally
        {
            await running.DisposeAsync();
        }
    }

    private static Process StartSecond(string exe, string suffix)
    {
        ProcessStartInfo start = new(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        start.Environment[InstanceTransports.SuffixVariable] = suffix;
        start.Environment[InstanceTransports.ForwardOnlyVariable] = "1";
        return Process.Start(start) ?? throw new InvalidOperationException("The app did not start.");
    }
}
#endif
