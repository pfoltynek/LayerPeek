using System.Runtime.InteropServices;

using Avalonia;

using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.Composition;
using PrusaTrayDiagnostic.App.Logging;
using PrusaTrayDiagnostic.App.Platform.Windows;
using PrusaTrayDiagnostic.App.SingleInstance;
using PrusaTrayDiagnostic.App.Windows;

namespace PrusaTrayDiagnostic.App;

internal static partial class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
#if DEBUG
        if (OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable(NativeLibrarySearch.ProbeVariable) is { Length: > 0 } library)
        {
            // Test of T-052: it needs the standard search order first, so it runs before the restriction below.
            return NativeLibrarySearch.RunProbe(library);
        }
#endif

        // First of all, before anything loads a native library: DLLs loaded by name (FFmpeg) and dependencies of DLLs are
        // then searched only in the application folder and System32, never in PATH (T-052).
        int dllSearchError = 0;
        if (OperatingSystem.IsWindows())
        {
            dllSearchError = NativeLibrarySearch.Restrict();
        }

#if DEBUG
        // Test mode (T-044): its own data folder and, required, its own instance names, so that a test start never
        // forwards to (or blocks) the user's running instance.
        AppEnvironment environment;
        try
        {
            environment = AppEnvironment.FromProcess();
        }
        catch (ArgumentException)
        {
            return AppEnvironment.InvalidTestModeExitCode;
        }

        if (environment.IsIsolated && InstanceTransports.NameSuffix.Length == 0)
        {
            return AppEnvironment.InvalidTestModeExitCode;
        }
#else
        AppEnvironment environment = AppEnvironment.Production;
#endif

        // One logger factory for the whole process (T-024), disposed last: a second start writes its records into the same
        // file before it exits, and the app's records are written even when the OS ends the session.
        using AppLogging logging = AppLogging.Create(environment);
        ILogger log = logging.Factory.CreateLogger(typeof(Program));
        string version = AboutViewModel.ProductVersion(typeof(Program).Assembly);
        LogStarted(log, version, RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture);
        if (OperatingSystem.IsWindows() && dllSearchError != 0)
        {
            // The app still works with the standard search order; only the hardening is missing.
            NativeLibrarySearch.LogRestrictionFailed(log, dllSearchError);
        }

        // The guard runs before the app starts: a later start only passes "activate" on and exits (T-043).
        InstanceCommandQueue instanceCommands = new();
        using SingleInstanceGuard? guard = InstanceTransports.TryCreateForCurrentPlatform(InstanceTransports.NameSuffix, environment) is { } transport
            ? new SingleInstanceGuard(transport, logging.Factory.CreateLogger<SingleInstanceGuard>())
            : null;
        if (guard is not null && guard.Start(InstanceCommand.Activate, instanceCommands.Post) != InstanceRole.Primary)
        {
            LogNotPrimary(log);
            return 0;
        }

        // Only the primary instance rotates the log files; a later start just appends its few records.
        logging.EnableRotation();

#if DEBUG
        if (Environment.GetEnvironmentVariable(InstanceTransports.ForwardOnlyVariable) == "1")
        {
            // Two-process test: this start was meant to find a running instance; never start a real app here.
            return InstanceTransports.ForwardOnlyExitCode;
        }
#endif

        if (OperatingSystem.IsWindows())
        {
            // Before any window exists, so that windows and toasts share the identity of the Start menu shortcut (T-010).
            // Only the identity of this process: nothing is registered. The test mode has its own id (T-044), so test windows
            // never group with the user's instance on the taskbar.
            WindowsAppIdentity.ApplyToCurrentProcess(environment.AppUserModelId);
        }

        return BuildAvaloniaApp(instanceCommands, environment, logging).StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Prusa Tray Diagnostic {Version} starting on {OperatingSystem} ({Architecture}).")]
    private static partial void LogStarted(ILogger logger, string version, string operatingSystem, Architecture architecture);

    [LoggerMessage(Level = LogLevel.Information, Message = "Another instance is running; this start only passed the activation on and exits.")]
    private static partial void LogNotPrimary(ILogger logger);

    /// <summary>Designer entry point.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    private static AppBuilder BuildAvaloniaApp(InstanceCommandQueue instanceCommands, AppEnvironment environment, AppLogging logging) =>
        AppBuilder.Configure(() => new App(instanceCommands, environment, logging))
            .UsePlatformDetect()
            .LogToTrace();
}
