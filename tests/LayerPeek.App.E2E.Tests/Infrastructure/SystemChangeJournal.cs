using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using LayerPeek.Tests.Shared;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>A process that owns journal entries: id plus start time, so that a reused process id is not mistaken for it.</summary>
internal sealed record ProcessOwner(int ProcessId, long StartTimeUtcTicks, string Name)
{
    public static ProcessOwner Current { get; } = Of(Process.GetCurrentProcess());

    public static ProcessOwner Of(Process process) =>
        new(process.Id, process.StartTime.ToUniversalTime().Ticks, process.ProcessName);

    /// <summary>Whether the owner still runs. A process that cannot be inspected counts as running (never restore under it).</summary>
    public bool IsAlive()
    {
        try
        {
            using Process process = Process.GetProcessById(ProcessId);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == StartTimeUtcTicks;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return true;
        }
    }

    public override string ToString() => $"{Name} ({ProcessId}, started {new DateTime(StartTimeUtcTicks, DateTimeKind.Utc).ToLocalTime():yyyy-MM-dd HH:mm:ss})";
}

/// <summary>One pending change in the journal: the original state, written before the change.</summary>
internal sealed record SystemChangeEntry(string Kind, string? Key, ProcessOwner Owner, DateTimeOffset Created, JsonElement Original, string Description)
{
    /// <summary>The journal file (not serialized).</summary>
    [JsonIgnore]
    public string File { get; init; } = string.Empty;
}

/// <summary>What a restore of pending entries did.</summary>
internal sealed class SystemChangeRestoreReport
{
    public List<string> Restored { get; } = [];

    /// <summary>Entries that could not be restored (kept in the journal), with the manual steps.</summary>
    public List<string> Failed { get; } = [];

    /// <summary>Entries of a process that still runs (it restores them itself, or its watchdog does).</summary>
    public List<string> StillOwned { get; } = [];

    public bool Success => Failed.Count == 0 && StillOwned.Count == 0;

    public bool IsEmpty => Restored.Count == 0 && Failed.Count == 0 && StillOwned.Count == 0;

    public override string ToString()
    {
        if (IsEmpty)
        {
            return "No pending system changes.";
        }

        StringBuilder text = new();
        Append("Restored", Restored);
        Append("NOT restored (still in the journal; restore by hand, see docs/troubleshooting.md)", Failed);
        Append("Owned by a test process that still runs (left alone)", StillOwned);
        return text.ToString().TrimEnd();

        void Append(string title, List<string> lines)
        {
            if (lines.Count == 0)
            {
                return;
            }

            text.AppendLine(CultureInfo.InvariantCulture, $"{title}:");
            foreach (string line in lines)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"  - {line.ReplaceLineEndings(Environment.NewLine + "    ")}");
            }
        }
    }
}

