using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace PrusaTrayDiagnostic.Tests.Shared;

/// <summary>
/// The git worktree (repository root) the tests were built from, and its short stable <see cref="Id"/>. Locks and
/// reserved %TEMP% folders of test runs carry the id, so that runs from different worktrees never wait for each other
/// and never delete each other's files.
/// </summary>
internal static class TestWorktree
{
    private const string MetadataKey = "TestWorktreeRoot";

    /// <summary>Length of an <see cref="Id"/>.</summary>
    public const int IdLength = 8;

    /// <summary>Normalized path of the repository root (set by <c>tests/Directory.Build.props</c> at build time).</summary>
    public static string Root { get; } = Normalize(
        typeof(TestWorktree).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(static attribute => attribute.Key == MetadataKey)?.Value
        ?? throw new InvalidOperationException($"The {MetadataKey} assembly metadata is missing (tests/Directory.Build.props)."));

    /// <summary>Id of <see cref="Root"/>: 8 lowercase hex characters.</summary>
    public static string Id { get; } = IdOf(Root);

    /// <summary>Absolute path without a trailing separator (a drive or file system root keeps its separator).</summary>
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    /// <summary>
    /// First 4 bytes of the SHA-256 of the normalized path, as hex. Paths are case-insensitive on Windows only, like
    /// the file system there, so one worktree always gets one id however its path is spelled.
    /// </summary>
    public static string IdOf(string worktree)
    {
        string key = Normalize(worktree);
        if (OperatingSystem.IsWindows())
        {
            key = key.ToUpperInvariant();
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 4));
    }

    /// <summary>Whether <paramref name="text"/> has the form of an <see cref="Id"/>.</summary>
    public static bool IsId(ReadOnlySpan<char> text)
    {
        if (text.Length != IdLength)
        {
            return false;
        }

        foreach (char c in text)
        {
            if (!char.IsAsciiHexDigitLower(c))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// A folder reserved for one test assembly in one worktree: <c>&lt;parent&gt;/&lt;name&gt;.&lt;id&gt;</c>, guarded by the
/// lock file <c>&lt;name&gt;.&lt;id&gt;.lock</c> next to it. The folder holds a marker file with the worktree path, so
/// that the crash sweep of any worktree can tell leftovers of a deleted worktree (removed) from the folder of another
/// existing worktree (never touched).
/// </summary>
internal sealed class ReservedTempDirectory
{
    /// <summary>Name of the marker file holding the worktree path.</summary>
    public const string MarkerFileName = ".worktree";

    /// <summary>
    /// Infix of a staging folder <c>&lt;name&gt;.&lt;id&gt;.tmp-&lt;guid&gt;</c>: <see cref="CreateAsync"/> writes the marker
    /// there and renames it to <see cref="Path"/>, so the reserved folder never exists without its marker.
    /// </summary>
    public const string StagingInfix = ".tmp-";

    /// <summary>A marker longer than this is not one of ours (it holds a single path).</summary>
    private const long MaxMarkerBytes = 32 * 1024;

    private readonly string _parent;
    private readonly string _name;
    private readonly string? _legacyDirectoryName;
    private readonly Func<string, Task>? _beforeDelete;

    /// <param name="parent">Where the folder and its lock live (%TEMP%, or a sandbox in the tests of this class).</param>
    /// <param name="name">Name of the test assembly's reservation, e.g. <c>PrusaTrayDiagnostic.Core.Tests</c>.</param>
    /// <param name="worktree">The worktree owning the folder.</param>
    /// <param name="legacyDirectoryName">
    /// The folder of older runs without a worktree id (guarded by the lock <c>&lt;name&gt;.lock</c>), or <see langword="null"/>.
    /// </param>
    /// <param name="beforeDelete">Called with a folder path before the folder is deleted (e.g. to stop its processes).</param>
    public ReservedTempDirectory(string parent, string name, string worktree, string? legacyDirectoryName = null, Func<string, Task>? beforeDelete = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parent);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _parent = TestWorktree.Normalize(parent);
        _name = name;
        _legacyDirectoryName = legacyDirectoryName;
        _beforeDelete = beforeDelete;
        Worktree = TestWorktree.Normalize(worktree);
        Id = TestWorktree.IdOf(Worktree);
        Path = System.IO.Path.Combine(_parent, $"{name}.{Id}");
    }

    /// <summary>The worktree owning the folder.</summary>
    public string Worktree { get; }

    /// <summary>Id of <see cref="Worktree"/>.</summary>
    public string Id { get; }

    /// <summary>The reserved folder.</summary>
    public string Path { get; }

    /// <summary>Lock file serializing the runs of this assembly in this worktree.</summary>
    public string LockPath => Path + ".lock";

    /// <summary>The marker file in <see cref="Path"/>.</summary>
    public string MarkerPath => System.IO.Path.Combine(Path, MarkerFileName);

    /// <summary>The reservation of <paramref name="name"/> in %TEMP% for the worktree the tests were built from.</summary>
    public static ReservedTempDirectory ForCurrentWorktree(string name, string? legacyDirectoryName = null, Func<string, Task>? beforeDelete = null) =>
        new(System.IO.Path.GetTempPath(), name, TestWorktree.Root, legacyDirectoryName, beforeDelete);

    /// <summary>Waits until no other run of this assembly in this worktree runs.</summary>
    public Task<TestRunLock> AcquireLockAsync(TimeSpan timeout) => TestRunLock.AcquireFileAsync(LockPath, timeout);

    /// <summary>
    /// Crash sweep, under the own lock: deletes the own folder, folders of worktrees that no longer exist (when no run of
    /// theirs holds their lock) and the legacy folder (when no older run holds the legacy lock). Folders of another
    /// existing worktree, folders of running runs and folders without a valid marker are never touched. Staging folders
    /// of <see cref="CreateAsync"/> left by a crash are removed when their lock is free. Other locks are only tried, never
    /// waited for, so two sweeping runs cannot deadlock; every folder is deleted while its lock is held. Two worktrees
    /// whose ids collide (2^-32) merely share one folder and lock: their runs serialize and never overlap.
    /// </summary>
    public async Task SweepAsync()
    {
        await DeleteAsync().ConfigureAwait(false);
        if (!Directory.Exists(_parent))
        {
            return;
        }

        string prefix = _name + ".";
        foreach (string candidate in Directory.EnumerateDirectories(_parent))
        {
            // Exactly <name>.<id> or <name>.<id>.tmp-<guid>, compared ordinally (a Windows search pattern "<name>.*"
            // would also match "<name>").
            string folder = System.IO.Path.GetFileName(candidate);
            if (!folder.StartsWith(prefix, StringComparison.Ordinal)
                || folder.Length < prefix.Length + TestWorktree.IdLength
                || !TestWorktree.IsId(folder.AsSpan(prefix.Length, TestWorktree.IdLength)))
            {
                continue;
            }

            string id = folder.Substring(prefix.Length, TestWorktree.IdLength);
            ReadOnlySpan<char> rest = folder.AsSpan(prefix.Length + TestWorktree.IdLength);
            if (rest.IsEmpty && id != Id)
            {
                await SweepOrphanAsync(candidate, id).ConfigureAwait(false);
            }
            else if (IsStagingSuffix(rest))
            {
                await SweepStagingAsync(candidate, id).ConfigureAwait(false);
            }
        }

        if (_legacyDirectoryName is not null)
        {
            string legacy = System.IO.Path.Combine(_parent, _legacyDirectoryName);
            if (Directory.Exists(legacy))
            {
                using TestRunLock? legacyLock = TestRunLock.TryAcquire(System.IO.Path.Combine(_parent, _name + ".lock"));
                if (legacyLock is not null)
                {
                    await DeleteAsync(legacy).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>
    /// Creates <see cref="Path"/> with its marker (after <see cref="SweepAsync"/>, under the own lock). The folder is
    /// prepared under a staging name and renamed, so a crash never leaves it without its marker; a crash before the
    /// rename leaves only the staging folder, which the next sweep of any worktree removes once its lock is free.
    /// </summary>
    public async Task CreateAsync()
    {
        string staging = $"{Path}{StagingInfix}{Guid.NewGuid():N}";
        try
        {
            Directory.CreateDirectory(staging);
            await File.WriteAllTextAsync(System.IO.Path.Combine(staging, MarkerFileName), Worktree).ConfigureAwait(false);
            await TestRunLock.RetryAsync($"move '{staging}' to '{Path}'", () => Directory.Move(staging, Path)).ConfigureAwait(false);
        }
        finally
        {
            // Normally already moved; after a failure the staging folder goes now (or with the next sweep).
            if (Directory.Exists(staging))
            {
                try
                {
                    Directory.Delete(staging, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Left for the next sweep; the original failure is what matters.
                }
            }
        }
    }

    /// <summary>Whether <see cref="Path"/> holds nothing but its marker.</summary>
    public bool HoldsOnlyMarker() =>
        Directory.Exists(Path)
        && Directory.EnumerateFileSystemEntries(Path).All(entry => string.Equals(entry, MarkerPath, StringComparison.Ordinal));

    /// <summary>Deletes the own folder (under the own lock).</summary>
    public Task DeleteAsync() => DeleteAsync(Path);

    private async Task SweepOrphanAsync(string directory, string id)
    {
        // A running run of that worktree holds its lock; holding it here also keeps a new run from starting meanwhile.
        using TestRunLock? orphanLock = TestRunLock.TryAcquire(directory + ".lock");
        if (orphanLock is null || ReadMarker(directory) is not { } worktree)
        {
            return;
        }

        // The marker must belong to the folder name (otherwise it is not one of ours), and the worktree must be gone.
        if (TryIdOf(worktree) == id && !Directory.Exists(worktree))
        {
            await DeleteAsync(directory).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A staging folder of <see cref="CreateAsync"/> that outlived a crash: deleted when its lock is free (no run of that
    /// worktree; runs stage only under their lock). It never holds anything but a marker, so no processes are stopped.
    /// </summary>
    private async Task SweepStagingAsync(string directory, string id)
    {
        if (id == Id)
        {
            // Under the own lock.
            await DeleteAsync(directory, stopProcesses: false).ConfigureAwait(false);
            return;
        }

        using TestRunLock? stagingLock = TestRunLock.TryAcquire(System.IO.Path.Combine(_parent, $"{_name}.{id}.lock"));
        if (stagingLock is not null)
        {
            await DeleteAsync(directory, stopProcesses: false).ConfigureAwait(false);
        }
    }

    /// <summary>Whether <paramref name="rest"/> (after <c>&lt;name&gt;.&lt;id&gt;</c>) is <c>.tmp-&lt;32 lowercase hex&gt;</c>.</summary>
    private static bool IsStagingSuffix(ReadOnlySpan<char> rest)
    {
        if (!rest.StartsWith(StagingInfix, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> guid = rest[StagingInfix.Length..];
        if (guid.Length != 32)
        {
            return false;
        }

        foreach (char c in guid)
        {
            if (!char.IsAsciiHexDigitLower(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The worktree path in the marker of <paramref name="directory"/>, or <see langword="null"/> without a valid one.</summary>
    private static string? ReadMarker(string directory)
    {
        try
        {
            FileInfo marker = new(System.IO.Path.Combine(directory, MarkerFileName));
            if (!marker.Exists || marker.Length > MaxMarkerBytes)
            {
                return null;
            }

            string text = File.ReadAllText(marker.FullName).Trim();
            return text.Length == 0 || text.Contains('\0', StringComparison.Ordinal) || !System.IO.Path.IsPathFullyQualified(text) ? null : text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable marker: the owner cannot be told, so the folder stays.
            return null;
        }
    }

    /// <summary><see cref="TestWorktree.IdOf"/>, or <see langword="null"/> for a path that cannot be normalized.</summary>
    private static string? TryIdOf(string worktree)
    {
        try
        {
            return TestWorktree.IdOf(worktree);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private async Task DeleteAsync(string directory, bool stopProcesses = true)
    {
        // Directory.Delete removes junctions and symbolic links inside the folder without following them.
        if (stopProcesses && _beforeDelete is not null)
        {
            await _beforeDelete(directory).ConfigureAwait(false);
        }

        await TestRunLock.RetryAsync($"delete '{directory}'", () => DeleteMarkerLast(directory)).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes <paramref name="directory"/> with its marker last: a deletion that fails half-way (a file still open) keeps
    /// the marker, so the folder never ends up without one and a later sweep can still tell its owner.
    /// </summary>
    private static void DeleteMarkerLast(string directory)
    {
        DirectoryInfo folder = new(directory);
        if (!folder.Exists)
        {
            return;
        }

        string marker = System.IO.Path.Combine(folder.FullName, MarkerFileName);
        foreach (FileSystemInfo entry in folder.EnumerateFileSystemInfos())
        {
            if (string.Equals(entry.FullName, marker, StringComparison.Ordinal))
            {
                continue;
            }

            if (entry is DirectoryInfo child)
            {
                child.Delete(recursive: true);
            }
            else
            {
                entry.Delete();
            }
        }

        folder.Delete(recursive: true);
    }
}
