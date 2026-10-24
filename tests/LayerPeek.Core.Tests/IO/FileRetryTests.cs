using LayerPeek.Core.IO;
using LayerPeek.Tests.Shared;

namespace LayerPeek.Core.Tests.IO;

/// <summary>Which file errors <see cref="FileRetry"/> waits out (user decision 2026-10-22).</summary>
public sealed class FileRetryTests
{
    [Theory]
    [InlineData(unchecked((int)0x80070020), false, true)] // ERROR_SHARING_VIOLATION
    [InlineData(unchecked((int)0x80070021), false, true)] // ERROR_LOCK_VIOLATION
    [InlineData(unchecked((int)0x80070020), true, true)]
    [InlineData(unchecked((int)0x80070070), false, false)] // ERROR_DISK_FULL
    [InlineData(unchecked((int)0x80070070), true, false)]
    [InlineData(unchecked((int)0x80070050), true, false)] // ERROR_FILE_EXISTS
    public void OnlySharingAndLockViolationsOfIoErrorsAreTransient(int hResult, bool replacing, bool expected)
    {
        Assert.Equal(expected, FileRetry.IsTransient(new IOException("test", hResult), replacing));
    }

    [Fact]
    public void AccessDeniedIsTransientOnlyWhileReplacing()
    {
        // A scanner holding the target makes the atomic replace fail with "access denied"; opening for reading does not.
        Assert.True(FileRetry.IsTransient(new UnauthorizedAccessException("test"), replacing: true));
        Assert.False(FileRetry.IsTransient(new UnauthorizedAccessException("test"), replacing: false));
    }

    [Fact]
    public void MissingFilesAndOtherExceptionsAreNotTransient()
    {
        Assert.False(FileRetry.IsTransient(new FileNotFoundException("test"), replacing: true));
        Assert.False(FileRetry.IsTransient(new DirectoryNotFoundException("test"), replacing: true));
        Assert.False(FileRetry.IsTransient(new InvalidOperationException("test"), replacing: true));
        Assert.False(FileRetry.IsTransient(new OperationCanceledException(), replacing: true));
    }

    [Fact]
    public void RetryDelaysGrowAndStayShort()
    {
        IReadOnlyList<TimeSpan> delays = FileRetry.Delays;

        Assert.Equal(5, delays.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(50), delays[0]);
        for (int i = 1; i < delays.Count; i++)
        {
            Assert.True(delays[i] > delays[i - 1], "Each retry waits longer than the one before.");
        }

        Assert.Equal(TimeSpan.FromMilliseconds(1550), delays.Aggregate(TimeSpan.Zero, static (sum, delay) => sum + delay));
    }

    [Fact]
    public async Task TransientErrorIsRetriedAfterEachDelayThenThrown()
    {
        ScriptedDelayTimeProvider time = new();
        int attempts = 0;
        IOException last = new("held", SharingViolation);

        IOException error = await Assert.ThrowsAsync<IOException>(() => FileRetry.RunAsync(
            _ =>
            {
                attempts++;
                throw attempts == FileRetry.Delays.Count + 1 ? last : new IOException("held", SharingViolation);
            },
            0,
            replacing: false,
            time,
            TestContext.Current.CancellationToken));

        Assert.Same(last, error);
        Assert.Equal(FileRetry.Delays.Count + 1, attempts);
        Assert.Equal(FileRetry.Delays, time.Delays);
    }

    [Fact]
    public async Task SucceedsOnceTheErrorIsGone()
    {
        ScriptedDelayTimeProvider time = new();
        int attempts = 0;

        int result = await FileRetry.RunAsync(
            (_, _) => ++attempts < 3 ? throw new IOException("held", SharingViolation) : Task.FromResult(attempts),
            0,
            replacing: false,
            time,
            TestContext.Current.CancellationToken);

        Assert.Equal(3, result);
        Assert.Equal(FileRetry.Delays.Take(2), time.Delays);
    }

    [Fact]
    public async Task OtherErrorsAndCancellationAreNotRetried()
    {
        ScriptedDelayTimeProvider time = new();
        int attempts = 0;

        await Assert.ThrowsAsync<IOException>(() => FileRetry.RunAsync(_ => { attempts++; throw new IOException("disk full", unchecked((int)0x80070070)); }, 0, replacing: true, time, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<OperationCanceledException>(() => FileRetry.RunAsync(_ => { attempts++; throw new OperationCanceledException(); }, 0, replacing: true, time, TestContext.Current.CancellationToken));

        Assert.Equal(2, attempts);
        Assert.Empty(time.Delays);
    }

    private const int SharingViolation = unchecked((int)0x80070020);
}
