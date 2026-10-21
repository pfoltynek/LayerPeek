using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.App.Tests.Cameras;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.App.UiTests.Infrastructure;
using PrusaTrayDiagnostic.App.Windows;
using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.App.UiTests.Cameras;

/// <summary>
/// The real camera windows (<see cref="CameraWindows"/>, the path behind <c>ITrayAppCommands.ToggleCamera</c>) on fakes:
/// two printers with cameras, a fake decoder and a fake clock. The window state goes to a store the test chooses
/// (memory by default, a temporary folder for persistence tests). The headless session sets the languages (English).
/// </summary>
internal sealed class CameraHarness : IAsyncDisposable
{
    public static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private readonly List<CameraViewModel> _created = [];

    /// <param name="screens">Monitors of the camera windows (T-035); <see langword="null"/>: the headless screen.</param>
    /// <param name="window">Window defaults of the printers with a camera; <see langword="null"/>: the defaults.</param>
    /// <param name="overlay">Print overlay of the printers with a camera (T-036); <see langword="null"/>: the defaults.</param>
    /// <param name="measureChrome">Fake frame measurement of the windows (T-040); <see langword="null"/>: the headless one.</param>
    /// <param name="track">Tracks each new window (T-043); <see langword="null"/>: none.</param>
    public CameraHarness(ICameraWindowStateStore? states = null, IScreenCatalog? screens = null, CameraWindowDefaults? window = null, CameraOverlayOptions? overlay = null, Func<CameraWindow, WindowChrome?>? measureChrome = null, Func<Avalonia.Controls.Window, IDisposable>? track = null)
    {
        States = states ?? new MemoryCameraWindowStateStore();
        ((IMonitoringObserver)Printers).ApplySettings(new AppSettings
        {
            Printers = [CameraPrinter("a", "Alpha", window, overlay), CameraPrinter("b", "Beta", window, overlay), TestPrinters.Definition("c", "NoCamera")],
        });
        Windows = new AuxiliaryWindowFactory(track).CameraWindows(Create, screens, measureChrome);
    }

    public FakeCameraFeedFactory Factory { get; } = new();

    public FakeTimeProvider Time { get; } = new();

    public FakePrinterMonitor Monitor { get; } = new();

    public MonitoredPrinters Printers { get; } = new();

    public ICameraWindowStateStore States { get; }

    public CameraWindows Windows { get; }

    public IReadOnlyList<CameraViewModel> Created => _created;

    public static PrinterDefinition CameraPrinter(string id, string name, CameraWindowDefaults? window = null, CameraOverlayOptions? overlay = null) =>
        TestPrinters.Definition(id, name) with
        {
            Camera = new CameraSettings(enabled: true, StreamUri) { Window = window ?? CameraWindowDefaults.Default, Overlay = overlay ?? CameraOverlayOptions.Default },
        };

    public CameraWindow Open(string printerId = "a")
    {
        Assert.True(Windows.Show(printerId));
        return Windows.OpenWindows[printerId];
    }

    /// <summary>Opens the window and waits until the generated frame is drawn and the status overlay is gone.</summary>
    public async Task<(CameraWindow Window, FakeCameraFeed Feed)> OpenStreamingAsync(int width, int height, uint bgra, string printerId = "a")
    {
        // Counted before opening: the stream may create its feed on the thread pool before Open returns.
        int feeds = Factory.Feeds.Count;
        CameraWindow window = Open(printerId);
        FakeCameraFeed feed = await Factory.WaitForFeedAsync(feeds + 1);
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
    }

    private CameraViewModel? Create(string printerId, PrinterDefinition? saved)
    {
        PrinterDefinition? printer = saved ?? Printers.Current.Printers?.FirstOrDefault(p => string.Equals(p.Id, printerId, StringComparison.OrdinalIgnoreCase));
        if (printer is null || CameraTarget.From(printer) is not { } target)
        {
            return null;
        }

        ReconnectingCameraFeed feed = new(Factory, Time, NullLogger<ReconnectingCameraFeed>.Instance, CameraFeedOptions.Default);
        CameraViewModel viewModel = new(target, feed, Monitor, Printers, new AvaloniaUiDispatcher(), States, Time, CameraFeedOptions.Default, NullLogger<CameraViewModel>.Instance);
        _created.Add(viewModel);
        return viewModel;
    }
}
