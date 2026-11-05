using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

using LayerPeek.Core.Cameras.Recording;

namespace LayerPeek.Core.Cameras.Timelapse;

/// <summary>
/// Exports a finished timelapse (T-158, MP4 of key frames) as a sequence of JPEG files (T-165, user decisions 2026-11-04):
/// only on demand, from any older timelapse file; nothing runs during a print.
/// <list type="bullet">
/// <item>The frames go into a folder next to the timelapse (<see cref="CameraRecordingFolders.JpegExportFolderName"/>,
/// <c>-2</c>, <c>-3</c> … when taken), numbered in order (<c>0001.jpg</c> …), at JPEG quality <see cref="DefaultQuality"/>,
/// in the size of the video. In a printer folder of the recordings it counts in their size limit and rotation.</item>
/// <item>The folder is written under a temporary name (<see cref="CameraRecordingFolders.UnfinishedJpegExportFolderName"/>)
/// and renamed when every frame is written: a cancelled or failed export leaves nothing (the temporary folder is deleted;
/// one left by a crash is removed by the rotation).</item>
/// <item>One export at a time (user decision 2026-11-04: a second <see cref="Start"/> while one runs is refused); the
/// timelapse is read twice, first only its packets to count the frames (no decoding, for the progress and the width of
/// the numbers), then decoded frame by frame into one reused buffer and encoded by one reused encoder.</item>
/// <item>A damaged end of the file (a timelapse cut off by a crash) ends the export with the frames before it; a file that
/// gives no frame fails. Nothing of it ever throws out of the exporter.</item>
/// </list>
/// <para>
/// Idle costs nothing: no thread, task or buffer exists between exports. The work runs on the thread pool and on the
/// decoder's own thread. Thread-safe; <see cref="Changed"/> and <see cref="Completed"/> come on any thread (subscribers
/// marshal themselves; a failing subscriber is logged).
/// </para>
/// </summary>
public sealed partial class CameraTimelapseJpegExporter : IAsyncDisposable, IDisposable
{
    /// <summary>JPEG quality of the frames (user decision 2026-11-04: about 85).</summary>
    public const int DefaultQuality = 85;

    /// <summary>Initial size of the reused JPEG buffer (a 720p frame of the Buddy3D is ~100–200 kB at quality 85).</summary>
    private const int InitialJpegCapacity = 256 * 1024;

    private readonly ICameraFeedFactory _feeds;
    private readonly ICameraJpegSequenceEncoderFactory _encoders;
    private readonly ICameraRecordingFileGuard? _guard;
    private readonly ILogger<CameraTimelapseJpegExporter> _logger;
    private readonly int _quality;
    private readonly Lock _gate = new();

    // Guarded by _gate.
    private Export? _current;
    private bool _disposed;

    /// <param name="feeds">Reads the timelapse file (FFmpeg: <c>file://</c> addresses).</param>
    /// <param name="encoders">Encodes the frames as JPEG.</param>
    /// <param name="guard">Keeps the timelapse and the folder being written away from the rotation; <see langword="null"/>: nothing is held.</param>
    /// <param name="quality">JPEG quality, 1–100.</param>
    public CameraTimelapseJpegExporter(
        ICameraFeedFactory feeds,
        ICameraJpegSequenceEncoderFactory encoders,
        ILogger<CameraTimelapseJpegExporter> logger,
        ICameraRecordingFileGuard? guard = null,
        int quality = DefaultQuality)
    {
        ArgumentNullException.ThrowIfNull(feeds);
        ArgumentNullException.ThrowIfNull(encoders);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThan(quality, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quality, 100);
        _feeds = feeds;
        _encoders = encoders;
        _logger = logger;
        _guard = guard;
        _quality = quality;
    }

    /// <summary>
    /// An export started, made progress (at most once per whole percent) or ended; read it with <see cref="Status"/>.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>An export ended: done, cancelled or failed. Raised after <see cref="Status"/> is idle again.</summary>
    public event EventHandler<CameraTimelapseExportResult>? Completed;

