using System.ComponentModel;
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
/// address is passed as a single argument (no shell), so a crafted printer address cannot run anything else. Addresses
/// with credentials (T-028) are built here from a safe base address, and they never reach the log.
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
        return Task.Run(() => Start(info, uri.Host, containsCredentials: false));
    }

    public Task<bool> OpenWithCredentialsAsync(Uri baseUri, string userName, string password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentException.ThrowIfNullOrEmpty(userName);
        ArgumentException.ThrowIfNullOrEmpty(password);
        // The same check as for plain addresses: userinfo may only come from the stored credentials, never from the address.
        if (!PrinterWebAddress.IsSafeToLaunch(baseUri))
        {
            LogRejected(baseUri.IsAbsoluteUri ? baseUri.Scheme : "relative");
            return Task.FromResult(false);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }

        ProcessStartInfo info = CreateStartInfo(PrinterWebAddress.WithCredentials(baseUri, userName, password), _platform);
        return Task.Run(() => !cancellationToken.IsCancellationRequested && Start(info, baseUri.Host, containsCredentials: true), CancellationToken.None);
    }

    /// <param name="address">Already escaped address, passed to the OS as is.</param>
    internal static ProcessStartInfo CreateStartInfo(string address, UrlLaunchPlatform platform)
    {
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

    private bool Start(ProcessStartInfo info, string host, bool containsCredentials)
    {
        try
        {
            using IDisposable? process = _start(info);
            return true;
        }
        catch (Exception ex)
        {
            // No browser registered, blocked by policy... The menu stays usable; the address host helps diagnose.
            if (containsCredentials)
            {
                // Process.Start puts the file name (on Windows the whole address, password included) into the exception
                // message, so neither the exception nor its message may reach the log.
                LogLaunchWithCredentialsFailed(host, ex.GetType().Name, ex is Win32Exception win32 ? win32.NativeErrorCode : ex.HResult);
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Opening the web interface of {Host} logged in failed ({ExceptionType}, error {ErrorCode}).")]
    private partial void LogLaunchWithCredentialsFailed(string host, string exceptionType, int errorCode);
}
