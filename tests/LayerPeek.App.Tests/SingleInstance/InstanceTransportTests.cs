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

/// <summary>
/// The macOS transport; Unix domain sockets exist on Windows 10+ too, so its logic runs there as well. On macOS the real
/// credentials (T-053: peer user, owner and mode of the socket's folder) are checked in every test. All folders are in the
/// reserved temp folders of the assembly (under <c>$TMPDIR</c>, private to the user on macOS, never the repository whose
/// volume may not enforce owners) and removed after each test and after a crashed run.
/// </summary>
public sealed class UnixSocketInstanceTransportTests : InstanceTransportTests
{
    private const UnixFileMode Mode700 = UnixSocketInstanceTransport.PrivateDirectoryMode;

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    private readonly string _directory = TestDirectories.NewDirectoryPath();

    // Short: Unix socket paths are limited (about 104 bytes); the folder of the test run is already long.
    private readonly string _socketPath = Path.Combine(TestDirectories.Root, Guid.NewGuid().ToString("N")[..8] + ".s");

    // A folder of its own for the socket folders of the T-053 tests, in the short reservation (socket paths stay short).
    private readonly string _shortDirectory = Path.Combine(TestDirectories.ShortRoot, Guid.NewGuid().ToString("N")[..8]);

    public UnixSocketInstanceTransportTests()
    {
        Assert.SkipUnless(Socket.OSSupportsUnixDomainSockets, "This OS has no Unix domain sockets.");
    }

    protected override void CleanUp()
    {
        TestDirectories.Delete(_directory);
        TestDirectories.Delete(_shortDirectory);
        File.Delete(_socketPath);
    }

    private protected override IInstanceTransport CreateTransport() =>
        new UnixSocketInstanceTransport(Path.Combine(_directory, "instance.lock"), _socketPath, FakeUnixCredentials.RealOrNull);

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
    public void LongDataFolderMovesTheSocketToThePrivateFolderOfTheUser()
    {
        string data = Path.Combine(Path.GetTempPath(), new string('d', 120));

        UnixInstancePaths paths = InstanceTransports.UnixPaths(data, "/tmp/user", 501, "-x");

        Assert.Equal(Path.Combine(data, "instance-x.lock"), paths.LockPath);
        Assert.Equal(Path.Combine("/tmp/user", "LayerPeek-501", "instance-x.sock"), paths.SocketPath);
        Assert.True(paths.PrivateSocketDirectory);
    }

    [Fact]
    public void LongDataFolderWithoutAUserIdKeepsTheOldTempSocket()
    {
        // Linux (no credentials): the unchecked T-043 behaviour.
        string data = Path.Combine(Path.GetTempPath(), new string('d', 120));

        UnixInstancePaths paths = InstanceTransports.UnixPaths(data, "/tmp/user", null, "-x");

        Assert.Equal(Path.Combine("/tmp/user", "LayerPeek-instance-x.sock"), paths.SocketPath);
        Assert.False(paths.PrivateSocketDirectory);
    }

    [Fact]
    public void ShortDataFolderKeepsTheSocketNextToTheLock()
    {
        UnixInstancePaths paths = InstanceTransports.UnixPaths("/Users/me/Library/Application Support/LayerPeek", "/tmp/user", 501, string.Empty);

        Assert.Equal(Path.Combine("/Users/me/Library/Application Support/LayerPeek", "instance.lock"), paths.LockPath);
        Assert.Equal(Path.Combine("/Users/me/Library/Application Support/LayerPeek", "instance.sock"), paths.SocketPath);
        Assert.False(paths.PrivateSocketDirectory);
    }

    [Theory]
    [InlineData(false, 0b111_101_101)] // 0755: the usual data folder
    [InlineData(false, 0b111_000_000)]
    [InlineData(true, 0b111_000_000)]
    public void FolderOfTheUserThatNobodyElseCanWriteIsAccepted(bool privateDirectory, int mode)
    {
        Assert.Null(UnixSocketInstanceTransport.SocketDirectoryProblem(new UnixEntry(UnixEntryKind.Directory, (UnixFileMode)mode, 501), 501, privateDirectory));
    }

    [Theory]
    [InlineData(false, 0b111_111_101, "open to other users")] // 0775
    [InlineData(false, 0b111_101_111, "open to other users")] // 0757
    [InlineData(true, 0b111_101_000, "must be 700")] // 0750
    [InlineData(true, 0b111_000_001, "must be 700")] // 0701
    [InlineData(true, 0b111_101_101, "must be 700")] // 0755
    public void FolderOpenToOthersIsRefused(bool privateDirectory, int mode, string expected)
    {
        string? problem = UnixSocketInstanceTransport.SocketDirectoryProblem(new UnixEntry(UnixEntryKind.Directory, (UnixFileMode)mode, 501), 501, privateDirectory);

        Assert.NotNull(problem);
        Assert.Contains(expected, problem, StringComparison.Ordinal);
    }

