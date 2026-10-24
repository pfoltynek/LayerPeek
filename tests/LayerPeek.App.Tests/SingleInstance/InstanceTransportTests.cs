using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.Principal;

using LayerPeek.App.SingleInstance;
using LayerPeek.App.Tests.Tray;

namespace LayerPeek.App.Tests.SingleInstance;

/// <summary>
/// The real transports with names of their own per test (never the production names, so a running app is never met).
/// Mutexes and pipes are kernel objects that vanish with their handles (at the latest when the test process ends);
/// lock and socket files live in the reserved temp folder of the assembly, removed even after a crashed run.
/// </summary>
public abstract class InstanceTransportTests : IAsyncDisposable
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    private readonly List<SingleInstanceGuard> _guards = [];

    private protected abstract IInstanceTransport CreateTransport();

    public async ValueTask DisposeAsync()
    {
        foreach (SingleInstanceGuard guard in _guards)
        {
            await guard.DisposeAsync();
        }

        CleanUp();
        GC.SuppressFinalize(this);
    }

    /// <summary>Removes the files of the test, after every guard released them.</summary>
    protected virtual void CleanUp()
    {
    }

    [Fact]
    public void OnlyOneHolderOfTheLock()
    {
        IInstanceTransport transport = CreateTransport();

        using IDisposable? first = transport.TryAcquireLock();
        using IDisposable? second = CreateTransport().TryAcquireLock();

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public void ReleasedLockCanBeTakenAgain()
    {
        IInstanceTransport transport = CreateTransport();
        transport.TryAcquireLock()!.Dispose();

        using IDisposable? again = transport.TryAcquireLock();

        Assert.NotNull(again);
    }

    [Fact]
    public async Task SecondStartForwardsItsCommandToTheFirst()
    {
        ReceivedCommands received = new();
        SingleInstanceGuard first = Guard();
        SingleInstanceGuard second = Guard();

        Assert.Equal(InstanceRole.Primary, first.Start(InstanceCommand.Activate, received.Add));
        Assert.Equal(InstanceRole.Forwarded, second.Start(InstanceCommand.Activate, static _ => Assert.Fail("Not the primary.")));

        Assert.Equal(InstanceCommand.Activate, await received.NextAsync(WaitTimeout));
    }

    [Fact]
    public async Task ManyStartsAreAllForwarded()
    {
        ReceivedCommands received = new();
        Assert.Equal(InstanceRole.Primary, Guard().Start(InstanceCommand.Activate, received.Add));

        InstanceRole[] roles = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            Task.Run(() => Guard().Start(InstanceCommand.Activate, static _ => { }))));

        Assert.All(roles, role => Assert.Equal(InstanceRole.Forwarded, role));
        for (int i = 0; i < roles.Length; i++)
        {
            Assert.Equal(InstanceCommand.Activate, await received.NextAsync(WaitTimeout));
        }
    }

    [Fact]
    public async Task SecondStartWaitsForTheFirstToListen()
    {
        // The first instance holds the lock but listens only a moment later (still starting).
        IInstanceTransport transport = CreateTransport();
        using IDisposable instanceLock = transport.TryAcquireLock()!;
        ReceivedCommands received = new();
        Task<InstanceRole> second = Task.Run(() => Guard().Start(InstanceCommand.Activate, static _ => { }));
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

        await using InstanceCommandServer server = new(transport.Listen(), received.Add, new ListLogger<InstanceCommandServer>());

        Assert.Equal(InstanceRole.Forwarded, await second.WaitAsync(WaitTimeout, TestContext.Current.CancellationToken));
        Assert.Equal(InstanceCommand.Activate, await received.NextAsync(WaitTimeout));
    }

    [Fact]
    public void ConnectingWithoutListenerTimesOut()
    {
        Stopwatch watch = Stopwatch.StartNew();

        Assert.Throws<TimeoutException>(() => CreateTransport().Connect(TimeSpan.FromMilliseconds(300)));

        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(250), $"Gave up after {watch.Elapsed}.");
    }

    [Fact]
    public void AfterTheFirstStopsTheNextStartIsThePrimary()
    {
        SingleInstanceGuard first = Guard();
        Assert.Equal(InstanceRole.Primary, first.Start(InstanceCommand.Activate, static _ => { }));
        first.Dispose();

        Assert.Equal(InstanceRole.Primary, Guard().Start(InstanceCommand.Activate, static _ => { }));
    }

    /// <summary>A guard on a fake clock that records its pauses.</summary>
    private protected sealed class FakeClockGuard
    {
        private readonly Microsoft.Extensions.Time.Testing.FakeTimeProvider _time = new();
        private readonly DateTimeOffset _start;

        public FakeClockGuard(IInstanceTransport transport)
        {
            _start = _time.GetUtcNow();
            Guard = new SingleInstanceGuard(transport, new ListLogger<SingleInstanceGuard>(), _time, delay =>
            {
                Sleeps.Add(delay);
                _time.Advance(delay);
            });
        }

        public SingleInstanceGuard Guard { get; }

        public List<TimeSpan> Sleeps { get; } = [];

        public TimeSpan Elapsed => _time.GetUtcNow() - _start;
    }

    private SingleInstanceGuard Guard()
    {
        SingleInstanceGuard guard = new(CreateTransport(), new ListLogger<SingleInstanceGuard>(), forwardTimeout: TimeSpan.FromSeconds(5));
        lock (_guards)
        {
            _guards.Add(guard);
        }

        return guard;
    }
}

