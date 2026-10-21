using Avalonia;

using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.Composition;
using PrusaTrayDiagnostic.App.Platform.Windows;
using PrusaTrayDiagnostic.App.SingleInstance;

namespace PrusaTrayDiagnostic.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
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

        // The guard runs before anything else: a later start only passes "activate" on and exits (T-043).
        // Logged like the app itself (no providers until T-024; the test log file in the test mode).
        using ILoggerFactory startupLogging = environment.CreateLoggerFactory();
        InstanceCommandQueue instanceCommands = new();
        using SingleInstanceGuard? guard = InstanceTransports.TryCreateForCurrentPlatform(InstanceTransports.NameSuffix, environment) is { } transport
            ? new SingleInstanceGuard(transport, startupLogging.CreateLogger<SingleInstanceGuard>())
            : null;
        if (guard is not null && guard.Start(InstanceCommand.Activate, instanceCommands.Post) != InstanceRole.Primary)
        {
            return 0;
        }

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
            // Only the identity of this process: nothing is registered (the test mode keeps it, so that its windows carry
            // the AppUserModelID the end-to-end tests read back).
            WindowsAppIdentity.ApplyToCurrentProcess();
        }

        return BuildAvaloniaApp(instanceCommands, environment).StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    /// <summary>Designer entry point.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    private static AppBuilder BuildAvaloniaApp(InstanceCommandQueue instanceCommands, AppEnvironment environment) =>
        AppBuilder.Configure(() => new App(instanceCommands, environment))
            .UsePlatformDetect()
            .LogToTrace();
}
