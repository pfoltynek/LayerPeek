using System.Text.Json;
using System.Text.Json.Serialization;

using PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

namespace PrusaTrayDiagnostic.App.E2E.Tests;

/// <summary>
/// Unit tests of the restore machinery of T-065 with fake kinds and a temporary journal folder: they run in every
/// <c>dotnet test</c> and change nothing in the system.
/// </summary>
public sealed class SystemChangeJournalTests
{
    private static readonly ProcessOwner Self = new(1, 100, "self");
    private static readonly ProcessOwner Dead = new(2, 200, "crashed");
    private static readonly ProcessOwner Running = new(3, 300, "running");

    [Fact]
    public async Task BeginRecordsTheOriginalBeforeAnythingChanges()
    {
        Setup setup = new();
        setup.Kind.Current = "user";

        await using SystemChange<FakeState> change = await setup.BeginAsync();

        Assert.Equal("user", change.Original.Value);
        SystemChangeEntry entry = ReadEntry(setup, "Fake.json");
        Assert.Equal("Fake", entry.Kind);
        Assert.Equal(Self, entry.Owner);
        Assert.Equal("user", entry.Original.GetProperty("value").GetString());
        Assert.Contains("by hand", entry.Description, StringComparison.Ordinal);
        Assert.Empty(setup.Kind.Applied);
    }

    [Fact]
    public async Task DisposeRestoresChecksAndRemovesTheEntryAndTheEmptyFolder()
    {
        Setup setup = new();
        setup.Kind.Current = "user";

        await using (SystemChange<FakeState> change = await setup.BeginAsync())
        {
            await change.ApplyAsync(new FakeState("test"), TestContext.Current.CancellationToken);
            Assert.Equal("test", setup.Kind.Current);
        }

        Assert.Equal("user", setup.Kind.Current);
        Assert.False(Directory.Exists(setup.Journal.Directory));
        Assert.False(File.Exists(setup.Journal.LockPath));
    }

