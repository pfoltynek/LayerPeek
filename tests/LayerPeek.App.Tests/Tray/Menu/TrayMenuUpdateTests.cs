using LayerPeek.App.Tests.Settings;
using LayerPeek.App.Tray;
using LayerPeek.App.Tray.Menu;
using LayerPeek.App.Updates;
using LayerPeek.Core.Notifications;
using LayerPeek.Core.Settings;
using LayerPeek.Core.Updates;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Tray.Menu;

/// <summary>
/// "New version X.Y.Z available" in the tray menu (T-058): only while a newer version is offered, right above "About", its
/// tooltip saying what a click does; a click reaches <see cref="ITrayAppCommands.InstallUpdate"/>.
/// </summary>
public sealed class TrayMenuUpdateTests : IDisposable
{
    private readonly CultureScope _culture = new("en-US");
    private readonly FakePrinterMonitor _monitor = new();
    private readonly ManualDispatcher _dispatcher = new();
    private readonly FakeChecker _checker = new();
    private readonly UpdateCheckService _service;
    private readonly UpdateOffer _offer;
    private readonly TrayMenuViewModel _viewModel;

    public TrayMenuUpdateTests()
    {
        _service = new UpdateCheckService(_checker, ReleaseVersion.Parse("0.3.1"), new MemoryUpdateStateStore(), NullNotificationService.Instance, TimeProvider.System, new ListLogger<UpdateCheckService>());
        _offer = new UpdateOffer(_service, installer: null, needsMoveToApplications: false, new FakeUrlLauncher(), new ListLogger<UpdateOffer>());
        ((IMonitoringObserver)_offer).ApplySettings(new AppSettings { CheckForUpdates = false });
        _viewModel = new TrayMenuViewModel(_monitor, _dispatcher, TestWebOpener.Create(new FakeUrlLauncher()), TrayMenuOptions.Default, updates: _offer);
        _viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();
    }

    public void Dispose()
    {
        _viewModel.Dispose();
        _offer.Dispose();
        _service.Dispose();
        _culture.Dispose();
    }

    [Fact]
    public void WithoutAnUpdateThereIsNoItem()
    {
        Assert.DoesNotContain(_viewModel.FixedItems, static item => item.Command == TrayMenuCommand.Update);
    }

    [Fact]
    public async Task ANewVersionAddsTheItemAboveAbout()
    {
        int rebuilds = 0;
        _viewModel.FixedItemsChanged += (_, _) => rebuilds++;
        _checker.Next = Release("0.3.2");

        await _offer.CheckNowAsync(TestContext.Current.CancellationToken);
        _dispatcher.RunAll();

        Assert.Collection(
            _viewModel.FixedItems,
            static item => Assert.Equal(TrayMenuCommand.Diagnostics, item.Command),
            static item => Assert.Equal(TrayMenuCommand.Settings, item.Command),
            static item => Assert.True(item.IsSeparator),
            static item =>
            {
                Assert.Equal(TrayMenuCommand.Update, item.Command);
                Assert.Equal("New version 0.3.2 available", item.Header);
                Assert.True(item.IsEnabled);
                Assert.False(item.HasAccessKey);
                Assert.Equal("Opens the release page in the browser.", item.ToolTip);
            },
            static item => Assert.Equal(TrayMenuCommand.About, item.Command),
            static item => Assert.Equal(TrayMenuCommand.Quit, item.Command));
        Assert.Equal(1, rebuilds);
    }

    [Fact]
    public async Task TheItemGoesWhenNoNewerVersionIsOfferedAnyMore()
    {
        _checker.Next = Release("0.3.2");
        await _offer.CheckNowAsync(TestContext.Current.CancellationToken);
        _dispatcher.RunAll();
        _checker.Next = null;

        await _offer.CheckNowAsync(TestContext.Current.CancellationToken);
        _dispatcher.RunAll();

        Assert.DoesNotContain(_viewModel.FixedItems, static item => item.Command == TrayMenuCommand.Update);
    }

    [Fact]
    public async Task TheSameVersionAgainChangesNothing()
    {
        _checker.Next = Release("0.3.2");
        await _offer.CheckNowAsync(TestContext.Current.CancellationToken);
        _dispatcher.RunAll();
        int rebuilds = 0;
        _viewModel.FixedItemsChanged += (_, _) => rebuilds++;

        await _offer.CheckNowAsync(TestContext.Current.CancellationToken);
        _dispatcher.RunAll();

        Assert.Equal(0, rebuilds);
    }

    [Fact]
    public async Task TheItemFollowsTheLanguage()
    {
        _checker.Next = Release("0.3.2");
        await _offer.CheckNowAsync(TestContext.Current.CancellationToken);
        _dispatcher.RunAll();

        using (new CultureScope("cs-CZ"))
        {
            _viewModel.RefreshTexts();
            Assert.Equal("Je dostupná nová verze 0.3.2", Assert.Single(_viewModel.FixedItems, static item => item.Command == TrayMenuCommand.Update).Header);
        }
    }

    [Fact]
    public void AClickReachesTheAppCommand()
    {
        FakeAppCommands commands = new();
        FakeMenuTarget target = new();
        using TrayMenuPresenter presenter = new(target, _viewModel, commands, new ListLogger<TrayMenuPresenter>());

        target.Click(new TrayMenuEntry(TrayMenuCommand.Update, "New version 0.3.2 available", IsEnabled: true));
        target.Click(new TrayMenuEntry(TrayMenuCommand.Update, "Downloading version 0.3.2…", IsEnabled: false));

        Assert.Equal(1, commands.InstallUpdateCount);
    }

    private static UpdateRelease Release(string version) =>
        new(ReleaseVersion.Parse(version), UpdateRelease.PageFor(ReleaseVersion.Parse(version)), IsPrerelease: false);

    private sealed class FakeChecker : IUpdateChecker
    {
        public UpdateRelease? Next { get; set; }

        public Task<UpdateRelease?> CheckAsync(bool includePrereleases, CancellationToken cancellationToken) => Task.FromResult(Next);
    }
}
