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
/// icon, settings, Start menu shortcut) is touched. On Windows the names are kernel objects that vanish with the
/// processes. On macOS (T-053) the lock file and socket go to the test data folder, with a long data folder to the private
/// socket folder in the temp folder; the short reserved temp folder of the assembly is that temp folder (the started exe
/// gets it as its <c>TMPDIR</c>), so all of it stays inside it (socket paths are limited to 103 bytes), removed after each test and after a crashed run.
/// </summary>
public sealed class SecondProcessTests
{
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task SecondStartOfTheExeForwardsActivateAndExitsWithZero()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The two-process test of the Windows transport.");
        string exe = AppExe();

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [SupportedOSPlatform("macos")]
    public async Task SecondStartOfTheMacAppForwardsActivateAndExitsWithZero(bool longDataFolder)
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "The two-process test of the macOS transport.");
        string exe = AppExe();
        string suffix = "t" + Guid.NewGuid().ToString("N")[..8];
        string folder = Path.Combine(TestDirectories.ShortRoot, Guid.NewGuid().ToString("N")[..8]);
        // The reserved folder itself: one level deeper and the private socket folder would not fit into a socket path.
        string temp = TestDirectories.ShortRoot;
        string privateFolder = Path.Combine(temp, "LayerPeek-" + MacUnixCredentials.Current.EffectiveUserId);
        string data = longDataFolder ? Path.Combine(folder, new string('d', 80)) : Path.Combine(folder, "d");
        UnixInstancePaths paths = InstanceTransports.UnixPaths(data, temp, MacUnixCredentials.Current.EffectiveUserId, "-" + suffix);
        Assert.Equal(longDataFolder, paths.PrivateSocketDirectory);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(paths.SocketPath) <= 103, $"The socket path of the test is too long: {paths.SocketPath}");
        ProductionLogState productionLog = ProductionLogState.Now();
        ReceivedCommands received = new();
        SingleInstanceGuard running = new(
            InstanceTransports.ForCurrentPlatform("-" + suffix, AppEnvironment.ForTests(data), temp),
            new ListLogger<SingleInstanceGuard>());
        try
        {
            Assert.Equal(InstanceRole.Primary, running.Start(InstanceCommand.Activate, received.Add));
            Assert.NotNull(running.Server);
            Assert.True(File.Exists(paths.SocketPath), $"No socket at {paths.SocketPath}.");
            if (longDataFolder)
            {
                Assert.Equal(UnixSocketInstanceTransport.PrivateDirectoryMode, File.GetUnixFileMode(Path.GetDirectoryName(paths.SocketPath)!));
            }

            using Process second = StartSecond(exe, suffix, data, temp);
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
            string log = await File.ReadAllTextAsync(Path.Combine(data, AppEnvironment.TestLogFolderName, LogFile.FileName), Ct);
            Assert.Contains($"[pid {second.Id}] LayerPeek.App.Program: Another instance is running", log, StringComparison.Ordinal);
            Assert.Equal(productionLog, ProductionLogState.Now());
        }
        finally
        {
            await running.DisposeAsync();
            TestDirectories.Delete(folder);
            TestDirectories.Delete(privateFolder);
        }

        Assert.False(File.Exists(paths.SocketPath));
    }

    /// <summary>The app exe next to the tests (skips without it or in a Release build of the app).</summary>
    private static string AppExe()
    {
        // The name switches exist only in Debug builds of the app; a Release app would meet the production names.
        Assert.SkipUnless(
            typeof(InstanceTransports).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration == "Debug",
            "Needs a Debug build of the app (test-only instance names).");
        string exe = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "LayerPeek.exe" : "LayerPeek");
        Assert.SkipUnless(File.Exists(exe), "The app exe is not next to the tests.");
        return exe;
    }

    /// <param name="tempDirectory">The <c>TMPDIR</c> of the started exe (macOS); <see langword="null"/>: inherited.</param>
    private static Process StartSecond(string exe, string suffix, string dataDirectory, string? tempDirectory = null)
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
        if (tempDirectory is not null)
        {
            start.Environment["TMPDIR"] = tempDirectory;
        }

        foreach (string variable in new[] { AppEnvironment.TestToastsVariable, AppEnvironment.TestCredentialPrefixVariable, AppEnvironment.TestStartDelayVariable })
        {
            start.Environment.Remove(variable);
        }

        return Process.Start(start) ?? throw new InvalidOperationException("The app did not start.");
    }
}
#endif