    [Fact]
    public async Task FailedTestStillRestores()
    {
        Setup setup = new();
        setup.Kind.Current = "user";

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await using SystemChange<FakeState> change = await setup.BeginAsync();
            await change.ApplyAsync(new FakeState("test"), TestContext.Current.CancellationToken);
            throw new InvalidDataException("The check of the test failed.");
        });

        Assert.Equal("user", setup.Kind.Current);
        Assert.False(Directory.Exists(setup.Journal.Directory));
    }

    [Fact]
    public async Task RestoreThatDoesNotTakeEffectKeepsTheEntryAndFails()
    {
        Setup setup = new();
        setup.Kind.Current = "user";
        SystemChange<FakeState> change = await setup.BeginAsync();
        await change.ApplyAsync(new FakeState("test"), TestContext.Current.CancellationToken);
        setup.Kind.RestoreIgnored = true;

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(async () => await change.DisposeAsync());

        Assert.Contains("not back", failure.Message, StringComparison.Ordinal);
        Assert.Contains("by hand", failure.Message, StringComparison.Ordinal);
        Assert.Single(setup.Journal.PendingFiles());

        // A later cleanup (fixture start, watchdog, -RestoreOnly) restores it once the restore works again.
        setup.Kind.RestoreIgnored = false;
        SystemChangeRestoreReport report = await setup.Journal.RestorePendingAsync(includeOwn: true, TimeSpan.Zero, TestContext.Current.CancellationToken);
        Assert.True(report.Success, report.ToString());
        Assert.Equal("user", setup.Kind.Current);
        Assert.False(Directory.Exists(setup.Journal.Directory));
    }

    [Fact]
    public async Task ChangeWhoseEntryVanishedIsStillRestoredAndFails()
    {
        Setup setup = new();
        setup.Kind.Current = "user";
        SystemChange<FakeState> change = await setup.BeginAsync();
        await change.ApplyAsync(new FakeState("test"), TestContext.Current.CancellationToken);
        File.Delete(change.Entry.File);

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(async () => await change.DisposeAsync());

        Assert.Contains("vanished", failure.Message, StringComparison.Ordinal);
        Assert.Equal("user", setup.Kind.Current);
        Assert.False(Directory.Exists(setup.Journal.Directory));
    }

    [Fact]
    public async Task EntryIsWrittenCompletelyWithoutATemporaryFile()
    {
        Setup setup = new();
        setup.Kind.Current = "user";

        await using SystemChange<FakeState> change = await setup.BeginAsync();

        Assert.Equal(["Fake.json"], Directory.EnumerateFiles(setup.Journal.Directory).Select(Path.GetFileName));
        SystemChangeEntry written = ReadEntry(setup, "Fake.json");
        Assert.Equal(change.Entry.Owner, written.Owner);
        Assert.Equal(change.Entry.Description, written.Description);
        Assert.Equal("user", written.Original.GetProperty("value").GetString());
    }

    [Fact]
    public void WatchdogOfATestRunCannotBeSwitchedOffByTheEnvironment()
    {
        Assert.SkipWhen(E2EFixture.IsAuxiliaryProcess, "Runs in the test run itself only.");
        string? previous = Environment.GetEnvironmentVariable(SystemChangeWatchdog.Variable);
        try
        {
            Environment.SetEnvironmentVariable(SystemChangeWatchdog.Variable, "0");

            Assert.True(SystemChangeWatchdog.IsEnabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SystemChangeWatchdog.Variable, previous);
        }
    }

    [Fact]
    public void WatchdogIsFoundByTheCommandLineItIsStartedWith()
    {
        // WaitUntilGoneAsync finds the watchdog by the end of its command line: a query that no longer matches would report
        // it gone at once and bring back the race of T-073.
        string commandLine = SystemChangeWatchdog.CommandLine(@"C:\Program Files\PowerShell\7\pwsh.exe", @"D:\repo\tools\run-e2e.ps1", 4242);

        Assert.EndsWith(SystemChangeWatchdog.CommandLineEnd(4242), commandLine, StringComparison.Ordinal);
        Assert.Contains($"LIKE '%{SystemChangeWatchdog.CommandLineEnd(4242)}'", SystemChangeWatchdog.ProcessQuery(4242), StringComparison.Ordinal);
        // Another process id is another watchdog.
        Assert.False(commandLine.EndsWith(SystemChangeWatchdog.CommandLineEnd(242), StringComparison.Ordinal));
    }

    [Fact]
    public async Task EntryOfACrashedProcessIsRestoredBeforeTheNewOriginalIsRead()
    {
        Setup setup = new();
        // A crashed process changed "user" to "test" and left its entry.
        setup.WriteEntry(Dead, "user");
        setup.Kind.Current = "test";

        await using SystemChange<FakeState> change = await setup.BeginAsync();

        Assert.Equal("user", change.Original.Value);
        // Restore and its check first, then the original is read.
        Assert.Equal(["restore:user", "read", "read"], setup.Kind.Calls);
        Assert.Equal(Self, ReadEntry(setup, "Fake.json").Owner);
    }

    [Fact]
    public async Task EntryOfACrashedProcessOfAnotherKindIsRestoredFirstToo()
    {
        Setup setup = new();
        setup.WriteEntry(Dead, "other-user", kind: "Other");
        setup.Other.Current = "test";

        await using SystemChange<FakeState> change = await setup.BeginAsync();

        Assert.Equal("other-user", setup.Other.Current);
        Assert.Single(setup.Journal.PendingFiles());
    }

    [Fact]
    public async Task UnrestorableEntryOfACrashedProcessBlocksANewChange()
    {
        Setup setup = new();
        setup.WriteEntry(Dead, "user");
        setup.Kind.Current = "test";
        setup.Kind.RestoreIgnored = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.BeginAsync());

        Assert.Equal(["restore:user", "read"], setup.Kind.Calls);
        Assert.Empty(setup.Kind.Applied);
        Assert.Equal(Dead, ReadEntry(setup, "Fake.json").Owner);
    }

    [Fact]
    public async Task RestorePendingWithoutEntriesChangesNothing()
    {
        Setup setup = new();
        setup.Kind.Current = "user";

        SystemChangeRestoreReport report = await setup.Journal.RestorePendingAsync(includeOwn: false, TimeSpan.Zero, TestContext.Current.CancellationToken);

        Assert.True(report.IsEmpty);
        Assert.True(report.Success);
        Assert.Equal("No pending system changes.", report.ToString());
        Assert.Empty(setup.Kind.Calls);
        Assert.False(Directory.Exists(setup.Journal.Directory));
    }

    [Fact]
    public async Task RestorePendingRestoresEntriesOfGoneProcessesOnly()
    {
        Setup setup = new();
        setup.WriteEntry(Dead, "user");
        setup.Kind.Current = "test";
        setup.WriteEntry(Running, "other-user", kind: "Other");
        setup.Other.Current = "other-test";

        SystemChangeRestoreReport report = await setup.Journal.RestorePendingAsync(includeOwn: false, TimeSpan.Zero, TestContext.Current.CancellationToken);

        Assert.Equal("user", setup.Kind.Current);
        Assert.Equal("other-test", setup.Other.Current);
        Assert.Single(report.Restored);
        string stillOwned = Assert.Single(report.StillOwned);
        Assert.Contains("running", stillOwned, StringComparison.Ordinal);
        Assert.False(report.Success);
        Assert.Equal(["Other.json"], setup.Journal.PendingFiles().Select(Path.GetFileName));
    }

    [Fact]
    public async Task RestorePendingWaitsForAnOwnerThatEnds()
    {
        Setup setup = new();
        setup.WriteEntry(Running, "user");
        setup.Kind.Current = "test";
        Task<SystemChangeRestoreReport> restore = setup.Journal.RestorePendingAsync(includeOwn: false, TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.Equal("test", setup.Kind.Current);

        setup.Alive.Remove(Running);
        SystemChangeRestoreReport report = await restore;

        Assert.True(report.Success, report.ToString());
        Assert.Equal("user", setup.Kind.Current);
    }

    [Fact]
    public async Task OwnActiveChangeIsLeftAloneUnlessAskedFor()
    {
        Setup setup = new();
        setup.Kind.Current = "user";
        SystemChange<FakeState> change = await setup.BeginAsync();
        await change.ApplyAsync(new FakeState("test"), TestContext.Current.CancellationToken);

        SystemChangeRestoreReport untouched = await setup.Journal.RestorePendingAsync(includeOwn: false, TimeSpan.Zero, TestContext.Current.CancellationToken);
        Assert.True(untouched.IsEmpty);
        Assert.Equal("test", setup.Kind.Current);

        SystemChangeRestoreReport restored = await setup.Journal.RestorePendingAsync(includeOwn: true, TimeSpan.Zero, TestContext.Current.CancellationToken);
        Assert.Single(restored.Restored);
        Assert.Equal("user", setup.Kind.Current);
    }

    [Fact]
    public async Task SecondChangeOfTheSameKindWhileTheFirstIsActiveIsRefused()
    {
        Setup setup = new();
        setup.Kind.Current = "user";
        await using SystemChange<FakeState> first = await setup.BeginAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.BeginAsync());
    }

    [Fact]
    public async Task UnreadableEntryIsReportedAndKept()
    {
        Setup setup = new();
        Directory.CreateDirectory(setup.Journal.Directory);
        await File.WriteAllTextAsync(Path.Combine(setup.Journal.Directory, "Fake.json"), "{ not json", TestContext.Current.CancellationToken);

        SystemChangeRestoreReport report = await setup.Journal.RestorePendingAsync(includeOwn: false, TimeSpan.Zero, TestContext.Current.CancellationToken);

        Assert.Contains("not readable", Assert.Single(report.Failed), StringComparison.Ordinal);
        Assert.Contains("NOT restored", report.ToString(), StringComparison.Ordinal);
        Assert.Single(setup.Journal.PendingFiles());
    }

    [Fact]
    public async Task HalfWrittenEntryOfACrashIsRemoved()
    {
        Setup setup = new();
        Directory.CreateDirectory(setup.Journal.Directory);
        await File.WriteAllTextAsync(Path.Combine(setup.Journal.Directory, "Fake.json.tmp"), "{ \"kind\": ", TestContext.Current.CancellationToken);

        SystemChangeRestoreReport report = await setup.Journal.RestorePendingAsync(includeOwn: false, TimeSpan.Zero, TestContext.Current.CancellationToken);

        Assert.True(report.Success);
        Assert.False(Directory.Exists(setup.Journal.Directory));
    }

    [Fact]
    public async Task WatchdogStartsOnceBeforeTheFirstEntry()
    {
        Setup setup = new();
        int started = 0;
        setup.BeforeFirstChange = () =>
        {
            Assert.Empty(setup.Journal.PendingFiles());
            started++;
        };

        await using (SystemChange<FakeState> change = await setup.BeginAsync())
        {
        }

        await using (SystemChange<FakeState> again = await setup.BeginAsync())
        {
        }

        Assert.Equal(1, started);
    }

    [Fact]
    public async Task WatchdogThatCannotStartPreventsTheChange()
    {
        Setup setup = new() { BeforeFirstChange = static () => throw new InvalidOperationException("No pwsh.") };

        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.BeginAsync());

        Assert.Empty(setup.Journal.PendingFiles());
        Assert.Empty(setup.Kind.Applied);
    }

    [Fact]
    public async Task UnregisteredKindIsRefused()
    {
        Setup setup = new();

        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Journal.BeginAsync(new FakeKind("Stranger", changesSystem: false), null, TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(setup.Journal.Directory));
    }

    [Fact]
    public async Task KeyedEntriesOfOneKindHaveFilesOfTheirOwn()
    {
        Setup setup = new();

        await using SystemChange<FakeState> first = await setup.BeginAsync("monitor-1");
        await using SystemChange<FakeState> second = await setup.BeginAsync("monitor-2");

        Assert.Equal(2, setup.Journal.PendingFiles().Count);
        Assert.Equal(setup.Journal.FileOf("Fake", "monitor-1"), first.Entry.File);
        Assert.Equal("monitor-1", ReadEntry(setup, Path.GetFileName(first.Entry.File)).Key);
    }

    [Fact]
    public async Task SystemChangeIsRefusedWithoutTheConfirmedWarning()
    {
        Assert.SkipWhen(SystemChanges.IsConfirmed, $"{SystemChanges.Variable}=1 in this run; the end-to-end guard test covers it (child process without it).");
        Setup setup = new();
        FakeKind system = new("System", changesSystem: true);
        setup.Register(system);

        await Assert.ThrowsAsync<InvalidOperationException>(() => SystemChange<FakeState>.BeginAsync(setup.Journal, system, null, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => system.ApplyStateAsync(null, new FakeState("test"), TestContext.Current.CancellationToken));

        Assert.Empty(system.Calls);
        Assert.False(Directory.Exists(setup.Journal.Directory));
    }

    [Fact]
    public async Task SystemChangeOfAKindTheWarningDidNotListIsRefused()
    {
        Assert.SkipWhen(SystemChanges.IsConfirmed, $"{SystemChanges.Variable}=1 in this run; the variables cannot be changed safely.");
        Setup setup = new();
        FakeKind system = new("System", changesSystem: true);
        setup.Register(system);
        string? kinds = Environment.GetEnvironmentVariable(SystemChanges.KindsVariable);
        try
        {
            // A confirmed run whose warning listed other kinds only.
            Environment.SetEnvironmentVariable(SystemChanges.Variable, "1");
            Environment.SetEnvironmentVariable(SystemChanges.KindsVariable, $"{SystemChangeTraits.Theme},Systems");

            InvalidOperationException begin = await Assert.ThrowsAsync<InvalidOperationException>(
                () => SystemChange<FakeState>.BeginAsync(setup.Journal, system, null, TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<InvalidOperationException>(() => system.ApplyStateAsync(null, new FakeState("test"), TestContext.Current.CancellationToken));

            Assert.Contains("did not list it", begin.Message, StringComparison.Ordinal);
            Assert.Empty(system.Calls);
            Assert.False(Directory.Exists(setup.Journal.Directory));
        }
        finally
        {
            Environment.SetEnvironmentVariable(SystemChanges.Variable, null);
            Environment.SetEnvironmentVariable(SystemChanges.KindsVariable, kinds);
        }
    }

    [Theory]
    [InlineData("1", "Theme,DisplayScale", "DisplayScale", true)]
    [InlineData("1", " Theme , MonitorOff ", "MonitorOff", true)]
    [InlineData("1", "Theme", "DisplayScale", false)]
    [InlineData("1", "theme", "Theme", false)]
    [InlineData("1", "ThemeX", "Theme", false)]
    [InlineData("1", null, "Theme", false)]
    [InlineData(null, "Theme", "Theme", false)]
    [InlineData("true", "Theme", "Theme", false)]
    public void KindIsConfirmedOnlyWhenTheWarningListedIt(string? confirmed, string? kinds, string trait, bool expected) =>
        Assert.Equal(expected, SystemChanges.IsConfirmedFor(trait, confirmed, kinds));

    [Fact]
    public void EveryKindThatChangesTheSystemHasAKnownTraitValue()
    {
        Assert.All(SystemChanges.Kinds.Values.Where(static kind => kind.ChangesSystem), static kind => Assert.Contains(kind.Trait, SystemChangeTraits.All));
        Assert.Null(SystemChanges.Probe.Trait);
        Assert.Equal(SystemChangeTraits.All.Count, SystemChangeTraits.All.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void EveryTestThatChangesTheSystemNamesAKnownKind()
    {
        List<(string Test, string Value)> traits = [.. typeof(SystemChangeJournalTests).Assembly.GetTypes()
            .SelectMany(static type => type.GetMethods())
            .SelectMany(static method => method.GetCustomAttributes(typeof(TraitAttribute), inherit: false).Cast<TraitAttribute>()
                .Where(static trait => trait.Name == SystemChanges.Trait)
                .Select(trait => ($"{method.DeclaringType?.Name}.{method.Name}", trait.Value)))];

        Assert.NotEmpty(traits);
        Assert.All(traits, static trait => Assert.True(SystemChangeTraits.All.Contains(trait.Value, StringComparer.Ordinal), $"{trait.Test}: {trait.Value}"));
        // Display changes have their self-tests (T-066).
        Assert.Contains(traits, static trait => trait.Value == SystemChangeTraits.DisplayScale);
        Assert.Contains(traits, static trait => trait.Value == SystemChangeTraits.MonitorOff);
    }

    [Fact]
    public async Task PendingEntriesAreRestoredInTheOrderOfTheirKinds()
    {
        Setup setup = new();
        List<string> calls = [];
        setup.Kind.SharedCalls = calls;
        setup.Other.SharedCalls = calls;
        // "Other" comes after "Fake" by name, but goes first (like a switched-off monitor before the scale).
        setup.Other.Order = -10;
        setup.WriteEntry(Dead, "a", "Fake");
        setup.WriteEntry(Dead, "b", "Other");

        SystemChangeRestoreReport report = await setup.Journal.RestorePendingAsync(includeOwn: false, TimeSpan.Zero, TestContext.Current.CancellationToken);

        Assert.Equal(["Other:restore", "Fake:restore"], calls);
        Assert.Equal(2, report.Restored.Count);
        Assert.True(report.Success, report.ToString());
    }

    [Fact]
    public void EverySystemKindNeedsTheWarningAndOnlyTheProbeDoesNot()
    {
        Assert.All(SystemChanges.Kinds.Values.Where(static kind => kind != SystemChanges.Probe), static kind => Assert.True(kind.ChangesSystem, kind.Name));
        Assert.False(SystemChanges.Probe.ChangesSystem);
        Assert.Equal(SystemChanges.Kinds.Count, SystemChanges.Kinds.Values.Select(static kind => kind.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void JournalLivesOutsideTheWorktreeInLocalAppData()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);

        Assert.Equal(Path.Combine(localAppData, "PrusaTrayDiagnostic.E2E.Restore"), SystemChanges.JournalDirectory);
        Assert.Equal(SystemChanges.JournalDirectory + ".lock", SystemChanges.Journal.LockPath);
    }

    [Fact]
    public void ReportListsWhatToDoByHand()
    {
        SystemChangeRestoreReport report = new();
        report.Restored.Add("Theme of x: was light");
        report.Failed.Add("DoNotDisturb of y: toggle not found" + Environment.NewLine + "By hand: Win+N");

        string text = report.ToString();

        Assert.Contains("Restored:", text, StringComparison.Ordinal);
        Assert.Contains("docs/troubleshooting.md", text, StringComparison.Ordinal);
        Assert.Contains("By hand: Win+N", text, StringComparison.Ordinal);
        Assert.False(report.Success);
    }

    [Fact]
    public void CurrentProcessIsAliveAndAGoneOneIsNot()
    {
        Assert.True(ProcessOwner.Current.IsAlive());
        Assert.False((ProcessOwner.Current with { StartTimeUtcTicks = ProcessOwner.Current.StartTimeUtcTicks - 1 }).IsAlive());
        Assert.False(new ProcessOwner(int.MaxValue - 1, 0, "gone").IsAlive());
    }

    private static SystemChangeEntry ReadEntry(Setup setup, string file) =>
        JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(setup.Journal.Directory, file)), SystemChangeJson.Default.SystemChangeEntry)!;

    /// <summary>A journal in a temporary folder with two fake kinds; owners alive: <see cref="Self"/>, <see cref="Running"/>.</summary>
    private sealed class Setup
    {
        private readonly Dictionary<string, ISystemChangeKind> _kinds = new(StringComparer.Ordinal);

        public Setup()
        {
            Register(Kind);
            Register(Other);
            Journal = new SystemChangeJournal(
                Path.Combine(UnitTestDirectories.Create(), "journal"),
                _kinds,
                Self,
                owner => Alive.Contains(owner),
                () => BeforeFirstChange?.Invoke());
        }

        public FakeKind Kind { get; } = new("Fake", changesSystem: false);

        public FakeKind Other { get; } = new("Other", changesSystem: false);

        public HashSet<ProcessOwner> Alive { get; } = [Self, Running];

        public Action? BeforeFirstChange { get; set; }

        public SystemChangeJournal Journal { get; }

        public void Register(FakeKind kind) => _kinds[kind.Name] = kind;

        public Task<SystemChange<FakeState>> BeginAsync(string? key = null) =>
            SystemChange<FakeState>.BeginAsync(Journal, Kind, key, TestContext.Current.CancellationToken);

        /// <summary>An entry as a crashed (or running) process left it.</summary>
        public void WriteEntry(ProcessOwner owner, string original, string kind = "Fake")
        {
            Directory.CreateDirectory(Journal.Directory);
            SystemChangeEntry entry = new(kind, null, owner, DateTimeOffset.Now, JsonSerializer.SerializeToElement(new FakeState(original), FakeJson.Default.FakeState), $"{kind} was {original}; by hand: nothing.");
            File.WriteAllText(Journal.FileOf(kind, null), JsonSerializer.Serialize(entry, SystemChangeJson.Default.SystemChangeEntry));
        }
    }
}

/// <summary>State of a <see cref="FakeKind"/>.</summary>
internal sealed record FakeState(string? Value);

/// <summary>An in-memory kind that records its calls.</summary>
internal sealed class FakeKind(string name, bool changesSystem) : SystemChangeKind<FakeState>(name, changesSystem ? name : null, FakeJson.Default.FakeState)
{
    /// <summary>See <see cref="ISystemChangeKind.RestoreOrder"/>.</summary>
    public int Order { get; set; }

    public override int RestoreOrder => Order;

    /// <summary>Shared call log of several kinds (restore order across kinds).</summary>
    public List<string>? SharedCalls { get; set; }

    public string? Current { get; set; }

    /// <summary>The restore "works" but the state stays (e.g. Explorer overwrote it).</summary>
    public bool RestoreIgnored { get; set; }

    public List<string> Calls { get; } = [];

    public List<string?> Applied { get; } = [];

    public override Task<FakeState> ReadStateAsync(string? key, CancellationToken cancellationToken)
    {
        Calls.Add("read");
        return Task.FromResult(new FakeState(Current));
    }

    public override string Describe(string? key, FakeState original) => $"{Name} was {original.Value ?? "(missing)"}; by hand: nothing.";

    protected override Task ApplyCoreAsync(string? key, FakeState state, CancellationToken cancellationToken)
    {
        Calls.Add($"apply:{state.Value}");
        Applied.Add(state.Value);
        Current = state.Value;
        return Task.CompletedTask;
    }

    protected override Task RestoreCoreAsync(string? key, FakeState original, CancellationToken cancellationToken)
    {
        Calls.Add($"restore:{original.Value}");
        SharedCalls?.Add($"{Name}:restore");
        if (!RestoreIgnored)
        {
            Current = original.Value;
        }

        return Task.CompletedTask;
    }
}

[JsonSerializable(typeof(FakeState))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class FakeJson : JsonSerializerContext;
