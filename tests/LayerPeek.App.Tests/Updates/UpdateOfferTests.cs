using LayerPeek.App.Installer;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tests.Tray.Menu;
using LayerPeek.App.Tray;
using LayerPeek.App.Updates;
using LayerPeek.Core.Notifications;
using LayerPeek.Core.Settings;
using LayerPeek.Core.Updates;

namespace LayerPeek.App.Tests.Updates;

/// <summary>
/// The offered update of T-058 between the check and the UI: the preferences from the settings (Q2 a, Q3 a), and what a click
/// does: the release page, or in a Velopack installation download, quit and restart (Q5 a, Q6 a).
/// </summary>
public sealed class UpdateOfferTests : IDisposable
{
    private readonly FakeChecker _checker = new();
    private readonly FakeUrlLauncher _urls = new();
    private readonly UpdateCheckService _service;

    public UpdateOfferTests()
    {
        _service = new UpdateCheckService(_checker, ReleaseVersion.Parse("0.3.1"), new MemoryUpdateStateStore(), NullNotificationService.Instance, TimeProvider.System, new ListLogger<UpdateCheckService>());
    }

    public void Dispose() => _service.Dispose();

    [Fact]
    public void ThePreferencesFollowTheSettings()
    {
        ReleaseVersion running = ReleaseVersion.Parse("0.3.1");

        UpdatePreferences defaults = UpdateOffer.PreferencesFor(new AppSettings(), running);
        Assert.Equal(new UpdatePreferences(AutomaticChecks: true, IncludePrereleases: true, Notify: true, PlaySound: false), defaults);

        UpdatePreferences changed = UpdateOffer.PreferencesFor(
            new AppSettings
            {
                CheckForUpdates = false,
                OfferPrereleaseUpdates = false,
                Notifications = new NotificationSettings { Enabled = false, Sound = NotificationSound.All },
            },
            running);
        Assert.Equal(new UpdatePreferences(AutomaticChecks: false, IncludePrereleases: false, Notify: false, PlaySound: true), changed);

        // From 1.0 on, pre-releases only by choice (T-017).
        Assert.False(UpdateOffer.PreferencesFor(new AppSettings(), ReleaseVersion.Parse("1.0.0")).IncludePrereleases);
    }

    [Fact]
    public async Task WithoutAnInstallationAClickOpensTheReleasePage()
    {
        _checker.Next = Release("0.3.2");
        using UpdateOffer offer = new(_service, installer: null, needsMoveToApplications: false, _urls, new ListLogger<UpdateOffer>());
        int changes = 0;
        offer.Changed += (_, _) => Interlocked.Increment(ref changes);
        ((IMonitoringObserver)offer).ApplySettings(new AppSettings { CheckForUpdates = false });

        await offer.CheckNowAsync(TestContext.Current.CancellationToken);
        bool opened = await offer.RunAsync();

        Assert.Equal(UpdateAction.OpenReleasePage, offer.Action);
        Assert.True(opened);
        Assert.Equal([new Uri("https://github.com/pfoltynek/LayerPeek/releases/tag/v0.3.2")], _urls.Opened);
        Assert.True(changes >= 1);
    }

    [Fact]
    public async Task WithoutAnOfferAClickDoesNothing()
    {
        using UpdateOffer offer = new(_service, installer: null, needsMoveToApplications: false, _urls, new ListLogger<UpdateOffer>());

        Assert.False(await offer.RunAsync());
        Assert.Empty(_urls.Opened);
    }

