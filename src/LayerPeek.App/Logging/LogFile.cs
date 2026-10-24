using System.Security.Cryptography;
using System.Text;

using Microsoft.Win32.SafeHandles;

namespace LayerPeek.App.Logging;

/// <summary>
/// The log file on disk, shared by every process of the app (T-043: a second start logs into the same file before it
/// exits). Each <see cref="Append"/> holds a named mutex of the file (machine-wide per user session) and writes at the
/// real end of the file in one call, so records of two processes never overwrite or split each other; a .NET
/// <c>FileMode.Append</c> stream would write at its own cached position instead. The file is opened per call with
/// <see cref="FileShare.ReadWrite"/> | <see cref="FileShare.Delete"/>, so it is never locked against readers, the other
/// process or a rotation, and every call writes to the current file even after another process rotated it.
/// <para>
/// Rotation by size, only where <see cref="RotationEnabled"/> (the primary instance): <see cref="FileName"/> becomes
/// <c>LayerPeek.1.log</c>, … up to <c>fileCount - 1</c>; the oldest is overwritten. A rotation that fails
/// (a file kept open without delete sharing, antivirus) is tried again with the next write and never costs an older file.
/// </para>
/// Not thread-safe: one writer thread per instance (the mutex is owned by the calling thread).
/// </summary>
internal sealed class LogFile : IDisposable
{
    public const string FileName = "LayerPeek.log";

    /// <summary>The current file while a rotation moves it to <c>.1</c>; a rotation cut short finishes with the next one.</summary>
    public const string RotatingFileName = "LayerPeek.rotating.log";

    public const long DefaultMaxBytes = 1024 * 1024;

    public const int DefaultFileCount = 5;

    /// <summary>
    /// How long a write waits for another process to finish its own. Writing without the mutex could overwrite that
    /// process's records, so after this time the write fails instead.
    /// </summary>
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(2);

    private readonly string _directory;
    private readonly long _maxBytes;
    private readonly int _fileCount;
    private readonly Mutex _lock;
    private volatile bool _rotationEnabled;

    // Writer thread only: a leftover of a rotation cut short was looked for once rotation got enabled.
    private bool _leftoverChecked;

    /// <param name="directory">Absolute path of the log folder; created with the first write.</param>
    /// <param name="maxBytes">Size above which the primary instance starts a new file.</param>
    /// <param name="fileCount">Number of files kept, the current one included (at least 2).</param>
    public LogFile(string directory, long maxBytes = DefaultMaxBytes, int fileCount = DefaultFileCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(fileCount, 2);
        if (!Path.IsPathFullyQualified(directory))
        {
            throw new ArgumentException("The log folder must be an absolute path.", nameof(directory));
        }

        _directory = Path.GetFullPath(directory);
        _maxBytes = maxBytes;
        _fileCount = fileCount;
        CurrentPath = PathOf(_directory, 0);
        MutexName = MutexNameOf(CurrentPath);
        _lock = new Mutex(initiallyOwned: false, MutexName);
    }

    /// <summary>Name of the mutex guarding the file (tests hold it like another writing process).</summary>
    internal string MutexName { get; }

    /// <summary>The current file.</summary>
    public string CurrentPath { get; }

    /// <summary>Whether this process rotates the files (only the primary instance does; off at first).</summary>
    public bool RotationEnabled
    {
        get => _rotationEnabled;
        set => _rotationEnabled = value;
    }

    /// <summary>The current file (<paramref name="index"/> 0) or an older one (1 = the newest of them).</summary>
    public static string PathOf(string directory, int index) =>
        Path.Combine(directory, index == 0 ? FileName : $"LayerPeek.{index}.log");

    /// <summary>Writes <paramref name="records"/> (whole records) at the end of the current file, rotating first if due.</summary>
    /// <exception cref="IOException">The file cannot be written, or another process kept the mutex too long.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder or file is not writable.</exception>
    public void Append(ReadOnlySpan<byte> records)
    {
        AcquireLock();
        try
        {
            if (_rotationEnabled && !_leftoverChecked)
            {
                // A rotation cut short by a crash would otherwise wait until the current file is full again.
                _leftoverChecked = true;
                if (File.Exists(Path.Combine(_directory, RotatingFileName)))
                {
                    Rotate();
                }
            }

            SafeFileHandle file = Open();
            try
            {
                long end = RandomAccess.GetLength(file);
                if (_rotationEnabled && end > 0 && end + records.Length > _maxBytes)
                {
                    file.Dispose();
                    Rotate();
                    file = Open();
                    end = RandomAccess.GetLength(file);
                }

                end = EndLine(file, end);
                RandomAccess.Write(file, records, end);
            }
            finally
            {
                file.Dispose();
            }
        }
        finally
        {
            _lock.ReleaseMutex();
        }
    }

    public void Dispose() => _lock.Dispose();

    /// <summary>
    /// A write that failed half-way (full disk, removed drive) leaves a record without its line end; it gets one first, so
    /// the next record still starts a line. Returns the new end of the file.
    /// </summary>
    private static long EndLine(SafeFileHandle file, long end)
    {
        if (end == 0)
        {
            return 0;
        }

        Span<byte> last = stackalloc byte[1];
        if (RandomAccess.Read(file, last, end - 1) == 1 && last[0] == (byte)'\n')
        {
            return end;
        }

        RandomAccess.Write(file, "\n"u8, end);
        return end + 1;
    }

    private void AcquireLock()
    {
        try
        {
            if (!_lock.WaitOne(LockTimeout))
            {
                throw new IOException("Another process kept the log file locked.");
            }
        }
        catch (AbandonedMutexException)
        {
            // A process ended while writing; this thread owns the mutex now.
        }
    }

    private SafeFileHandle Open()
    {
        try
        {
            return OpenCurrent();
        }
        catch (DirectoryNotFoundException)
        {
            // First write, or the user deleted the folder.
            Directory.CreateDirectory(_directory);
            return OpenCurrent();
        }
    }

    private SafeFileHandle OpenCurrent() =>
        File.OpenHandle(CurrentPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

    /// <summary>
    /// Moves the current file to <c>.1</c>. It first leaves under <see cref="RotatingFileName"/> in one rename, so a current
    /// file that cannot be moved touches nothing; then only the older files up to the first gap shift, so a rotation cut
    /// short and retried never shifts (and overwrites) them twice.
    /// </summary>
    private void Rotate()
    {
        try
        {
            string rotating = Path.Combine(_directory, RotatingFileName);
            if (!File.Exists(rotating))
            {
                File.Move(CurrentPath, rotating);
            }

            int free = 1;
            while (free < _fileCount - 1 && File.Exists(PathOf(_directory, free)))
            {
                free++;
            }

            for (int index = free - 1; index >= 1; index--)
            {
                File.Move(PathOf(_directory, index), PathOf(_directory, index + 1), overwrite: true);
            }

            File.Move(rotating, PathOf(_directory, 1), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Tried again with the next write; the current file just grows meanwhile.
        }
    }

    /// <summary>
    /// One mutex per log file: the file path decides, compared like the file system (case-insensitively on Windows). On
    /// Windows in the global namespace: the same user signed in twice (console and remote desktop) runs a primary instance
    /// in each session, both writing and rotating the one file in %LOCALAPPDATA%.
    /// </summary>
    private static string MutexNameOf(string path)
    {
        string key = OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;
        string scope = OperatingSystem.IsWindows() ? @"Global\" : string.Empty;
        return scope + "LayerPeek.Log." + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 8));
    }
}
