using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

using Velopack;

using LayerPeek.App.Composition;

namespace LayerPeek.App.Installer;

/// <summary>
/// The first thing <c>Program.Main</c> does after the DLL search restriction (T-017, T-052): inside a Velopack installation
/// <c>VelopackApp.Run()</c>, which runs the installer hooks and ends the process for them before the single-instance guard
/// (T-043), without UI and also while the app runs (verified in T-056). Outside an installation Velopack is not created at
/// all: its locator would write a log into <c>%LOCALAPPDATA%\velopack</c> (macOS: <c>~/Library/Logs</c>) on every development,
/// test and <c>.zip</c> start. Windows since T-017; macOS since T-058 (the <c>.app</c> of <c>vpk</c>, without installer hooks:
/// a macOS installation is a bundle copied by the user, Velopack has nothing to install or uninstall there).
/// </summary>
internal static class VelopackStartup
{
    /// <summary>
    /// Runs Velopack for this start. Returns normally for a normal start; the hooks end the process inside Velopack
    /// (exit code 0, or -1 when a hook threw).
    /// </summary>
    /// <param name="failure">
    /// Velopack failed on a normal start: the app goes on without it (no updates in this run); <c>Program.Main</c> logs it
    /// once the log exists.
    /// </param>
    /// <returns>The exit code when this start must end now, <see langword="null"/> to go on with the app.</returns>
    public static int? Run(string[] args, out Exception? failure)
    {
        ArgumentNullException.ThrowIfNull(args);
        failure = null;
        if (OperatingSystem.IsWindows())
        {
            return Run(args, Environment.ProcessPath, File.Exists, RunVelopack, out failure);
        }

        if (OperatingSystem.IsMacOS())
        {
            return Run(args, Environment.ProcessPath, File.Exists, RunVelopackMac, out failure, VelopackInstallation.IsInstalledMac);
        }

        return null;
    }

    /// <param name="executablePath">The running exe.</param>
    /// <param name="fileExists">Whether a file exists.</param>
    /// <param name="runVelopack">Runs Velopack with the arguments (in the app: <c>VelopackApp.Run()</c> with the hooks).</param>
    /// <param name="isInstalled">The installation check of the OS; default: Windows (<see cref="VelopackInstallation.IsInstalled"/>).</param>
    internal static int? Run(
        string[] args,
        string? executablePath,
        Func<string, bool> fileExists,
        Action<string[]> runVelopack,
        out Exception? failure,
        Func<string?, Func<string, bool>, bool>? isInstalled = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(runVelopack);
        failure = null;
        if (!(isInstalled ?? VelopackInstallation.IsInstalled)(executablePath, fileExists))
        {
            // A hook start of a copy that is not installed must neither start the app nor touch the installed app's state.
            return VelopackInstallation.IsHookStart(args) ? 0 : null;
        }

        try
        {
            runVelopack(args);
            return null;
        }
        catch (Exception ex)
        {
            // A hook start reports the failure to the installer; a normal start must never fail because of Velopack.
            failure = ex;
            return VelopackInstallation.IsHookStart(args) ? HookFailedExitCode : null;
        }
    }

    /// <summary>The exit code of a hook start whose Velopack failed (what Velopack itself uses for a failed hook).</summary>
    public const int HookFailedExitCode = -1;

    /// <summary>Lets Velopack log the failures of a hook (it goes on with the install or uninstall either way).</summary>
    /// <exception cref="InvalidOperationException">At least one step failed.</exception>
    internal static void ThrowOnFailures(string hook, IReadOnlyList<string> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        if (failures.Count > 0)
        {
            throw new InvalidOperationException($"LayerPeek {hook} hook: {string.Join(" | ", failures)}");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void RunVelopack(string[] args) =>
        VelopackApp.Build()
            .SetArgs(args)
            // Applying an update ends every process of the installation, a running primary instance too: updates are applied
            // only by the primary instance after it saved its state (AppUpdates), never by a start.
            .SetAutoApplyOnStartup(false)
            .OnAfterInstallFastCallback(_ => RunHook("install", static hooks => hooks.AfterInstall()))
            .OnBeforeUninstallFastCallback(_ => RunHook("uninstall", static hooks => hooks.BeforeUninstall()))
            .Run();

    /// <summary>
    /// macOS (T-058): no hooks (nothing is installed or uninstalled there); the start after an update is a normal start.
    /// Updates are applied only by the primary instance after it saved its state (<see cref="AppUpdates"/>).
    /// </summary>
    [SupportedOSPlatform("macos")]
    private static void RunVelopackMac(string[] args) =>
        VelopackApp.Build()
            .SetArgs(args)
            .SetAutoApplyOnStartup(false)
            .Run();

    /// <summary>
    /// One hook with the production registrations (never the test mode: an installed app is a Release build). What the
    /// components log goes to Velopack's log; only steps that failed make the hook fail (user decision 2026-10-24).
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void RunHook(string hook, Func<InstallerHooks, IReadOnlyList<string>> step)
    {
        using HookLog log = new(WriteToVelopackLog);
        using ILoggerFactory loggers = LoggerFactory.Create(builder => builder.AddProvider(log));
        ThrowOnFailures(hook, step(AppServices.CreateInstallerHooks(loggers)));
    }

    /// <summary>Velopack's own log of the hook start (<c>%LOCALAPPDATA%\velopack\velopack_LayerPeek.log</c>).</summary>
    private static void WriteToVelopackLog(LogLevel level, string message)
    {
        try
        {
            Velopack.Locators.VelopackLocator.Current.Log.Log(
                level >= LogLevel.Error ? Velopack.Logging.VelopackLogLevel.Error
                : level == LogLevel.Warning ? Velopack.Logging.VelopackLogLevel.Warning
                : Velopack.Logging.VelopackLogLevel.Information,
                "LayerPeek: " + message,
                null);
        }
        catch (Exception)
        {
            // Writing the log must never fail the hook.
        }
    }
}

/// <summary>Passes what the components of a hook log (information and above) on: a hook has no log file of its own.</summary>
internal sealed class HookLog(Action<LogLevel, string> write) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new Logger(write);

    public void Dispose()
    {
    }

    private sealed class Logger(Action<LogLevel, string> write) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                write(logLevel, exception is null ? formatter(state, exception) : $"{formatter(state, exception)} {exception.GetType().Name}: {exception.Message}");
            }
        }
    }
}