/// <summary>
/// The persistent record of system changes waiting to be restored (T-065): one JSON file per change in a machine-wide
/// folder (default <c>%LOCALAPPDATA%\LayerPeek.E2E.Restore</c>; the system state is shared by every worktree).
/// <para>
/// The original state is written <b>before</b> the change, and older entries of a crashed process are restored
/// <b>before</b> the original is read, so a crash never makes a test state look like the original. An entry is removed
/// only after its restore was checked; the empty folder is removed too. An entry belongs to the process that wrote it:
/// others restore it only once that process is gone, so a cleanup can never undo the change of a test still running.
/// Every operation runs under the lock file next to the folder.
/// </para>
/// </summary>
internal sealed class SystemChangeJournal
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly IReadOnlyDictionary<string, ISystemChangeKind> _kinds;
    private readonly Func<ProcessOwner, bool> _isAlive;
    private readonly Action? _beforeFirstChange;
    private readonly Lock _firstChangeLock = new();
    private bool _firstChangeDone;

    /// <param name="directory">The journal folder.</param>
    /// <param name="kinds">Every kind an entry may name, by <see cref="ISystemChangeKind.Name"/>.</param>
    /// <param name="self">The owner of entries this journal writes.</param>
    /// <param name="isAlive">Whether an owner still runs.</param>
    /// <param name="beforeFirstChange">Called once before the first entry is written (starts the watchdog).</param>
    public SystemChangeJournal(
        string directory,
        IReadOnlyDictionary<string, ISystemChangeKind> kinds,
        ProcessOwner self,
        Func<ProcessOwner, bool> isAlive,
        Action? beforeFirstChange = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        _kinds = kinds;
        Self = self;
        _isAlive = isAlive;
        _beforeFirstChange = beforeFirstChange;
    }

    public string Directory { get; }

    public ProcessOwner Self { get; }

    /// <summary>Lock file next to the folder (inside it, the folder could never be removed).</summary>
    public string LockPath => Directory + ".lock";

    /// <summary>The journal files waiting to be restored.</summary>
    public IReadOnlyList<string> PendingFiles() =>
        System.IO.Directory.Exists(Directory) ? [.. System.IO.Directory.EnumerateFiles(Directory, "*.json").Order(StringComparer.Ordinal)] : [];

    /// <summary>
    /// Records the original state of <paramref name="kind"/> (after restoring entries of crashed processes) and returns
    /// the entry; the caller changes the system only afterwards.
    /// </summary>
    public async Task<SystemChangeEntry> BeginAsync(ISystemChangeKind kind, string? key, CancellationToken cancellationToken)
    {
        if (!_kinds.TryGetValue(kind.Name, out ISystemChangeKind? registered) || !ReferenceEquals(registered, kind))
        {
            throw new InvalidOperationException($"The kind '{kind.Name}' is not registered, so a crash could not be restored.");
        }

        using TestRunLock journalLock = await TestRunLock.AcquireFileAsync(LockPath, LockTimeout).ConfigureAwait(false);
        string file = FileOf(kind.Name, key);

        // First the leftovers of crashed processes: only then is the current state the original one.
        SystemChangeRestoreReport stale = new();
        await RestoreEntriesAsync(stale, includeOwn: false, cancellationToken).ConfigureAwait(false);
        if (stale.Failed.Count != 0)
        {
            throw new InvalidOperationException($"Changes of a crashed test run could not be restored, so no new change starts.{Environment.NewLine}{stale}");
        }

        if (System.IO.File.Exists(file))
        {
            SystemChangeEntry existing = Read(file);
            throw new InvalidOperationException(existing.Owner == Self
                ? $"A {kind.Name} change of this process is still active ('{file}'); dispose it first."
                : $"A {kind.Name} change of {existing.Owner}, which still runs, is pending ('{file}').");
        }

        JsonElement original = await kind.ReadAsync(key, cancellationToken).ConfigureAwait(false);
        StartWatchdogOnce();
        SystemChangeEntry entry = new(kind.Name, key, Self, DateTimeOffset.Now, original, kind.Describe(key, original)) { File = file };
        Write(entry);
        return entry;
    }

    /// <summary>
    /// Restores the entry, checks the state and removes the entry; a failed check keeps it and throws. An entry whose file
    /// vanished (deleted by hand, or restored by someone who took this process for gone) is still restored from memory,
    /// then the change fails: the protection of the journal was broken meanwhile.
    /// </summary>
    public async Task CompleteAsync(SystemChangeEntry entry, CancellationToken cancellationToken)
    {
        using TestRunLock journalLock = await TestRunLock.AcquireFileAsync(LockPath, LockTimeout).ConfigureAwait(false);
        bool vanished = !System.IO.File.Exists(entry.File);
        await RestoreAsync(entry, cancellationToken).ConfigureAwait(false);
        DeleteFolderIfEmpty();
        if (vanished)
        {
            throw new InvalidOperationException(
                $"The journal entry '{entry.File}' vanished before its restore; the {entry.Kind} state was restored from memory and checked, "
                + "but nothing protected it in the meantime.");
        }
    }

    /// <summary>Restores the entry and checks the state, keeping the entry (the change stays open).</summary>
    public async Task RestoreKeepingEntryAsync(SystemChangeEntry entry, CancellationToken cancellationToken)
    {
        using TestRunLock journalLock = await TestRunLock.AcquireFileAsync(LockPath, LockTimeout).ConfigureAwait(false);
        await RestoreAsync(entry, cancellationToken, deleteEntry: false).ConfigureAwait(false);
    }

    /// <summary>
    /// Restores every pending entry whose owner is gone (with <paramref name="includeOwn"/> also the own ones, e.g. at
    /// the end of a run). Entries of a running owner are waited for up to <paramref name="waitForOwners"/> and then
    /// reported as still owned. Without entries nothing is touched.
    /// </summary>
    public async Task<SystemChangeRestoreReport> RestorePendingAsync(bool includeOwn, TimeSpan waitForOwners, CancellationToken cancellationToken)
    {
        SystemChangeRestoreReport report = new();
        DateTime deadline = DateTime.UtcNow + waitForOwners;
        while (true)
        {
            if (!System.IO.Directory.Exists(Directory))
            {
                return report;
            }

            List<string> stillOwned;
            using (await TestRunLock.AcquireFileAsync(LockPath, LockTimeout).ConfigureAwait(false))
            {
                stillOwned = await RestoreEntriesAsync(report, includeOwn, cancellationToken).ConfigureAwait(false);
                DeleteFolderIfEmpty();
            }

            if (stillOwned.Count == 0)
            {
                return report;
            }

            if (DateTime.UtcNow >= deadline)
            {
                report.StillOwned.AddRange(stillOwned);
                return report;
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The file of an entry: <c>&lt;kind&gt;.json</c>, or <c>&lt;kind&gt;.&lt;hash of key&gt;.json</c>.</summary>
    public string FileOf(string kind, string? key) =>
        Path.Combine(Directory, key is null ? $"{kind}.json" : $"{kind}.{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 4))}.json");

    /// <summary>Under the lock: restores the entries of gone owners; returns the descriptions of entries of running ones.</summary>
    private async Task<List<string>> RestoreEntriesAsync(SystemChangeRestoreReport report, bool includeOwn, CancellationToken cancellationToken)
    {
        List<string> stillOwned = [];
        // A temporary file exists only inside Write under the lock: one seen now was left by a crash before its rename,
        // i.e. before the change was applied.
        if (System.IO.Directory.Exists(Directory))
        {
            foreach (string temporary in System.IO.Directory.EnumerateFiles(Directory, "*.json.tmp"))
            {
                await TestRunLock.RetryAsync($"delete '{temporary}'", () => System.IO.File.Delete(temporary)).ConfigureAwait(false);
            }
        }

        List<SystemChangeEntry> entries = [];
        foreach (string file in PendingFiles())
        {
            try
            {
                entries.Add(Read(file));
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException)
            {
                report.Failed.Add($"'{file}' is not readable ({ex.Message}); restore by hand.");
            }
        }

        // E.g. a switched-off monitor first: only then can the scale of that monitor go back.
        foreach (SystemChangeEntry entry in entries
            .OrderBy(entry => _kinds.TryGetValue(entry.Kind, out ISystemChangeKind? kind) ? kind.RestoreOrder : 0)
            .ThenBy(static entry => entry.File, StringComparer.Ordinal))
        {
            string file = entry.File;
            bool own = entry.Owner == Self;
            if (own && !includeOwn)
            {
                // An active change of this process: it restores it itself.
                continue;
            }

            if (!own && _isAlive(entry.Owner))
            {
                stillOwned.Add($"{entry.Kind} of {entry.Owner}: {entry.Description}");
                continue;
            }

            try
            {
                await RestoreAsync(entry, cancellationToken).ConfigureAwait(false);
                report.Restored.Add($"{entry.Kind} of {entry.Owner}: {entry.Description}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                report.Failed.Add($"{entry.Kind} of {entry.Owner} ('{file}'): {ex.Message}{Environment.NewLine}{entry.Description}");
            }
        }

        return stillOwned;
    }

    /// <summary>Restores one entry and removes it once the state is checked.</summary>
    private async Task RestoreAsync(SystemChangeEntry entry, CancellationToken cancellationToken, bool deleteEntry = true)
    {
        if (!_kinds.TryGetValue(entry.Kind, out ISystemChangeKind? kind))
        {
            throw new InvalidOperationException($"Unknown kind '{entry.Kind}' in '{entry.File}'; restore by hand: {entry.Description}");
        }

        await kind.RestoreAsync(entry.Key, entry.Original, cancellationToken).ConfigureAwait(false);
        JsonElement current = await kind.ReadAsync(entry.Key, cancellationToken).ConfigureAwait(false);
        if (!kind.Matches(entry.Original, current))
        {
            throw new InvalidOperationException(
                $"The {entry.Kind} state is not back after the restore (kept in '{entry.File}').{Environment.NewLine}"
                + $"Original: {entry.Original.GetRawText()}{Environment.NewLine}Now: {current.GetRawText()}{Environment.NewLine}{entry.Description}");
        }

        if (deleteEntry)
        {
            await TestRunLock.RetryAsync($"delete '{entry.File}'", () => System.IO.File.Delete(entry.File)).ConfigureAwait(false);
        }
    }

    private void StartWatchdogOnce()
    {
        lock (_firstChangeLock)
        {
            if (_firstChangeDone)
            {
                return;
            }

            _beforeFirstChange?.Invoke();
            _firstChangeDone = true;
        }
    }

    private static SystemChangeEntry Read(string file)
    {
        SystemChangeEntry entry = JsonSerializer.Deserialize(System.IO.File.ReadAllText(file), SystemChangeJson.Default.SystemChangeEntry)
            ?? throw new InvalidOperationException("Empty entry.");
        return entry with { File = file };
    }

    /// <summary>
    /// Written to a temporary file, flushed to the disk and renamed, so neither a crash nor a power cut (restart in the
    /// middle of a run) leaves a half-written entry.
    /// </summary>
    private void Write(SystemChangeEntry entry)
    {
        System.IO.Directory.CreateDirectory(Directory);
        string temporary = entry.File + ".tmp";
        using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(stream, entry, SystemChangeJson.Default.SystemChangeEntry);
            stream.Flush(flushToDisk: true);
        }

        System.IO.File.Move(temporary, entry.File, overwrite: false);
    }

    private void DeleteFolderIfEmpty()
    {
        if (System.IO.Directory.Exists(Directory) && !System.IO.Directory.EnumerateFileSystemEntries(Directory).Any())
        {
            System.IO.Directory.Delete(Directory);
        }
    }
}
