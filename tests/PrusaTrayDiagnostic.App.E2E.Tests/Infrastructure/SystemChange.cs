using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>
/// One kind of change of the system an end-to-end test may make (T-065): theme, Do Not Disturb, tray icon promotion…
/// The kind reads the current state, applies a state and restores an original one; the states are stored as JSON in the
/// <see cref="SystemChangeJournal"/>, so that another process (start cleanup, watchdog, <c>run-e2e.ps1 -RestoreOnly</c>)
/// can restore them after a crash without the object that made the change. Kinds are stateless.
/// </summary>
internal interface ISystemChangeKind
{
    /// <summary>Stable name, the start of the journal file name (letters and digits only).</summary>
    string Name { get; }

    /// <summary>
    /// Whether the kind changes settings of Windows (needs the confirmed warning of <c>tools/run-e2e.ps1</c>); only the
    /// probe of the infrastructure self-tests, which touches a key of the tests' own, says no.
    /// </summary>
    bool ChangesSystem { get; }

    /// <summary>
    /// The value of the trait <see cref="SystemChanges.Trait"/> a test that makes this change carries (one of
    /// <see cref="SystemChangeTraits"/>; the warning lists the change by it), or <see langword="null"/> for a kind that
    /// changes nothing of the system. The change is refused unless the warning of the run listed this value.
    /// </summary>
    string? Trait { get; }

    /// <summary>
    /// Order of the restore of pending entries (lower first; entries of the same order by file name): a switched-off
    /// monitor comes back before the scale of that monitor can be restored.
    /// </summary>
    int RestoreOrder { get; }

    Task<JsonElement> ReadAsync(string? key, CancellationToken cancellationToken);

    /// <summary>Restores <paramref name="original"/>; never needs the warning (it only goes back).</summary>
    Task RestoreAsync(string? key, JsonElement original, CancellationToken cancellationToken);

    bool Matches(JsonElement original, JsonElement current);

    /// <summary>The original state and how to restore it by hand, for the journal and failure reports.</summary>
    string Describe(string? key, JsonElement original);
}

/// <summary>A kind with a typed state (see <see cref="ISystemChangeKind"/>).</summary>
internal abstract class SystemChangeKind<TState> : ISystemChangeKind
    where TState : notnull
{
    private readonly JsonTypeInfo<TState> _typeInfo;

    /// <param name="name">Stable name of the kind (journal file name).</param>
    /// <param name="trait">
    /// Trait value of the tests that make the change (see <see cref="ISystemChangeKind.Trait"/>); <see langword="null"/>
    /// only for a kind that changes nothing of the system.
    /// </param>
    /// <param name="typeInfo">Source-generated JSON of the state.</param>
    protected SystemChangeKind(string name, string? trait, JsonTypeInfo<TState> typeInfo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!name.All(char.IsAsciiLetterOrDigit))
        {
            throw new ArgumentException("A kind name has letters and digits only (it is part of a file name).", nameof(name));
        }

        Name = name;
        Trait = trait;
        _typeInfo = typeInfo;
    }

    public string Name { get; }

    public bool ChangesSystem => Trait is not null;

    public string? Trait { get; }

    public virtual int RestoreOrder => 0;

    public abstract Task<TState> ReadStateAsync(string? key, CancellationToken cancellationToken);

    /// <summary>
    /// Applies <paramref name="state"/>. A kind that changes the system refuses without the confirmed warning that listed
    /// its <see cref="Trait"/>, whoever calls it (the guard of <see cref="SystemChanges"/>).
    /// </summary>
    public Task ApplyStateAsync(string? key, TState state, CancellationToken cancellationToken)
    {
        if (Trait is { } trait)
        {
            SystemChanges.EnsureConfirmed(trait);
        }

        return ApplyCoreAsync(key, state, cancellationToken);
    }

    public virtual bool Matches(TState original, TState current) => EqualityComparer<TState>.Default.Equals(original, current);

    public abstract string Describe(string? key, TState original);

    public TState Deserialize(JsonElement state) =>
        state.Deserialize(_typeInfo) ?? throw new InvalidOperationException($"The journal holds no {Name} state.");

    public JsonElement Serialize(TState state) => JsonSerializer.SerializeToElement(state, _typeInfo);

    protected abstract Task ApplyCoreAsync(string? key, TState state, CancellationToken cancellationToken);

    /// <summary>Goes back to <paramref name="original"/>; by default the same as applying it.</summary>
    protected virtual Task RestoreCoreAsync(string? key, TState original, CancellationToken cancellationToken) =>
        ApplyCoreAsync(key, original, cancellationToken);

    async Task<JsonElement> ISystemChangeKind.ReadAsync(string? key, CancellationToken cancellationToken) =>
        Serialize(await ReadStateAsync(key, cancellationToken).ConfigureAwait(false));

    Task ISystemChangeKind.RestoreAsync(string? key, JsonElement original, CancellationToken cancellationToken) =>
        RestoreCoreAsync(key, Deserialize(original), cancellationToken);

    bool ISystemChangeKind.Matches(JsonElement original, JsonElement current) => Matches(Deserialize(original), Deserialize(current));

    string ISystemChangeKind.Describe(string? key, JsonElement original) => Describe(key, Deserialize(original));
}

/// <summary>
/// A change in progress: the original state is in the journal before anything changes, and disposing restores it and
/// checks that it is back (also when the test failed). Created only through <see cref="SystemChanges"/>; the constructor
/// refuses a kind that changes the system without the confirmed warning.
/// </summary>
internal sealed class SystemChange<TState> : IAsyncDisposable
    where TState : notnull
{
    private readonly SystemChangeJournal _journal;
    private readonly SystemChangeKind<TState> _kind;
    private SystemChangeEntry? _entry;
    private bool _disposed;

    private SystemChange(SystemChangeJournal journal, SystemChangeKind<TState> kind, string? key)
    {
        if (kind.Trait is { } trait)
        {
            SystemChanges.EnsureConfirmed(trait);
        }

        _journal = journal;
        _kind = kind;
        Key = key;
    }

    public string? Key { get; }

    /// <summary>The state before the change (read after older entries of a crashed run were restored).</summary>
    public TState Original { get; private set; } = default!;

    internal SystemChangeEntry Entry => _entry ?? throw new InvalidOperationException("Not begun.");

    internal static async Task<SystemChange<TState>> BeginAsync(SystemChangeJournal journal, SystemChangeKind<TState> kind, string? key, CancellationToken cancellationToken)
    {
        SystemChange<TState> change = new(journal, kind, key);
        change._entry = await journal.BeginAsync(kind, key, cancellationToken).ConfigureAwait(false);
        change.Original = kind.Deserialize(change._entry.Original);
        return change;
    }

    public Task<TState> ReadAsync(CancellationToken cancellationToken = default) => _kind.ReadStateAsync(Key, cancellationToken);

    public Task ApplyAsync(TState state, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _kind.ApplyStateAsync(Key, state, cancellationToken);
    }

    /// <summary>
    /// Restores <see cref="Original"/> now and checks it, keeping the change open (and its journal entry): for a test that
    /// checks the restore, or a run-wide change between tests.
    /// </summary>
    public Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _journal.RestoreKeepingEntryAsync(Entry, cancellationToken);
    }

    /// <summary>Restores <see cref="Original"/>, checks it and removes the journal entry; a failure keeps the entry.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed || _entry is null)
        {
            return;
        }

        _disposed = true;
        await _journal.CompleteAsync(_entry, CancellationToken.None).ConfigureAwait(false);
    }
}
