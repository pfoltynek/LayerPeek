using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.Composition;
using PrusaTrayDiagnostic.App.Logging;
using PrusaTrayDiagnostic.App.Platform.Windows;

namespace PrusaTrayDiagnostic.App.Tests.Logging;

/// <summary>The logging of the process (T-024) as <c>Program.Main</c> creates it.</summary>
public sealed class AppLoggingTests
{
#if DEBUG
    [Fact]
    [SupportedOSPlatform("windows")]
    public void DllSearchWarningOfProgramMainReachesTheFile()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The DLL search restriction exists on Windows only.");
        string folder = TestDirectories.NewDirectoryPath();
        try
        {
            AppEnvironment environment = AppEnvironment.ForTests(folder);
            using (AppLogging logging = AppLogging.Create(environment))
            {
                // Program.Main logs the result of NativeLibrarySearch.Restrict() as soon as the logging exists (T-052).
                NativeLibrarySearch.LogRestrictionFailed(logging.Factory.CreateLogger(typeof(Program)), 87);
            }

            string log = File.ReadAllText(LogFile.PathOf(environment.LogDirectory(), 0));
            Assert.Contains(" WRN [pid ", log, StringComparison.Ordinal);
            Assert.Contains("PrusaTrayDiagnostic.App.Program: Restricting the DLL search", log, StringComparison.Ordinal);
            Assert.Contains("(error 87)", log, StringComparison.Ordinal);
        }
        finally
        {
            TestDirectories.Delete(folder);
        }
    }

    [Fact]
    public void LoggersCreatedAfterDisposeDropTheirRecordsWithoutThrowing()
    {
        string folder = TestDirectories.NewDirectoryPath();
        try
        {
            AppLogging logging = AppLogging.Create(AppEnvironment.ForTests(folder));
            logging.Dispose();

            // Work still running while the process ends.
            ILogger late = logging.Factory.CreateLogger("Late");
            late.Write(LogLevel.Error, "after dispose");
            Assert.True(logging.Flush(TimeSpan.FromSeconds(1)));
            logging.Dispose();

            Assert.False(Directory.Exists(Path.Combine(folder, AppEnvironment.TestLogFolderName)));
        }
        finally
        {
            TestDirectories.Delete(folder);
        }
    }
#endif

    [Fact]
    public void DisabledLoggingWritesNothingAndNeverThrows()
    {
        using AppLogging logging = AppLogging.Disabled();
        ILogger logger = logging.Factory.CreateLogger("Cat");

        Assert.False(logger.IsEnabled(LogLevel.Critical));
        Assert.Null(logging.Directory);
        Assert.True(logging.Flush(TimeSpan.Zero));
        logging.EnableRotation();
    }
}
