using System.Diagnostics;

using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.Tray.Menu;

namespace PrusaTrayDiagnostic.App.Platform;

/// <summary>How the default browser is started.</summary>
public enum UrlLaunchPlatform
{
    /// <summary>Shell execute of the URL (<c>ShellExecuteEx</c>), which opens the registered browser.</summary>
    Windows,

    /// <summary><c>open &lt;url&gt;</c>.</summary>
    MacOS,

    /// <summary><c>xdg-open &lt;url&gt;</c> (not a supported platform, kept so that the app degrades gracefully).</summary>
    Linux,
}

/// <summary>
/// Opens addresses with the OS default browser through <see cref="Process"/> (no reflection, Native AOT safe).
/// Only addresses accepted by <see cref="PrinterWebAddress.IsSafeToLaunch"/> are started, and on macOS/Linux the
/// address is passed as a single argument (no shell), so a crafted printer address cannot run anything else.
/// </summary>
internal sealed partial class ProcessUrlLauncher : IUrlLauncher
{
    private readonly ILogger _logger;
    private readonly UrlLaunchPlatform _platform;
    private readonly Func<ProcessStartInfo, IDisposable?> _start;

    public ProcessUrlLauncher(ILogger<ProcessUrlLauncher> logger)
        : this(logger, CurrentPlatform(), static info => Process.Start(info))
    {
    }

    /// <param name="start">Starts the process; returns its handle (disposed right away) or <see langword="null"/>.</param>
    internal ProcessUrlLauncher(ILogger logger, UrlLaunchPlatform platform, Func<ProcessStartInfo, IDisposable?> start)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(start);
        _logger = logger;
        _platform = platform;
        _start = start;
    }

    public Task<bool> OpenAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!PrinterWebAddress.IsSafeToLaunch(uri))
        {
            LogRejected(uri.IsAbsoluteUri ? uri.Scheme : "relative");
            return Task.FromResult(false);
        }

        ProcessStartInfo info = CreateStartInfo(uri, _platform);
        // Starting a process (ShellExecuteEx on Windows) can take a moment: never on the UI thread.
        return Task.Run(() => Start(info, uri));
    }

    internal static ProcessStartInfo CreateStartInfo(Uri uri, UrlLaunchPlatform platform)
    {
        string address = uri.AbsoluteUri;
        if (platform == UrlLaunchPlatform.Windows)
        {
            return new ProcessStartInfo(address) { UseShellExecute = true };
        }

        ProcessStartInfo info = new(platform == UrlLaunchPlatform.MacOS ? "open" : "xdg-open") { UseShellExecute = false };
        info.ArgumentList.Add(address);
        return info;
    }

    private static UrlLaunchPlatform CurrentPlatform() =>
        OperatingSystem.IsWindows() ? UrlLaunchPlatform.Windows
        : OperatingSystem.IsMacOS() ? UrlLaunchPlatform.MacOS
        : UrlLaunchPlatform.Linux;

    private bool Start(ProcessStartInfo info, Uri uri)
    {
        try
        {
            using IDisposable? process = _start(info);
            return true;
        }
        catch (Exception ex)
        {
            // No browser registered, blocked by policy... The menu stays usable; the address host helps diagnose.
            LogLaunchFailed(ex, uri.Host);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused to open an address that is not a plain http(s) address (scheme '{Scheme}').")]
    private partial void LogRejected(string scheme);

    [LoggerMessage(Level = LogLevel.Error, Message = "Opening the web interface of {Host} in the browser failed.")]
    private partial void LogLaunchFailed(Exception exception, string host);
}
