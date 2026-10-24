using Microsoft.Extensions.Logging;

using LayerPeek.App.Platform.Windows;
using LayerPeek.App.Tests.Tray;

namespace LayerPeek.App.Tests.Platform.Windows;

/// <summary>Decision logic only: an in-memory store stands in for the registry, nothing touches it.</summary>
public sealed class TrayIconPromotionTests : IDisposable
{
    private const string OwnExe = @"C:\Users\test\AppData\Local\LayerPeek\current\LayerPeek.exe";
    private const int Attempts = 3;

    private readonly InMemoryNotifyIconSettings _settings = new();
    private readonly ListLogger<TrayIconPromotion> _logger = new();

    /// <summary>Each release ends one pause between two reads.</summary>
    private readonly SemaphoreSlim _pause = new(0);

    private TrayIconPromotion CreatePromotion() => new(_settings, _logger, _pause.WaitAsync, Attempts);

    public void Dispose() => _pause.Dispose();

    [Theory]
    [InlineData(@"C:\Apps\LayerPeek.exe", true)]
    [InlineData(@"D:\src\bin\Debug\net11.0\layerpeek.EXE", true)]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void AppliesOnlyToTheAppExecutable(string? processPath, bool expected)
    {
        Assert.Equal(expected, TrayIconPromotion.AppliesTo(processPath));
    }

    [Fact]
    public async Task EntryWithoutValueIsPromoted()
    {
        _settings.Add("1", OwnExe, promoted: null);

        TrayIconPromotionResult result = await CreatePromotion().PromoteAsync(OwnExe, TestContext.Current.CancellationToken);

        Assert.Equal(TrayIconPromotionResult.Promoted, result);
        Assert.Equal(["1"], _settings.PromotedKeys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ExistingChoiceOfTheUserIsKept(int promoted)
    {
        _settings.Add("1", OwnExe, promoted);

        TrayIconPromotionResult result = await CreatePromotion().PromoteAsync(OwnExe, TestContext.Current.CancellationToken);

        Assert.Equal(TrayIconPromotionResult.LeftToUser, result);
        Assert.Empty(_settings.PromotedKeys);
    }

    [Fact]
    public async Task OtherExecutablesAreNeverTouched()
    {
        _settings.Add("other", @"C:\Other\LayerPeek.exe", promoted: null);
        _settings.Add("own", @"c:\users\TEST\AppData\Local\LayerPeek\.\current\LayerPeek.EXE", promoted: null);
        _settings.Add("broken", null, promoted: null);

        TrayIconPromotionResult result = await CreatePromotion().PromoteAsync(OwnExe, TestContext.Current.CancellationToken);

        Assert.Equal(TrayIconPromotionResult.Promoted, result);
        Assert.Equal(["own"], _settings.PromotedKeys);
    }

    [Fact]
    public async Task OnlyUndecidedEntriesOfTheExecutableArePromoted()
    {
        _settings.Add("hidden", OwnExe, promoted: 0);
        _settings.Add("new", OwnExe, promoted: null);

        TrayIconPromotionResult result = await CreatePromotion().PromoteAsync(OwnExe, TestContext.Current.CancellationToken);

        Assert.Equal(TrayIconPromotionResult.Promoted, result);
        Assert.Equal(["new"], _settings.PromotedKeys);
    }

    [Fact]
    public async Task WaitsUntilExplorerCreatesTheEntry()
    {
        Task<TrayIconPromotionResult> promotion = CreatePromotion().PromoteAsync(OwnExe, TestContext.Current.CancellationToken);
        Assert.False(promotion.IsCompleted);
        Assert.Equal(1, _settings.ReadCount);

        _settings.Add("1", OwnExe, promoted: null);
        _pause.Release();

        Assert.Equal(TrayIconPromotionResult.Promoted, await promotion);
        Assert.Equal(2, _settings.ReadCount);
        Assert.Equal(["1"], _settings.PromotedKeys);
    }

    [Fact]
    public async Task GivesUpAfterTheLimit()
    {
        Task<TrayIconPromotionResult> promotion = CreatePromotion().PromoteAsync(OwnExe, TestContext.Current.CancellationToken);

        Assert.False(promotion.IsCompleted);
        _pause.Release(Attempts - 1);

        Assert.Equal(TrayIconPromotionResult.NotFound, await promotion);
        Assert.Equal(Attempts, _settings.ReadCount);
        Assert.Empty(_settings.PromotedKeys);
    }

    [Fact]
    public async Task CancellationEndsTheWaitWithoutThrowing()
    {
        using CancellationTokenSource cancellation = new();
        Task<TrayIconPromotionResult> promotion = CreatePromotion().PromoteAsync(OwnExe, cancellation.Token);

        await cancellation.CancelAsync();

        Assert.Equal(TrayIconPromotionResult.Cancelled, await promotion);
        Assert.Empty(_settings.PromotedKeys);
    }

    [Fact]
    public async Task RegistryFailureIsLoggedNotThrown()
    {
        _settings.Failure = new UnauthorizedAccessException("denied");

        TrayIconPromotionResult result = await CreatePromotion().PromoteAsync(OwnExe, TestContext.Current.CancellationToken);

        Assert.Equal(TrayIconPromotionResult.Failed, result);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Exception == _settings.Failure);
    }

    [Fact]
    public async Task FailedWriteDoesNotStopTheOtherEntries()
    {
        _settings.Add("1", OwnExe, promoted: null);
        _settings.Add("2", OwnExe, promoted: null);
        _settings.FailingPromoteKey = "1";

        TrayIconPromotionResult result = await CreatePromotion().PromoteAsync(OwnExe, TestContext.Current.CancellationToken);

        Assert.Equal(TrayIconPromotionResult.Promoted, result);
        Assert.Equal(["2"], _settings.PromotedKeys);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task OnlyFailedWritesReportFailed()
    {
        _settings.Add("1", OwnExe, promoted: null);
        _settings.FailingPromoteKey = "1";

        TrayIconPromotionResult result = await CreatePromotion().PromoteAsync(OwnExe, TestContext.Current.CancellationToken);

        Assert.Equal(TrayIconPromotionResult.Failed, result);
        Assert.Empty(_settings.PromotedKeys);
    }

    [Fact]
    public void AtLeastOneAttemptIsRequired()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrayIconPromotion(_settings, _logger, attempts: 0));
    }

