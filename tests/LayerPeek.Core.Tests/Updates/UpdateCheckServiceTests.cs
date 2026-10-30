using System.Collections.Concurrent;

using Microsoft.Extensions.Time.Testing;

using LayerPeek.Core.Notifications;
using LayerPeek.Core.Tests.Monitoring;
using LayerPeek.Core.Updates;

namespace LayerPeek.Core.Tests.Updates;

/// <summary>
/// The update check of T-058: a new version raises the event and one notification; the same or an older version nothing; a
/// repeated check of the same version no further notification. When it checks (Q1 a: shortly after the start, then once a
/// day from the saved time of the last check), what it remembers, and that failures are only logged. Time is fake: no test
/// waits for the real timers.
/// </summary>
public sealed class UpdateCheckServiceTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 30, 8, 0, 0, TimeSpan.Zero);
    private static readonly UpdateCheckTimings Timings = new(TimeSpan.FromSeconds(90), TimeSpan.FromHours(24), TimeSpan.FromHours(1));
    private static readonly UpdatePreferences On = new(AutomaticChecks: true, IncludePrereleases: true, Notify: true, PlaySound: false);

    private readonly FakeTimeProvider _time = new(Start);
    private readonly FakeChecker _checker = new();
    private readonly MemoryStore _store = new();
    private readonly RecordingNotifications _notifications = new();
    private readonly ListLogger<UpdateCheckService> _logger = new();
    private UpdateCheckService? _service;

    public void Dispose() => _service?.Dispose();

    [Fact]
    public async Task ANewVersionRaisesTheEventAndOneNotification()
    {
        _checker.Next = Release("0.3.2");
        UpdateCheckService service = Create("0.3.1");
        int changes = 0;
        service.AvailableChanged += (_, _) => changes++;
        service.ApplyPreferences(On);

        UpdateCheckResult result = await service.CheckNowAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckOutcome.UpdateAvailable, result.Outcome);
        Assert.Equal(ReleaseVersion.Parse("0.3.2"), result.Release?.Version);
        Assert.Equal(ReleaseVersion.Parse("0.3.2"), service.Available?.Version);
        Assert.Equal(1, changes);
        UpdateNotification notification = Assert.Single(_notifications.Shown);
        Assert.Equal("0.3.2", notification.Version);
        Assert.False(notification.PlaySound);
        Assert.Equal(ReleaseVersion.Parse("0.3.2"), _store.State.NotifiedVersion);
        Assert.Equal(Start, _store.State.LastCheck);
    }

    [Theory]
    [InlineData("0.3.1")]
    [InlineData("0.3.0")]
    [InlineData("0.3.1-rc.1")]
    public async Task TheSameOrAnOlderVersionDoesNothing(string newest)
    {
        _checker.Next = Release(newest);
        UpdateCheckService service = Create("0.3.1");
        int changes = 0;
        service.AvailableChanged += (_, _) => changes++;
        service.ApplyPreferences(On);

        UpdateCheckResult result = await service.CheckNowAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckOutcome.UpToDate, result.Outcome);
        Assert.Null(service.Available);
        Assert.Equal(0, changes);
        Assert.Empty(_notifications.Shown);
        Assert.Null(_store.State.NotifiedVersion);
    }

    [Fact]
    public async Task NoReleaseAtAllIsUpToDate()
    {
        UpdateCheckService service = Create("0.3.1");
        service.ApplyPreferences(On);

        Assert.Equal(UpdateCheckOutcome.UpToDate, (await service.CheckNowAsync(TestContext.Current.CancellationToken)).Outcome);
        Assert.Empty(_notifications.Shown);
    }

    [Fact]
    public async Task ARepeatedCheckOfTheSameVersionNotifiesNoMore()
    {
        _checker.Next = Release("0.3.2");
        UpdateCheckService service = Create("0.3.1");
        service.ApplyPreferences(On);

        await service.CheckNowAsync(TestContext.Current.CancellationToken);
        await service.CheckNowAsync(TestContext.Current.CancellationToken);
        await service.CheckNowAsync(TestContext.Current.CancellationToken);

        Assert.Single(_notifications.Shown);
        Assert.Equal(3, _checker.Checks.Count);
    }

    [Fact]
    public async Task AVersionNotifiedInAnEarlierRunIsNotNotifiedAgain()
    {
        _store.State = new UpdateState(Start.AddDays(-2), null, ReleaseVersion.Parse("0.3.2"));
        _checker.Next = Release("0.3.2");
        UpdateCheckService service = Create("0.3.1");
        service.ApplyPreferences(On);

        await service.CheckNowAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_notifications.Shown);
        Assert.Equal(ReleaseVersion.Parse("0.3.2"), service.Available?.Version);
    }

    [Fact]
    public async Task ANewerVersionThanTheNotifiedOneIsNotifiedAgain()
    {
        _checker.Next = Release("0.3.2");
        UpdateCheckService service = Create("0.3.1");
        service.ApplyPreferences(On);
        await service.CheckNowAsync(TestContext.Current.CancellationToken);

        _checker.Next = Release("0.4.0");
        await service.CheckNowAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["0.3.2", "0.4.0"], _notifications.Shown.Select(static n => n.Version));
        Assert.Equal(ReleaseVersion.Parse("0.4.0"), service.Available?.Version);
    }

    [Fact]
    public async Task WithNotificationsOffTheVersionCountsAsNotified()
    {
        _checker.Next = Release("0.3.2");
        UpdateCheckService service = Create("0.3.1");
        service.ApplyPreferences(On with { Notify = false });

        await service.CheckNowAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_notifications.Shown);
        Assert.Equal(ReleaseVersion.Parse("0.3.2"), _store.State.NotifiedVersion);
        Assert.Equal(ReleaseVersion.Parse("0.3.2"), service.Available?.Version);
    }

    [Fact]
    public async Task TheSoundFollowsThePreferences()
    {
        _checker.Next = Release("0.3.2");
        UpdateCheckService service = Create("0.3.1");
        service.ApplyPreferences(On with { PlaySound = true });

        await service.CheckNowAsync(TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(_notifications.Shown).PlaySound);
    }

    [Fact]
    public async Task AFailingNotificationIsOnlyLogged()
    {
        _notifications.Failure = new InvalidOperationException("no notification center");
        _checker.Next = Release("0.3.2");
        UpdateCheckService service = Create("0.3.1");
        service.ApplyPreferences(On);

        UpdateCheckResult result = await service.CheckNowAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckOutcome.UpdateAvailable, result.Outcome);
        Assert.Contains(_logger.Entries, static entry => entry.Exception is InvalidOperationException);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PrereleasesAreAskedForAndOfferedAsThePreferencesSay(bool prereleases)
    {
        _checker.Next = Release("0.4.0-beta.1", prerelease: true);
        UpdateCheckService service = Create("0.3.1");
        service.ApplyPreferences(On with { IncludePrereleases = prereleases });

        UpdateCheckResult result = await service.CheckNowAsync(TestContext.Current.CancellationToken);

        Assert.Equal([prereleases], _checker.Checks);
        // A checker that returns a pre-release though none was asked for: never offered.
        Assert.Equal(prereleases ? UpdateCheckOutcome.UpdateAvailable : UpdateCheckOutcome.UpToDate, result.Outcome);
    }

    [Fact]
    public async Task TurningPrereleasesOffWithdrawsAnOfferedPrereleaseAndChecksAgain()
    {
        _checker.Next = Release("0.3.2", prerelease: true);
        UpdateCheckService service = Create("0.3.1");
        service.ApplyPreferences(On);
        await service.CheckNowAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(service.Available);
        _checker.Next = null;

        service.ApplyPreferences(On with { IncludePrereleases = false });

        Assert.Null(service.Available);
        await WaitForChecksAsync(2);
        Assert.Equal([true, false], _checker.Checks);
    }

    public static TheoryData<Exception> Failures => new()
    {
        new HttpRequestException("403 rate limit", null, System.Net.HttpStatusCode.Forbidden),
        new HttpRequestException("404", null, System.Net.HttpStatusCode.NotFound),
        new TaskCanceledException("timeout"),
        new System.Text.Json.JsonException("invalid JSON"),
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task AFailedCheckIsOnlyLoggedAndKeepsTheOffer(Exception failure)
    {
        _checker.Next = Release("0.3.2");
        UpdateCheckService service = Create("0.3.1");
        service.ApplyPreferences(On);
        await service.CheckNowAsync(TestContext.Current.CancellationToken);
        DateTimeOffset? lastCheck = _store.State.LastCheck;
        _time.Advance(TimeSpan.FromHours(1));
        _checker.Failure = failure;

        UpdateCheckResult result = await service.CheckNowAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckOutcome.Failed, result.Outcome);
        Assert.Equal(ReleaseVersion.Parse("0.3.2"), service.Available?.Version);
        Assert.Contains(_logger.Entries, entry => entry.Exception == failure && entry.Level == Microsoft.Extensions.Logging.LogLevel.Information);
        // A failed check is no check: the next one is due an hour later, not a day.
        Assert.Equal(lastCheck, _store.State.LastCheck);
    }

    [Fact]
    public async Task TheFirstAutomaticCheckComesShortlyAfterTheStart()
    {
        _checker.Next = Release("0.3.2");
        UpdateCheckService service = Create("0.3.1");
        service.ApplyPreferences(On);
        await WaitForLoadAsync();

        _time.Advance(Timings.StartDelay - TimeSpan.FromSeconds(1));
        Assert.Empty(_checker.Checks);

        _time.Advance(TimeSpan.FromSeconds(1));
        await WaitForChecksAsync(1);
        await WaitUntilAsync(() => service.Available is not null);
        Assert.Single(_notifications.Shown);
    }

    [Fact]
    public async Task ThenItChecksOnceADayFromTheLastCheck()
    {
        UpdateCheckService service = Create("0.3.1");
        service.ApplyPreferences(On);
        await WaitForLoadAsync();
        _time.Advance(Timings.StartDelay);
        await WaitForChecksAsync(1);
        await WaitUntilAsync(() => _store.State.LastCheck is not null);

        // Hourly looks for 23 hours: nothing is due.
        for (int hour = 1; hour < 24; hour++)
        {
            _time.Advance(TimeSpan.FromHours(1));
        }

        Assert.Single(_checker.Checks);
        _time.Advance(TimeSpan.FromHours(1));
        await WaitForChecksAsync(2);
    }

    [Fact]
    public async Task AFreshCheckOfAnEarlierRunWaitsForItsDay()
    {
        _store.State = new UpdateState(Start.AddHours(-23), null, null);
        UpdateCheckService service = Create("0.3.1");
        service.ApplyPreferences(On);
        await WaitForLoadAsync();

        _time.Advance(Timings.StartDelay);
        Assert.Empty(_checker.Checks);

        // 23 h + 90 s after the saved check: the next look (an hour after the first one) finds it due.
        _time.Advance(TimeSpan.FromHours(1));
        await WaitForChecksAsync(1);
    }

    [Fact]
    public async Task AClockSetFarBackCountsAsNoCheck()
    {
        _store.State = new UpdateState(Start.AddDays(3), null, null);
        UpdateCheckService service = Create("0.3.1");
        service.ApplyPreferences(On);
        await WaitForLoadAsync();

        _time.Advance(Timings.StartDelay);

        await WaitForChecksAsync(1);
    }

    [Fact]
    public async Task WithAutomaticChecksOffNothingIsCheckedButAManualCheckWorks()
    {
        _checker.Next = Release("0.3.2");
        UpdateCheckService service = Create("0.3.1");
        service.ApplyPreferences(On with { AutomaticChecks = false });
        await WaitForLoadAsync();

        _time.Advance(TimeSpan.FromDays(3));
        Assert.Empty(_checker.Checks);

        UpdateCheckResult result = await service.CheckNowAsync(TestContext.Current.CancellationToken);
        Assert.Equal(UpdateCheckOutcome.UpdateAvailable, result.Outcome);
    }

    [Fact]
    public async Task TheReleaseFoundByAnEarlierRunIsOfferedRightAfterTheStart()
    {
        _store.State = new UpdateState(Start.AddHours(-2), Release("0.3.2"), ReleaseVersion.Parse("0.3.2"));
        UpdateCheckService service = Create("0.3.1");
        int changes = 0;
        service.AvailableChanged += (_, _) => changes++;

        service.ApplyPreferences(On);
        await WaitUntilAsync(() => service.Available is not null);

        Assert.Equal(ReleaseVersion.Parse("0.3.2"), service.Available?.Version);
        Assert.Equal(1, changes);
        // Notified in that run already, and no check before its day.
        Assert.Empty(_notifications.Shown);
        Assert.Empty(_checker.Checks);
    }

    [Fact]
    public async Task ARememberedReleaseThatIsNotNewerAnyMoreIsNotOffered()
    {
        // The user installed 0.3.2 meanwhile (by hand).
        _store.State = new UpdateState(Start.AddHours(-2), Release("0.3.2"), ReleaseVersion.Parse("0.3.2"));
        UpdateCheckService service = Create("0.3.2");
        service.ApplyPreferences(On);
        await WaitForLoadAsync();

        Assert.Null(service.Available);
    }

    [Fact]
    public async Task OneCheckAtATime()
    {
        TaskCompletionSource<UpdateRelease?> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _checker.Gate = gate.Task;
        UpdateCheckService service = Create("0.3.1");
        service.ApplyPreferences(On);

        Task<UpdateCheckResult> first = service.CheckNowAsync(TestContext.Current.CancellationToken);
        Task<UpdateCheckResult> second = service.CheckNowAsync(TestContext.Current.CancellationToken);
        await WaitForChecksAsync(1);
        gate.SetResult(Release("0.3.2"));

        Assert.Equal((await first).Release, (await second).Release);
        Assert.Single(_checker.Checks);
    }

    [Fact]
    public async Task AfterDisposingNothingIsCheckedOrReported()
    {
        UpdateCheckService service = Create("0.3.1");
        service.ApplyPreferences(On);
        await WaitForLoadAsync();
        service.Dispose();

        _time.Advance(TimeSpan.FromDays(2));
        UpdateCheckResult result = await service.CheckNowAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckOutcome.Failed, result.Outcome);
        Assert.Empty(_checker.Checks);
    }

    private UpdateCheckService Create(string running) =>
        _service = new UpdateCheckService(_checker, ReleaseVersion.Parse(running), _store, _notifications, _time, _logger, Timings);

    private static UpdateRelease Release(string version, bool prerelease = false)
    {
        ReleaseVersion parsed = ReleaseVersion.Parse(version);
        return new UpdateRelease(parsed, UpdateRelease.PageFor(parsed), prerelease || parsed.IsPrerelease);
    }

    /// <summary>The service reads its state in the background before its timer acts.</summary>
    private Task WaitForLoadAsync() => WaitUntilAsync(() => _store.Loads > 0);

    private Task WaitForChecksAsync(int count) => WaitUntilAsync(() => _checker.Checks.Count >= count);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }

        // Let the continuation that made it true finish (state saved, event raised).
        await Task.Delay(20, TestContext.Current.CancellationToken);
    }

    private sealed class FakeChecker : IUpdateChecker
    {
        private readonly ConcurrentQueue<bool> _checks = new();

        public UpdateRelease? Next { get; set; }

        public Exception? Failure { get; set; }

        public Task<UpdateRelease?>? Gate { get; set; }

        public IReadOnlyList<bool> Checks => [.. _checks];

        public Task<UpdateRelease?> CheckAsync(bool includePrereleases, CancellationToken cancellationToken)
        {
            _checks.Enqueue(includePrereleases);
            if (Gate is { } gate)
            {
                return gate;
            }

            return Failure is not null ? Task.FromException<UpdateRelease?>(Failure) : Task.FromResult(Next);
        }
    }

    private sealed class MemoryStore : IUpdateStateStore
    {
        private int _loads;

        public UpdateState State { get; set; } = UpdateState.Empty;

        public int Loads => Volatile.Read(ref _loads);

        public Task<UpdateState> LoadAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _loads);
            return Task.FromResult(State);
        }

        public Task SaveAsync(UpdateState state, CancellationToken cancellationToken)
        {
            State = state;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingNotifications : INotificationService
    {
        private readonly ConcurrentQueue<UpdateNotification> _shown = new();

        public Exception? Failure { get; set; }

        public IReadOnlyList<UpdateNotification> Shown => [.. _shown];

        public void Show(PrintNotification notification) => throw new InvalidOperationException("No print notification is expected.");

        public void ShowUpdateAvailable(UpdateNotification notification)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            _shown.Enqueue(notification);
        }
    }
}
