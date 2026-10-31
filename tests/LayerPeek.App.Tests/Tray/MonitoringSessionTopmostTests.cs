using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Tests.Cameras;
using LayerPeek.App.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Tray;

/// <summary>
/// "Always on top" saved by the pin of a camera window (T-117) becomes part of the running settings without a restart, like
/// a saved layout (<see cref="MonitoringSessionLayoutTests"/>): the open window keeps it, a window opened later uses it.
/// </summary>
public sealed class MonitoringSessionTopmostTests : IAsyncDisposable
{
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private readonly FakePrinterMonitor _monitor = new();
    private readonly FakeTopmostWriter _writer = new();
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
        _session = new MonitoringSession(() => store, _monitor, [_printers], NullLogger<MonitoringSession>.Instance, topmostWriter: _writer);
        _session.Start();
        await _monitor.WaitForRunsAsync(1).WaitAsync(TestTimeouts.Wait, Ct);
        return _session;
    }

    [Fact]
    public async Task SavedOptionBecomesPartOfTheRunningSettingsWithoutARestart()
    {
        MonitoringSession session = await StartAsync();
        int changes = 0;
        _printers.Changed += (_, _) => changes++;

        Assert.True(await _writer.SaveAsync("A", CameraWindowMode.Normal, true, Ct));

        Assert.Equal(CameraWindowDefaults.Default with { TopmostNormal = true }, session.CurrentSettings!.Printers[0].Camera.Window);
        Assert.Same(CameraWindowDefaults.Default, session.CurrentSettings.Printers[1].Camera.Window);
        Assert.True(_printers.Current.Printers![0].Camera.Window.TopmostNormal);
        Assert.Equal(1, changes);
        Assert.Single(_monitor.Runs);
    }

    [Fact]
    public async Task SameOptionOrAnUnknownPrinterChangesNothing()
    {
        MonitoringSession session = await StartAsync();
        AppSettings before = session.CurrentSettings!;
        int changes = 0;
        _printers.Changed += (_, _) => changes++;

        await _writer.SaveAsync("a", CameraWindowMode.Mini, true, Ct);
        await _writer.SaveAsync("removed", CameraWindowMode.Normal, true, Ct);

        Assert.Same(before, session.CurrentSettings);
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task AfterDisposalSavedOptionsAreIgnored()
    {
        MonitoringSession session = await StartAsync();
        AppSettings before = session.CurrentSettings!;
        await session.DisposeAsync();
        _session = null;

        await _writer.SaveAsync("a", CameraWindowMode.Normal, true, Ct);

        Assert.Same(before, session.CurrentSettings);
    }
}
