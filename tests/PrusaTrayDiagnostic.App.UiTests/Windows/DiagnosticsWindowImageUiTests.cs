using Avalonia;
using Avalonia.Automation;
using Avalonia.Headless;

using PrusaTrayDiagnostic.App.Diagnostics;
using PrusaTrayDiagnostic.App.Printers;
using PrusaTrayDiagnostic.App.Resources;
using PrusaTrayDiagnostic.App.Tests.Diagnostics;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.App.UiTests.Infrastructure;
using PrusaTrayDiagnostic.App.Windows;
using PrusaTrayDiagnostic.Core.History;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.App.UiTests.Windows;

/// <summary>The printer image in the header of the diagnostics window (T-039): 48 × 48 next to the name or the selector.</summary>
public sealed class DiagnosticsWindowImageUiTests(HeadlessSession session)
{
    private static readonly PrinterModelCode Mk4SCode = new(1, 4, 1);

    [Fact]
    public Task SelectedPrinterImageIsShownAt48() =>
        session.RunAsync(static scope =>
        {
            (DiagnosticsWindow window, DiagnosticsViewModel viewModel) = Open(
                scope,
                TestPrinters.Definition("a", "Alpha") with { DetectedModel = Mk4SCode },
                TestPrinters.Definition("b", "Beta") with { Image = PrinterImageChoice.ForModel(PrinterModel.CoreOne) });

            PrinterImageView image = window.Find<PrinterImageView>("DiagnosticsPrinterImage");

            Assert.True(image.IsEffectivelyVisible);
            Assert.Equal(new Size(48, 48), image.Bounds.Size);
            Assert.Equal(48, image.LastIllustrationPixelSize);
            Assert.Equal("mk4", image.Picture!.IllustrationId);
            Assert.Equal(Strings.DiagnosticsPrinterImage, AutomationProperties.GetName(image));

            viewModel.SelectedPrinter = viewModel.Printers[1];
            window.Settle();

            Assert.Equal("core-one", image.Picture!.IllustrationId);
            return Task.CompletedTask;
        });

    [Fact]
    public Task HeaderWithImageAndSelector() =>
        session.RunAsync(static scope =>
        {
            (DiagnosticsWindow window, _) = Open(
                scope,
                TestPrinters.Definition("a", "XL 5T") with { DetectedModel = new PrinterModelCode(3, 1, 0) },
                TestPrinters.Definition("b", "Beta"));

            SnapshotAssert.Matches(window, "diagnostics-printer-image");
            return Task.CompletedTask;
        });

    [Fact]
    public Task WithoutPrintersThereIsNoImage() =>
        session.RunAsync(static scope =>
        {
            (DiagnosticsWindow window, _) = Open(scope);

            Assert.False(window.IsShown<PrinterImageView>("DiagnosticsPrinterImage"));
            return Task.CompletedTask;
        });

    private static (DiagnosticsWindow Window, DiagnosticsViewModel ViewModel) Open(UiTestScope scope, params PrinterDefinition[] printers)
    {
        MonitoredPrinters monitored = new();
        ((IMonitoringObserver)monitored).ApplySettings(AppSettings.Default with { Printers = printers });
        FakePrinterInfoSource info = new();
        DiagnosticsViewModel viewModel = scope.Defer(new DiagnosticsViewModel(
            new FakePrinterMonitor(),
            monitored,
            new TelemetryHistory(),
            new AvaloniaUiDispatcher(),
            info,
            new FixedClock(TestPrinters.Now),
            images: new CustomPrinterImages()));
        scope.Defer(new CancelPendingInfo(info));
        DiagnosticsWindow window = scope.Track(new DiagnosticsWindow { DataContext = viewModel });
        window.Show();
        window.SetRenderScaling(1.0);
        window.Settle();
        return (window, viewModel);
    }

    private sealed class CancelPendingInfo(FakePrinterInfoSource info) : IDisposable
    {
        public void Dispose() => info.CancelPending();
    }
}
