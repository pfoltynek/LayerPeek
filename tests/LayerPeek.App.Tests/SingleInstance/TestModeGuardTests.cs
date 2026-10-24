#if DEBUG
using System.Diagnostics;
using System.Runtime.Versioning;

using LayerPeek.App.Composition;
using LayerPeek.App.SingleInstance;

namespace LayerPeek.App.Tests.SingleInstance;

/// <summary>
/// The real exe refuses an incomplete test mode (T-044) with exit code 4 before the single-instance guard runs: without
/// the instance name suffix (it would meet the user's running instance) or with a relative data folder. Should the guard
/// ever run first, the forward-only switch makes the start exit with 3 instead of starting an app, and the test is skipped
/// while a LayerPeek process runs, so a regression can never reach the user's instance.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TestModeGuardTests
{
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(30);

    public TestModeGuardTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Starts the Windows exe.");
    }

    /// <summary>A LayerPeek process that was not started from the exe next to the tests (e.g. the user's instance).</summary>
    private static bool OtherInstanceRuns(string testExe)
    {
        foreach (Process process in Process.GetProcessesByName("LayerPeek"))
        {
            using (process)
            {
                string? path;
                try
                {
                    path = process.MainModule?.FileName;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    path = null;
                }

                if (!string.Equals(path, testExe, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, "e2e-guard")]
    public async Task IncompleteTestModeExitsWithFourBeforeTheInstanceGuard(bool absoluteFolder, string? suffix)
    {
        string exe = Path.Combine(AppContext.BaseDirectory, "LayerPeek.exe");
        Assert.SkipUnless(File.Exists(exe), "The app exe is not next to the tests.");
        Assert.SkipWhen(OtherInstanceRuns(exe), "A LayerPeek instance (not one of these tests) runs; never risk talking to it.");

        string folder = TestDirectories.NewDirectoryPath();
        try
        {
            ProcessStartInfo start = new(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory,
            };
            start.Environment[AppEnvironment.DataDirectoryVariable] = absoluteFolder ? folder : "relative-test-data";
            start.Environment.Remove(InstanceTransports.SuffixVariable);
            if (suffix is not null)
            {
                start.Environment[InstanceTransports.SuffixVariable] = suffix;
            }

            start.Environment[InstanceTransports.ForwardOnlyVariable] = "1";
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("The app did not start.");
            try
            {
                await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(ExitTimeout, TestContext.Current.CancellationToken);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }

            Assert.Equal(AppEnvironment.InvalidTestModeExitCode, process.ExitCode);
            // Nothing was written: no test log, no lock, no settings.
            Assert.False(Directory.Exists(folder), "The refused start wrote into the test folder.");
            Assert.False(Directory.Exists(Path.Combine(AppContext.BaseDirectory, "relative-test-data")));
        }
        finally
        {
            TestDirectories.Delete(folder);
        }
    }

    /// <summary>
    /// The opt-in switches of T-067 are checked like the data folder: a credential prefix outside
    /// <c>LayerPeek.E2E/</c> (the production prefix included) or an invalid start delay ends the start with 4
    /// before anything is written.
    /// </summary>
    [Theory]
    [InlineData(AppEnvironment.TestCredentialPrefixVariable, "LayerPeek")]
    [InlineData(AppEnvironment.TestCredentialPrefixVariable, "LayerPeek/x")]
    [InlineData(AppEnvironment.TestCredentialPrefixVariable, "LayerPeek.E2E/")]
    [InlineData(AppEnvironment.TestStartDelayVariable, "60001")]
    [InlineData(AppEnvironment.TestStartDelayVariable, "soon")]
    public async Task InvalidTestSwitchExitsWithFourBeforeTheInstanceGuard(string variable, string value)
    {
        string folder = TestDirectories.NewDirectoryPath();
        try
        {
            int exitCode = await RunExeAsync(start =>
            {
                start.Environment[AppEnvironment.DataDirectoryVariable] = folder;
                start.Environment[InstanceTransports.SuffixVariable] = "e2e-guard-" + Guid.NewGuid().ToString("N")[..8];
                start.Environment[variable] = value;
            });

            Assert.Equal(AppEnvironment.InvalidTestModeExitCode, exitCode);
            Assert.False(Directory.Exists(folder), "The refused start wrote into the test folder.");
        }
        finally
        {
            TestDirectories.Delete(folder);
        }
    }

    /// <summary>A valid test prefix passes the check: the start goes on to the guard (and the forward-only switch ends it with 3).</summary>
    [Fact]
    public async Task ValidTestCredentialPrefixPassesTheTestModeCheck()
    {
        string folder = TestDirectories.NewDirectoryPath();
        try
        {
            int exitCode = await RunExeAsync(start =>
            {
                start.Environment[AppEnvironment.DataDirectoryVariable] = folder;
                start.Environment[InstanceTransports.SuffixVariable] = "e2e-guard-" + Guid.NewGuid().ToString("N")[..8];
                start.Environment[AppEnvironment.TestCredentialPrefixVariable] = "LayerPeek.E2E/guard-test";
                start.Environment[AppEnvironment.TestStartDelayVariable] = "60000";
            });

            Assert.Equal(InstanceTransports.ForwardOnlyExitCode, exitCode);
        }
        finally
        {
            TestDirectories.Delete(folder);
        }
    }

    /// <summary>Runs the exe next to the tests with the forward-only switch (never a real app) and returns its exit code.</summary>
    private static async Task<int> RunExeAsync(Action<ProcessStartInfo> configure)
    {
        string exe = Path.Combine(AppContext.BaseDirectory, "LayerPeek.exe");
        Assert.SkipUnless(File.Exists(exe), "The app exe is not next to the tests.");
        Assert.SkipWhen(OtherInstanceRuns(exe), "A LayerPeek instance (not one of these tests) runs; never risk talking to it.");

        ProcessStartInfo start = new(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        foreach (string variable in new[] { AppEnvironment.TestToastsVariable, AppEnvironment.TestCredentialPrefixVariable, AppEnvironment.TestStartDelayVariable })
        {
            start.Environment.Remove(variable);
        }

        configure(start);
        start.Environment[InstanceTransports.ForwardOnlyVariable] = "1";
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("The app did not start.");
        try
        {
            await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(ExitTimeout, TestContext.Current.CancellationToken);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }

        return process.ExitCode;
    }
}
#endif
