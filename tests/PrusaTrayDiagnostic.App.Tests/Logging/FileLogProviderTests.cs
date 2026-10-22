using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.Logging;

namespace PrusaTrayDiagnostic.App.Tests.Logging;

/// <summary>The log file of T-024: format, levels, queue, flush, rotation and two writers sharing one file.</summary>
public sealed partial class FileLogProviderTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly string _folder = TestDirectories.NewDirectoryPath();
    private readonly List<IDisposable> _owned = [];

    public void Dispose()
    {
        foreach (IDisposable owned in Enumerable.Reverse(_owned))
        {
            owned.Dispose();
        }

        TestDirectories.Delete(_folder);
    }

    [Fact]
    public void RecordCarriesTimeWithOffsetLevelPidAndCategory()
    {
        DateTimeOffset time = new(2026, 10, 21, 14, 3, 12, 345, TimeSpan.FromHours(2));

        string record = LogLineFormatter.Format(time, LogLevel.Information, 12345, "Tray", "Started.", null);

        Assert.Equal("2026-10-21 14:03:12.345 +02:00 INF [pid 12345] Tray: Started.\n", record);
    }

    [Theory]
    [InlineData(LogLevel.Trace, "TRC")]
    [InlineData(LogLevel.Debug, "DBG")]
    [InlineData(LogLevel.Information, "INF")]
    [InlineData(LogLevel.Warning, "WRN")]
    [InlineData(LogLevel.Error, "ERR")]
    [InlineData(LogLevel.Critical, "CRT")]
    public void LevelsHaveThreeLetters(LogLevel level, string name) => Assert.Equal(name, LogLineFormatter.LevelName(level));

    [Fact]
    public void ExceptionAndLineBreaksContinueOnIndentedLines()
    {
        Exception exception = Thrown();

        string record = LogLineFormatter.Format(DateTimeOffset.Now, LogLevel.Error, 1, "Cat", "first\r\nsecond\rthird\u2028fourth\u0085fifth\n", exception);

        string[] lines = record.TrimEnd('\n').Split('\n');
        Assert.EndsWith("Cat: first", lines[0], StringComparison.Ordinal);
        Assert.Equal(
            [LogLineFormatter.Indent + "second", LogLineFormatter.Indent + "third", LogLineFormatter.Indent + "fourth", LogLineFormatter.Indent + "fifth"],
            lines[1..5]);
        Assert.All(lines[1..], line => Assert.StartsWith(LogLineFormatter.Indent, line, StringComparison.Ordinal));
        Assert.Contains("System.InvalidOperationException: outer", lines[5], StringComparison.Ordinal);
        Assert.DoesNotContain('\u2028', record);
        // The whole exception: inner exception and the stack trace with method names.
        Assert.Contains("inner", record, StringComparison.Ordinal);
        Assert.Contains(nameof(Thrown), record, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', record);
    }

    [Fact]
    public void FileIsUtf8WithoutBomWithLfLineEndsAndOneRecordPerCall()
    {
        FileLogProvider provider = NewProvider(out _);
        ILogger logger = provider.CreateLogger("Cat");

        logger.Write(LogLevel.Information, "Žluťoučký kůň");
        logger.Write(LogLevel.Warning, "second");

        Assert.True(provider.Flush(Wait));
        byte[] bytes = File.ReadAllBytes(LogPath(0));
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble), "No BOM.");
        string text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain('\r', text);
        string[] lines = text.TrimEnd('\n').Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Matches(RecordStart(), lines[0]);
        Assert.EndsWith($"INF [pid {Environment.ProcessId}] Cat: Žluťoučký kůň", lines[0], StringComparison.Ordinal);
        Assert.Contains(" WRN ", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void MinimumLevelSwitchesWhileRunning()
    {
        FileLogProvider provider = NewProvider(out LogLevelSwitch level);
        ILogger logger = provider.CreateLogger("Cat");

        Assert.False(logger.IsEnabled(LogLevel.Debug));
        logger.Write(LogLevel.Debug, "hidden");
        level.MinimumLevel = LogLevel.Debug;
        Assert.True(logger.IsEnabled(LogLevel.Debug));
        logger.Write(LogLevel.Debug, "shown");
        level.MinimumLevel = LogLevel.Information;
        logger.Write(LogLevel.Debug, "hidden again");
        Assert.False(logger.IsEnabled(LogLevel.None));

        Assert.True(provider.Flush(Wait));
        string text = File.ReadAllText(LogPath(0));
        Assert.Contains("DBG", text, StringComparison.Ordinal);
        Assert.Contains("shown", text, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FlushWaitsForEverythingLoggedFromManyThreads()
    {
        FileLogProvider provider = NewProvider(out _);

        Parallel.For(0, 8, thread =>
        {
            ILogger logger = provider.CreateLogger($"T{thread}");
            for (int i = 0; i < 500; i++)
            {
                logger.Write(LogLevel.Information, $"record {i}");
            }
        });

        Assert.True(provider.Flush(Wait));
        string[] lines = ReadRecords(LogPath(0));
        Assert.Equal(8 * 500, lines.Length);
        Assert.All(lines, line => Assert.Matches(RecordStart(), line));
        for (int thread = 0; thread < 8; thread++)
        {
            // In order per thread.
            string category = $"] T{thread}: record ";
            int[] indexes = [.. lines.Where(line => line.Contains(category, StringComparison.Ordinal)).Select(line => int.Parse(line[(line.LastIndexOf(' ') + 1)..], CultureInfo.InvariantCulture))];
            Assert.Equal(Enumerable.Range(0, 500), indexes);
        }
    }

    [Fact]
    public void TwoIndependentWritersLoseAndSplitNoRecords()
    {
        // Like two processes: each has its own file object (own mutex handle) and writer thread; the first one rotates.
        LogFile primaryFile = Own(new LogFile(_folder, maxBytes: 64 * 1024, fileCount: 100));
        primaryFile.RotationEnabled = true;
        FileLogProvider primary = Own(new FileLogProvider(primaryFile, new LogLevelSwitch(LogLevel.Information)));
        FileLogProvider secondary = Own(new FileLogProvider(Own(new LogFile(_folder, maxBytes: 64 * 1024, fileCount: 100)), new LogLevelSwitch(LogLevel.Information)));

        const int PerThread = 1000;
        Parallel.For(0, 8, thread =>
        {
            ILogger logger = (thread % 2 == 0 ? primary : secondary).CreateLogger($"W{thread}");
            string padding = new('x', 40 + thread);
            for (int i = 0; i < PerThread; i++)
            {
                logger.Write(LogLevel.Information, $"{i} {padding}");
            }
        });

        Assert.True(primary.Flush(Wait));
        Assert.True(secondary.Flush(Wait));
        string[] files = Directory.GetFiles(_folder);
        Assert.True(files.Length > 1, "The primary writer rotated.");
        string[] lines = [.. files.SelectMany(ReadRecords)];
        Assert.Equal(8 * PerThread, lines.Length);
        Assert.All(lines, line => Assert.Matches(RecordStart(), line));
        Assert.Equal(8 * PerThread, lines.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void RotationKeepsTheNewestFilesOnly()
    {
        LogFile file = Own(new LogFile(_folder, maxBytes: 1000, fileCount: 5));
        file.RotationEnabled = true;
        byte[] record = Encoding.UTF8.GetBytes(new string('r', 299) + "\n");

        for (int i = 0; i < 40; i++)
        {
            file.Append(record);
        }

        Assert.Equal(
            ["PrusaTrayDiagnostic.1.log", "PrusaTrayDiagnostic.2.log", "PrusaTrayDiagnostic.3.log", "PrusaTrayDiagnostic.4.log", LogFile.FileName],
            Directory.GetFiles(_folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.All(Directory.GetFiles(_folder), path => Assert.InRange(new FileInfo(path).Length, 1, 1000));
    }

    [Fact]
    public void WithoutRotationTheFileOnlyGrows()
    {
        LogFile file = Own(new LogFile(_folder, maxBytes: 1000, fileCount: 5));
        byte[] record = Encoding.UTF8.GetBytes(new string('r', 299) + "\n");

        for (int i = 0; i < 10; i++)
        {
            file.Append(record);
        }

        Assert.Equal([LogFile.FileName], Directory.GetFiles(_folder).Select(Path.GetFileName));
        Assert.Equal(3000, new FileInfo(LogPath(0)).Length);
    }

    [Fact]
    public void RotationThatFailsIsTriedAgainLater()
    {
        // Only Windows refuses to rename a file open without delete sharing.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows file sharing only.");
        LogFile file = Own(new LogFile(_folder, maxBytes: 1000, fileCount: 5));
        file.RotationEnabled = true;
        Directory.CreateDirectory(_folder);
        for (int index = 1; index <= 4; index++)
        {
            File.WriteAllText(LogPath(index), $"old {index}\n");
        }

        byte[] record = Encoding.UTF8.GetBytes(new string('r', 299) + "\n");
        for (int i = 0; i < 3; i++)
        {
            file.Append(record);
        }

        // A reader without delete sharing (an editor, antivirus) blocks renaming the current file, not appending to it;
        // every retry leaves the older files alone.
        using (new FileStream(LogPath(0), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            for (int i = 0; i < 3; i++)
            {
                file.Append(record);
            }

            Assert.Equal(1800, new FileInfo(LogPath(0)).Length);
            for (int index = 1; index <= 4; index++)
            {
                Assert.Equal($"old {index}\n", File.ReadAllText(LogPath(index)));
            }
        }

        file.Append(record);

        // One shift: the oldest goes, the others move up by one, and the full current file is the newest of them.
        Assert.Equal(300, new FileInfo(LogPath(0)).Length);
        Assert.Equal(1800, new FileInfo(LogPath(1)).Length);
        Assert.Equal(["old 1\n", "old 2\n", "old 3\n"], [File.ReadAllText(LogPath(2)), File.ReadAllText(LogPath(3)), File.ReadAllText(LogPath(4))]);
        Assert.False(File.Exists(Path.Combine(_folder, LogFile.RotatingFileName)));
    }

    [Fact]
    public void RotationCutShortIsFinishedWithoutShiftingTwice()
    {
        LogFile file = Own(new LogFile(_folder, maxBytes: 1000, fileCount: 5));
        file.RotationEnabled = true;
        Directory.CreateDirectory(_folder);
        // A crash after the older files shifted: .1 is free, the old current file still under its rotating name.
        File.WriteAllText(Path.Combine(_folder, LogFile.RotatingFileName), "was current\n");
        File.WriteAllText(LogPath(2), "old 1\n");
        File.WriteAllText(LogPath(3), "old 2\n");
        File.WriteAllText(LogPath(0), new string('r', 999) + "\n");

        file.Append(Encoding.UTF8.GetBytes("new\n"));

        // The cut-short rotation finishes into the free .1 first, then the full current file rotates: each older file
        // moved exactly once, nothing lost.
        Assert.Equal("new\n", File.ReadAllText(LogPath(0)));
        Assert.Equal(1000, new FileInfo(LogPath(1)).Length);
        Assert.Equal(
            ["was current\n", "old 1\n", "old 2\n"],
            [File.ReadAllText(LogPath(2)), File.ReadAllText(LogPath(3)), File.ReadAllText(LogPath(4))]);
        Assert.False(File.Exists(Path.Combine(_folder, LogFile.RotatingFileName)));
    }

    [Fact]
    public void FullQueueDropsRecordsAndSaysHowMany()
    {
        LogFile file = Own(new LogFile(_folder));
        FileLogProvider provider = Own(new FileLogProvider(file, new LogLevelSwitch(LogLevel.Information), queueCapacity: 10));
        ILogger logger = provider.CreateLogger("Cat");
        const int Total = 30;

        // Another process writing holds the file's mutex: the writer waits and the queue fills up.
        using (HoldFileLock(file))
        {
            for (int i = 0; i < Total; i++)
            {
                logger.Write(LogLevel.Information, $"record {i}");
            }
        }

        Assert.True(provider.Flush(Wait));
        logger.Write(LogLevel.Information, "after");
        Assert.True(provider.Flush(Wait));
        string[] lines = ReadRecords(LogPath(0));
        int written = lines.Count(line => line.Contains("Cat: record ", StringComparison.Ordinal));
        // Drops while the writer was busy with a later batch are reported with the one after it; on a slow machine the
        // stuck batch may even outlast the lock timeout and count as unwritten.
        string[] reports = [.. lines.Where(line => DroppedReport().IsMatch(line))];
        int dropped = reports.Sum(report => int.Parse(DroppedReport().Match(report).Groups[1].Value, CultureInfo.InvariantCulture));
        // The queue holds 10; the writer may have taken a few into the batch it is stuck with.
        Assert.InRange(written, 10, Total - 1);
        Assert.True(dropped > 0);
        Assert.Equal(Total, written + dropped);
        Assert.All(reports, report => Assert.Contains(" WRN ", report, StringComparison.Ordinal));
        Assert.EndsWith("Cat: after", lines[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void FlushGivesUpAfterItsTimeout()
    {
        LogFile file = Own(new LogFile(_folder));
        FileLogProvider provider = Own(new FileLogProvider(file, new LogLevelSwitch(LogLevel.Information)));

        using (HoldFileLock(file))
        {
            provider.CreateLogger("Cat").Write(LogLevel.Information, "waiting");
            Assert.False(provider.Flush(TimeSpan.FromMilliseconds(100)));
        }

        Assert.True(provider.Flush(Wait));
    }

    [Fact]
    public void DisposeWritesTheQueueAndLaterRecordsAreDroppedSilently()
    {
        FileLogProvider provider = NewProvider(out _);
        ILogger logger = provider.CreateLogger("Cat");
        for (int i = 0; i < 100; i++)
        {
            logger.Write(LogLevel.Information, $"before {i}");
        }

        provider.Dispose();
        logger.Write(LogLevel.Error, "after", new InvalidOperationException("late"));
        provider.Dispose();

        Assert.True(provider.Flush(Wait));
        string[] lines = ReadRecords(LogPath(0));
        Assert.Equal(100, lines.Length);
        Assert.DoesNotContain(lines, line => line.Contains("after", StringComparison.Ordinal));
    }

    [Fact]
    public void UnwritableFolderNeverBreaksLogging()
    {
        // A file where the folder should be: every write fails.
        Directory.CreateDirectory(_folder);
        string blocked = Path.Combine(_folder, "blocked");
        File.WriteAllText(blocked, "not a folder");
        FileLogProvider provider = Own(new FileLogProvider(Own(new LogFile(blocked)), new LogLevelSwitch(LogLevel.Information)));
        ILogger logger = provider.CreateLogger("Cat");

        logger.Write(LogLevel.Information, "lost");
        Assert.True(provider.Flush(Wait));

        // The writer goes on: a folder that appears later is written, and the file says what was lost.
        File.Delete(blocked);
        logger.Write(LogLevel.Information, "kept");
        Assert.True(provider.Flush(Wait));
        string[] lines = ReadRecords(LogFile.PathOf(blocked, 0));
        Assert.Equal(2, lines.Length);
        Assert.EndsWith("Cat: kept", lines[0], StringComparison.Ordinal);
        Assert.EndsWith("Log messages lost: 1 (writing the log file failed).", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void ALockHeldTooLongLosesTheBatchInsteadOfOverwriting()
    {
        LogFile file = Own(new LogFile(_folder));
        FileLogProvider provider = Own(new FileLogProvider(file, new LogLevelSwitch(LogLevel.Information)));
        ILogger logger = provider.CreateLogger("Cat");

        // Another process stuck inside its write: this one gives up after the lock timeout and never writes unlocked.
        using (HoldFileLock(file))
        {
            logger.Write(LogLevel.Information, "lost");
            Assert.True(provider.Flush(Wait));
            Assert.False(File.Exists(LogPath(0)));
        }

        logger.Write(LogLevel.Information, "kept");
        Assert.True(provider.Flush(Wait));
        string[] lines = ReadRecords(LogPath(0));
        Assert.EndsWith("Cat: kept", lines[0], StringComparison.Ordinal);
        Assert.EndsWith("Log messages lost: 1 (writing the log file failed).", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void FlushWithoutTimeLimitWaitsForTheWriter()
    {
        FileLogProvider provider = NewProvider(out _);
        provider.CreateLogger("Cat").Write(LogLevel.Information, "record");

        Assert.True(provider.Flush(Timeout.InfiniteTimeSpan));
        Assert.Single(ReadRecords(LogPath(0)));
    }

    [Fact]
    public void DeletedFolderIsCreatedAgain()
    {
        FileLogProvider provider = NewProvider(out _);
        ILogger logger = provider.CreateLogger("Cat");
        logger.Write(LogLevel.Information, "first");
        Assert.True(provider.Flush(Wait));

        Directory.Delete(_folder, recursive: true);
        logger.Write(LogLevel.Information, "second");

        Assert.True(provider.Flush(Wait));
        Assert.EndsWith("Cat: second", Assert.Single(ReadRecords(LogPath(0))), StringComparison.Ordinal);
    }

    [Fact]
    public void RecordAfterAWriteThatFailedHalfWayStartsALine()
    {
        LogFile file = Own(new LogFile(_folder));
        Directory.CreateDirectory(_folder);
        File.WriteAllText(LogPath(0), "complete\nhalf a rec");

        file.Append(Encoding.UTF8.GetBytes("next\n"));

        Assert.Equal("complete\nhalf a rec\nnext\n", File.ReadAllText(LogPath(0)));
    }

    [Fact]
    public void LeftoverOfARotationIsFinishedWithTheFirstWrite()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, LogFile.RotatingFileName), "was current\n");
        File.WriteAllText(LogPath(1), "old 1\n");
        LogFile file = Own(new LogFile(_folder));

        // A second start (no rotation) leaves it to the primary instance.
        file.Append(Encoding.UTF8.GetBytes("secondary\n"));
        Assert.True(File.Exists(Path.Combine(_folder, LogFile.RotatingFileName)));

        file.RotationEnabled = true;
        file.Append(Encoding.UTF8.GetBytes("primary\n"));

        Assert.False(File.Exists(Path.Combine(_folder, LogFile.RotatingFileName)));
        Assert.Equal("was current\n", File.ReadAllText(LogPath(1)));
        Assert.Equal("old 1\n", File.ReadAllText(LogPath(2)));
        Assert.Equal("secondary\nprimary\n", File.ReadAllText(LogPath(0)));
    }

    [Fact]
    public void WindowsMutexCoversEverySessionOfTheUser()
    {
        string name = Own(new LogFile(_folder)).MutexName;

        if (OperatingSystem.IsWindows())
        {
            Assert.StartsWith(@"Global\PrusaTrayDiagnostic.Log.", name, StringComparison.Ordinal);
            // Compared like the file system: one file, one mutex however its path is spelled.
            Assert.Equal(name, Own(new LogFile(_folder.ToUpperInvariant())).MutexName);
        }

        Assert.NotEqual(name, Own(new LogFile(Path.Combine(_folder, "other"))).MutexName);
    }

    [Fact]
    public void LogFileRejectsARelativeFolder() =>
        Assert.Throws<ArgumentException>(() => new LogFile(Path.Combine("relative", "logs")));

    private FileLogProvider NewProvider(out LogLevelSwitch level)
    {
        level = new LogLevelSwitch(LogLevel.Information);
        return Own(new FileLogProvider(Own(new LogFile(_folder)), level));
    }

    private T Own<T>(T disposable)
        where T : IDisposable
    {
        _owned.Add(disposable);
        return disposable;
    }

    private string LogPath(int index) => LogFile.PathOf(_folder, index);

    /// <summary>The records of a file (a record is a line that is not indented), read with sharing like a support tool.</summary>
    private static string[] ReadRecords(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using StreamReader reader = new(stream, Encoding.UTF8);
        return [.. reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(line => !line.StartsWith(LogLineFormatter.Indent, StringComparison.Ordinal))];
    }

    /// <summary>Holds the mutex of <paramref name="file"/> on a thread of its own until disposed, like a writing process.</summary>
    private static Release HoldFileLock(LogFile file)
    {
        using ManualResetEventSlim held = new();
        ManualResetEventSlim release = new();
        Thread holder = new(() =>
        {
            using Mutex mutex = new(initiallyOwned: false, file.MutexName);
            mutex.WaitOne();
            held.Set();
            release.Wait();
            mutex.ReleaseMutex();
        });
        holder.Start();
        held.Wait();
        return new Release(release, holder);
    }

    private static InvalidOperationException Thrown()
    {
        try
        {
            try
            {
                throw new ArgumentException("inner");
            }
            catch (ArgumentException inner)
            {
                throw new InvalidOperationException("outer", inner);
            }
        }
        catch (InvalidOperationException ex)
        {
            return ex;
        }
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2} (TRC|DBG|INF|WRN|ERR|CRT) \[pid \d+\] \S+: ")]
    private static partial Regex RecordStart();

    [GeneratedRegex(@"Log messages lost: (\d+) \((the log queue was full|writing the log file failed)\)\.$")]
    private static partial Regex DroppedReport();

    private sealed class Release(ManualResetEventSlim release, Thread holder) : IDisposable
    {
        public void Dispose()
        {
            release.Set();
            holder.Join();
            release.Dispose();
        }
    }
}
