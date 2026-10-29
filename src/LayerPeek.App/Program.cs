using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Avalonia;
using Avalonia.Threading;

using Microsoft.Extensions.Logging;

using LayerPeek.App.Composition;
using LayerPeek.App.Installer;
using LayerPeek.App.Logging;
using LayerPeek.App.Notifications;
using LayerPeek.App.Platform;
using LayerPeek.App.Platform.Windows;
using LayerPeek.App.SingleInstance;
using LayerPeek.App.Windows;

namespace LayerPeek.App;

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

        // Right after (T-017): inside a Velopack installation its hooks (install, uninstall) run and end the process here,
        // before the single-instance guard, the log and any window, also while the app runs. Outside one nothing happens.
        if (VelopackStartup.Run(args, out Exception? velopackFailure) is { } installerExit)
        {
            return installerExit;
        }

#if DEBUG
        if (args.Contains(AppEnvironment.ClearToastsArgument, StringComparer.Ordinal))
        {
            // After a crashed test run (T-067, T-068): only the toasts and the shortcut of the test app go, nothing else
            // starts (no log, no single-instance guard, no Avalonia), so it never meets a running instance. macOS (T-020): the
            // notifications of the test bundle, only when this is the test bundle.
            if (OperatingSystem.IsMacOS())
            {
                return Platform.MacOS.TestNotificationCleanup.RunFromCommandLine(Console.Error);
            }

            if (!OperatingSystem.IsWindows())
            {
                Console.Error.WriteLine("Toasts of the test mode exist on Windows and macOS only.");
                return 1;
            }

            return TestToastCleanup.RunFromCommandLine(Console.Error);
        }

        if (args.Contains(AppEnvironment.NotificationStatusArgument, StringComparer.Ordinal))
        {
            // The end-to-end test of macOS notifications (T-020): permission and delivered notifications of the test bundle,
            // without asking anything; nothing else starts.
            if (!OperatingSystem.IsMacOS())
            {
                Console.Error.WriteLine("The notification status exists on macOS only.");
                return 1;
            }

            return Platform.MacOS.TestNotificationCleanup.StatusFromCommandLine(Console.Out, Console.Error);
        }

        if (args.Contains(AppEnvironment.ClearAutostartArgument, StringComparer.Ordinal))
        {
            // After a crashed test run (T-019): only the autostart stand-ins in the test data folder go; never the real Login
            // Item or Startup shortcut, and nothing else starts (no log, no single-instance guard, no Avalonia).
            return Platform.TestAutostartCleanup.RunFromCommandLine(Environment.GetEnvironmentVariable, Console.Error);
        }

        // A start by Windows for a click on a test toast (T-068) carries the test switches on its command line: they are set
        // before anything reads them, and an invalid one refuses the start rather than starting the production app.
        bool testActivation = ToastActivatorCommandLine.HasTestEnvironment(args);
        if (testActivation && !ToastActivatorCommandLine.TryApplyTestEnvironment(args, Environment.SetEnvironmentVariable))
        {
            return AppEnvironment.InvalidTestModeExitCode;
        }

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

        if (!environment.IsIsolated && AppEnvironment.IsTestStart(args, Environment.GetEnvironmentVariable))
        {
            // A start by a test (run marker, forward-only switch) without its test data folder: never the production mode,
            // so the user's log folder, instance names and settings are never touched by it.
            return AppEnvironment.InvalidTestModeExitCode;
        }

        if (testActivation && (!environment.IsIsolated || !Directory.Exists(environment.DataDirectory())))
        {
            // A toast of a test run that is over: never a production start, never a new test folder.
            return AppEnvironment.InvalidTestModeExitCode;
        }
#else
        AppEnvironment environment = AppEnvironment.Production;
#endif

        // One logger factory for the whole process (T-024), disposed last: a second start writes its records into the same
        // file before it exits, and the app's records are written even when the OS ends the session.
        using AppLogging logging = AppLogging.Create(environment);
        // Right after the logging exists: a crash leaves its stack trace in the log (T-046), also in a second start.
        UnhandledExceptionLogger crashes = new(logging.Factory, logging.Flush);
        crashes.AttachToProcess();
        ILogger log = logging.Factory.CreateLogger(typeof(Program));
        string version = AboutViewModel.ProductVersion(typeof(Program).Assembly);
        LogStarted(log, version, RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture);
        if (OperatingSystem.IsWindows() && dllSearchError != 0)
        {
            // The app still works with the standard search order; only the hardening is missing.
            NativeLibrarySearch.LogRestrictionFailed(log, dllSearchError);
        }

        if (velopackFailure is not null)
        {
            // The app runs without Velopack in this start (T-017): no updates, nothing else is missing.
            LogVelopackFailed(log, velopackFailure);
        }