    /// <summary>What runs now; <see cref="CameraTimelapseExportStatus.Idle"/> between exports.</summary>
    public CameraTimelapseExportStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _current is { } export
                    ? new CameraTimelapseExportStatus(true, export.PrinterId, export.SourceFile, Volatile.Read(ref export.FramesDone), Volatile.Read(ref export.FramesTotal))
                    : CameraTimelapseExportStatus.Idle;
            }
        }
    }

    /// <summary>The running export's work (for tests); <see langword="null"/> when none runs.</summary>
    internal Task? Running
    {
        get
        {
            lock (_gate)
            {
                return _current?.Done.Task;
            }
        }
    }

    /// <summary>
    /// Exports <paramref name="timelapseFile"/> in the background; <see cref="Completed"/> tells how it ended. Never throws
    /// for the file (a wrong or damaged file ends the export with its problem).
    /// </summary>
    /// <param name="printerId">The printer whose menu starts it (for the notification); <see langword="null"/>: none.</param>
    /// <returns><see langword="false"/> when an export runs already (it goes on) or the exporter is disposed.</returns>
    public bool Start(string timelapseFile, string? printerId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timelapseFile);
        Export export;
        lock (_gate)
        {
            if (_disposed || _current is not null)
            {
                return false;
            }

            export = new Export(timelapseFile, printerId);
            _current = export;
        }

        // Announced before the work can report anything.
        RaiseChanged();
        _ = Task.Run(() => RunAsync(export), CancellationToken.None);
        return true;
    }

    /// <summary>Cancels the running export, if any; it ends soon with <see cref="CameraTimelapseExportProblem.Cancelled"/>.</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            _current?.Cancellation.Cancel();
        }
    }

    /// <summary>Cancels a running export and waits until it cleaned up.</summary>
    public async ValueTask DisposeAsync()
    {
        Task? running;
        lock (_gate)
        {
            _disposed = true;
            _current?.Cancellation.Cancel();
            running = _current?.Done.Task;
        }

        if (running is not null)
        {
            await running.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    /// <summary>Cancels a running export without waiting (it cleans up in the background).</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _current?.Cancellation.Cancel();
        }
    }

    /// <summary>
    /// The address of a local file for <see cref="ICameraFeedFactory.Create"/>, whose <see cref="Uri.LocalPath"/> is the
    /// path again (also with <c>#</c>, <c>%</c> or spaces in a print title); <see langword="null"/> when there is none.
    /// </summary>
    internal static Uri? FileUri(string fullPath)
    {
        if (Uri.TryCreate(fullPath, UriKind.Absolute, out Uri? implicitFile) && implicitFile.IsFile
            && string.Equals(implicitFile.LocalPath, fullPath, StringComparison.Ordinal))
        {
            return implicitFile;
        }

        try
        {
            Uri built = new UriBuilder(Uri.UriSchemeFile, string.Empty) { Path = fullPath }.Uri;
            if (string.Equals(built.LocalPath, fullPath, StringComparison.Ordinal))
            {
                return built;
            }

            // A '#' together with a valid "%XX" in the path (e.g. a printer folder "Printer #1 %20"): both attempts above
            // unescape the "%XX". Every segment escaped on its own (also '%' and '#') gives exactly the path back.
            return EscapedFileUri(fullPath) is { } escaped && string.Equals(escaped.LocalPath, fullPath, StringComparison.Ordinal)
                ? escaped
                : null;
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// <c>file://</c> with every segment of <paramref name="fullPath"/> percent-escaped (a Windows drive or UNC host kept
    /// as it is); <see langword="null"/> for a path of another form.
    /// </summary>
    /// <exception cref="UriFormatException">The escaped address is still no valid URI.</exception>
    private static Uri? EscapedFileUri(string fullPath)
    {
        string path = Path.DirectorySeparatorChar == '\\' ? fullPath.Replace('\\', '/') : fullPath;
        string prefix;
        string rest;
        if (path.StartsWith("//", StringComparison.Ordinal))
        {
            // UNC: file://host/share/…
            int slash = path.IndexOf('/', 2);
            if (slash < 0)
            {
                return null;
            }

            prefix = "file:" + path[..slash];
            rest = path[slash..];
        }
        else if (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '/')
        {
            // Windows drive: file:///C:/…
            prefix = "file:///" + path[..2];
            rest = path[2..];
        }
        else if (path.StartsWith('/'))
        {
            prefix = "file://";
            rest = path;
        }
        else
        {
            return null;
        }

        return new Uri(prefix + string.Join('/', rest.Split('/').Select(Uri.EscapeDataString)), UriKind.Absolute);
    }

    private async Task RunAsync(Export export)
    {
        CameraTimelapseExportResult result;
        try
        {
            result = await ExportAsync(export, export.Cancellation.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Defensive: ExportAsync turns every failure into a result.
            LogUnexpectedFailure(ex);
            result = new CameraTimelapseExportResult(export.PrinterId, export.SourceFile, null, 0, CameraTimelapseExportProblem.Failed);
        }

        lock (_gate)
        {
            _current = null;
        }

        // Nobody can cancel it any more (Cancel only reaches the current export, under the lock).
        export.Cancellation.Dispose();
        if (result.Succeeded)
        {
            string folderName = Path.GetFileName(result.OutputFolder) ?? "";
            LogExported(result.Frames, folderName);
        }
        else
        {
            string fileName = Path.GetFileName(export.SourceFile);
            LogNotExported(result.Problem, fileName);
        }

        RaiseChanged();
        try
        {
            Completed?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            LogSubscriberFailed(ex);
        }
        finally
        {
            export.Done.TrySetResult();
        }
    }

    private async Task<CameraTimelapseExportResult> ExportAsync(Export export, CancellationToken cancellationToken)
    {
        string source;
        try
        {
            source = Path.GetFullPath(export.SourceFile);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return export.Fail(CameraTimelapseExportProblem.Unreadable);
        }

        string fileName = Path.GetFileName(source);
        string? folder = Path.GetDirectoryName(source);
        if (!CameraRecordingFolders.IsTimelapseFileName(fileName))
        {
            return export.Fail(CameraTimelapseExportProblem.NotATimelapse);
        }

        if (folder is null || !File.Exists(source) || FileUri(source) is not { } uri)
        {
            return export.Fail(CameraTimelapseExportProblem.Unreadable);
        }

        ICameraFeed counter;
        try
        {
            counter = _feeds.Create(uri);
        }
        catch (CameraFeedException ex)
        {
            // Creating a feed opens nothing: it fails only without the decoder.
            LogReadFailed(ex.Message);
            return export.Fail(CameraTimelapseExportProblem.Unavailable);
        }

        string finalName = CameraRecordingFolders.JpegExportFolderName(fileName);
        string partial = Path.Join(folder, CameraRecordingFolders.UnfinishedJpegExportFolderName(finalName));
        IDisposable? hold = _guard?.Hold([source, partial]);
        bool keep = false;
        try
        {
            int total = await CountFramesAsync(counter, cancellationToken).ConfigureAwait(false);
            if (total == 0)
            {
                return export.Fail(CameraTimelapseExportProblem.Unreadable);
            }

            Volatile.Write(ref export.FramesTotal, total);
            RaiseChanged();

            PrepareFolder(partial);
            int written = await WriteFramesAsync(export, uri, partial, total, cancellationToken).ConfigureAwait(false);
            if (written == 0)
            {
                return export.Fail(CameraTimelapseExportProblem.Unreadable);
            }

            string output = Publish(folder, partial, finalName);
            keep = true;
            return new CameraTimelapseExportResult(export.PrinterId, source, output, written, CameraTimelapseExportProblem.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return export.Fail(CameraTimelapseExportProblem.Cancelled, export.Written);
        }
        catch (CameraFeedException ex)
        {
            LogReadFailed(ex.Message);
            return export.Fail(CameraTimelapseExportProblem.Unreadable, export.Written);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogWriteFailed(ex.GetType().Name);
            return export.Fail(CameraRecording.IsDiskFull(ex) ? CameraTimelapseExportProblem.DiskFull : CameraTimelapseExportProblem.WriteFailed, export.Written);
        }
        catch (Exception ex)
        {
            LogUnexpectedFailure(ex);
            return export.Fail(CameraTimelapseExportProblem.Failed, export.Written);
        }
        finally
        {
            if (!keep)
            {
                DeleteFolder(partial);
            }

            // Lets the rotation count the new folder (or the timelapse go, when it is the oldest).
            hold?.Dispose();
        }
    }

    /// <summary>
    /// The frames of the timelapse, from its packets alone (no decoder; one packet is one frame of a timelapse). A damaged
    /// end counts the packets before it, like <see cref="WriteFramesAsync"/> exports the frames before it.
    /// </summary>
    private async Task<int> CountFramesAsync(ICameraFeed feed, CancellationToken cancellationToken)
    {
        await using (feed.ConfigureAwait(false))
        {
            int count = 0;
            try
            {
                await foreach (CameraStreamItem item in feed.ReadAsync(new CameraStreamDemand(frames: false, packets: true), cancellationToken).ConfigureAwait(false))
                {
                    // Feeds without packets (test fakes) yield frames instead; either counts.
                    if (item.Kind != CameraStreamItemKind.StreamStarted)
                    {
                        count++;
                    }
                }
            }
            catch (CameraFeedException ex) when (count > 0)
            {
                LogDamagedEnd(count, ex.Message);
            }

            return count;
        }
    }

    /// <summary>
    /// Decodes the frames one by one and writes each as <c>NNNN.jpg</c> into <paramref name="folder"/>. Every frame reuses the
    /// decoder's buffer, the encoder and one JPEG buffer; a frame's file is written in one call. Runs mostly on the decoder's
    /// thread (inline continuations, see <see cref="ICameraFeed.ReadFramesAsync"/>).
    /// </summary>
    private async Task<int> WriteFramesAsync(Export export, Uri uri, string folder, int total, CancellationToken cancellationToken)
    {
        ICameraFeed feed = _feeds.Create(uri);
        await using (feed.ConfigureAwait(false))
        {
            using ICameraJpegSequenceEncoder encoder = _encoders.Create(_quality);
            using MemoryStream jpeg = new(InitialJpegCapacity);
            int written = 0;
            int percent = 0;
            try
            {
                await foreach (CameraFrame frame in feed.ReadFramesAsync(cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    jpeg.SetLength(0);
                    encoder.Encode(frame, jpeg);
                    WriteFile(Path.Join(folder, CameraRecordingFolders.JpegExportFrameFileName(written + 1, total)), jpeg);
                    written++;
                    Volatile.Write(ref export.FramesDone, written);
                    int now = (int)Math.Min(written * 100L / total, 100);
                    if (now != percent)
                    {
                        percent = now;
                        RaiseChanged();
                    }
                }
            }
            catch (CameraFeedException ex) when (written > 0)
            {
                // A timelapse cut off by a crash: its readable frames are the export.
                LogDamagedEnd(written, ex.Message);
            }

            return written;
        }
    }

    /// <summary>Writes the JPEG in <paramref name="jpeg"/> into a new file (never over an existing one), in one call.</summary>
    private static void WriteFile(string path, MemoryStream jpeg)
    {
        using SafeFileHandle file = File.OpenHandle(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, FileOptions.None, preallocationSize: jpeg.Length);
        RandomAccess.Write(file, new ReadOnlySpan<byte>(jpeg.GetBuffer(), 0, (int)jpeg.Length), fileOffset: 0);
    }

    /// <summary>
    /// Creates the temporary folder empty. One that exists is a leftover of a crashed export of the same timelapse (only one
    /// export runs at a time, and only in this single instance of the app): it is replaced.
    /// </summary>
    private static void PrepareFolder(string partial)
    {
        if (Directory.Exists(partial))
        {
            Directory.Delete(partial, recursive: true);
        }

        Directory.CreateDirectory(partial);
    }

    /// <summary>
    /// Gives the written folder its final name next to the timelapse: <paramref name="finalName"/>, or with <c>-2</c>,
    /// <c>-3</c> … when taken (an earlier export of the same timelapse stays as it is).
    /// </summary>
    private static string Publish(string folder, string partial, string finalName)
    {
        const int MaxAttempts = 1000;
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            string target = Path.Join(folder, attempt == 1 ? finalName : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{finalName}-{attempt}"));
            if (Directory.Exists(target) || File.Exists(target))
            {
                continue;
            }

            try
            {
                Directory.Move(partial, target);
                return target;
            }
            catch (IOException) when (Directory.Exists(target) || File.Exists(target))
            {
                // Taken meanwhile: the next suffix.
            }
        }

        throw new IOException("No free name for the export folder.");
    }

    private void DeleteFolder(string partial)
    {
        try
        {
            if (Directory.Exists(partial))
            {
                // Only the frames this export wrote; a link inside would be removed, never followed.
                Directory.Delete(partial, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The rotation removes it once it is old enough.
            LogDeleteFailed(ex.GetType().Name);
        }
    }

    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            LogSubscriberFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Exported {Frames} timelapse frames as JPEG into '{FolderName}'.")]
    private partial void LogExported(int frames, string folderName);

    [LoggerMessage(Level = LogLevel.Information, Message = "The JPEG export of '{FileName}' ended without a folder ({Problem}).")]
    private partial void LogNotExported(CameraTimelapseExportProblem problem, string fileName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading the timelapse for the JPEG export failed: {Message}")]
    private partial void LogReadFailed(string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The timelapse ends damaged after {Frames} frames; the frames before are exported: {Message}")]
    private partial void LogDamagedEnd(int frames, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Writing the JPEG export failed ({ErrorType}).")]
    private partial void LogWriteFailed(string errorType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The unfinished JPEG export folder could not be deleted ({ErrorType}); the rotation removes it later.")]
    private partial void LogDeleteFailed(string errorType);

    [LoggerMessage(Level = LogLevel.Error, Message = "The JPEG export of a timelapse failed unexpectedly.")]
    private partial void LogUnexpectedFailure(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "A subscriber of the JPEG export failed.")]
    private partial void LogSubscriberFailed(Exception exception);

    /// <summary>One export; the counters are written by its work and read by <see cref="Status"/>.</summary>
    private sealed class Export(string sourceFile, string? printerId)
    {
        public int FramesDone;
        public int FramesTotal;

        public string SourceFile { get; } = sourceFile;

        public string? PrinterId { get; } = printerId;

        public CancellationTokenSource Cancellation { get; } = new();

        /// <summary>Completed when the export cleaned up and told how it ended.</summary>
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Frames written so far.</summary>
        public int Written => Volatile.Read(ref FramesDone);

        public CameraTimelapseExportResult Fail(CameraTimelapseExportProblem problem, int frames = 0) =>
            new(PrinterId, SourceFile, null, frames, problem);
    }
}
