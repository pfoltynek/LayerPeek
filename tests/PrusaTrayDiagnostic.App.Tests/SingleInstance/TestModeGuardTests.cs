#if DEBUG
using System.Diagnostics;
using System.Runtime.Versioning;

using PrusaTrayDiagnostic.App.Composition;
using PrusaTrayDiagnostic.App.SingleInstance;

namespace PrusaTrayDiagnostic.App.Tests.SingleInstance;

/// <summary>
/// The real exe refuses an incomplete test mode (T-044) with exit code 4 before the single-instance guard runs: without
/// the instance name suffix (it would meet the user's running instance) or with a relative data folder. Should the guard
/// ever run first, the forward-only switch makes the start exit with 3 instead of starting an app, and the test is skipped
/// while a PrusaTrayDiagnostic process runs, so a regression can never reach the user's instance.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TestModeGuardTests
{
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(30);

    public TestModeGuardTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Starts the Windows exe.");
    }

    /// <summary>A PrusaTrayDiagnostic process that was not started from the exe next to the tests (e.g. the user's instance).</summary>
    private static bool OtherInstanceRuns(string testExe)
    {
        foreach (Process process in Process.GetProcessesByName("PrusaTrayDiagnostic"))
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
        string exe = Path.Combine(AppContext.BaseDirectory, "PrusaTrayDiagnostic.exe");
        Assert.SkipUnless(File.Exists(exe), "The app exe is not next to the tests.");
        Assert.SkipWhen(OtherInstanceRuns(exe), "A PrusaTrayDiagnostic instance (not one of these tests) runs; never risk talking to it.");

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
}
#endif
