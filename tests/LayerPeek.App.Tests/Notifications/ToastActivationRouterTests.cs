using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Notifications;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tests.Tray.Menu;
using LayerPeek.App.Tray;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;
using LayerPeek.Core.Web;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Notifications;

public sealed class ToastActivationRouterTests : IDisposable
{
    private readonly FakeUrlLauncher _launcher = new();
    private readonly FakeWebGateway _gateway = FakeWebGateway.WithoutPassword();
    private readonly ListLogger<ToastActivationRouter> _logger = new();
    private readonly ToastActivationRouter _router;

    public ToastActivationRouterTests()
    {
        _router = new ToastActivationRouter(TestWebOpener.Create(_launcher, _gateway), _logger);
    }

    public void Dispose() => _router.Dispose();

    [Fact]
    public async Task ClickOpensPrinterLoggedInLikeTheTrayMenu()
    {
        _gateway.Open = static (_, _) => Task.FromResult(PrinterWebAccess.Proxied(FakeWebGateway.ProxyAddress));
        Apply(Definition(), Definition(id: "xl", name: "XL") with { Address = "192.168.1.20" });

        bool opened = await _router.ActivateAsync(ToastLaunchArgument.ForPrinter("xl"));

        Assert.True(opened);
        Assert.Equal("xl", Assert.Single(_gateway.Opened).Id);
        Assert.Equal([FakeWebGateway.ProxyAddress], _launcher.OpenedSecret);
        Assert.Empty(_launcher.Opened);
    }

    [Fact]
    public async Task ClickOpensWebInterfaceOfThePrinter()
    {
        Apply(Definition(), Definition(id: "xl", name: "XL") with { Address = "192.168.1.20" });

        bool opened = await _router.ActivateAsync(ToastLaunchArgument.ForPrinter("xl"));

        Assert.True(opened);
        Assert.Equal(new Uri("http://192.168.1.20/"), Assert.Single(_launcher.Opened));
    }

    [Fact]
    public async Task PrinterIdIsMatchedIgnoringCase()
    {
        Apply(Definition(id: "MK4"));

        Assert.True(await _router.ActivateAsync(ToastLaunchArgument.ForPrinter("mk4")));
        Assert.Single(_launcher.Opened);
    }

    [Fact]
    public async Task PrusaConnectPrinterOpensPrusaConnect()
    {
        Apply(Definition() with { ConnectionType = ConnectionType.PrusaConnect, Address = "c0ffee" });

        Assert.True(await _router.ActivateAsync(ToastLaunchArgument.ForPrinter("mk4")));
        Assert.Equal(new Uri("https://connect.prusa3d.com/"), Assert.Single(_launcher.Opened));
    }

    [Fact]
    public async Task CurrentAddressIsUsedAfterSettingsChange()
    {
        Apply(Definition());
        Apply(Definition() with { Address = "mk4.local" });

        Assert.True(await _router.ActivateAsync(ToastLaunchArgument.ForPrinter("mk4")));
        Assert.Equal(new Uri("http://mk4.local/"), Assert.Single(_launcher.Opened));
    }

