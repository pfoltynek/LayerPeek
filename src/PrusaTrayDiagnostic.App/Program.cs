using Avalonia;

using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.Platform.Windows;
using PrusaTrayDiagnostic.App.SingleInstance;

namespace PrusaTrayDiagnostic.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // The guard runs before anything else: a later start only passes "activate" on and exits (T-043).
        // No log providers yet (file logging follows in T-024), like the loggers of the app itself.
        using ILoggerFactory startupLogging = LoggerFactory.Create(static _ => { });
        InstanceCommandQueue instanceCommands = new();
        using SingleInstanceGuard? guard = InstanceTransports.TryCreateForCurrentPlatform(InstanceTransports.NameSuffix) is { } transport
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
            WindowsAppIdentity.ApplyToCurrentProcess();
        }

        return BuildAvaloniaApp(instanceCommands).StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    /// <summary>Designer entry point.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    private static AppBuilder BuildAvaloniaApp(InstanceCommandQueue instanceCommands) =>
        AppBuilder.Configure(() => new App(instanceCommands))
            .UsePlatformDetect()
            .LogToTrace();
}
