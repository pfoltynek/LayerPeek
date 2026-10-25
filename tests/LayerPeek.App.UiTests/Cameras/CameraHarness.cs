using Avalonia.Headless;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Cameras;
using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Previews;
using LayerPeek.App.Tests.Cameras;
using LayerPeek.App.Tests.Cameras.Layout;
using LayerPeek.App.Tests.Cameras.Overlay;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tray;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Cooldown;
using LayerPeek.Core.History;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.UiTests.Cameras;

/// <summary>
/// The real camera windows (<see cref="CameraWindows"/>, the path behind <c>ITrayAppCommands.ToggleCamera</c>) on fakes:
/// two printers with cameras, a fake decoder and a fake clock. The window state goes to a store the test chooses
/// (memory by default, a temporary folder for persistence tests). The headless session sets the languages (English).
/// </summary>
internal sealed class CameraHarness : IAsyncDisposable
{
    public static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private readonly List<CameraViewModel> _created = [];
    private readonly ICameraMonitorPathUpdater? _monitorPaths;
    private readonly OverlayWidgetRegistry _widgets;
    private readonly PrintThumbnails? _thumbnails;

    /// <param name="screens">Monitors of the camera windows (T-035); <see langword="null"/>: the headless screen.</param>
    /// <param name="window">Window defaults of the printers with a camera; <see langword="null"/>: the defaults.</param>
    /// <param name="overlay">Widget layout of the printers with a camera (T-081); <see langword="null"/>: the default layout.</param>
    /// <param name="measureChrome">Fake frame measurement of the windows (T-040); <see langword="null"/>: the headless one.</param>
    /// <param name="track">Tracks each new window (T-043); <see langword="null"/>: none.</param>
    /// <param name="predictChrome">Fake frame prediction per monitor scaling (T-071 Q3); <see langword="null"/>: the headless one (assumed chrome).</param>
    /// <param name="monitorPaths">Receives saved camera monitors found on another connector (user decision 2026-10-23); <see langword="null"/>: none.</param>
    /// <param name="widgets">The overlay widgets of the windows (T-081); <see langword="null"/>: the test widgets <see cref="TestOverlayWidgets.Ids"/>.</param>
    /// <param name="opacity">The global card opacity (T-077).</param>
    /// <param name="thumbnails">The G-code thumbnails of the Print name widget (T-087); <see langword="null"/>: none.</param>
    public CameraHarness(ICameraWindowStateStore? states = null, IScreenCatalog? screens = null, CameraWindowDefaults? window = null, CameraOverlayLayout? overlay = null, Func<CameraWindow, WindowChrome?>? measureChrome = null, Func<Avalonia.Controls.Window, IDisposable>? track = null, Func<CameraWindow, double, WindowChrome?>? predictChrome = null, ICameraMonitorPathUpdater? monitorPaths = null, OverlayWidgetRegistry? widgets = null, int opacity = CameraOverlayOpacity.DefaultPercent, PrintThumbnails? thumbnails = null)
    {
        LayoutWriter = new FakeLayoutWriter();
        States = states ?? new MemoryCameraWindowStateStore();
        _monitorPaths = monitorPaths;
        _thumbnails = thumbnails;
        _widgets = widgets ?? TestOverlayWidgets.Registry([.. TestOverlayWidgets.Ids]);
        AppSettings settings = new()
        {
            Printers = [CameraPrinter("a", "Alpha", window, overlay), CameraPrinter("b", "Beta", window, overlay), TestPrinters.Definition("c", "NoCamera")],
            CameraOverlayOpacityPercent = opacity,
        };
        ((IMonitoringObserver)Printers).ApplySettings(settings);
        // The cool-down states of the mini preview strip (T-089) from the snapshots the test raises.
        Cooldown = new CooldownMonitor(Monitor, TimeProvider.System, NullLogger<CooldownMonitor>.Instance);
        Cooldown.Configure(settings.Printers, settings.PollingInterval);
        Windows = new AuxiliaryWindowFactory(track).CameraWindows(Create, screens, measureChrome, predictChrome);
    }

    public CooldownMonitor Cooldown { get; }

    public FakeCameraFeedFactory Factory { get; } = new();

    /// <summary>Records the layouts saved by the layout mode of the windows (T-091); nothing on disk.</summary>
    public FakeLayoutWriter LayoutWriter { get; }

    public FakeTimeProvider Time { get; } = new();

    /// <summary>The telemetry history the windows chart (T-084); empty and not configured until a test records samples.</summary>
    public TelemetryHistory History { get; } = new();

    public FakePrinterMonitor Monitor { get; } = new();

    public MonitoredPrinters Printers { get; } = new();

    public ICameraWindowStateStore States { get; }

    public CameraWindows Windows { get; }

    public IReadOnlyList<CameraViewModel> Created => _created;

    public static PrinterDefinition CameraPrinter(string id, string name, CameraWindowDefaults? window = null, CameraOverlayLayout? overlay = null) =>
        TestPrinters.Definition(id, name) with
        {
            Camera = new CameraSettings(enabled: true, StreamUri) { Window = window ?? CameraWindowDefaults.Default, Overlay = overlay ?? CameraOverlayLayout.Default },
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

    /// <summary>
    /// The user resizes the mini preview with its bottom right grip to <paramref name="width"/> × <paramref name="height"/>
    /// and releases it; the window corrects its height afterwards (T-089). Headless has no size loop and runs the posted
    /// work right after the press: the size is set first, as the system's modal size loop does before
    /// <c>BeginResizeDrag</c> returns, then the grip is pressed and released.
    /// </summary>
    public static void ResizeMiniWithGrip(CameraWindow window, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.Width = width;
        window.Height = height;
        window.Settle();
        Avalonia.Point grip = new(window.ClientSize.Width - 2, window.ClientSize.Height - 2);
        window.MouseDown(grip, Avalonia.Input.MouseButton.Left);
        window.MouseUp(grip, Avalonia.Input.MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.Settle();
    }

    public async ValueTask DisposeAsync()
    {
        Windows.Shutdown();
        foreach (CameraViewModel viewModel in _created)
        {
            viewModel.Dispose();
            await viewModel.StreamCompletion.WaitAsync(TestTimeouts.Wait);
        }

        Cooldown.Dispose();
    }

    private CameraViewModel? Create(string printerId, PrinterDefinition? saved)
    {
        PrinterDefinition? printer = saved ?? Printers.Current.Printers?.FirstOrDefault(p => string.Equals(p.Id, printerId, StringComparison.OrdinalIgnoreCase));
        if (printer is null || CameraTarget.From(printer) is not { } target)
        {
            return null;
        }

        ReconnectingCameraFeed feed = new(Factory, Time, NullLogger<ReconnectingCameraFeed>.Instance, CameraFeedOptions.Default);
        CameraViewModel viewModel = new(target, feed, Monitor, Printers, new AvaloniaUiDispatcher(), States, Time, CameraFeedOptions.Default, NullLogger<CameraViewModel>.Instance, _monitorPaths, _widgets, History, Cooldown, _thumbnails, LayoutWriter);
        _created.Add(viewModel);
        return viewModel;
    }
}
