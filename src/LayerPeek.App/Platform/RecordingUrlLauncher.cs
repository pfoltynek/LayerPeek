#if DEBUG
using System.Text;

using Microsoft.Extensions.Logging;

using LayerPeek.App.Tray.Menu;

namespace LayerPeek.App.Platform;

/// <summary>
/// Debug builds only: the <see cref="IUrlLauncher"/> of the isolated test mode (T-067, user decision 2026-10-22). No
/// browser is started; every address that would be opened is appended to <see cref="FileName"/> in the test data folder,
/// one line per address, each line written at once, so that a test reads complete lines only and opens the address itself.
/// The same addresses are refused as by <see cref="ProcessUrlLauncher"/>; no address (the local web proxy carries its
/// access token, T-028) ever reaches the log.
/// </summary>
internal sealed partial class RecordingUrlLauncher : IUrlLauncher
{
    /// <summary>The file in the test data folder.</summary>
    public const string FileName = "opened-urls.txt";

    private readonly string _filePath;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();

    /// <param name="dataDirectory">The test data folder; created with the first address.</param>
    public RecordingUrlLauncher(string dataDirectory, ILogger<RecordingUrlLauncher> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(logger);
        _filePath = Path.Combine(dataDirectory, FileName);
        _logger = logger;
    }

    /// <summary>The file the addresses go to.</summary>
    public string FilePath => _filePath;

    public Task<bool> OpenAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return Task.FromResult(Record(uri));
    }

    public Task<bool> OpenSecretAsync(Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return Task.FromResult(!cancellationToken.IsCancellationRequested && Record(uri));
    }

    private bool Record(Uri uri)
    {
        if (!PrinterWebAddress.IsSafeToLaunch(uri))
        {
            LogRejected(uri.IsAbsoluteUri ? uri.Scheme : "relative");
            return false;
        }

        byte[] line = Encoding.UTF8.GetBytes(uri.AbsoluteUri + "\n");
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                // One write of the whole line: a reader sees either nothing or the complete line.
                using FileStream file = new(_filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                file.Write(line);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only the type: the message holds the path, not the address, but stays out like everywhere else here.
            LogRecordFailed(uri.Host, ex.GetType().Name);
            return false;
        }

        LogRecorded(uri.Host);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused to open an address that is not a plain http(s) address (scheme '{Scheme}').")]
    private partial void LogRejected(string scheme);

    [LoggerMessage(Level = LogLevel.Information, Message = "Test mode: an address on {Host} was recorded instead of opening the browser.")]
    private partial void LogRecorded(string host);

    [LoggerMessage(Level = LogLevel.Error, Message = "Test mode: recording an address on {Host} failed ({ExceptionType}).")]
    private partial void LogRecordFailed(string host, string exceptionType);
}
#endif
