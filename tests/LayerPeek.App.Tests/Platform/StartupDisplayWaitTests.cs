using Microsoft.Extensions.Logging;

using LayerPeek.App.Platform;
using LayerPeek.App.Tests.Tray;

namespace LayerPeek.App.Tests.Platform;

/// <summary>
/// T-096: the UI starts only once a display is active (Avalonia.Native fails with kCVReturnInvalidDisplay otherwise), polls
/// while every display sleeps, never blocks on a failing check and stops on a quit signal. Fakes only: runs on every OS.
/// </summary>
public sealed class StartupDisplayWaitTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    private readonly ListLogger<StartupDisplayWait> _logger = new();
    private readonly List<TimeSpan> _pauses = [];

    [Fact]
    public void AnActiveDisplayStartsAtOnceWithoutPauseOrLog()
    {
        FakeDisplays displays = new((true, 1u, 0));

        Assert.Equal(StartupDisplayWaitResult.Ready, Create(displays).WaitForDisplay(CancellationToken.None));

        Assert.Equal(1, displays.Calls);
        Assert.Empty(_pauses);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public void SleepingDisplaysArePolledUntilOneWakes()
    {
        FakeDisplays displays = new((true, 0u, 0), (true, 0u, 0), (true, 0u, 0), (true, 2u, 0));

        Assert.Equal(StartupDisplayWaitResult.Ready, Create(displays).WaitForDisplay(CancellationToken.None));

        Assert.Equal(4, displays.Calls);
        Assert.Equal([Interval, Interval, Interval], _pauses);
        Assert.Collection(
            _logger.Entries,
            static e =>
            {
                Assert.Equal(LogLevel.Warning, e.Level);
                Assert.Contains("No active display", e.Message, StringComparison.Ordinal);
            },
            static e =>
            {
                Assert.Equal(LogLevel.Information, e.Level);
                Assert.Contains("2 active display(s)", e.Message, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void AFailingCheckStartsTheUiAnyway()
    {
        FakeDisplays displays = new((true, 0u, 0), (false, 0u, 1001));

        Assert.Equal(StartupDisplayWaitResult.Unknown, Create(displays).WaitForDisplay(CancellationToken.None));

        (LogLevel level, string message, _) = _logger.Entries[^1];
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains("error 1001", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AThrowingCheckStartsTheUiAnyway()
    {
        FakeDisplays displays = new() { Throw = new DllNotFoundException("CoreGraphics") };

        Assert.Equal(StartupDisplayWaitResult.Unknown, Create(displays).WaitForDisplay(CancellationToken.None));

        Assert.IsType<DllNotFoundException>(Assert.Single(_logger.Entries).Exception);
    }

    [Fact]
    public void AQuitSignalWhileWaitingEndsTheWaitWithoutStartingTheUi()
    {
        using CancellationTokenSource quit = new();
        FakeDisplays displays = new((true, 0u, 0), (true, 0u, 0), (true, 0u, 0));
        StartupDisplayWait wait = new(displays, (interval, token) =>
        {
            _pauses.Add(interval);
            if (_pauses.Count == 2)
            {
                quit.Cancel();
            }

            return token.IsCancellationRequested;
        }, _logger, Interval);

        Assert.Equal(StartupDisplayWaitResult.Cancelled, wait.WaitForDisplay(quit.Token));

        Assert.Equal(2, displays.Calls);
        Assert.Contains("Asked to quit", _logger.Entries[^1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRealPauseReturnsEarlyOnCancellation()
    {
        using CancellationTokenSource quit = new();
        quit.Cancel();

        Assert.True(StartupDisplayWait.BlockingPause(TimeSpan.FromMinutes(5), quit.Token));
        Assert.False(StartupDisplayWait.BlockingPause(TimeSpan.FromMilliseconds(1), CancellationToken.None));
    }

    [Fact]
    public void TheDefaultIntervalIsOneSecond() =>
        Assert.Equal(TimeSpan.FromSeconds(1), StartupDisplayWait.DefaultPollInterval);

    private StartupDisplayWait Create(FakeDisplays displays) => new(displays, (interval, token) =>
    {
        _pauses.Add(interval);
        return token.IsCancellationRequested;
    }, _logger, Interval);

    private sealed class FakeDisplays(params (bool Known, uint Count, int Error)[] answers) : IActiveDisplays
    {
        public int Calls { get; private set; }

        public Exception? Throw { get; init; }

        public bool TryCount(out uint count, out int error)
        {
            if (Throw is not null)
            {
                throw Throw;
            }

            (bool known, count, error) = answers[Math.Min(Calls, answers.Length - 1)];
            Calls++;
            return known;
        }
    }
}
