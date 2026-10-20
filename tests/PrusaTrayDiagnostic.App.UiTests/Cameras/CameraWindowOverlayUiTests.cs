using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;

using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.App.Localization;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.App.UiTests.Infrastructure;
using PrusaTrayDiagnostic.App.Windows;
using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

using static PrusaTrayDiagnostic.App.Tests.Tray.TestPrinters;

namespace PrusaTrayDiagnostic.App.UiTests.Cameras;

/// <summary>
/// The print overlay of the real camera window (T-036): exactly the selected items of the current mode (the mini preview has
/// its own, full screen uses the normal window's), in the chosen corner (below the window buttons in the top right corner),
/// and saved settings change an open window in place.
/// </summary>
public sealed class CameraWindowOverlayUiTests(HeadlessSession session)
{
    private const uint Grey = 0xFF606060;

    /// <summary>Distance of the overlay from the edges of the image.</summary>
    private const double Margin = 6;

    private static readonly CameraOverlayOptions Selection = new()
    {
        Normal = CameraOverlayItems.PrinterName | CameraOverlayItems.NozzleTemperature | CameraOverlayItems.BedTemperature | CameraOverlayItems.FinishTime,
        Mini = CameraOverlayItems.Progress | CameraOverlayItems.FileName,
        Corner = CameraOverlayCorner.TopRight,
    };

    private static CameraViewModel ViewModel(CameraWindow window) => (CameraViewModel)window.DataContext!;

    private static PrinterSnapshot Printing() => new()
    {
        PrinterId = "a",
        State = PrinterState.Printing,
        Timestamp = Now,
        Job = new PrintJob { FileName = "benchy_0.4n_0.2mm_PLA_MK4S_2h10m.bgcode", ProgressPercent = 42.7, Elapsed = TimeSpan.FromMinutes(130), Remaining = TimeSpan.FromMinutes(65) },
        Telemetry = new Telemetry { Nozzle = new Temperature(214.6, 215), Bed = new Temperature(59.8, 60) },
    };

    private static void Run(CameraWindow window, Action action)
    {
        action();
        Dispatcher.UIThread.RunJobs();
        window.Settle();
    }

    private static async Task<CameraWindow> OpenPrintingAsync(CameraHarness harness, double width = 480, double height = 270)
    {
        (CameraWindow window, _) = await harness.OpenStreamingAsync(160, 90, Grey);
        if (ViewModel(window).IsNormal)
        {
            window.Width = width;
            window.Height = height;
        }

        harness.Monitor.Raise(Printing());
        await Ui.UntilAsync(() => ViewModel(window).IsPrintOverlayVisible && OverlayTexts(window).Count > 0, "overlay shown");
        window.Settle();
        return window;
    }

    private static void Save(CameraHarness harness, CameraOverlayOptions overlay, CameraWindowDefaults? window = null)
    {
        // What saving the settings does for an open window: the monitoring session applies the new printer definitions.
        ((IMonitoringObserver)harness.Printers).ApplySettings(new AppSettings
        {
            Printers = [CameraHarness.CameraPrinter("a", "Alpha", window, overlay), CameraHarness.CameraPrinter("b", "Beta")],
        });
        Dispatcher.UIThread.RunJobs();
    }

    private static Border Overlay(CameraWindow window) => window.Find<Border>("CameraPrintOverlay");

    private static List<string> OverlayTexts(CameraWindow window)
    {
        Border overlay = Overlay(window);
        return overlay.IsEffectivelyVisible
            ? [.. overlay.GetVisualDescendants().OfType<TextBlock>().Where(static block => block.IsEffectivelyVisible).Select(static block => block.Text ?? string.Empty)]
            : [];
    }

