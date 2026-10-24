using System.Diagnostics;

using Microsoft.Extensions.Logging;

namespace LayerPeek.App.Platform;

/// <summary>Shows a folder in the file manager (T-048: the log folder).</summary>
internal interface IFolderLauncher
{
    /// <summary>
    /// Creates <paramref name="directory"/> when it does not exist yet and opens it in Explorer / Finder. Never throws;
    /// a failure is logged.
    /// </summary>
    /// <returns><see langword="false"/> when the folder could not be created or the file manager not started.</returns>
    Task<bool> OpenAsync(string directory);
}

/// <summary>
/// Starts the file manager by absolute path, never looked up through PATH (T-052, finding L4): <c>explorer.exe</c> from the
/// Windows folder, <c>/usr/bin/open</c> on macOS. The folder is a single argument (no shell).
/// </summary>
internal sealed partial class ProcessFolderLauncher : IFolderLauncher
{
    private readonly ILogger _logger;
    private readonly UrlLaunchPlatform _platform;
    private readonly Func<ProcessStartInfo, IDisposable?> _start;

    public ProcessFolderLauncher(ILogger<ProcessFolderLauncher> logger)
        : this(logger, ProcessUrlLauncher.CurrentPlatform(), static info => Process.Start(info))
    {
    }

    /// <param name="start">Starts the process; returns its handle (disposed right away) or <see langword="null"/>.</param>
    internal ProcessFolderLauncher(ILogger logger, UrlLaunchPlatform platform, Func<ProcessStartInfo, IDisposable?> start)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(start);
        _logger = logger;
        _platform = platform;
        _start = start;
    }

    public Task<bool> OpenAsync(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Path.IsPathFullyQualified(directory))
        {
            throw new ArgumentException("The folder must be an absolute path.", nameof(directory));
        }

        // Creating the folder and starting a process can take a moment: never on the UI thread.
        return Task.Run(() => Open(Path.GetFullPath(directory)));
    }

    /// <param name="directory">Absolute path of the folder.</param>
    /// <param name="windowsDirectory">The Windows folder (tests); default: the one of this system.</param>
    /// <exception cref="InvalidOperationException">The Windows folder is not known.</exception>
    internal static ProcessStartInfo CreateStartInfo(string directory, UrlLaunchPlatform platform, string? windowsDirectory = null)
    {
        string fileManager = platform switch
        {
            UrlLaunchPlatform.Windows => Path.Combine(windowsDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
            UrlLaunchPlatform.MacOS => "/usr/bin/open",
            _ => "/usr/bin/xdg-open",
        };
        if (platform == UrlLaunchPlatform.Windows && !Path.IsPathFullyQualified(fileManager))
        {
            // No Windows folder: a relative name would be looked up next to the app first (T-052, L4).
            throw new InvalidOperationException("The file manager has no absolute path.");
        }

        ProcessStartInfo info = new(fileManager) { UseShellExecute = false };
        info.ArgumentList.Add(directory);
        return info;
    }

    private bool Open(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            _start(CreateStartInfo(directory, _platform))?.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            LogOpenFailed(ex);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Opening the log folder failed.")]
    private partial void LogOpenFailed(Exception exception);
}
