using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.App.Tests.Cameras;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.App.Windows;
using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.App.UiTests;

/// <summary>
/// The real camera windows (<see cref="CameraWindows"/>, the path behind <c>ITrayAppCommands.ShowCamera</c>) on fakes:
/// two printers with cameras, a fake decoder and a fake clock. The window state goes to a store the test chooses
/// (memory by default, a temporary folder for persistence tests).
/// </summary>
internal sealed class CameraHarness : IAsyncDisposable
{
    public static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private readonly List<CameraViewModel> _created = [];
    private readonly CultureScope _culture = new("en-US");

    public CameraHarness(ICameraWindowStateStore? states = null)
    {
        States = states ?? new MemoryCameraWindowStateStore();
        ((IMonitoringObserver)Printers).ApplySettings(new AppSettings
        {
            Printers = [CameraPrinter("a", "Alpha"), CameraPrinter("b", "Beta"), TestPrinters.Definition("c", "NoCamera")],
        });
        Windows = new CameraWindows(Create);
    }

    public FakeCameraFeedFactory Factory { get; } = new();

    public FakeTimeProvider Time { get; } = new();

    public FakePrinterMonitor Monitor { get; } = new();

    public MonitoredPrinters Printers { get; } = new();

    public ICameraWindowStateStore States { get; }

    public CameraWindows Windows { get; }

    public IReadOnlyList<CameraViewModel> Created => _created;

    public static PrinterDefinition CameraPrinter(string id, string name) =>
        TestPrinters.Definition(id, name) with { Camera = new CameraSettings(enabled: true, StreamUri) };

    public CameraWindow Open(string printerId = "a")
    {
        Assert.True(Windows.Show(printerId));
        return Windows.OpenWindows[printerId];
    }

    /// <summary>Opens the window and waits until the generated frame is drawn and the status overlay is gone.</summary>
    public async Task<(CameraWindow Window, FakeCameraFeed Feed)> OpenStreamingAsync(int width, int height, uint bgra, string printerId = "a")
    {
        CameraWindow window = Open(printerId);
        FakeCameraFeed feed = await Factory.WaitForFeedAsync(Factory.Feeds.Count + 1);
        feed.SendColor(width, height, bgra);
        CameraViewModel viewModel = (CameraViewModel)window.DataContext!;
        await Ui.UntilAsync(() => viewModel.HasFrame && !viewModel.IsStatusVisible, "frame shown");
        return (window, feed);
    }

    public async ValueTask DisposeAsync()
    {
        Windows.Shutdown();
        foreach (CameraViewModel viewModel in _created)
        {
            viewModel.Dispose();
            await viewModel.StreamCompletion.WaitAsync(TestTimeouts.Wait);
        }

        _culture.Dispose();
    }

    private CameraViewModel? Create(string printerId)
    {
        PrinterDefinition? printer = Printers.Current.Printers?.FirstOrDefault(p => string.Equals(p.Id, printerId, StringComparison.OrdinalIgnoreCase));
        if (printer is null || CameraTarget.From(printer) is not { } target)
        {
            return null;
        }

        ReconnectingCameraFeed feed = new(Factory, Time, NullLogger<ReconnectingCameraFeed>.Instance, CameraFeedOptions.Default);
        CameraViewModel viewModel = new(target, feed, Monitor, Printers, new AvaloniaUiDispatcher(), States, NullLogger<CameraViewModel>.Instance);
        _created.Add(viewModel);
        return viewModel;
    }
}
