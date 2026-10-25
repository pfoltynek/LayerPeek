using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Tests.Cameras.Layout;
using LayerPeek.App.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Tray;

/// <summary>
/// A layout saved by the layout mode of a camera window (T-091) becomes part of the running settings without a restart, so
/// that the open window keeps it, a window opened later uses it and monitoring goes on.
/// </summary>
public sealed class MonitoringSessionLayoutTests : IAsyncDisposable
{
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private static readonly CameraOverlayLayout Moved = CameraOverlayLayout.Default.MoveTo(OverlayWidgetIds.Temperatures, OverlayAnchor.TopRight, 0);

    private readonly FakePrinterMonitor _monitor = new();
    private readonly FakeLayoutWriter _writer = new();
    private readonly MonitoredPrinters _printers = new();
    private MonitoringSession? _session;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            await _session.DisposeAsync();
        }
    }

    private static PrinterDefinition CameraPrinter(string id) =>
        Definition(id, id.ToUpperInvariant()) with { Camera = new CameraSettings(enabled: true, StreamUri) };

    private async Task<MonitoringSession> StartAsync()
    {
        FakeSettingsStore store = FakeSettingsStore.Returning(AppSettings.Default with { Printers = [CameraPrinter("a"), CameraPrinter("b")] });
        _session = new MonitoringSession(() => store, _monitor, [_printers], NullLogger<MonitoringSession>.Instance, layoutWriter: _writer);
        _session.Start();
        await _monitor.WaitForRunsAsync(1).WaitAsync(TestTimeouts.Wait, Ct);
        return _session;
    }

    [Fact]
    public async Task SavedLayoutBecomesPartOfTheRunningSettingsWithoutARestart()
    {
        MonitoringSession session = await StartAsync();
        int changes = 0;
        _printers.Changed += (_, _) => changes++;

        Assert.True(await _writer.SaveAsync("A", Moved, Ct));

        Assert.Equal(Moved, session.CurrentSettings!.Printers[0].Camera.Overlay);
        Assert.Equal(CameraOverlayLayout.Default, session.CurrentSettings.Printers[1].Camera.Overlay);
        Assert.Equal(Moved, _printers.Current.Printers![0].Camera.Overlay);
        Assert.Equal(1, changes);
        Assert.Single(_monitor.Runs);
    }

    [Fact]
    public async Task SameLayoutOrAnUnknownPrinterChangesNothing()
    {
        MonitoringSession session = await StartAsync();
        AppSettings before = session.CurrentSettings!;
        int changes = 0;
        _printers.Changed += (_, _) => changes++;

        await _writer.SaveAsync("a", CameraOverlayLayout.Default, Ct);
        await _writer.SaveAsync("removed", Moved, Ct);

        Assert.Same(before, session.CurrentSettings);
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task AfterDisposalSavedLayoutsAreIgnored()
    {
        MonitoringSession session = await StartAsync();
        AppSettings before = session.CurrentSettings!;
        await session.DisposeAsync();
        _session = null;

        await _writer.SaveAsync("a", Moved, Ct);

        Assert.Same(before, session.CurrentSettings);
    }
}