    [Fact]
    public void FolderOfAnotherUserOrNoFolderIsRefused()
    {
        Assert.Contains("another user (uid 0", UnixSocketInstanceTransport.SocketDirectoryProblem(new UnixEntry(UnixEntryKind.Directory, Mode700, 0), 501, true), StringComparison.Ordinal);
        Assert.Contains("symbolic link", UnixSocketInstanceTransport.SocketDirectoryProblem(new UnixEntry(UnixEntryKind.SymbolicLink, Mode700, 501), 501, false), StringComparison.Ordinal);
        Assert.Contains("not a folder", UnixSocketInstanceTransport.SocketDirectoryProblem(new UnixEntry(UnixEntryKind.Other, Mode700, 501), 501, false), StringComparison.Ordinal);
        Assert.Contains("does not exist", UnixSocketInstanceTransport.SocketDirectoryProblem(null, 501, true), StringComparison.Ordinal);
    }

    [SupportedOSPlatform("macos")]
    [Fact]
    public async Task PrivateSocketFolderIsCreated700AndCarriesCommands()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "The real owner and mode checks are the macOS ones.");
        string folder = Path.Combine(_shortDirectory, "LayerPeek-" + MacUnixCredentials.Current.EffectiveUserId);
        ReceivedCommands received = new();
        await using GuardScope first = new(PrivateTransport(folder));
        await using GuardScope second = new(PrivateTransport(folder));

        Assert.Equal(InstanceRole.Primary, first.Guard.Start(InstanceCommand.Activate, received.Add));
        Assert.Equal(Mode700, File.GetUnixFileMode(folder));
        Assert.Equal(InstanceRole.Forwarded, second.Guard.Start(InstanceCommand.Activate, static _ => Assert.Fail("Not the primary.")));

        Assert.Equal(InstanceCommand.Activate, await received.NextAsync(WaitTimeout));
    }

    [SupportedOSPlatform("macos")]
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SocketFolderOfAnotherUserTurnsTheGuardOff(bool privateDirectory)
    {
        // The folder is this user's; the credentials claim another user (no second account is needed for the test).
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "The real owner and mode checks are the macOS ones.");
        string folder = Path.Combine(_shortDirectory, "f");
        Directory.CreateDirectory(folder, Mode700);
        FakeUnixCredentials stranger = new() { EffectiveUserId = MacUnixCredentials.Current.EffectiveUserId + 1 };

        AssertGuardOff(Transport(folder, stranger, privateDirectory), "belongs to another user");
    }

    [SupportedOSPlatform("macos")]
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SymbolicLinkInPlaceOfTheSocketFolderTurnsTheGuardOff(bool privateDirectory)
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "The real owner and mode checks are the macOS ones.");
        string target = Path.Combine(_shortDirectory, "target");
        Directory.CreateDirectory(target, Mode700);
        string folder = Path.Combine(_shortDirectory, "f");
        Directory.CreateSymbolicLink(folder, target);

        AssertGuardOff(Transport(folder, MacUnixCredentials.Current, privateDirectory), "symbolic link");

        // Nothing went through the link: no lock file, no socket.
        Assert.Empty(Directory.EnumerateFileSystemEntries(target));
    }

    [Theory]
    [InlineData(true, 0b111_101_101)] // 0755
    [InlineData(true, 0b111_000_001)] // 0701
    [InlineData(false, 0b111_111_101)] // 0775
    [SupportedOSPlatform("macos")]
    public void SocketFolderOpenToOthersTurnsTheGuardOff(bool privateDirectory, int mode)
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "The real owner and mode checks are the macOS ones.");
        string folder = Path.Combine(_shortDirectory, "f");
        Directory.CreateDirectory(folder);
        File.SetUnixFileMode(folder, (UnixFileMode)mode);

        AssertGuardOff(Transport(folder, MacUnixCredentials.Current, privateDirectory), "open to other users");
    }

    [SupportedOSPlatform("macos")]
    [Fact]
    public void FileInPlaceOfThePrivateFolderTurnsTheGuardOff()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "The real owner and mode checks are the macOS ones.");
        Directory.CreateDirectory(_shortDirectory);
        string folder = Path.Combine(_shortDirectory, "f");
        File.WriteAllText(folder, string.Empty);

        AssertGuardOff(Transport(folder, MacUnixCredentials.Current, privateDirectory: true), "cannot be used");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConnectionOfAnotherUserIsRefusedUnread(bool userKnown)
    {
        // The running instance sees every client as another user (or cannot tell): the command never arrives.
        FakeUnixCredentials credentials = new();
        uint stranger = credentials.EffectiveUserId + 1;
        FakeUnixCredentials refusing = new() { Peer = () => userKnown ? stranger : null };
        ReceivedCommands received = new();
        ListLogger<SingleInstanceGuard> log = new();
        await using GuardScope first = new(PeerTransport(refusing), log);
        await using GuardScope second = new(PeerTransport(credentials), forwardTimeout: TimeSpan.FromSeconds(1));

        Assert.Equal(InstanceRole.Primary, first.Guard.Start(InstanceCommand.Activate, received.Add));
        Assert.Equal(InstanceRole.NotForwarded, second.Guard.Start(InstanceCommand.Activate, static _ => Assert.Fail("Not the primary.")));

        Assert.True(refusing.PeerChecks > 0);
        Assert.Contains(log.Entries, entry => entry.Message.Contains(
            userKnown ? $"another user (uid {stranger})" : "another user (uid (null))", StringComparison.Ordinal));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => received.NextAsync(TimeSpan.FromMilliseconds(200)));
    }

    [SupportedOSPlatform("macos")]
    [Fact]
    public async Task RealPeerCheckSeesTheSameUser()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "getpeereid is the macOS check.");
        Directory.CreateDirectory(_shortDirectory);
        string path = Path.Combine(_shortDirectory, "p.s");
        using Socket server = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        server.Bind(new UnixDomainSocketEndPoint(path));
        server.Listen(1);
        using Socket client = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await client.ConnectAsync(new UnixDomainSocketEndPoint(path), TestContext.Current.CancellationToken);
        using Socket accepted = await server.AcceptAsync(TestContext.Current.CancellationToken);

        Assert.True(MacUnixCredentials.Current.TryGetPeerUserId(accepted, out uint peer));
        Assert.True(MacUnixCredentials.Current.TryGetPeerUserId(client, out uint serverUser));

        Assert.Equal(MacUnixCredentials.Current.EffectiveUserId, peer);
        Assert.Equal(MacUnixCredentials.Current.EffectiveUserId, serverUser);
    }

    [SupportedOSPlatform("macos")]
    [Fact]
    public void RealEntriesTellFoldersLinksFilesAndTheirOwner()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "lstat is the macOS check.");
        MacUnixCredentials credentials = MacUnixCredentials.Current;
        string folder = Path.Combine(_shortDirectory, "f");
        Directory.CreateDirectory(folder, Mode700);
        string file = Path.Combine(_shortDirectory, "file");
        File.WriteAllText(file, string.Empty);
        string link = Path.Combine(_shortDirectory, "link");
        File.CreateSymbolicLink(link, folder);

        Assert.Equal(new UnixEntry(UnixEntryKind.Directory, Mode700, credentials.EffectiveUserId), credentials.GetEntry(folder));
        Assert.Equal(UnixEntryKind.Other, credentials.GetEntry(file)?.Kind);
        Assert.Equal(credentials.EffectiveUserId, credentials.GetEntry(file)?.OwnerId);
        Assert.Equal(UnixEntryKind.SymbolicLink, credentials.GetEntry(link)?.Kind);
        Assert.Null(credentials.GetEntry(Path.Combine(_shortDirectory, "missing")));
        // A system folder of root: the owner comes from the file system, not from this process.
        Assert.Equal(0u, credentials.GetEntry("/usr")?.OwnerId);
    }

    [SupportedOSPlatform("macos")]
    private UnixSocketInstanceTransport PrivateTransport(string folder) =>
        Transport(folder, MacUnixCredentials.Current, privateDirectory: true);

    private UnixSocketInstanceTransport Transport(string socketFolder, IUnixCredentials credentials, bool privateDirectory) =>
        new(Path.Combine(privateDirectory ? _directory : socketFolder, "instance.lock"), Path.Combine(socketFolder, "s"), credentials, privateDirectory);

    private UnixSocketInstanceTransport PeerTransport(IUnixCredentials credentials) =>
        new(Path.Combine(_directory, "instance.lock"), _socketPath, credentials);

    /// <summary>The guard runs unguarded at once (no waiting, no listener) and logs why.</summary>
    private static void AssertGuardOff(UnixSocketInstanceTransport transport, string reason)
    {
        Microsoft.Extensions.Time.Testing.FakeTimeProvider time = new();
        List<TimeSpan> sleeps = [];
        ListLogger<SingleInstanceGuard> log = new();
        using SingleInstanceGuard guard = new(transport, log, time, sleeps.Add);

        InstanceRole role = guard.Start(InstanceCommand.Activate, static _ => { });

        Assert.Equal(InstanceRole.Primary, role);
        Assert.Null(guard.Server);
        Assert.Empty(sleeps);
        InstanceLockUnavailableException failure = Assert.IsType<InstanceLockUnavailableException>(
            Assert.Single(log.Entries, entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Error).Exception);
        Assert.Contains(reason, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A guard released (and its socket removed) at the end of the test.</summary>
    private sealed class GuardScope(IInstanceTransport transport, ListLogger<SingleInstanceGuard>? log = null, TimeSpan? forwardTimeout = null) : IAsyncDisposable
    {
        public SingleInstanceGuard Guard { get; } = new(transport, log ?? new ListLogger<SingleInstanceGuard>(), forwardTimeout: forwardTimeout ?? TimeSpan.FromSeconds(5));

        public ValueTask DisposeAsync() => Guard.DisposeAsync();
    }
}
