using PrusaTrayDiagnostic.Tests.Shared;

namespace PrusaTrayDiagnostic.Core.Tests.TestInfrastructure;

/// <summary>
/// Locks and crash sweep of folders reserved per worktree (T-032). Every test works in its own sandbox under
/// <see cref="TestDirectories.Root"/> (as the parent instead of %TEMP%); fake worktrees are folders in the sandbox, a
/// "deleted" worktree is a path there that does not exist.
/// </summary>
public sealed class ReservedTempDirectoryTests : IDisposable
{
    private const string Name = "Probe.Tests";

    private readonly string _sandbox = Directory.CreateDirectory(TestDirectories.NewDirectoryPath()).FullName;

    public void Dispose() => TestDirectories.Delete(_sandbox);

    [Fact]
    public void FolderAndLockCarryTheWorktreeId()
    {
        ReservedTempDirectory reserved = Reserve(ExistingWorktree("a"));

        Assert.Equal(Path.Combine(_sandbox, $"{Name}.{reserved.Id}"), reserved.Path);
        Assert.Equal(reserved.Path + ".lock", reserved.LockPath);
        Assert.Equal(TestWorktree.IdOf(reserved.Worktree), reserved.Id);
    }

    [Fact]
    public async Task CreateWritesTheMarkerWithTheWorktreePath()
    {
        string worktree = ExistingWorktree("a");
        ReservedTempDirectory reserved = Reserve(worktree + Path.DirectorySeparatorChar);

        await reserved.CreateAsync();

        Assert.Equal(TestWorktree.Normalize(worktree), File.ReadAllText(reserved.MarkerPath));
        Assert.True(reserved.HoldsOnlyMarker());
        File.WriteAllText(Path.Combine(reserved.Path, "failure.png"), "x");
        Assert.False(reserved.HoldsOnlyMarker());
        // Staged and renamed: nothing but the reserved folder (and the fake worktrees) is left in the parent.
        Assert.Equal([reserved.Path, Path.Combine(_sandbox, "worktrees")], Directory.GetDirectories(_sandbox).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task SweepRemovesStagingFoldersWhoseLockIsFree()
    {
        ReservedTempDirectory own = Reserve(ExistingWorktree("own"));
        ReservedTempDirectory other = Reserve(ExistingWorktree("other"));
        ReservedTempDirectory running = Reserve(ExistingWorktree("running"));
        // A crash between creating the staging folder and renaming it (any worktree, existing or not).
        string ownStaging = Stage(own);
        string otherStaging = Stage(other);
        string deletedStaging = Stage(Reserve(Path.Combine(_sandbox, "deleted-worktree")));
        string runningStaging = Stage(running);
        // Names that only look like staging folders.
        string[] foreign =
        [
            own.Path + ReservedTempDirectory.StagingInfix + "x",
            own.Path + ReservedTempDirectory.StagingInfix + Guid.NewGuid().ToString("N").ToUpperInvariant(),
            own.Path + ".tmp" + Guid.NewGuid().ToString("N"),
        ];
        foreach (string directory in foreign)
        {
            Directory.CreateDirectory(directory);
        }

        List<string> stopped = [];
        ReservedTempDirectory sweeping = Reserve(own.Worktree, beforeDelete: directory =>
        {
            stopped.Add(directory);
            return Task.CompletedTask;
        });
        using (await running.AcquireLockAsync(TimeSpan.FromSeconds(5)))
        {
            await SweepAsync(sweeping);
            Assert.True(Directory.Exists(runningStaging));
        }

        Assert.False(Directory.Exists(ownStaging));
        Assert.False(Directory.Exists(otherStaging));
        Assert.False(Directory.Exists(deletedStaging));
        Assert.All(foreign, static directory => Assert.True(Directory.Exists(directory), directory));
        // A staging folder never holds processes: only the own folder is prepared for deletion (E2E: kill browsers).
        Assert.Equal([own.Path], stopped);

        await SweepAsync(sweeping);
        Assert.False(Directory.Exists(runningStaging));
    }

    [Fact]
    public async Task SweepKeepsAFolderWhoseMarkerIsOversizedOrNotAPath()
    {
        ReservedTempDirectory own = Reserve(ExistingWorktree("own"));
        // Each would look orphaned (a deleted worktree) if its marker were trusted.
        ReservedTempDirectory huge = Reserve(Path.Combine(_sandbox, "deleted-huge"));
        ReservedTempDirectory nul = Reserve(Path.Combine(_sandbox, "deleted-nul"));
        ReservedTempDirectory relative = Reserve(Path.Combine(_sandbox, "deleted-relative"));
        ReservedTempDirectory folderMarker = Reserve(Path.Combine(_sandbox, "deleted-folder-marker"));
        Directory.CreateDirectory(huge.Path);
        File.WriteAllText(huge.MarkerPath, huge.Worktree + new string(' ', 64 * 1024));
        Directory.CreateDirectory(nul.Path);
        File.WriteAllText(nul.MarkerPath, nul.Worktree + "\0");
        Directory.CreateDirectory(relative.Path);
        File.WriteAllText(relative.MarkerPath, "deleted-relative");
        Directory.CreateDirectory(folderMarker.MarkerPath);

        await SweepAsync(own);

        Assert.True(Directory.Exists(huge.Path));
        Assert.True(Directory.Exists(nul.Path));
        Assert.True(Directory.Exists(relative.Path));
        Assert.True(Directory.Exists(folderMarker.Path));
    }

    [Fact]
    public async Task SweepDeletesAJunctionInsideAFolderWithoutFollowingIt()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Junctions exist on Windows only.");
        }

        ReservedTempDirectory own = Reserve(ExistingWorktree("own"));
        ReservedTempDirectory orphan = Reserve(Path.Combine(_sandbox, "deleted-worktree"));
        await orphan.CreateAsync();
        string outside = Directory.CreateDirectory(Path.Combine(_sandbox, "outside")).FullName;
        string precious = Path.Combine(outside, "precious.txt");
        File.WriteAllText(precious, "keep");
        string junction = Path.Combine(orphan.Path, "junction");
        using (System.Diagnostics.Process mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", ["/c", "mklink", "/J", junction, outside])
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        })!)
        {
            await mklink.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            await mklink.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(0, mklink.ExitCode);
        }

        Assert.True(File.Exists(Path.Combine(junction, "precious.txt")));

        await SweepAsync(own);

        Assert.False(Directory.Exists(orphan.Path));
        Assert.True(File.Exists(precious));
    }

    [Fact]
    public async Task CreateOverAnExistingFolderFailsAndLeavesNoStagingFolder()
    {
        ReservedTempDirectory reserved = Reserve(ExistingWorktree("a"));
        string leftover = Path.Combine(Directory.CreateDirectory(reserved.Path).FullName, "leftover.txt");
        File.WriteAllText(leftover, "x");

        await Assert.ThrowsAsync<InvalidOperationException>(reserved.CreateAsync);

        Assert.True(File.Exists(leftover));
        Assert.Empty(Directory.GetDirectories(_sandbox, "*" + ReservedTempDirectory.StagingInfix + "*"));
    }

    [Fact]
    public async Task DeletionThatFailsHalfWayKeepsTheMarker()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Only Windows refuses to delete an open file.");
        }

        ReservedTempDirectory reserved = Reserve(ExistingWorktree("a"));
        await reserved.CreateAsync();
        string busy = Path.Combine(reserved.Path, "z-busy.bin");
        Directory.CreateDirectory(Path.Combine(reserved.Path, "a-deleted"));

        using (new FileStream(busy, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(reserved.DeleteAsync);

            // The folder keeps its marker, so a later sweep can still tell its owner.
            Assert.True(File.Exists(reserved.MarkerPath));
        }

        await reserved.DeleteAsync();
        Assert.False(Directory.Exists(reserved.Path));
    }

    [Fact]
    public async Task SweepRemovesOwnLeftovers()
    {
        ReservedTempDirectory reserved = Reserve(ExistingWorktree("a"));
        await reserved.CreateAsync();
        Directory.CreateDirectory(Path.Combine(reserved.Path, "leftover", "deep"));

        using (await reserved.AcquireLockAsync(TimeSpan.FromSeconds(5)))
        {
            await reserved.SweepAsync();
        }

        Assert.False(Directory.Exists(reserved.Path));
    }

    [Fact]
    public async Task SweepRemovesFoldersOfADeletedWorktree()
    {
        ReservedTempDirectory own = Reserve(ExistingWorktree("own"));
        ReservedTempDirectory orphan = Reserve(Path.Combine(_sandbox, "deleted-worktree"));
        await orphan.CreateAsync();
        Directory.CreateDirectory(Path.Combine(orphan.Path, "profile"));
        List<string> stopped = [];
        ReservedTempDirectory sweeping = Reserve(own.Worktree, beforeDelete: directory =>
        {
            stopped.Add(directory);
            return Task.CompletedTask;
        });

        await SweepAsync(sweeping);

        Assert.False(Directory.Exists(orphan.Path));
        Assert.False(File.Exists(orphan.LockPath));
        // Its processes are stopped before the folder goes (E2E: browsers whose profile lies there).
        Assert.Contains(orphan.Path, stopped);
    }

    [Fact]
    public async Task SweepNeverTouchesTheFolderOfAnotherExistingWorktree()
    {
        ReservedTempDirectory own = Reserve(ExistingWorktree("own"));
        ReservedTempDirectory other = Reserve(ExistingWorktree("other"));
        await other.CreateAsync();
        string otherFile = Path.Combine(other.Path, "data.txt");
        File.WriteAllText(otherFile, "in use");
        List<string> stopped = [];
        ReservedTempDirectory sweeping = Reserve(own.Worktree, beforeDelete: directory =>
        {
            stopped.Add(directory);
            return Task.CompletedTask;
        });

        await SweepAsync(sweeping);

        Assert.True(File.Exists(otherFile));
        Assert.DoesNotContain(other.Path, stopped);
    }

    [Fact]
    public async Task SweepKeepsTheFolderOfADeletedWorktreeWhileItsRunHoldsTheLock()
    {
        ReservedTempDirectory own = Reserve(ExistingWorktree("own"));
        ReservedTempDirectory running = Reserve(Path.Combine(_sandbox, "deleted-worktree"));
        await running.CreateAsync();

        using (await running.AcquireLockAsync(TimeSpan.FromSeconds(5)))
        {
            await SweepAsync(own);
            Assert.True(File.Exists(running.MarkerPath));
        }

        // Once that run has ended, the next sweep removes it.
        await SweepAsync(own);
        Assert.False(Directory.Exists(running.Path));
    }

    [Fact]
    public async Task SweepKeepsFoldersWithoutAValidMarker()
    {
        ReservedTempDirectory own = Reserve(ExistingWorktree("own"));
        string deleted = Path.Combine(_sandbox, "deleted-worktree");
        // No marker (never left by CreateAsync, which stages the marker first; e.g. made by hand): the owner cannot be told.
        string noMarker = Directory.CreateDirectory(Reserve(deleted).Path).FullName;
        // A marker of another worktree than the folder name says: not one of ours.
        ReservedTempDirectory mismatched = Reserve(Path.Combine(_sandbox, "deleted-elsewhere"));
        Directory.CreateDirectory(mismatched.Path);
        File.WriteAllText(mismatched.MarkerPath, deleted);
        // Names that only look similar.
        string[] foreign =
        [
            Path.Combine(_sandbox, Name),
            Path.Combine(_sandbox, Name + ".Snapshots"),
            Path.Combine(_sandbox, Name + ".ABCDEF12"),
            Path.Combine(_sandbox, Name + ".abcdef123"),
            Path.Combine(_sandbox, "Other." + Name + ".abcdef12"),
        ];
        foreach (string directory in foreign)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, ReservedTempDirectory.MarkerFileName), deleted);
        }

        await SweepAsync(own);

        Assert.True(Directory.Exists(noMarker));
        Assert.True(Directory.Exists(mismatched.Path));
        Assert.All(foreign, static directory => Assert.True(Directory.Exists(directory), directory));
    }

    [Fact]
    public async Task SweepRemovesTheLegacyFolderWhenNoOlderRunHoldsTheLegacyLock()
    {
        string legacy = Directory.CreateDirectory(Path.Combine(_sandbox, Name + ".Legacy", "old")).Parent!.FullName;
        ReservedTempDirectory own = Reserve(ExistingWorktree("own"), legacyDirectoryName: Name + ".Legacy");

        await SweepAsync(own);

        Assert.False(Directory.Exists(legacy));
        Assert.False(File.Exists(Path.Combine(_sandbox, Name + ".lock")));
    }

    [Fact]
    public async Task SweepKeepsTheLegacyFolderWhileAnOlderRunHoldsTheLegacyLock()
    {
        string legacy = Directory.CreateDirectory(Path.Combine(_sandbox, Name + ".Legacy")).FullName;
        ReservedTempDirectory own = Reserve(ExistingWorktree("own"), legacyDirectoryName: Name + ".Legacy");

        using (await TestRunLock.AcquireFileAsync(Path.Combine(_sandbox, Name + ".lock"), TimeSpan.FromSeconds(5)))
        {
            await SweepAsync(own);
            Assert.True(Directory.Exists(legacy));
        }

        await SweepAsync(own);
        Assert.False(Directory.Exists(legacy));
    }

    [Fact]
    public async Task RunsInTheSameWorktreeAreSerialized()
    {
        string worktree = ExistingWorktree("a");
        ReservedTempDirectory first = Reserve(worktree);
        // The same worktree spelled differently is the same scope.
        ReservedTempDirectory second = Reserve(OperatingSystem.IsWindows() ? worktree.ToUpperInvariant() + "\\" : worktree + "/");
        Assert.Equal(first.LockPath, second.LockPath, ignoreCase: OperatingSystem.IsWindows());

        using (await first.AcquireLockAsync(TimeSpan.FromSeconds(5)))
        {
            Assert.Null(TestRunLock.TryAcquire(second.LockPath));
            TimeoutException timeout = await Assert.ThrowsAsync<TimeoutException>(() => second.AcquireLockAsync(TimeSpan.FromMilliseconds(300)));
            // Which lock and how long it waited.
            Assert.Contains(first.LockPath, timeout.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("after waiting", timeout.Message, StringComparison.Ordinal);
        }

        using TestRunLock next = await second.AcquireLockAsync(TimeSpan.FromSeconds(5));
        Assert.True(File.Exists(next.Path));
    }

    [Fact]
    public async Task RunsInDifferentWorktreesDoNotBlockEachOther()
    {
        ReservedTempDirectory first = Reserve(ExistingWorktree("a"));
        ReservedTempDirectory second = Reserve(ExistingWorktree("b"));

        using TestRunLock firstLock = await first.AcquireLockAsync(TimeSpan.FromSeconds(5));
        using TestRunLock? secondLock = TestRunLock.TryAcquire(second.LockPath);

        Assert.NotNull(secondLock);
        Assert.NotEqual(first.LockPath, second.LockPath);
    }

    [Fact]
    public async Task ReleasedLockLeavesNoFile()
    {
        ReservedTempDirectory reserved = Reserve(ExistingWorktree("a"));

        using (await reserved.AcquireLockAsync(TimeSpan.FromSeconds(5)))
        {
            Assert.True(File.Exists(reserved.LockPath));
        }

        Assert.False(File.Exists(reserved.LockPath));
    }

    private ReservedTempDirectory Reserve(string worktree, string? legacyDirectoryName = null, Func<string, Task>? beforeDelete = null) =>
        new(_sandbox, Name, worktree, legacyDirectoryName, beforeDelete);

    /// <summary>A staging folder of <paramref name="reserved"/> left by a crash before the rename (with its marker).</summary>
    private static string Stage(ReservedTempDirectory reserved)
    {
        string staging = Directory.CreateDirectory(reserved.Path + ReservedTempDirectory.StagingInfix + Guid.NewGuid().ToString("N")).FullName;
        File.WriteAllText(Path.Combine(staging, ReservedTempDirectory.MarkerFileName), reserved.Worktree);
        return staging;
    }

    private string ExistingWorktree(string name) => Directory.CreateDirectory(Path.Combine(_sandbox, "worktrees", name)).FullName;

    private static async Task SweepAsync(ReservedTempDirectory reserved)
    {
        using TestRunLock own = await reserved.AcquireLockAsync(TimeSpan.FromSeconds(5));
        await reserved.SweepAsync();
    }
}
