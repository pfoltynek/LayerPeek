using System.Text;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using PrusaTrayDiagnostic.App.SingleInstance;
using PrusaTrayDiagnostic.App.Tests.Tray;

namespace PrusaTrayDiagnostic.App.Tests.SingleInstance;

public sealed class SingleInstanceGuardTests : IAsyncDisposable
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    private readonly FakeInstanceTransport _transport = new();
    private readonly FakeTimeProvider _time = new();
    private readonly ListLogger<SingleInstanceGuard> _logger = new();
    private readonly ReceivedCommands _received = new();
    private readonly List<TimeSpan> _sleeps = [];
    private readonly SingleInstanceGuard _guard;
    private readonly DateTimeOffset _start;

    public SingleInstanceGuardTests()
    {
        _start = _time.GetUtcNow();
        _guard = new SingleInstanceGuard(_transport, _logger, _time, Sleep);
    }

    public ValueTask DisposeAsync() => _guard.DisposeAsync();

    [Fact]
    public void FirstInstanceTakesTheLockAndListens()
    {
        FakeLock instanceLock = new();
        _transport.QueueLock(() => instanceLock);

        InstanceRole role = _guard.Start(InstanceCommand.Activate, _received.Add);

        Assert.Equal(InstanceRole.Primary, role);
        Assert.Equal(["lock", "listen"], _transport.Calls);
        Assert.NotNull(_guard.Server);
        Assert.Equal(0, instanceLock.DisposeCount);
    }

    [Fact]
    public void SecondInstanceSendsItsCommandAndDoesNotListen()
    {
        _transport.DefaultConnect = static _ => new RecordingStream();

        InstanceRole role = _guard.Start(InstanceCommand.Activate, _received.Add);

        Assert.Equal(InstanceRole.Forwarded, role);
        Assert.Equal(["lock", "connect"], _transport.Calls);
        RecordingStream sent = Assert.Single(_transport.Sent);
        Assert.True(sent.Disposed);
        Assert.Equal("activate\n", Encoding.ASCII.GetString(sent.Written));
        Assert.Null(_guard.Server);
        Assert.Null(_transport.Listener);
    }

    [Fact]
    public void ConnectionAttemptsAreLimitedSoThatTheLockIsCheckedAgain()
    {
        _transport.DefaultConnect = static _ => new RecordingStream();

        _guard.Start(InstanceCommand.Activate, _received.Add);

        Assert.Equal([SingleInstanceGuard.ConnectAttemptTimeout], _transport.ConnectTimeouts);
    }

    [Fact]
    public void SecondInstanceRetriesUntilTheRunningInstanceListens()
    {
        // The first instance holds the lock but is still starting (e.g. autostart together with a click).
        _transport.QueueConnect(TimeoutAfter);
        _transport.QueueConnect(TimeoutAfter);
        _transport.QueueConnect(_ => new RecordingStream());

        InstanceRole role = _guard.Start(InstanceCommand.Activate, _received.Add);

        Assert.Equal(InstanceRole.Forwarded, role);
        Assert.Equal(["lock", "connect", "lock", "connect", "lock", "connect"], _transport.Calls);
        Assert.Single(_transport.Sent);
    }

    [Fact]
    public void BrokenConnectionsAreRetriedAfterAPause()
    {
        _transport.QueueConnect(_ => throw new IOException("Pipe broken."));
        _transport.QueueConnect(_ => new RecordingStream());

        InstanceRole role = _guard.Start(InstanceCommand.Activate, _received.Add);

        Assert.Equal(InstanceRole.Forwarded, role);
        Assert.Equal([SingleInstanceGuard.RetryDelay], _sleeps);
    }

    [Fact]
    public void SecondInstanceGivesUpAfterTheForwardTimeoutAndExits()
    {
        _transport.DefaultConnect = TimeoutAfter;

        InstanceRole role = _guard.Start(InstanceCommand.Activate, _received.Add);

        Assert.Equal(InstanceRole.NotForwarded, role);
        Assert.Equal(5, _transport.Calls.Count(call => call == "connect"));
        Assert.True(_time.GetUtcNow() - _start >= SingleInstanceGuard.DefaultForwardTimeout);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Exception is TimeoutException);
        Assert.Null(_transport.Listener);
    }

    [Fact]
    public void LastAttemptGetsOnlyTheRemainingTime()
    {
        SingleInstanceGuard guard = new(_transport, _logger, _time, Sleep, TimeSpan.FromMilliseconds(1500));
        _transport.DefaultConnect = TimeoutAfter;

        guard.Start(InstanceCommand.Activate, _received.Add);

        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(500)], _transport.ConnectTimeouts);
    }

    [Fact]
    public void RefusedConnectionMeansAnotherInstanceRunsWithoutRetrying()
    {
        // E.g. an elevated instance: the pipe exists but this process may not open it.
        _transport.DefaultConnect = static _ => throw new UnauthorizedAccessException("Access denied.");

        InstanceRole role = _guard.Start(InstanceCommand.Activate, _received.Add);

        Assert.Equal(InstanceRole.NotForwarded, role);
        Assert.Equal(["lock", "connect"], _transport.Calls);
        Assert.Contains(_logger.Entries, entry => entry.Exception is UnauthorizedAccessException);
    }

    [Fact]
    public void LockThatCannotBeOpenedMeansAnotherInstanceRuns()
    {
        _transport.DefaultLock = static () => throw new UnauthorizedAccessException("Access denied.");

        InstanceRole role = _guard.Start(InstanceCommand.Activate, _received.Add);

        Assert.Equal(InstanceRole.NotForwarded, role);
        Assert.Equal(["lock"], _transport.Calls);
    }

    [Fact]
    public void UnusableLockLetsTheAppRunUnguarded()
    {
        _transport.DefaultLock = static () => throw new WaitHandleCannotBeOpenedException("Another kind of object.");

        InstanceRole role = _guard.Start(InstanceCommand.Activate, _received.Add);

        Assert.Equal(InstanceRole.Primary, role);
        Assert.Null(_guard.Server);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public void SecondInstanceTakesOverWhenTheRunningOneExitsMeanwhile()
    {
        FakeLock instanceLock = new();
        _transport.QueueLock(static () => null);
        _transport.QueueLock(() => instanceLock);
        _transport.DefaultConnect = TimeoutAfter;

        InstanceRole role = _guard.Start(InstanceCommand.Activate, _received.Add);

        Assert.Equal(InstanceRole.Primary, role);
        Assert.Equal(["lock", "connect", "lock", "listen"], _transport.Calls);
    }

    [Fact]
    public void PrimaryInstanceStillRunsWhenListeningFails()
    {
        FakeLock instanceLock = new();
        _transport.QueueLock(() => instanceLock);
        _transport.ListenFailure = new IOException("All pipe instances are busy.");

        InstanceRole role = _guard.Start(InstanceCommand.Activate, _received.Add);

        Assert.Equal(InstanceRole.Primary, role);
        Assert.Null(_guard.Server);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Error && entry.Exception is IOException);
    }

    [Fact]
    public async Task PrimaryInstanceReportsReceivedCommands()
    {
        _transport.QueueLock(static () => new FakeLock());
        _guard.Start(InstanceCommand.Activate, _received.Add);

        _transport.Listener!.AddClient(new MemoryStream("activate\n"u8.ToArray()));

        Assert.Equal(InstanceCommand.Activate, await _received.NextAsync(WaitTimeout));
    }

    [Fact]
    public async Task DisposingStopsListeningAndReleasesTheLock()
    {
        FakeLock instanceLock = new();
        _transport.QueueLock(() => instanceLock);
        _guard.Start(InstanceCommand.Activate, _received.Add);

        await _guard.DisposeAsync();

        Assert.True(_transport.Listener!.Disposed);
        Assert.Equal(1, instanceLock.DisposeCount);
        Assert.Null(_guard.Server);
    }

    [Fact]
    public void StartingTwiceIsRefused()
    {
        _transport.QueueLock(static () => new FakeLock());
        _guard.Start(InstanceCommand.Activate, _received.Add);

        Assert.Throws<InvalidOperationException>(() => _guard.Start(InstanceCommand.Activate, _received.Add));
    }

    private Stream TimeoutAfter(TimeSpan timeout)
    {
        // A real connection attempt waits for its timeout before giving up.
        _time.Advance(timeout);
        throw new TimeoutException("Nobody listened.");
    }

    private void Sleep(TimeSpan delay)
    {
        _sleeps.Add(delay);
        _time.Advance(delay);
    }
}