[SupportedOSPlatform("windows")]
public sealed class NamedPipeInstanceTransportTests : InstanceTransportTests
{
    private readonly string _suffix = "-test-" + Guid.NewGuid().ToString("N");

    public NamedPipeInstanceTransportTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Named mutexes and pipes are the Windows transport.");
    }

    private protected override IInstanceTransport CreateTransport()
    {
        (string mutexName, string pipeName) = InstanceTransports.WindowsNames(CurrentSid, CurrentSession, _suffix);
        return new NamedPipeInstanceTransport(mutexName, pipeName);
    }

    [Fact]
    public async Task SecondStartGivesUpWhenTheRunningInstanceNeverReads()
    {
        // A pipe without buffers whose server accepts but never reads: the write would block forever.
        (_, string pipeName) = InstanceTransports.WindowsNames(CurrentSid, CurrentSession, _suffix);
        using IDisposable instanceLock = CreateTransport().TryAcquireLock()!;
        await using System.IO.Pipes.NamedPipeServerStream stalled = new(
            pipeName,
            System.IO.Pipes.PipeDirection.InOut,
            1,
            System.IO.Pipes.PipeTransmissionMode.Byte,
            System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly,
            0,
            0);
        Task connected = stalled.WaitForConnectionAsync(TestContext.Current.CancellationToken);
        SingleInstanceGuard second = new(CreateTransport(), new ListLogger<SingleInstanceGuard>(), forwardTimeout: TimeSpan.FromSeconds(2));
        Stopwatch watch = Stopwatch.StartNew();

        InstanceRole role = await Task.Run(() => second.Start(InstanceCommand.Activate, static _ => { }), TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(InstanceRole.NotForwarded, role);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(4), $"Took {watch.Elapsed}.");
        Assert.True(connected.IsCompletedSuccessfully);
        second.Dispose();
    }

    [Fact]
    public async Task ListeningPipeTakesACommandBeforeAnybodyReadsIt()
    {
        // The running instance may be busy with another client: a later start's small write must not wait for it.
        IInstanceTransport transport = CreateTransport();
        using IDisposable instanceLock = transport.TryAcquireLock()!;
        using IInstanceListener listener = transport.Listen();
        (_, string pipeName) = InstanceTransports.WindowsNames(CurrentSid, CurrentSession, _suffix);
        await using System.IO.Pipes.NamedPipeClientStream client = new(
            ".",
            pipeName,
            System.IO.Pipes.PipeDirection.InOut,
            System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        await client.WriteAsync(InstanceProtocol.Encode(InstanceCommand.Activate), TestContext.Current.CancellationToken)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void ProductionNamesCarryTheUserAndSession()
    {
        (string mutexName, string pipeName) = InstanceTransports.WindowsNames(CurrentSid, CurrentSession, string.Empty);

        Assert.Equal($@"Local\LayerPeek-{CurrentSid}", mutexName);
        Assert.Equal($"LayerPeek-{CurrentSid}-{CurrentSession}", pipeName);
    }

    [Fact]
    public void PipeOfAnotherUserIsRefused()
    {
        // A pipe that exists but whose DACL does not admit this process (like one of an elevated instance).
        string pipeName = "LayerPeek-test-denied-" + Guid.NewGuid().ToString("N");
        System.IO.Pipes.PipeSecurity security = new();
        security.AddAccessRule(new System.IO.Pipes.PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            System.IO.Pipes.PipeAccessRights.FullControl,
            System.Security.AccessControl.AccessControlType.Allow));
        security.AddAccessRule(new System.IO.Pipes.PipeAccessRule(
            WindowsIdentity.GetCurrent().User!,
            System.IO.Pipes.PipeAccessRights.CreateNewInstance | System.IO.Pipes.PipeAccessRights.ReadPermissions,
            System.Security.AccessControl.AccessControlType.Allow));
        using System.IO.Pipes.NamedPipeServerStream server = System.IO.Pipes.NamedPipeServerStreamAcl.Create(
            pipeName,
            System.IO.Pipes.PipeDirection.InOut,
            1,
            System.IO.Pipes.PipeTransmissionMode.Byte,
            System.IO.Pipes.PipeOptions.Asynchronous,
            0,
            0,
            security);
        NamedPipeInstanceTransport transport = new(@"Local\" + pipeName, pipeName);

        Assert.Throws<UnauthorizedAccessException>(() => transport.Connect(TimeSpan.FromSeconds(1)));
    }

    private static string CurrentSid => WindowsIdentity.GetCurrent().User!.Value;

    private static int CurrentSession
    {
        get
        {
            using Process current = Process.GetCurrentProcess();
            return current.SessionId;
        }
    }
}

/// <summary>The macOS transport; Unix domain sockets exist on Windows 10+ too, so its logic runs here as well.</summary>
public sealed class UnixSocketInstanceTransportTests : InstanceTransportTests
{
    private readonly string _directory = TestDirectories.NewDirectoryPath();

    // Short: Unix socket paths are limited (about 104 bytes); the folder of the test run is already long.
    private readonly string _socketPath = Path.Combine(TestDirectories.Root, Guid.NewGuid().ToString("N")[..8] + ".s");

    public UnixSocketInstanceTransportTests()
    {
        Assert.SkipUnless(Socket.OSSupportsUnixDomainSockets, "This OS has no Unix domain sockets.");
    }

    protected override void CleanUp()
    {
        TestDirectories.Delete(_directory);
        File.Delete(_socketPath);
    }

    private protected override IInstanceTransport CreateTransport() =>
        new UnixSocketInstanceTransport(Path.Combine(_directory, "instance.lock"), _socketPath);

    [Fact]
    public void LockFileThatCannotBeOpenedIsNotAnotherInstance()
    {
        // Access denied (here: the lock path is a folder) is no lock contention: the app runs unguarded at once.
        string lockPath = Path.Combine(_directory, "instance.lock");
        Directory.CreateDirectory(lockPath);
        FakeClockGuard guard = new(new UnixSocketInstanceTransport(lockPath, _socketPath));

        InstanceRole role = guard.Guard.Start(InstanceCommand.Activate, static _ => { });

        Assert.Equal(InstanceRole.Primary, role);
        Assert.Null(guard.Guard.Server);
        Assert.Empty(guard.Sleeps);
        Assert.Throws<InstanceLockUnavailableException>(() => new UnixSocketInstanceTransport(lockPath, _socketPath).TryAcquireLock());
    }

    [Fact]
    public void DataFolderThatCannotBeCreatedIsNotAnotherInstance()
    {
        // Another IO error (here: a file is in the way of the folder) is no lock contention either: no 5 s retrying.
        Directory.CreateDirectory(_directory);
        string blocker = Path.Combine(_directory, "file");
        File.WriteAllText(blocker, string.Empty);
        UnixSocketInstanceTransport transport = new(Path.Combine(blocker, "instance.lock"), _socketPath);
        FakeClockGuard guard = new(transport);

        InstanceRole role = guard.Guard.Start(InstanceCommand.Activate, static _ => { });

        Assert.Equal(InstanceRole.Primary, role);
        Assert.Null(guard.Guard.Server);
        Assert.Equal(TimeSpan.Zero, guard.Elapsed);
    }

    [Fact]
    public void StaleSocketFileOfACrashedInstanceIsReplaced()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_socketPath)!);
        File.WriteAllText(_socketPath, "left over");
        IInstanceTransport transport = CreateTransport();
        using IDisposable instanceLock = transport.TryAcquireLock()!;

        using IInstanceListener listener = transport.Listen();
        using Stream client = transport.Connect(TimeSpan.FromSeconds(2));

        Assert.True(client.CanWrite);
    }

    [Fact]
    public void StoppedListenerRemovesItsSocketFile()
    {
        IInstanceTransport transport = CreateTransport();
        using IDisposable instanceLock = transport.TryAcquireLock()!;

        transport.Listen().Dispose();

        Assert.False(File.Exists(_socketPath));
    }

    [Fact]
    public void LongDataFolderMovesTheSocketToTheTempFolder()
    {
        string data = Path.Combine(Path.GetTempPath(), new string('d', 120));

        (string lockPath, string socketPath) = InstanceTransports.UnixPaths(data, "/tmp/user", "-x");

        Assert.Equal(Path.Combine(data, "instance-x.lock"), lockPath);
        Assert.Equal(Path.Combine("/tmp/user", "LayerPeek-instance-x.sock"), socketPath);
    }

    [Fact]
    public void ShortDataFolderKeepsTheSocketNextToTheLock()
    {
        (string lockPath, string socketPath) = InstanceTransports.UnixPaths("/Users/me/Library/Application Support/LayerPeek", "/tmp/user", string.Empty);

        Assert.Equal(Path.Combine("/Users/me/Library/Application Support/LayerPeek", "instance.lock"), lockPath);
        Assert.Equal(Path.Combine("/Users/me/Library/Application Support/LayerPeek", "instance.sock"), socketPath);
    }
}