    [Fact]
    public async Task UnknownPrinterOpensNothing()
    {
        Apply(Definition());

        Assert.False(await _router.ActivateAsync(ToastLaunchArgument.ForPrinter("xl")));
        Assert.Empty(_launcher.Opened);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Information);
    }

    [Fact]
    public async Task RemovedPrinterOpensNothing()
    {
        Apply(Definition());
        Apply();

        Assert.False(await _router.ActivateAsync(ToastLaunchArgument.ForPrinter("mk4")));
        Assert.Empty(_launcher.Opened);
    }

    [Fact]
    public async Task ClickBeforeAnySettingsOpensNothingOnceTheWaitIsOver()
    {
        FakeTimeProvider time = new();
        using ToastActivationRouter router = new(TestWebOpener.Create(_launcher, _gateway), _logger, time);

        Task<bool> click = router.ActivateAsync(ToastLaunchArgument.ForPrinter("mk4"));
        Assert.False(click.IsCompleted);
        time.Advance(ToastActivationRouter.SettingsWait);

        Assert.False(await click.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Empty(_launcher.Opened);
    }

    [Fact]
    public async Task QuittingEndsTheWaitForTheFirstSettingsWithoutOpening()
    {
        // The app quits (the container disposes the router) while a click still waits: the wait ends at once, and settings
        // applied late open nothing either.
        FakeTimeProvider time = new();
        ToastActivationRouter router = new(TestWebOpener.Create(_launcher, _gateway), _logger, time);
        Task<bool> click = router.ActivateAsync(ToastLaunchArgument.ForPrinter("mk4"));
        Assert.False(click.IsCompleted);

        router.Dispose();
        router.Dispose();

        Assert.False(await click.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        ((IMonitoringObserver)router).ApplySettings(new AppSettings { Printers = [Definition()] });
        Assert.False(await router.ActivateAsync(ToastLaunchArgument.ForPrinter("mk4")));
        Assert.Empty(_launcher.Opened);
    }

    [Fact]
    public async Task ClickThatStartedTheAppWaitsForTheFirstSettings()
    {
        // Windows started the app for a click in the notification center (T-068): monitoring applies the printers later.
        Task<bool> click = _router.ActivateAsync(ToastLaunchArgument.ForPrinter("mk4"));
        Assert.False(click.IsCompleted);

        Apply(Definition());

        Assert.True(await click.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Equal(new Uri("http://192.168.1.10/"), Assert.Single(_launcher.Opened));
    }

    [Fact]
    public async Task OneClickReportedTwiceOpensTheWebOnce()
    {
        // A banner click may reach the app twice: the toast's Activated event and the COM activator (T-068).
        FakeTimeProvider time = new();
        using ToastActivationRouter router = new(TestWebOpener.Create(_launcher, _gateway), _logger, time);
        ((IMonitoringObserver)router).ApplySettings(new AppSettings { Printers = [Definition(), Definition(id: "xl", name: "XL")] });

        Assert.True(await router.ActivateAsync(ToastLaunchArgument.ForPrinter("mk4")));
        Assert.False(await router.ActivateAsync(ToastLaunchArgument.ForPrinter("mk4")));
        // Another printer is another click.
        Assert.True(await router.ActivateAsync(ToastLaunchArgument.ForPrinter("xl")));
        time.Advance(ToastActivationRouter.DuplicateWindow);
        // Later, the same toast again is a new click.
        Assert.True(await router.ActivateAsync(ToastLaunchArgument.ForPrinter("mk4")));

        Assert.Equal(3, _launcher.Opened.Count);
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("ftp://192.168.1.10")]
    [InlineData("http://user:secret@192.168.1.10")]
    public async Task UnsafeAddressOpensNothing(string address)
    {
        Apply(Definition() with { Address = address });

        Assert.False(await _router.ActivateAsync(ToastLaunchArgument.ForPrinter("mk4")));
        Assert.Empty(_launcher.Opened);
        (LogLevel level, string message, _) = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Warning, level);
        // The address may contain credentials: it is never logged.
        Assert.DoesNotContain("secret", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    public async Task MalformedArgumentOpensNothing(string? argument)
    {
        Apply(Definition());

        Assert.False(await _router.ActivateAsync(argument));
        Assert.Empty(_launcher.Opened);
    }

    [Fact]
    public async Task LauncherFailureIsReported()
    {
        Apply(Definition());
        _launcher.Result = false;

        Assert.False(await _router.ActivateAsync(ToastLaunchArgument.ForPrinter("mk4")));
    }

    [Fact]
    public async Task MonitoringFailureKeepsPrinters()
    {
        Apply(Definition());

        ((IMonitoringObserver)_router).ReportMonitoringFailed();

        Assert.True(await _router.ActivateAsync(ToastLaunchArgument.ForPrinter("mk4")));
    }

    [Fact]
    public async Task ConcurrentSettingsAndClicksAreSafe()
    {
        Task apply = Task.Run(() =>
        {
            for (int i = 0; i < 500; i++)
            {
                Apply(Definition(), Definition(id: $"p{i}"));
            }
        }, TestContext.Current.CancellationToken);
        Task click = Task.Run(async () =>
        {
            for (int i = 0; i < 500; i++)
            {
                await _router.ActivateAsync(ToastLaunchArgument.ForPrinter("mk4"));
            }
        }, TestContext.Current.CancellationToken);

        await Task.WhenAll(apply, click);

        Assert.All(_launcher.Opened, uri => Assert.Equal(new Uri("http://192.168.1.10/"), uri));
    }

    /// <summary>T-101: a click on the test notification opens no web, only the settings window.</summary>
    [Fact]
    public async Task TheTestNotificationOpensTheSettingsWindowAndNoWeb()
    {
        Apply(Definition());
        int opened = 0;
        _router.AttachSettingsOpener(() => opened++);

        Assert.True(await _router.ActivateAsync(ToastLaunchArgument.Settings));

        Assert.Equal(1, opened);
        Assert.Empty(_launcher.Opened);
        Assert.Empty(_launcher.OpenedSecret);
        Assert.Empty(_gateway.Opened);
    }

    /// <summary>
    /// T-101 / T-099: a click before the UI runs (macOS waiting for a display) needs no printers and is held, once however
    /// often it came, until the UI attaches its opener.
    /// </summary>
    [Fact]
    public async Task AClickBeforeTheUiRunsIsHeldOnceUntilTheOpenerIsAttached()
    {
        FakeTimeProvider time = new();
        using ToastActivationRouter router = new(TestWebOpener.Create(_launcher, _gateway), _logger, time);

        Assert.True(await router.ActivateAsync(ToastLaunchArgument.Settings));
        time.Advance(ToastActivationRouter.DuplicateWindow);
        Assert.True(await router.ActivateAsync(ToastLaunchArgument.Settings));
        int opened = 0;
        router.AttachSettingsOpener(() => opened++);

        Assert.Equal(1, opened);
        Assert.Contains(_logger.Entries, static e => e.Message.Contains("waits for the UI", StringComparison.Ordinal));
        // Later clicks go straight to the window.
        time.Advance(ToastActivationRouter.DuplicateWindow);
        Assert.True(await router.ActivateAsync(ToastLaunchArgument.Settings));
        Assert.Equal(2, opened);
        Assert.Throws<InvalidOperationException>(() => router.AttachSettingsOpener(() => { }));
    }

    [Fact]
    public async Task TheSameClickOnTheTestNotificationReportedTwiceOpensOnce()
    {
        int opened = 0;
        _router.AttachSettingsOpener(() => opened++);

        Assert.True(await _router.ActivateAsync(ToastLaunchArgument.Settings));
        Assert.False(await _router.ActivateAsync(ToastLaunchArgument.Settings));

        Assert.Equal(1, opened);
    }

    [Fact]
    public async Task AfterQuittingTheTestNotificationOpensNothing()
    {
        FakeTimeProvider time = new();
        ToastActivationRouter router = new(TestWebOpener.Create(_launcher, _gateway), _logger, time);
        // Held for the UI, which never came: the app quits.
        Assert.True(await router.ActivateAsync(ToastLaunchArgument.Settings));
        router.Dispose();
        int opened = 0;
        router.AttachSettingsOpener(() => opened++);

        time.Advance(ToastActivationRouter.DuplicateWindow);
        Assert.False(await router.ActivateAsync(ToastLaunchArgument.Settings));
        Assert.Equal(0, opened);
    }

    [Fact]
    public async Task AFailingOpenerIsOnlyLogged()
    {
        _router.AttachSettingsOpener(static () => throw new InvalidOperationException("no window"));

        Assert.True(await _router.ActivateAsync(ToastLaunchArgument.Settings));
        Assert.Contains(_logger.Entries, static e => e.Exception is InvalidOperationException);
    }

    [Fact]
    public void NullArgumentsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new ToastActivationRouter(null!, _logger));
        Assert.Throws<ArgumentNullException>(() => new ToastActivationRouter(TestWebOpener.Create(_launcher), null!));
        Assert.Throws<ArgumentNullException>(() => ((IMonitoringObserver)_router).ApplySettings(null!));
    }

    private void Apply(params PrinterDefinition[] printers) =>
        ((IMonitoringObserver)_router).ApplySettings(new AppSettings { Printers = printers });
}