    [Fact]
    public async Task InAnInstallationAClickDownloadsAndQuits()
    {
        FakeEngine engine = new() { Next = new AvailableUpdate("0.3.2", null) };
        AppUpdates installer = new(engine, new ListLogger<AppUpdates>());
        using UpdateCheckService service = new(installer, ReleaseVersion.Parse("0.3.1"), new MemoryUpdateStateStore(), NullNotificationService.Instance, TimeProvider.System, new ListLogger<UpdateCheckService>());
        using UpdateOffer offer = new(service, installer, needsMoveToApplications: true, _urls, new ListLogger<UpdateOffer>());
        int quits = 0;
        offer.AttachQuit(() =>
        {
            quits++;
            return Task.CompletedTask;
        });
        ((IMonitoringObserver)offer).ApplySettings(new AppSettings { CheckForUpdates = false });
        await offer.CheckNowAsync(TestContext.Current.CancellationToken);
        List<bool> installing = [];
        offer.Changed += (_, _) => installing.Add(offer.IsInstalling);

        bool scheduled = await offer.RunAsync();

        Assert.Equal(UpdateAction.InstallAndRestart, offer.Action);
        // An installation that can update itself never needs to be moved.
        Assert.False(offer.NeedsMoveToApplications);
        Assert.True(scheduled);
        Assert.Equal(1, quits);
        Assert.Equal(["download 0.3.2", "apply 0.3.2"], engine.Steps);
        Assert.Equal([true, false], installing);
        Assert.Empty(_urls.Opened);
    }

    [Fact]
    public async Task AFailedDownloadOpensTheReleasePageInstead()
    {
        FakeEngine engine = new() { Next = new AvailableUpdate("0.3.2", null), DownloadFailure = new HttpRequestException("gone") };
        AppUpdates installer = new(engine, new ListLogger<AppUpdates>());
        using UpdateCheckService service = new(installer, ReleaseVersion.Parse("0.3.1"), new MemoryUpdateStateStore(), NullNotificationService.Instance, TimeProvider.System, new ListLogger<UpdateCheckService>());
        using UpdateOffer offer = new(service, installer, needsMoveToApplications: false, _urls, new ListLogger<UpdateOffer>());
        offer.AttachQuit(() => throw new InvalidOperationException("must not quit"));
        ((IMonitoringObserver)offer).ApplySettings(new AppSettings { CheckForUpdates = false });
        await offer.CheckNowAsync(TestContext.Current.CancellationToken);

        Assert.False(await offer.RunAsync());
        Assert.Equal([new Uri("https://github.com/pfoltynek/LayerPeek/releases/tag/v0.3.2")], _urls.Opened);
        Assert.False(offer.IsInstalling);
    }

    [Fact]
    public void AMacAppThatCannotUpdateWhereItRunsIsToldToMove()
    {
        using UpdateOffer offer = new(_service, installer: null, needsMoveToApplications: true, _urls, new ListLogger<UpdateOffer>());

        Assert.True(offer.NeedsMoveToApplications);
        Assert.Equal(UpdateAction.OpenReleasePage, offer.Action);
    }

    private static UpdateRelease Release(string version) =>
        new(ReleaseVersion.Parse(version), UpdateRelease.PageFor(ReleaseVersion.Parse(version)), IsPrerelease: false);

    private sealed class FakeChecker : IUpdateChecker
    {
        public UpdateRelease? Next { get; set; }

        public Task<UpdateRelease?> CheckAsync(bool includePrereleases, CancellationToken cancellationToken) => Task.FromResult(Next);
    }

    private sealed class FakeEngine : IUpdateEngine
    {
        public AvailableUpdate? Next { get; set; }

        public Exception? DownloadFailure { get; set; }

        public List<string> Steps { get; } = [];

        public Task<AvailableUpdate?> CheckAsync(bool prereleases, CancellationToken cancellationToken) => Task.FromResult(Next);

        public Task DownloadAsync(AvailableUpdate update, CancellationToken cancellationToken)
        {
            if (DownloadFailure is not null)
            {
                return Task.FromException(DownloadFailure);
            }

            Steps.Add($"download {update.Version}");
            return Task.CompletedTask;
        }

        public void ApplyOnExit(AvailableUpdate update) => Steps.Add($"apply {update.Version}");
    }
}
