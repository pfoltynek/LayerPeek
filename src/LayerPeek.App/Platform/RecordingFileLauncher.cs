#if DEBUG
using System.Text;

using Microsoft.Extensions.Logging;

namespace LayerPeek.App.Platform;

/// <summary>
/// Debug builds only: the <see cref="IFileLauncher"/> of the isolated test mode (T-210, user decision 2026-11-13: checking
/// that the app hands the right file to the system is enough, QuickTime Player is never started). Every recording that
/// would be opened is appended to <see cref="FileName"/> in the test data folder as its full path, one line per file, each
/// line written at once, so that a check reads complete lines only. The same files are refused as by
/// <see cref="ProcessFileLauncher"/>; no path reaches the log (it may hold the user name).
/// </summary>
internal sealed partial class RecordingFileLauncher : IFileLauncher
{
    /// <summary>The file in the test data folder.</summary>
    public const string FileName = "opened-files.txt";

    private readonly string _filePath;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();

    /// <param name="dataDirectory">The test data folder; created with the first file.</param>
    public RecordingFileLauncher(string dataDirectory, ILogger<RecordingFileLauncher> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(logger);
        _filePath = Path.Combine(dataDirectory, FileName);
        _logger = logger;
    }

    /// <summary>The file the paths go to.</summary>
    public string FilePath => _filePath;

    public Task<bool> OpenAsync(string file)
    {
        ArgumentNullException.ThrowIfNull(file);
        // Like the real launcher: checking the file can take a moment, never on the UI thread.
        return Task.Run(() => Record(file));
    }

    private bool Record(string file)
    {
        if (!ProcessFileLauncher.IsAllowed(file))
        {
            LogRefused();
            return false;
        }

        string path = Path.GetFullPath(file);
        if (!File.Exists(path))
        {
            LogMissing();
            return false;
        }

        byte[] line = Encoding.UTF8.GetBytes(path + "\n");
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                // One write of the whole line: a reader sees either nothing or the complete line.
                using FileStream stream = new(_filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                stream.Write(line);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogRecordFailed(ex.GetType().Name);
            return false;
        }

        LogRecorded();
        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused to open a file that is not a recording.")]
    private partial void LogRefused();

    [LoggerMessage(Level = LogLevel.Information, Message = "The recording to open does not exist any more.")]
    private partial void LogMissing();

    [LoggerMessage(Level = LogLevel.Information, Message = "Test mode: a recording was recorded instead of opening it in its default app.")]
    private partial void LogRecorded();

    [LoggerMessage(Level = LogLevel.Error, Message = "Test mode: recording the file to open failed ({ExceptionType}).")]
    private partial void LogRecordFailed(string exceptionType);
}
#endif
