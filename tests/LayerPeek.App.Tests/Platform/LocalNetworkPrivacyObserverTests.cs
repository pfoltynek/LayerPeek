using LayerPeek.App.Platform;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Platform;

/// <summary>T-097: "no route to host" of a monitored PrusaLink printer reaches the local network permission (for its log line).</summary>
public sealed class LocalNetworkPrivacyObserverTests : IDisposable
{
    private readonly FakePrinterMonitor _monitor = new();
    private readonly FakeLocalNetworkPrivacy _privacy = new();
    private readonly LocalNetworkPrivacyObserver _observer;

    public LocalNetworkPrivacyObserverTests() => _observer = new LocalNetworkPrivacyObserver(_monitor, _privacy);

    public void Dispose() => _observer.Dispose();

    [Fact]
    public void HostUnreachableOfAMonitoredPrinterAsksWithItsHostAndId()
    {
        _observer.ApplySettings(new AppSettings { Printers = [Definition("mk4") with { Address = "http://192.168.1.10:8080/" }] });

        _monitor.Raise(PrinterSnapshot.Unavailable("MK4", PrinterState.Offline, Now, "The printer is not reachable.", PrinterProblem.HostUnreachable));

        (string? host, string subject) = Assert.Single(_privacy.Calls);
        Assert.Equal("192.168.1.10", host);
        Assert.Equal("printer 'MK4'", subject);
    }

    [Fact]
    public void OtherProblemsUnknownPrintersAndCloudPrintersAreNotAsked()
    {
        _observer.ApplySettings(new AppSettings
        {
            Printers =
            [
                Definition("mk4"),
                new PrinterDefinition { Id = "xl", Name = "XL", ConnectionType = ConnectionType.PrusaConnect, Address = "c0ffee00-0000-4000-8000-000000000001" },
            ],
        });

        _monitor.Raise(PrinterSnapshot.Unavailable("mk4", PrinterState.Offline, Now, "x", PrinterProblem.NotReachable));
        _monitor.Raise(PrinterSnapshot.Unavailable("other", PrinterState.Offline, Now, "x", PrinterProblem.HostUnreachable));
        _monitor.Raise(PrinterSnapshot.Unavailable("xl", PrinterState.Offline, Now, "x", PrinterProblem.HostUnreachable));

        Assert.Empty(_privacy.Calls);
    }

    [Fact]
    public void DisposingUnsubscribes()
    {
        Assert.Equal(1, _monitor.SubscriberCount);

        _observer.Dispose();
        _observer.Dispose();

        Assert.Equal(0, _monitor.SubscriberCount);
    }

    [Fact]
    public void TheHostIsTheOneThePrusaLinkClientConnectsTo()
    {
        Assert.Equal("prusa-mk4.local", LocalNetworkPrivacyObserver.HostOf(Definition() with { Address = "prusa-mk4.local" }));
        Assert.Null(LocalNetworkPrivacyObserver.HostOf(Definition() with { Address = "ftp://printer" }));
        Assert.Null(LocalNetworkPrivacyObserver.HostOf(null));
    }
}