    [Fact]
    public async Task EmptyExecutablePathIsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => CreatePromotion().PromoteAsync("", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ClearPromotionsTouchesOnlyTheOwnExecutableAndKeepsTheEntries()
    {
        _settings.Add("own1", OwnExe, promoted: 1);
        _settings.Add("own2", OwnExe.ToUpperInvariant(), promoted: 0);
        _settings.Add("own3", OwnExe, promoted: null);
        _settings.Add("other", @"C:\Other\LayerPeek.exe", promoted: 1);

        int cleared = CreatePromotion().ClearPromotions(OwnExe);

        Assert.Equal(2, cleared);
        Assert.Equal(["own1", "own2"], _settings.ClearedKeys);
        Assert.Equal(4, _settings.Entries.Count);
        Assert.Equal(1, _settings.Entries["other"].Promoted);
    }

    [Fact]
    public void ClearPromotionsContinuesAfterAFailedWrite()
    {
        _settings.Add("own1", OwnExe, promoted: 1);
        _settings.Add("own2", OwnExe, promoted: 1);
        _settings.FailingClearKey = "own1";

        int cleared = CreatePromotion().ClearPromotions(OwnExe);

        Assert.Equal(1, cleared);
        Assert.Equal(["own2"], _settings.ClearedKeys);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void ClearPromotionsReturnsZeroWhenTheRegistryCannotBeRead()
    {
        _settings.Failure = new UnauthorizedAccessException("denied");

        Assert.Equal(0, CreatePromotion().ClearPromotions(OwnExe));
    }
}

/// <summary>The notification area settings of Explorer, in memory.</summary>
internal sealed class InMemoryNotifyIconSettings : INotifyIconSettings
{
    public Dictionary<string, (string? Path, int? Promoted)> Entries { get; } = [];

    public List<string> PromotedKeys { get; } = [];

    public int ReadCount { get; private set; }

    public Exception? Failure { get; set; }

    public List<string> ClearedKeys { get; } = [];

    public string? FailingClearKey { get; set; }

    public string? FailingPromoteKey { get; set; }

    public void Add(string key, string? path, int? promoted) => Entries[key] = (path, promoted);

    public IReadOnlyList<NotifyIconEntry> ReadEntries()
    {
        ReadCount++;
        if (Failure is not null)
        {
            throw Failure;
        }

        return [.. Entries.Select(e => new NotifyIconEntry(e.Key, e.Value.Path, e.Value.Promoted is not null))];
    }

    public void Promote(string keyName)
    {
        if (keyName == FailingPromoteKey)
        {
            throw new UnauthorizedAccessException("denied");
        }

        PromotedKeys.Add(keyName);
        Entries[keyName] = (Entries[keyName].Path, 1);
    }

    public void ClearPromotion(string keyName)
    {
        if (keyName == FailingClearKey)
        {
            throw new IOException("locked");
        }

        ClearedKeys.Add(keyName);
        Entries[keyName] = (Entries[keyName].Path, null);
    }
}
