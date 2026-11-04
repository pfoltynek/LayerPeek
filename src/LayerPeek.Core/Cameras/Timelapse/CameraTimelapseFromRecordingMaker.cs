using System.Globalization;

using Microsoft.Extensions.Logging;

using LayerPeek.Core.Cameras.Recording;

namespace LayerPeek.Core.Cameras.Timelapse;

/// <summary>
/// Makes a timelapse from a finished recording (T-164, variant F of the spike T-149, user decisions 2026-11-04): only on
/// demand, from any recording file; nothing runs during a print and nothing is decoded or encoded.
/// <list type="bullet">
/// <item>The recording's packets are read in order (no decoder) and the first key frame at or after every step of the
/// interval (the live timelapse's, T-160) in recording time is written as it is, at
/// <see cref="ICameraTimelapseRecorder.FramesPerSecond"/>, into fragmented MP4 by the recording writer (the same as the
/// live timelapse, T-158). Key frames further apart than the interval give one frame each (the effective interval is then
/// the key frame spacing).</item>
/// <item>A recording split into segments (<c>-2</c>, <c>-3</c> …, T-144) is one whole: its segments are read in order and
/// their recording times follow each other (the outage between them is not known and does not count). When a segment's
/// stream parameters (codec parameters, image size) differ, its frames start the next timelapse file (<c>-2</c> …), like
/// the live timelapse.</item>
/// <item>The output goes next to the recording (<see cref="CameraRecordingFolders.RecordingTimelapseFileName"/>); in a
/// printer folder of the recordings it counts in their size limit and rotation. It is written under a temporary name
/// (<see cref="CameraRecordingFolders.UnfinishedFileName"/>) and renamed when complete: a cancelled or failed job leaves
/// nothing (a leftover of a crash is removed by the rotation); an existing file is never overwritten.</item>
/// <item>A damaged segment (a crash leftover cut off mid-fragment, garbage) gives the frames before the damage; the other
/// segments go on. Nothing that can be read gives <see cref="CameraTimelapseExportProblem.Unreadable"/>. Nothing of it ever
/// throws out of the maker.</item>
/// <item>One job at a time (a second <see cref="Start"/> while one runs is refused); independent of the JPEG export (T-165).</item>
/// </list>
/// <para>
/// Idle costs nothing: no thread, task or buffer exists between jobs. The work runs on the thread pool and on the decoder's
/// own thread (inline continuations, see <see cref="ICameraFeed.ReadAsync"/>); a packet costs a copy into the decoder's
/// reused buffer and a few comparisons, a written frame one muxer call. Thread-safe; <see cref="Changed"/> and
/// <see cref="Completed"/> come on any thread (subscribers marshal themselves; a failing subscriber is logged).
/// </para>
/// </summary>
public sealed partial class CameraTimelapseFromRecordingMaker : IAsyncDisposable, IDisposable
{
    /// <summary>The unit of recording time: <see cref="TimeSpan"/> ticks.</summary>
    private static readonly CameraTimeBase TimeSpanTicks = new(1, (int)TimeSpan.TicksPerSecond);

    private readonly ICameraFeedFactory _feeds;
    private readonly ICameraRecordingWriterFactory _writers;
    private readonly ICameraRecordingFileGuard? _guard;
    private readonly ILogger<CameraTimelapseFromRecordingMaker> _logger;
    private readonly Lock _gate = new();

    // Guarded by _gate.
    private Job? _current;
    private bool _disposed;

    /// <param name="feeds">Reads the recording (FFmpeg: <c>file://</c> addresses, packets only).</param>
    /// <param name="writers">Writes the timelapse (the recording writer, fragmented MP4).</param>
    /// <param name="guard">Keeps the recording and the file being written away from the rotation; <see langword="null"/>: nothing is held.</param>
    public CameraTimelapseFromRecordingMaker(
        ICameraFeedFactory feeds,
        ICameraRecordingWriterFactory writers,
        ILogger<CameraTimelapseFromRecordingMaker> logger,
        ICameraRecordingFileGuard? guard = null)
    {
        ArgumentNullException.ThrowIfNull(feeds);
        ArgumentNullException.ThrowIfNull(writers);
        ArgumentNullException.ThrowIfNull(logger);
        _feeds = feeds;
        _writers = writers;
        _logger = logger;
        _guard = guard;
    }