    private static Rect BoundsInWindow(Control control, CameraWindow window) =>
        new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);

    private static StackPanel WindowButtons(CameraWindow window) =>
        window.GetVisualDescendants().OfType<StackPanel>().Single(static panel => panel.Classes.Contains("windowButtons"));

    // ---- Items per mode ----

    [Fact]
    public Task DefaultOverlayShowsTodaysItemsBottomLeft() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new();
            CameraWindow window = await OpenPrintingAsync(harness);

            Assert.Equal(["Printing – 42%", "1 h 5 min remaining"], OverlayTexts(window));
            Rect overlay = BoundsInWindow(Overlay(window), window);
            Assert.Equal(Margin, overlay.X, 1);
            Assert.Equal(window.ClientSize.Height - Margin, overlay.Bottom, 1);
        });

    [Fact]
    public Task EachModeShowsExactlyItsSelectedItems() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(window: CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal }, overlay: Selection);
            CameraWindow window = await OpenPrintingAsync(harness);
            CameraViewModel viewModel = ViewModel(window);
            string[] normal = ["Alpha", "Nozzle: 215 / 215 °C", "Bed: 60 / 60 °C", "Done at 1:05 PM"];

            Assert.Equal(normal, OverlayTexts(window));

            Run(window, () => viewModel.ToggleFullScreenCommand.Execute(null));
            Assert.True(viewModel.IsFullScreen);
            Assert.Equal(normal, OverlayTexts(window));

            Run(window, () => viewModel.ToggleFullScreenCommand.Execute(null));
            Run(window, () => viewModel.ShowMiniCommand.Execute(null));
            Assert.True(viewModel.IsMini);
            Assert.Equal(["42%", "benchy_0.4n_0.2mm_PLA_MK4S_2h10m.bgcode"], OverlayTexts(window));

            Run(window, () => viewModel.ShowNormalCommand.Execute(null));
            Assert.Equal(normal, OverlayTexts(window));
        });

    [Fact]
    public Task NoItemsShowsNoOverlayButTheConnectionStateStays() =>
        session.RunAsync(async scope =>
        {
            CameraOverlayOptions none = new() { Normal = CameraOverlayItems.None, Mini = CameraOverlayItems.None };
            await using CameraHarness harness = new(overlay: none);
            CameraWindow window = harness.Open();
            harness.Monitor.Raise(Printing());
            Dispatcher.UIThread.RunJobs();
            window.Settle();

            Assert.False(Overlay(window).IsEffectivelyVisible);
            Assert.True(Ui.ShowsText(window, "Connecting to the camera…"));
        });

    // ---- Corner ----

    [Theory]
    [InlineData(CameraOverlayCorner.TopLeft)]
    [InlineData(CameraOverlayCorner.TopRight)]
    [InlineData(CameraOverlayCorner.BottomLeft)]
    [InlineData(CameraOverlayCorner.BottomRight)]
    public Task OverlayIsInTheChosenCornerInEveryMode(CameraOverlayCorner corner) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(window: CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal }, overlay: Selection with { Corner = corner });
            CameraWindow window = await OpenPrintingAsync(harness);
            CameraViewModel viewModel = ViewModel(window);

            foreach (Action switchMode in new Action[]
            {
                () => { },
                () => viewModel.ToggleFullScreenCommand.Execute(null),
                () => viewModel.ToggleFullScreenCommand.Execute(null),
                () => viewModel.ShowMiniCommand.Execute(null),
            })
            {
                Run(window, switchMode);
                string context = $"{corner} / {viewModel.Mode}";
                Rect overlay = BoundsInWindow(Overlay(window), window);
                Rect buttons = BoundsInWindow(WindowButtons(window), window);
                Size client = window.ClientSize;
                Assert.True(overlay.Width > 0 && overlay.Height > 0, context);

                bool right = corner is CameraOverlayCorner.TopRight or CameraOverlayCorner.BottomRight;
                bool top = corner is CameraOverlayCorner.TopLeft or CameraOverlayCorner.TopRight;
                Assert.True(Math.Abs((right ? client.Width - overlay.Right : overlay.X) - Margin) < 1, $"{context}: {overlay} in {client}");
                if (top)
                {
                    // The top right corner starts below the window buttons, which must never cover it.
                    double expectedTop = right ? buttons.Bottom + 4 : Margin;
                    Assert.True(Math.Abs(overlay.Y - expectedTop) < 1, $"{context}: top {overlay.Y}, buttons end at {buttons.Bottom}");
                    Assert.False(overlay.Intersects(buttons), $"{context}: {overlay} overlaps the buttons {buttons}");
                }
                else
                {
                    Assert.True(Math.Abs(client.Height - overlay.Bottom - Margin) < 1, $"{context}: {overlay} in {client}");
                }
            }
        });

    [Fact]
    public Task RightCornersAlignTheTextToTheRight() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(overlay: Selection with { Corner = CameraOverlayCorner.BottomRight });
            CameraWindow window = await OpenPrintingAsync(harness);

            Assert.All(
                Overlay(window).GetVisualDescendants().OfType<TextBlock>(),
                static block => Assert.Equal(Avalonia.Media.TextAlignment.Right, block.TextAlignment));
        });

    // ---- Saved settings ----

    [Fact]
    public Task SavedOverlayChangesTheOpenWindowWithoutMovingIt() =>
        session.RunAsync(async scope =>
        {
            CameraWindowDefaults defaults = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal };
            await using CameraHarness harness = new(window: defaults);
            CameraWindow window = await OpenPrintingAsync(harness);
            window.Position = new PixelPoint(123, 45);
            window.Settle();
            PixelPoint position = window.Position;
            Size size = window.ClientSize;
            Assert.Equal(["Printing – 42%", "1 h 5 min remaining"], OverlayTexts(window));

            Save(harness, Selection, defaults);
            window.Settle();

            Assert.Equal(["Alpha", "Nozzle: 215 / 215 °C", "Bed: 60 / 60 °C", "Done at 1:05 PM"], OverlayTexts(window));
            Rect overlay = BoundsInWindow(Overlay(window), window);
            Assert.Equal(window.ClientSize.Width - Margin, overlay.Right, 1);
            Assert.True(overlay.Y > Margin);
            Assert.Equal(position, window.Position);
            Assert.Equal(size, window.ClientSize);
            Assert.True(ViewModel(window).IsNormal);
        });

    [Fact]
    public Task SavedMiniItemsChangeAnOpenMiniPreviewWithoutMovingIt() =>
        session.RunAsync(async scope =>
        {
            CameraWindowDefaults defaults = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini };
            await using CameraHarness harness = new(window: defaults);
            CameraWindow window = await OpenPrintingAsync(harness);
            PixelPoint position = window.Position;
            Size size = window.ClientSize;
            Assert.True(ViewModel(window).IsMini);

            Save(harness, Selection with { Corner = CameraOverlayCorner.TopLeft }, defaults);
            window.Settle();

            Assert.Equal(["42%", "benchy_0.4n_0.2mm_PLA_MK4S_2h10m.bgcode"], OverlayTexts(window));
            Rect overlay = BoundsInWindow(Overlay(window), window);
            Assert.Equal(Margin, overlay.X, 1);
            Assert.Equal(Margin, overlay.Y, 1);
            Assert.Equal(position, window.Position);
            Assert.Equal(size, window.ClientSize);
        });

    [Fact]
    public Task SavedOverlayOfAnotherPrinterLeavesThisWindowAlone() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new();
            CameraWindow window = await OpenPrintingAsync(harness);

            ((IMonitoringObserver)harness.Printers).ApplySettings(new AppSettings
            {
                Printers = [CameraHarness.CameraPrinter("a", "Alpha"), CameraHarness.CameraPrinter("b", "Beta", overlay: Selection)],
            });
            Dispatcher.UIThread.RunJobs();
            window.Settle();

            Assert.Equal(["Printing – 42%", "1 h 5 min remaining"], OverlayTexts(window));
        });

    // ---- Language ----

    [Fact]
    public Task OverlayFollowsALanguageSwitch() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(overlay: Selection with { Normal = CameraOverlayOptions.AllItems });
            CameraWindow window = await OpenPrintingAsync(harness);

            Assert.True(new UiLanguage().Apply("de"));
            ViewModel(window).RefreshTexts();
            window.Settle();

            Assert.Contains("Düse: 215 / 215 °C", OverlayTexts(window));
            Assert.Contains("Heizbett: 60 / 60 °C", OverlayTexts(window));
            Assert.Contains("Druckzeit: 2 h 10 min", OverlayTexts(window));
        });

    // ---- Snapshots ----

    [Fact]
    public Task SnapshotOfAllItemsInTheTopRightCorner() =>
        session.RunAsync(async scope =>
        {
            CameraOverlayOptions all = Selection with { Normal = CameraOverlayOptions.AllItems };
            await using CameraHarness harness = new(window: CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal }, overlay: all);
            CameraWindow window = await OpenPrintingAsync(harness, 480, 270);

            SnapshotAssert.Matches(window, "camera-overlay-all-top-right");
        });

    [Fact]
    public Task SnapshotOfTheMiniPreviewWithItsOwnItems() =>
        session.RunAsync(async scope =>
        {
            CameraOverlayOptions overlay = Selection with { Corner = CameraOverlayCorner.BottomRight };
            await using CameraHarness harness = new(window: CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini }, overlay: overlay);
            CameraWindow window = await OpenPrintingAsync(harness);

            SnapshotAssert.Matches(window, "camera-overlay-mini");
        });

    [Fact]
    public Task SnapshotOfTheDefaultOverlay() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(window: CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal });
            CameraWindow window = await OpenPrintingAsync(harness, 480, 270);

            SnapshotAssert.Matches(window, "camera-overlay-default");
        });
}
