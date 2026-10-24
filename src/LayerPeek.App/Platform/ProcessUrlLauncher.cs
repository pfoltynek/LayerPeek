using System.ComponentModel;
using System.Diagnostics;

using Microsoft.Extensions.Logging;

using LayerPeek.App.Tray.Menu;

namespace LayerPeek.App.Platform;

/// <summary>How the default browser is started.</summary>
public enum UrlLaunchPlatform
{
    /// <summary>Shell execute of the URL (<c>ShellExecuteEx</c>), which opens the registered browser.</summary>
    Windows,

    /// <summary><c>/usr/bin/open &lt;url&gt;</c>.</summary>
    MacOS,

    /// <summary><c>/usr/bin/xdg-open &lt;url&gt;</c> (not a supported platform, kept so that the app degrades gracefully).</summary>
    Linux,
}

/// <summary>
/// Opens addresses with the OS default browser through <see cref="Process"/> (no reflection, Native AOT safe).
/// Only addresses accepted by <see cref="PrinterWebAddress.IsSafeToLaunch"/> are started, and on macOS/Linux the
/// address is passed as a single argument (no shell), so a crafted printer address cannot run anything else. Secret
/// addresses (the local web proxy with its access token, T-028) never reach the log.
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

        ProcessStartInfo info = CreateStartInfo(uri.AbsoluteUri, _platform);
        // Starting a process (ShellExecuteEx on Windows) can take a moment: never on the UI thread.
        return Task.Run(() => Start(info, uri.Host, secret: false));
    }

    public Task<bool> OpenSecretAsync(Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!PrinterWebAddress.IsSafeToLaunch(uri))
        {
            // Only the scheme: the rest of the address may be the secret.
            LogRejected(uri.IsAbsoluteUri ? uri.Scheme : "relative");
            return Task.FromResult(false);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }

        ProcessStartInfo info = CreateStartInfo(uri.AbsoluteUri, _platform);
        return Task.Run(() => !cancellationToken.IsCancellationRequested && Start(info, uri.Host, secret: true), CancellationToken.None);
    }

    /// <param name="address">Already escaped address, passed to the OS as is.</param>
    internal static ProcessStartInfo CreateStartInfo(string address, UrlLaunchPlatform platform)
    {
        if (platform == UrlLaunchPlatform.Windows)
        {
            return new ProcessStartInfo(address) { UseShellExecute = true };
        }

        // By absolute path, never looked up through PATH: a planted tool of the same name would receive the address, for the
        // local web proxy including its access token (T-052, finding L4).
        ProcessStartInfo info = new(platform == UrlLaunchPlatform.MacOS ? "/usr/bin/open" : "/usr/bin/xdg-open") { UseShellExecute = false };
        info.ArgumentList.Add(address);
        return info;
    }

    internal static UrlLaunchPlatform CurrentPlatform() =>
        OperatingSystem.IsWindows() ? UrlLaunchPlatform.Windows
        : OperatingSystem.IsMacOS() ? UrlLaunchPlatform.MacOS
        : UrlLaunchPlatform.Linux;

    private bool Start(ProcessStartInfo info, string host, bool secret)
    {
        try
        {
            using IDisposable? process = _start(info);
            return true;
        }
        catch (Exception ex)
        {
            // No browser registered, blocked by policy... The menu stays usable; the address host helps diagnose.
            if (secret)
            {
                // Process.Start puts the file name (on Windows the whole address, access token included) into the exception
                // message, so neither the exception nor its message may reach the log.
                LogSecretLaunchFailed(host, ex.GetType().Name, ex is Win32Exception win32 ? win32.NativeErrorCode : ex.HResult);
            }
            else
            {
                LogLaunchFailed(ex, host);
            }

            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused to open an address that is not a plain http(s) address (scheme '{Scheme}').")]
    private partial void LogRejected(string scheme);

    [LoggerMessage(Level = LogLevel.Error, Message = "Opening the web interface of {Host} in the browser failed.")]
    private partial void LogLaunchFailed(Exception exception, string host);

    [LoggerMessage(Level = LogLevel.Error, Message = "Opening the logged-in web interface on {Host} failed ({ExceptionType}, error {ErrorCode}).")]
    private partial void LogSecretLaunchFailed(string host, string exceptionType, int errorCode);
}