    /// <summary>Whether this build can write the timelapse (the MP4 muxer of the recordings).</summary>
    public CameraRecorderAvailability Availability => _writers.Availability;

    /// <summary>A job started, made progress (at most once per whole percent) or ended; read it with <see cref="Status"/>.</summary>
    public event EventHandler? Changed;

    /// <summary>A job ended: done, cancelled or failed. Raised after <see cref="Status"/> is idle again.</summary>
    public event EventHandler<CameraTimelapseFromRecordingResult>? Completed;

    /// <summary>What runs now; <see cref="CameraTimelapseFromRecordingStatus.Idle"/> between jobs.</summary>
    public CameraTimelapseFromRecordingStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _current is { } job
                    ? new CameraTimelapseFromRecordingStatus(true, job.PrinterId, job.SourceFile, Volatile.Read(ref job.Frames), Volatile.Read(ref job.Percent))
                    : CameraTimelapseFromRecordingStatus.Idle;
            }
        }
    }

    /// <summary>The running job's work (for tests); <see langword="null"/> when none runs.</summary>
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
    /// Makes a timelapse of <paramref name="recordingFile"/> in the background; <see cref="Completed"/> tells how it ended.
    /// Never throws for the file (a wrong or damaged file ends the job with its problem).
    /// </summary>
    /// <param name="interval">Recording time between two frames (the live timelapse's, T-160).</param>
    /// <param name="printerId">The printer whose menu starts it (for the notification); <see langword="null"/>: none.</param>
    /// <returns><see langword="false"/> when a job runs already (it goes on) or the maker is disposed.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="interval"/> is not positive.</exception>
    public bool Start(string recordingFile, TimeSpan interval, string? printerId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordingFile);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        Job job;
        lock (_gate)
        {
            if (_disposed || _current is not null)
            {
                return false;
            }

            job = new Job(recordingFile, printerId, interval);
            _current = job;
        }

        // Announced before the work can report anything.
        RaiseChanged();
        _ = Task.Run(() => RunAsync(job), CancellationToken.None);
        return true;
    }

    /// <summary>Cancels the running job, if any; it ends soon with <see cref="CameraTimelapseExportProblem.Cancelled"/>.</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            _current?.Cancellation.Cancel();
        }
    }

    /// <summary>Cancels a running job and waits until it cleaned up.</summary>
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

    /// <summary>Cancels a running job without waiting (it cleans up in the background).</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _current?.Cancellation.Cancel();
        }
    }

    /// <summary>
    /// The files of the recording <paramref name="source"/> (full path) in order, and the name they share without the
    /// extension: the recorder continues a recording after an outage in <c>name-2.mp4</c>, <c>name-3.mp4</c> … (T-144), so
    /// the chosen file comes with its siblings, whichever of them was chosen. <c>name-2</c> is a segment of <c>name</c>
    /// unless files <c>name-2-N</c> exist (then it is a title that ends with <c>-2</c>). Missing numbers (deleted by the user
    /// or the rotation) are skipped.
    /// </summary>
    /// <exception cref="IOException">The folder cannot be read.</exception>
    /// <exception cref="UnauthorizedAccessException">No permission to read the folder.</exception>
    internal static (string Stem, IReadOnlyList<string> Files) FindSegments(string source)
    {
        string folder = Path.GetDirectoryName(source) ?? throw new ArgumentException("The recording path needs a folder.", nameof(source));
        string fileName = Path.GetFileName(source);
        string extension = Path.GetExtension(fileName);
        string stem = fileName[..^extension.Length];
        string[] names = [.. Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = true })
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))];

        string seriesStem = stem;
        if (!names.Any(name => SegmentNumber(name, stem, extension) >= 2) && SplitSegment(stem) is { } prefix)
        {
            seriesStem = prefix;
        }

        List<(int Number, string Path)> files = [];
        foreach (string name in names)
        {
            int number = SegmentNumber(name, seriesStem, extension);
            if (number > 0 && !CameraRecordingFolders.IsTimelapseFileName(name))
            {
                files.Add((number, Path.Join(folder, name)));
            }
        }

        if (!files.Exists(file => string.Equals(Path.GetFileName(file.Path), fileName, StringComparison.OrdinalIgnoreCase)))
        {
            // Not listed (removed meanwhile): the chosen file alone, which then fails as missing.
            return (stem, [source]);
        }

        files.Sort(static (a, b) => a.Number.CompareTo(b.Number));
        return (seriesStem, [.. files.Select(static file => file.Path)]);
    }

    /// <summary><c>stem.ext</c>: 1, <c>stem-N.ext</c> with N ≥ 2: N, anything else: 0.</summary>
    private static int SegmentNumber(string name, string stem, string extension)
    {
        if (name.Length < stem.Length + extension.Length || !name.StartsWith(stem, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        ReadOnlySpan<char> rest = name.AsSpan(stem.Length, name.Length - stem.Length - extension.Length);
        if (rest.IsEmpty)
        {
            return 1;
        }

        // The recorder writes no leading zeros (and "-05" of a time is no segment).
        return rest.Length > 1 && rest[0] == '-' && rest[1] != '0' && !rest[1..].ContainsAnyExceptInRange('0', '9')
            && int.TryParse(rest[1..], NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number >= 2
            ? number
            : 0;
    }

    /// <summary>
    /// The name before a <c>-N</c> (N ≥ 2) at the end of <paramref name="stem"/>, never inside its timestamp (an untitled
    /// recording ends with the seconds); <see langword="null"/> without one.
    /// </summary>
    private static string? SplitSegment(string stem)
    {
        int dash = stem.LastIndexOf('-');
        return dash >= CameraRecordingFolders.TimestampLength && SegmentNumber(stem, stem[..dash], extension: "") >= 2 ? stem[..dash] : null;
    }

    private async Task RunAsync(Job job)
    {
        CameraTimelapseFromRecordingResult result;
        try
        {
            result = await MakeAsync(job, job.Cancellation.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Defensive: MakeAsync turns every failure into a result.
            LogUnexpectedFailure(ex);
            result = job.Fail(CameraTimelapseExportProblem.Failed);
        }

        lock (_gate)
        {
            _current = null;
        }

        // Nobody can cancel it any more (Cancel only reaches the current job, under the lock).
        job.Cancellation.Dispose();
        string fileName = Path.GetFileName(job.SourceFile);
        if (result.Succeeded)
        {
            LogMade(result.Frames, result.OutputFiles.Count, fileName);
        }
        else
        {
            LogNotMade(result.Problem, fileName);
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
            job.Done.TrySetResult();
        }
    }

    private async Task<CameraTimelapseFromRecordingResult> MakeAsync(Job job, CancellationToken cancellationToken)
    {
        string source;
        try
        {
            source = Path.GetFullPath(job.SourceFile);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return job.Fail(CameraTimelapseExportProblem.Unreadable);
        }

        string fileName = Path.GetFileName(source);
        string? folder = Path.GetDirectoryName(source);
        if (!CameraRecordingFolders.IsRecordingFileName(fileName) || CameraRecordingFolders.IsTimelapseFileName(fileName))
        {
            return job.Fail(CameraTimelapseExportProblem.NotARecording);
        }

        if (folder is null || !File.Exists(source))
        {
            return job.Fail(CameraTimelapseExportProblem.Unreadable);
        }

        if (!_writers.Availability.IsAvailable)
        {
            return job.Fail(CameraTimelapseExportProblem.Unavailable);
        }

        string stem;
        IReadOnlyList<string> segments;
        long totalBytes = 0;
        try
        {
            (stem, segments) = FindSegments(source);
            foreach (string segment in segments)
            {
                totalBytes += new FileInfo(segment).Length;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogReadFailed(ex.GetType().Name);
            return job.Fail(CameraTimelapseExportProblem.Unreadable);
        }

        Output output = new(this, folder, CameraRecordingFolders.RecordingTimelapseFileName(stem));
        IDisposable? hold = _guard?.Hold(segments);
        Reader reader = new(this, job, Math.Max(totalBytes, 1));
        bool published = false;
        try
        {
            foreach (string segment in segments)
            {
                if (CameraTimelapseJpegExporter.FileUri(segment) is not { } uri)
                {
                    // A path no file address can carry unchanged: that segment cannot be read (like a damaged one).
                    LogReadFailed("The path of a segment cannot be given to the demuxer.");
                    continue;
                }

                ICameraFeed feed;
                try
                {
                    feed = _feeds.Create(uri);
                }
                catch (CameraFeedException ex)
                {
                    // Creating a feed opens nothing: it fails only without the decoder library.
                    LogReadFailed(ex.Message);
                    return job.Fail(CameraTimelapseExportProblem.Unavailable);
                }

                await reader.ReadSegmentAsync(feed, output, cancellationToken).ConfigureAwait(false);
            }

            if (reader.Frames == 0)
            {
                return job.Fail(CameraTimelapseExportProblem.Unreadable);
            }

            IReadOnlyList<string> files = output.Publish();
            published = true;
            return new CameraTimelapseFromRecordingResult(job.PrinterId, source, files, reader.Frames, CameraTimelapseExportProblem.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return job.Fail(CameraTimelapseExportProblem.Cancelled, reader.Frames);
        }
        catch (CameraRecordingException ex)
        {
            LogWriteFailed(ex.Failure.ToString());
            return job.Fail(
                ex.Failure switch
                {
                    CameraRecordingFailure.DiskFull => CameraTimelapseExportProblem.DiskFull,
                    CameraRecordingFailure.Unavailable => CameraTimelapseExportProblem.Unavailable,
                    // The recording's stream cannot be stored (no parameter sets, another codec).
                    CameraRecordingFailure.StreamFailed => CameraTimelapseExportProblem.Unreadable,
                    _ => CameraTimelapseExportProblem.WriteFailed,
                },
                reader.Frames);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogWriteFailed(ex.GetType().Name);
            return job.Fail(CameraRecording.IsDiskFull(ex) ? CameraTimelapseExportProblem.DiskFull : CameraTimelapseExportProblem.WriteFailed, reader.Frames);
        }
        catch (Exception ex)
        {
            LogUnexpectedFailure(ex);
            return job.Fail(CameraTimelapseExportProblem.Failed, reader.Frames);
        }
        finally
        {
            if (!published)
            {
                output.Discard();
            }

            output.ReleaseHolds();
            // Lets the rotation count the new file (or the recording go, when it is the oldest).
            hold?.Dispose();
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Made a timelapse of {Frames} frames in {Files} file(s) from the recording '{FileName}'.")]
    private partial void LogMade(int frames, int files, string fileName);

    [LoggerMessage(Level = LogLevel.Information, Message = "The timelapse from the recording '{FileName}' ended without a file ({Problem}).")]
    private partial void LogNotMade(CameraTimelapseExportProblem problem, string fileName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading the recording for a timelapse failed: {Message}")]
    private partial void LogReadFailed(string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A segment of the recording ends damaged after {Packets} packets; its frames before are used: {Message}")]
    private partial void LogDamagedSegment(long packets, string message);

    [LoggerMessage(Level = LogLevel.Information, Message = "A segment of the recording has other stream parameters; its frames start the next timelapse file.")]
    private partial void LogStreamChanged();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Writing the timelapse from a recording failed ({Error}).")]
    private partial void LogWriteFailed(string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "An unfinished timelapse file could not be deleted ({ErrorType}); the rotation removes it later.")]
    private partial void LogDeleteFailed(string errorType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Making a timelapse from a recording failed unexpectedly.")]
    private partial void LogUnexpectedFailure(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "A subscriber of the timelapse from a recording failed.")]
    private partial void LogSubscriberFailed(Exception exception);

    /// <summary>One job; the counters are written by its work and read by <see cref="Status"/>.</summary>
    private sealed class Job(string sourceFile, string? printerId, TimeSpan interval)
    {
        public int Frames;
        public int Percent;

        public string SourceFile { get; } = sourceFile;

        public string? PrinterId { get; } = printerId;

        public TimeSpan Interval { get; } = interval;

        public CancellationTokenSource Cancellation { get; } = new();

        /// <summary>Completed when the job cleaned up and told how it ended.</summary>
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CameraTimelapseFromRecordingResult Fail(CameraTimelapseExportProblem problem, int frames = 0) =>
            new(PrinterId, SourceFile, [], frames, problem);
    }

    /// <summary>
    /// Picks the frames of all segments of one job in recording time: a segment's time starts where the previous one ended,
    /// and a key frame is taken when its time reached the next step of the interval. One thread at a time.
    /// </summary>
    private sealed class Reader(CameraTimelapseFromRecordingMaker owner, Job job, long totalBytes)
    {
        private readonly long _interval = job.Interval.Ticks;
        private long _bytesRead;
        private long _segmentOffset;
        private long _nextDue;

        public int Frames { get; private set; }

        /// <summary>Reads one segment (disposing <paramref name="feed"/>); a damaged segment ends where the damage starts.</summary>
        public async Task ReadSegmentAsync(ICameraFeed feed, Output output, CancellationToken cancellationToken)
        {
            await using (feed.ConfigureAwait(false))
            {
                CameraStreamInfo? info = null;
                bool newStream = false;
                long? origin = null;
                long end = _segmentOffset;
                long packets = 0;
                try
                {
                    await foreach (CameraStreamItem item in feed.ReadAsync(new CameraStreamDemand(frames: false, packets: true), cancellationToken).ConfigureAwait(false))
                    {
                        if (item.Kind == CameraStreamItemKind.StreamStarted)
                        {
                            info = item.Info;
                            newStream = true;
                            continue;
                        }

                        if (item.Kind != CameraStreamItemKind.Packet || info is null)
                        {
                            continue;
                        }

                        CameraPacket packet = item.Packet;
                        packets++;
                        Progress(packet.Data.Length);
                        if ((packet.Dts ?? packet.Pts) is not { } timestamp)
                        {
                            continue;
                        }

                        long ticks = RecordingClock.Rescale(timestamp, packet.TimeBase, TimeSpanTicks);
                        origin ??= ticks;
                        long time = _segmentOffset + (ticks - origin.Value);
                        long duration = packet.Duration > 0 ? RecordingClock.Rescale(packet.Duration, packet.TimeBase, TimeSpanTicks) : 0;
                        end = Math.Max(end, time + duration);
                        if (!packet.IsKeyFrame || time < _nextDue)
                        {
                            continue;
                        }

                        cancellationToken.ThrowIfCancellationRequested();
                        if (newStream)
                        {
                            newStream = false;
                            if (output.ContinuesWith(info) is false)
                            {
                                owner.LogStreamChanged();
                                output.Close();
                            }
                        }

                        output.Write(info, packet);
                        Frames++;
                        Volatile.Write(ref job.Frames, Frames);
                        // The next step of the interval after this frame: the steps stay on one grid, and sparse key frames
                        // never give two frames for one step.
                        _nextDue = ((time / _interval) + 1) * _interval;
                    }
                }
                catch (CameraFeedException ex)
                {
                    // A crash leftover cut off mid-fragment, or garbage: its frames before the damage are used.
                    owner.LogDamagedSegment(packets, ex.Message);
                }

                _segmentOffset = Math.Max(end, _segmentOffset);
            }
        }

        private void Progress(int bytes)
        {
            _bytesRead += bytes;
            // Below 100 until the job ended: the container's own bytes are not counted.
            int percent = (int)Math.Min(_bytesRead * 100 / totalBytes, 99);
            if (percent != job.Percent)
            {
                Volatile.Write(ref job.Percent, percent);
                owner.RaiseChanged();
            }
        }
    }

    /// <summary>
    /// The timelapse files of one job: written under temporary names (each one held from the rotation), renamed to their
    /// final names in order by <see cref="Publish"/>, deleted by <see cref="Discard"/>. One thread at a time.
    /// </summary>
    private sealed class Output(CameraTimelapseFromRecordingMaker owner, string folder, string fileName)
    {
        private const int MaxNames = 10_000;

        private readonly string _stem = Path.GetFileNameWithoutExtension(fileName);
        private readonly string _extension = Path.GetExtension(fileName);
        private readonly List<string> _temporary = [];
        private readonly List<IDisposable> _holds = [];
        private ICameraRecordingWriter? _writer;
        private CameraStreamInfo? _info;
        private RecordingClock _clock;

        /// <summary>
        /// Whether a stream described by <paramref name="info"/> can go on in the open file (the same codec parameters and
        /// size); <see langword="null"/> when no file is open.
        /// </summary>
        public bool? ContinuesWith(CameraStreamInfo info)
        {
            if (_writer is null || _info is not { } open)
            {
                return null;
            }

            return info.Codec == open.Codec && info.Width == open.Width && info.Height == open.Height
                && info.Extradata.Span.SequenceEqual(open.Extradata.Span);
        }

        /// <summary>Writes <paramref name="keyFrame"/> as the next frame, into a new file when none is open.</summary>
        /// <exception cref="CameraRecordingException">The file cannot be created or written.</exception>
        public void Write(CameraStreamInfo info, in CameraPacket keyFrame)
        {
            if (_writer is null)
            {
                Open(info, keyFrame.Data.Span);
            }

            _clock.Next(keyFrame, out long pts, out long dts, out long duration);
            _writer!.Write(keyFrame.Data.Span, pts, dts, duration, isKeyFrame: true);
        }

        /// <summary>Completes the open file, if any; the next frame starts the next one.</summary>
        /// <exception cref="CameraRecordingException">Completing failed.</exception>
        public void Close()
        {
            if (_writer is not { } writer)
            {
                return;
            }

            _writer = null;
            try
            {
                writer.Complete();
            }
            finally
            {
                writer.Dispose();
            }
        }

        /// <summary>Completes the files and gives them their final names in order (<c>name.mp4</c>, then <c>-2</c>, <c>-3</c> … for a taken name).</summary>
        /// <exception cref="CameraRecordingException">Completing failed.</exception>
        /// <exception cref="IOException">Renaming failed (the remaining temporary files are deleted by <see cref="Discard"/>).</exception>
        public List<string> Publish()
        {
            Close();
            List<string> published = new(_temporary.Count);
            int next = 1;
            for (int index = 0; index < _temporary.Count; index++)
            {
                (string target, next) = MoveToFreeName(_temporary[index], next);
                published.Add(target);
                _temporary[index] = "";
            }

            _temporary.Clear();
            return published;
        }

        /// <summary>Closes and deletes every temporary file (best effort; the rotation removes what is left later).</summary>
        public void Discard()
        {
            ICameraRecordingWriter? writer = _writer;
            _writer = null;
            writer?.Dispose();
            foreach (string path in _temporary)
            {
                if (path.Length == 0)
                {
                    continue;
                }

                try
                {
                    File.Delete(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    owner.LogDeleteFailed(ex.GetType().Name);
                }
            }

            _temporary.Clear();
        }

        /// <summary>Lets the rotation see the temporary files again (they are gone or renamed by now).</summary>
        public void ReleaseHolds()
        {
            foreach (IDisposable hold in _holds)
            {
                hold.Dispose();
            }

            _holds.Clear();
        }

        private void Open(CameraStreamInfo info, ReadOnlySpan<byte> firstKeyFrame)
        {
            int number = _temporary.Count + 1;
            string name = number == 1 ? _stem + _extension : string.Create(CultureInfo.InvariantCulture, $"{_stem}-{number}{_extension}");
            string path = Path.Join(folder, CameraRecordingFolders.UnfinishedFileName(name));
            if (owner._guard is { } guard)
            {
                _holds.Add(guard.Hold([path]));
            }

            // Listed before it exists: whatever was created goes with Discard. Reserved empty like a recording's file (the
            // writer's contract); one left by a crashed job of the same recording is replaced (only one job runs, in this
            // single instance of the app).
            _temporary.Add(path);
            new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete).Dispose();
            _writer = owner._writers.Create(path, info, firstKeyFrame);
            _info = info;
            _clock = RecordingClock.AtFrameRate(info.TimeBase, ICameraTimelapseRecorder.FramesPerSecond);
        }

        private (string Target, int Next) MoveToFreeName(string temporary, int from)
        {
            for (int number = from; number < from + MaxNames; number++)
            {
                string target = Path.Join(folder, number == 1 ? _stem + _extension : string.Create(CultureInfo.InvariantCulture, $"{_stem}-{number}{_extension}"));
                if (File.Exists(target) || Directory.Exists(target))
                {
                    continue;
                }

                try
                {
                    File.Move(temporary, target, overwrite: false);
                    return (target, number + 1);
                }
                catch (IOException) when (File.Exists(target) || Directory.Exists(target))
                {
                    // Taken meanwhile: the next number.
                }
            }

            throw new IOException("No free name for the timelapse.");
        }
    }
}
