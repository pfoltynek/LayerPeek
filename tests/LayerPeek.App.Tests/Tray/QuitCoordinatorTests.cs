using Microsoft.Extensions.Logging;

using LayerPeek.App.Tray;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Tray;

public sealed class QuitCoordinatorTests
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(2);

    private readonly List<TimeSpan> _stopCalls = [];
    private readonly ListLogger<QuitCoordinator> _logger = new();
    private int _shutdownCount;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task QuitStopsWithTimeoutThenShutsDown()
    {
        List<string> order = [];
        QuitCoordinator quit = new(
            timeout =>
            {
                order.Add($"stop {timeout.TotalSeconds}");
                return Task.CompletedTask;
            },
            () => order.Add("shutdown"),
            StopTimeout,
            Grace,
            _logger);

        await quit.QuitAsync();

        Assert.Equal(["stop 3", "shutdown"], order);
        Assert.True(quit.IsQuitting);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task SecondQuitIsIgnored()
    {
        TaskCompletionSource stopping = new(TaskCreationOptions.RunContinuationsAsynchronously);
        QuitCoordinator quit = Create(_ => stopping.Task);

        Task first = quit.QuitAsync();
        await quit.QuitAsync();
        stopping.SetResult();
        await first.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Single(_stopCalls);
        Assert.Equal(1, _shutdownCount);
    }

    [Fact]
    public async Task FailingStopIsLoggedAndStillShutsDown()
    {
        InvalidOperationException failure = new("dispose failed");
        QuitCoordinator quit = Create(_ => Task.FromException(failure));

        await quit.QuitAsync();

        Assert.Equal(1, _shutdownCount);
        (LogLevel level, _, Exception? exception) = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Error, level);
        Assert.Same(failure, exception);
    }

    [Fact]
    public async Task SynchronouslyThrowingStopIsLoggedAndStillShutsDown()
    {
        QuitCoordinator quit = Create(_ => throw new InvalidOperationException("boom"));

        await quit.QuitAsync();

        Assert.Equal(1, _shutdownCount);
        Assert.Equal(LogLevel.Error, Assert.Single(_logger.Entries).Level);
    }

    [Fact]
    public async Task HangingStopIsAbandonedAfterHardLimitWithWarning()
    {
        TaskCompletionSource never = new(TaskCreationOptions.RunContinuationsAsynchronously);
        QuitCoordinator quit = new(
            timeout =>
            {
                _stopCalls.Add(timeout);
                return never.Task;
            },
            () => _shutdownCount++,
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMilliseconds(20),
            _logger);

        await quit.QuitAsync().WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal(1, _shutdownCount);
        (LogLevel level, string message, Exception? exception) = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Null(exception);
        Assert.Contains("00:00:00.04", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThrowingLoggerNeverBreaksQuit()
    {
        QuitCoordinator failing = new(
            _ => Task.FromException(new InvalidOperationException("dispose failed")),
            () => _shutdownCount++,
            StopTimeout,
            Grace,
            new ThrowingLogger());
        TaskCompletionSource never = new(TaskCreationOptions.RunContinuationsAsynchronously);
        QuitCoordinator hanging = new(
            _ => never.Task,
            () => _shutdownCount++,
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(10),
            new ThrowingLogger());

        await failing.QuitAsync().WaitAsync(TestTimeouts.Wait, Ct);
        await hanging.QuitAsync().WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal(2, _shutdownCount);
    }

    [Fact]
    public async Task QuitStopsRunningMonitoring()
    {
        FakePrinterMonitor monitor = new();
        ManualDispatcher dispatcher = new();
        using TrayStatusViewModel viewModel = new(monitor, dispatcher);
        await using MonitoringSession session = new(
            () => FakeSettingsStore.Returning(new AppSettings { Printers = [Definition()] }),
            monitor,
            [viewModel],
            new ListLogger<MonitoringSession>());
        CancellationToken monitorToken = default;
        TaskCompletionSource running = new(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.RunBehavior = async token =>
        {
            monitorToken = token;
            running.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        };
        session.Start();
        await running.Task.WaitAsync(TestTimeouts.Wait, Ct);
        QuitCoordinator quit = new(session.StopAsync, () => _shutdownCount++, StopTimeout, Grace, _logger);

        await quit.QuitAsync().WaitAsync(TestTimeouts.Wait, Ct);

        Assert.True(monitorToken.IsCancellationRequested);
        Assert.True(session.Completion.IsCompletedSuccessfully);
        Assert.Equal(1, _shutdownCount);
        Assert.Empty(_logger.Entries);
    }

    private QuitCoordinator Create(Func<TimeSpan, Task> stop) =>
        new(
            timeout =>
            {
                _stopCalls.Add(timeout);
                return stop(timeout);
            },
            () => _shutdownCount++,
            StopTimeout,
            Grace,
            _logger);
}