#if DEBUG
        foreach (string ignored in environment.IgnoredTestSwitches)
        {
            // Never a reason to fail: without the test data folder this is a normal start of the developer.
            LogIgnoredTestSwitch(log, ignored);
        }
#endif

        // Clicks on toasts in the notification center reach the app through its COM toast activator (T-068, Windows only;
        // none in the test mode without toasts).
        ToastActivationInbox? toastClicks = OperatingSystem.IsWindows() && environment.ToastActivatorClsid is not null
            ? new ToastActivationInbox(environment.AppUserModelId, logging.Factory.CreateLogger<ToastActivationInbox>())
            : null;
        ToastActivatorServer? toastActivator = null;
        try
        {
            // The guard runs before the app starts: a later start only passes "activate" on and exits (T-043).
            InstanceCommand? passOn = InstanceCommand.Activate;
            if (OperatingSystem.IsWindows() && ToastActivatorCommandLine.IsActivationStart(args))
            {
                // Windows started the app for a click (T-068): its activator serves first, to learn the click, which then
                // goes to the running instance (like a second start) or is handled here once the app runs.
                toastActivator = StartToastActivator(environment, toastClicks, log);
                passOn = toastClicks?.WaitForFirst(ToastActivationWait) is { } click && InstanceCommand.TryForToast(click, out InstanceCommand toast)
                    ? toast
                    : null;
                LogStartedForToast(log, passOn is not null);
            }

            InstanceCommandQueue instanceCommands = new();
            using SingleInstanceGuard? guard = InstanceTransports.TryCreateForCurrentPlatform(InstanceTransports.NameSuffix, environment) is { } transport
                ? new SingleInstanceGuard(transport, logging.Factory.CreateLogger<SingleInstanceGuard>())
                : null;
            if (guard is not null && guard.Start(passOn, instanceCommands.Post) != InstanceRole.Primary)
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
                if (toastClicks is not null)
                {
                    // Clicks (also the one this start was made for) wait in the command queue until the app runs.
                    toastActivator ??= StartToastActivator(environment, toastClicks, log);
                    toastClicks.Attach(click => PostToastClick(instanceCommands, click, log));
                }
            }

            if (OperatingSystem.IsMacOS() && !WaitForMacDisplay(logging.Factory))
            {
                // Asked to quit while every display slept (T-096): nothing of the UI has started.
                return 0;
            }

            if (OperatingSystem.IsMacOS())
            {
                // Before AppKit finishes launching (T-020): the delegate of the notification center must be set by then, so
                // that a click that started the app reaches it too. Clicks wait in the command queue until the app runs.
                OpenMacNotifications(environment, args, instanceCommands, logging.Factory, log);
            }

            return BuildAvaloniaApp(instanceCommands, environment, logging, crashes).StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
        }
        finally
        {
            // Windows starts the app again for a later click.
            if (OperatingSystem.IsWindows())
            {
                toastActivator?.Dispose();
            }
        }
    }

    /// <summary>How long a start by Windows waits for the click it was made for (Windows reports it right after the activator is registered).</summary>
    private static readonly TimeSpan ToastActivationWait = TimeSpan.FromSeconds(10);

    /// <summary>The COM toast activator of this run (T-068), or <see langword="null"/> without one or when it cannot be registered.</summary>
    [SupportedOSPlatform("windows")]
    private static ToastActivatorServer? StartToastActivator(AppEnvironment environment, ToastActivationInbox? clicks, ILogger log)
    {
        if (clicks is null || environment.ToastActivatorClsid is not { } clsid)
        {
            return null;
        }

        try
        {
            return ToastActivatorServer.Start(clsid, clicks.Receive);
        }
        catch (Exception ex)
        {
            // The app runs; clicks in the notification center do not reach it.
            LogToastActivatorFailed(log, ex);
            return null;
        }
    }

    /// <summary>
    /// Waits until a display is active (T-096): Avalonia.Native cannot start its render timer while every display sleeps and
    /// would end the process. Only the primary instance waits (a later start forwards to it), before the notification center
    /// and AppKit. SIGTERM and SIGINT end the wait (e.g. logout or <c>kill</c> before the display woke).
    /// </summary>
    /// <returns><see langword="false"/> when the process was asked to quit meanwhile.</returns>
    [SupportedOSPlatform("macos")]
    private static bool WaitForMacDisplay(ILoggerFactory loggers)
    {
        using CancellationTokenSource quit = new();
        void OnQuitSignal(PosixSignalContext context)
        {
            // Handled here: the wait ends and Main returns, so the log is flushed and the single-instance guard released.
            context.Cancel = true;
            quit.Cancel();
        }

        using PosixSignalRegistration terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnQuitSignal);
        using PosixSignalRegistration interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, OnQuitSignal);
        StartupDisplayWait wait = new(new Platform.MacOS.MacActiveDisplays(), StartupDisplayWait.BlockingPause, loggers.CreateLogger<StartupDisplayWait>());
        return wait.WaitForDisplay(quit.Token) != StartupDisplayWaitResult.Cancelled;
    }

    /// <summary>
    /// The notification center of macOS for this run (T-020), only as the bundle the environment allows
    /// (<see cref="AppEnvironment.NotificationBundleIdentifier"/>); published for the composition root. Without it (a
    /// development build, another bundle, the test mode without notifications) the app shows no notifications. Never throws.
    /// </summary>
    [SupportedOSPlatform("macos")]
    private static void OpenMacNotifications(AppEnvironment environment, string[] args, InstanceCommandQueue commands, ILoggerFactory loggers, ILogger log)
    {
        if (AppEnvironment.SkipsNotificationCenter(args))
        {
            // The AOT smoke test (user decision 2026-10-29): the notification center is not touched at all in this run.
            LogNoMacNotifications(log, $"started with {AppEnvironment.SkipNotificationCenterArgument}");
            return;
        }

        try
        {
            Platform.MacOS.MacUserNotificationCenter? center = Platform.MacOS.MacUserNotificationCenter.TryOpen(
                environment.NotificationBundleIdentifierFor(args),
                Platform.MacOS.MacMainBundle.Identifier,
                loggers.CreateLogger<Platform.MacOS.MacUserNotificationCenter>(),
                out string? reason);
            if (center is null)
            {
                LogNoMacNotifications(log, reason ?? "unknown");
                return;
            }

            center.InstallClickHandler(click =>
            {
                if (click is null)
                {
                    LogUnknownToastClick(log);
                    return;
                }

                PostToastClick(commands, click, log);
            });
            center.Publish();
        }
        catch (Exception ex)
        {
            // The app runs without notifications.
            LogMacNotificationsFailed(log, ex);
        }
    }

    private static void PostToastClick(InstanceCommandQueue commands, string click, ILogger log)
    {
        if (InstanceCommand.TryForToast(click, out InstanceCommand toast))
        {
            commands.Post(toast);
        }
        else
        {
            // Not an argument of the app's own toasts; the argument itself is not logged.
            LogUnknownToastClick(log);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Started by Windows for a click on a notification (click received: {Received}).")]
    private static partial void LogStartedForToast(ILogger logger, bool received);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Registering the toast activator failed; clicks on notifications in the notification center will not reach the app.")]
    private static partial void LogToastActivatorFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignored a click on a notification with an unrecognized argument.")]
    private static partial void LogUnknownToastClick(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "No system notifications in this run: {Reason}.")]
    private static partial void LogNoMacNotifications(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Opening the notification center of macOS failed; notifications will not be shown.")]
    private static partial void LogMacNotificationsFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "LayerPeek {Version} starting on {OperatingSystem} ({Architecture}).")]
    private static partial void LogStarted(ILogger logger, string version, string operatingSystem, Architecture architecture);

    [LoggerMessage(Level = LogLevel.Information, Message = "Another instance is running; this start only passed the activation on and exits.")]
    private static partial void LogNotPrimary(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Velopack failed at startup; the app runs without updates in this start.")]
    private static partial void LogVelopackFailed(ILogger logger, Exception exception);

#if DEBUG
    [LoggerMessage(Level = LogLevel.Warning, Message = "The test switch {Name} is ignored outside the test mode.")]
    private static partial void LogIgnoredTestSwitch(ILogger logger, string name);
#endif

    /// <summary>Designer entry point.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    private static AppBuilder BuildAvaloniaApp(InstanceCommandQueue instanceCommands, AppEnvironment environment, AppLogging logging, UnhandledExceptionLogger crashes) =>
        AppBuilder.Configure(() => new App(instanceCommands, environment, logging))
            .UsePlatformDetect()
            // macOS (T-019): a menu bar app without a Dock icon. Info.plist of LayerPeek.app has LSUIElement for the start,
            // but Avalonia sets its own activation policy when it starts (ShowInDock true = a regular app with a Dock icon), so
            // the accessory policy is set here too; it also keeps a development build out of the Dock.
            .With(new MacOSPlatformOptions { ShowInDock = false })
            .LogToTrace()
            // The UI thread's dispatcher exists once the platform is set up, before the app starts.
            .AfterSetup(_ => crashes.AttachToDispatcher(Dispatcher.UIThread));
}
