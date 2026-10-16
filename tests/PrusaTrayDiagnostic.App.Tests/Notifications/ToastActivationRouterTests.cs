using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.Notifications;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.App.Tests.Tray.Menu;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

using static PrusaTrayDiagnostic.App.Tests.Tray.TestPrinters;

namespace PrusaTrayDiagnostic.App.Tests.Notifications;

public sealed class ToastActivationRouterTests
{
    private readonly FakeUrlLauncher _launcher = new();
    private readonly ListLogger<ToastActivationRouter> _logger = new();
    private readonly ToastActivationRouter _router;

    public ToastActivationRouterTests()
    {
        _router = new ToastActivationRouter(_launcher, _logger);
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
    public async Task ClickBeforeAnySettingsOpensNothing()
    {
        Assert.False(await _router.ActivateAsync(ToastLaunchArgument.ForPrinter("mk4")));
        Assert.Empty(_launcher.Opened);
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

    [Fact]
    public void NullArgumentsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new ToastActivationRouter(null!, _logger));
        Assert.Throws<ArgumentNullException>(() => new ToastActivationRouter(_launcher, null!));
        Assert.Throws<ArgumentNullException>(() => ((IMonitoringObserver)_router).ApplySettings(null!));
    }

    private void Apply(params PrinterDefinition[] printers) =>
        ((IMonitoringObserver)_router).ApplySettings(new AppSettings { Printers = printers });
}
