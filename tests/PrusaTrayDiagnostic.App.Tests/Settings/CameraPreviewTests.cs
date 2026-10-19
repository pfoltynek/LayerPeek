using Microsoft.Extensions.Time.Testing;

using PrusaTrayDiagnostic.App.Settings;
using PrusaTrayDiagnostic.App.Tests.Cameras;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

using static PrusaTrayDiagnostic.App.Tests.Settings.SettingsTestData;

namespace PrusaTrayDiagnostic.App.Tests.Settings;

/// <summary>
/// The snapshot of the camera "Test" button on the Camera sub-tab (T-034): shown after a successful test only, cleared by
/// an address change, a new test and a failed or cancelled one, and released when the printer or the window goes away.
/// The camera runs on the real <see cref="CameraSnapshotTester"/> over a fake stream; the bitmap is a fake image (no
/// graphics platform here), so that its disposal can be checked.
/// </summary>
public sealed class CameraPreviewTests : IDisposable
{
    private const string StreamUrl = "rtsp://192.168.0.227/live";
    private const uint Red = 0xFFFF0000;

    private static readonly PasswordPolicy Supported = new(IsSupported: true, MaxBytes: 2560);

    private readonly CultureScope _culture = new("en-US");
    private readonly FakeEditorStore _store = new();
    private readonly FakeCameraFeedFactory _factory = new();
    private readonly FakeTimeProvider _time = new();
    private readonly FakePreviewRenderer _renderer = new();
    private SettingsViewModel? _viewModel;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _viewModel?.Dispose();
        _culture.Dispose();
    }

    private async Task<SettingsViewModel> LoadAsync(ICameraTester? tester = null, params PrinterDefinition[] printers)
    {
        if (printers.Length == 0)
        {
            printers = [LinkPrinter("a", "Alpha") with { Camera = new CameraSettings(enabled: true, new Uri(StreamUrl)) }];
        }

        _store.Data = new SettingsEditorData(new AppSettings { Printers = printers }, SettingsLoadStatus.Loaded, [], Ids());
        SettingsViewModel vm = new(
            _store,
            new FakeConnectionTester(),
            Supported,
            cameraTester: tester ?? new CameraSnapshotTester(_factory, _time, new ListLogger<CameraSnapshotTester>()),
            cameraPreviewRenderer: _renderer);
        _viewModel = vm;
        await vm.LoadAsync();
        return vm;
    }

    private async Task<PrinterEditorViewModel> LoadOneAsync(ICameraTester? tester = null) => (await LoadAsync(tester)).Printers[0];

    /// <summary>Runs a test that the stream answers with one frame; returns the feed.</summary>
    private async Task<FakeCameraFeed> TestWithFrameAsync(PrinterEditorViewModel printer, int number, int width = 64, int height = 36, uint color = Red)
    {
        Task test = printer.TestCameraCommand.ExecuteAsync(null);
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(number);
        feed.SendColor(width, height, color);
        await test.WaitAsync(TestTimeouts.Wait, Ct);
        return feed;
    }

    private static void AssertNoPreview(PrinterEditorViewModel printer)
    {
        Assert.Null(printer.CameraPreview);
        Assert.False(printer.HasCameraPreview);
    }

    [Fact]
    public async Task NoPreviewBeforeATest()
    {
        PrinterEditorViewModel printer = await LoadOneAsync();

        AssertNoPreview(printer);
        // Never opened automatically (decision 2026-10-19: only after clicking Test).
        Assert.Empty(_factory.Uris);
    }

    [Fact]
    public async Task SuccessfulTestShowsTheSnapshotAndClosesTheStream()
    {
        PrinterEditorViewModel printer = await LoadOneAsync();
        List<string?> changes = [];
        printer.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        FakeCameraFeed feed = await TestWithFrameAsync(printer, 1, 64, 36);

        FakePreviewImage preview = Assert.Single(_renderer.Rendered);
        Assert.Same(preview, printer.CameraPreview);
        Assert.True(printer.HasCameraPreview);
        Assert.Equal((64, 36), (preview.Source.Width, preview.Source.Height));
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, preview.Source.Pixels[..4].ToArray());
        Assert.Equal("The camera works (64 × 36 px).", printer.CameraTestResultText);
        Assert.Contains(nameof(PrinterEditorViewModel.CameraPreview), changes);
        Assert.Contains(nameof(PrinterEditorViewModel.HasCameraPreview), changes);
        await feed.Disposed.WaitAsync(TestTimeouts.Wait, Ct);
    }

    [Fact]
    public async Task TimeoutClearsThePreview()
    {
        PrinterEditorViewModel printer = await LoadOneAsync();
        await TestWithFrameAsync(printer, 1);
        FakePreviewImage old = printer.CameraPreview as FakePreviewImage ?? throw new InvalidOperationException("No preview.");

        Task test = printer.TestCameraCommand.ExecuteAsync(null);
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(2);
        _time.Advance(TimeSpan.FromSeconds(15));
        await test.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal("No image arrived from the camera within 15 s.", printer.CameraTestResultText);
        AssertNoPreview(printer);
        Assert.Equal(1, old.DisposeCount);
        Assert.Single(_renderer.Rendered);
        await feed.Disposed.WaitAsync(TestTimeouts.Wait, Ct);
    }

    [Fact]
    public async Task FailureClearsThePreview()
    {
        PrinterEditorViewModel printer = await LoadOneAsync();
        await TestWithFrameAsync(printer, 1);
        FakePreviewImage old = (FakePreviewImage)printer.CameraPreview!;

        Task test = printer.TestCameraCommand.ExecuteAsync(null);
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(2);
        feed.Fail(new CameraFeedException("Connection refused."));
        await test.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal("The camera stream could not be opened: Connection refused.", printer.CameraTestResultText);
        AssertNoPreview(printer);
        Assert.True(old.IsDisposed);
        await feed.Disposed.WaitAsync(TestTimeouts.Wait, Ct);
    }

    [Fact]
    public async Task UnexpectedTesterExceptionClearsThePreview()
    {
        FakeCameraTester tester = new() { Immediate = new CameraTestResult(CameraTestOutcome.Success, 64, 36, Image: TestCameraImages.Solid(64, 36, Red)) };
        PrinterEditorViewModel printer = await LoadOneAsync(tester);
        await printer.TestCameraCommand.ExecuteAsync(null);
        FakePreviewImage old = (FakePreviewImage)printer.CameraPreview!;

        tester.Immediate = null;
        tester.Throw = new InvalidOperationException("boom");
        await printer.TestCameraCommand.ExecuteAsync(null);

        AssertNoPreview(printer);
        Assert.True(old.IsDisposed);
    }

    [Fact]
    public async Task ChangingTheAddressClearsThePreview()
    {
        PrinterEditorViewModel printer = await LoadOneAsync();
        await TestWithFrameAsync(printer, 1);
        FakePreviewImage old = (FakePreviewImage)printer.CameraPreview!;

        printer.CameraUrl = "rtsp://192.168.0.228/live";

        AssertNoPreview(printer);
        Assert.Equal(1, old.DisposeCount);
        Assert.Null(printer.CameraTestResultText);
    }

    [Fact]
    public async Task NewTestClearsTheOldPreviewRightAway()
    {
        PrinterEditorViewModel printer = await LoadOneAsync();
        await TestWithFrameAsync(printer, 1, color: Red);
        FakePreviewImage old = (FakePreviewImage)printer.CameraPreview!;

        Task test = printer.TestCameraCommand.ExecuteAsync(null);

        // While the new test waits for an image, the old one is gone (it might not match the stream any more).
        AssertNoPreview(printer);
        Assert.True(old.IsDisposed);
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(2);
        feed.SendColor(32, 24, 0xFF0000FF);
        await test.WaitAsync(TestTimeouts.Wait, Ct);

        FakePreviewImage current = (FakePreviewImage)printer.CameraPreview!;
        Assert.NotSame(old, current);
        Assert.False(current.IsDisposed);
        Assert.Equal((32, 24), (current.Source.Width, current.Source.Height));
        Assert.Equal(1, old.DisposeCount);
    }

    [Fact]
    public async Task RefusedTestClearsThePreview()
    {
        FakeCameraTester tester = new() { Immediate = new CameraTestResult(CameraTestOutcome.Success, 64, 36, Image: TestCameraImages.Solid(64, 36, Red)) };
        SettingsViewModel vm = await LoadAsync(tester);
        PrinterEditorViewModel printer = vm.Printers[0];
        await printer.TestCameraCommand.ExecuteAsync(null);
        FakePreviewImage old = (FakePreviewImage)printer.CameraPreview!;

        // Through the address field (an edit clears it anyway), then a test that is refused.
        printer.CameraUrl = "http://192.168.0.227/live";
        await printer.TestCameraCommand.ExecuteAsync(null);

        AssertNoPreview(printer);
        Assert.Equal(1, old.DisposeCount);
        Assert.Single(tester.Calls);
    }

    [Fact]
    public async Task CancelledTestLeavesNoPreviewAndClosesTheStream()
    {
        PrinterEditorViewModel printer = await LoadOneAsync();
        await TestWithFrameAsync(printer, 1);
        FakePreviewImage old = (FakePreviewImage)printer.CameraPreview!;

        Task test = printer.TestCameraCommand.ExecuteAsync(null);
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(2);
        printer.CancelCameraTestCommand.Execute(null);
        await test.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal("Test cancelled.", printer.CameraTestResultText);
        AssertNoPreview(printer);
        Assert.True(old.IsDisposed);
        Assert.Single(_renderer.Rendered);
        await feed.Disposed.WaitAsync(TestTimeouts.Wait, Ct);
    }

    [Fact]
    public async Task ImageArrivingAfterCancellationIsNotShown()
    {
        FakeCameraTester tester = new() { Pending = new TaskCompletionSource<CameraTestResult>(TaskCreationOptions.RunContinuationsAsynchronously) };
        PrinterEditorViewModel printer = await LoadOneAsync(tester);
        Task test = printer.TestCameraCommand.ExecuteAsync(null);

        printer.CancelCameraTestCommand.Execute(null);
        tester.Pending.SetResult(new CameraTestResult(CameraTestOutcome.Success, 64, 36, Image: TestCameraImages.Solid(64, 36, Red)));
        await test.WaitAsync(TestTimeouts.Wait, Ct);

        AssertNoPreview(printer);
        Assert.Empty(_renderer.Rendered);
    }

    [Fact]
    public async Task ClosingTheWindowReleasesThePreview()
    {
        SettingsViewModel vm = await LoadAsync();
        PrinterEditorViewModel printer = vm.Printers[0];
        await TestWithFrameAsync(printer, 1);
        FakePreviewImage preview = (FakePreviewImage)printer.CameraPreview!;

        vm.Dispose();

        AssertNoPreview(printer);
        Assert.Equal(1, preview.DisposeCount);
    }

    [Fact]
    public async Task ImageArrivingAfterTheWindowClosedIsNeverRendered()
    {
        FakeCameraTester tester = new() { Pending = new TaskCompletionSource<CameraTestResult>(TaskCreationOptions.RunContinuationsAsynchronously) };
        SettingsViewModel vm = await LoadAsync(tester);
        PrinterEditorViewModel printer = vm.Printers[0];
        Task test = printer.TestCameraCommand.ExecuteAsync(null);

        vm.Dispose();
        tester.Pending.SetResult(new CameraTestResult(CameraTestOutcome.Success, 64, 36, Image: TestCameraImages.Solid(64, 36, Red)));
        await test.WaitAsync(TestTimeouts.Wait, Ct);

        AssertNoPreview(printer);
        Assert.Empty(_renderer.Rendered);
    }

    [Fact]
    public async Task RemovingThePrinterReleasesItsPreview()
    {
        SettingsViewModel vm = await LoadAsync(null, LinkPrinter("a", "Alpha") with { Camera = new CameraSettings(enabled: true, new Uri(StreamUrl)) }, LinkPrinter("b", "Beta", "192.168.1.21"));
        PrinterEditorViewModel alpha = vm.Printers[0];
        await TestWithFrameAsync(alpha, 1);
        FakePreviewImage preview = (FakePreviewImage)alpha.CameraPreview!;
        vm.SelectedPrinter = alpha;

        vm.RemovePrinterCommand.Execute(null);

        Assert.DoesNotContain(alpha, vm.Printers);
        AssertNoPreview(alpha);
        Assert.Equal(1, preview.DisposeCount);
    }

    [Fact]
    public async Task EachPrinterKeepsItsOwnPreviewWhileTheWindowIsOpen()
    {
        SettingsViewModel vm = await LoadAsync(
            null,
            LinkPrinter("a", "Alpha") with { Camera = new CameraSettings(enabled: true, new Uri(StreamUrl)) },
            LinkPrinter("b", "Beta", "192.168.1.21") with { Camera = new CameraSettings(enabled: true, new Uri("rtsp://192.168.0.228/live")) });
        await TestWithFrameAsync(vm.Printers[0], 1);
        FakePreviewImage alpha = (FakePreviewImage)vm.Printers[0].CameraPreview!;

        vm.SelectedPrinter = vm.Printers[1];
        AssertNoPreview(vm.Printers[1]);
        vm.SelectedPrinter = vm.Printers[0];

        Assert.Same(alpha, vm.Printers[0].CameraPreview);
        Assert.False(alpha.IsDisposed);
    }

    [Fact]
    public async Task LanguageSwitchKeepsThePreview()
    {
        SettingsViewModel vm = await LoadAsync();
        PrinterEditorViewModel printer = vm.Printers[0];
        await TestWithFrameAsync(printer, 1);
        FakePreviewImage preview = (FakePreviewImage)printer.CameraPreview!;
        List<string?> changes = [];
        printer.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        using (new CultureScope("de-DE"))
        {
            vm.RefreshTexts();
            Assert.Equal("Die Kamera funktioniert (64 × 36 px).", printer.CameraTestResultText);
        }

        Assert.Same(preview, printer.CameraPreview);
        Assert.False(preview.IsDisposed);
        Assert.DoesNotContain(nameof(PrinterEditorViewModel.CameraPreview), changes);
        // Not tested again.
        Assert.Single(_factory.Feeds);
    }

    [Fact]
    public async Task TurningTheCameraOffOrSavingKeepsThePreview()
    {
        // Only an address change and a failed test clear it (decision 2026-10-19).
        SettingsViewModel vm = await LoadAsync();
        PrinterEditorViewModel printer = vm.Printers[0];
        await TestWithFrameAsync(printer, 1);
        FakePreviewImage preview = (FakePreviewImage)printer.CameraPreview!;

        printer.CameraEnabled = false;
        printer.Name = "Alpha 2";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Single(_store.Saves);
        Assert.Same(preview, printer.CameraPreview);
        Assert.False(preview.IsDisposed);
    }

    [Fact]
    public async Task SuccessWithoutAnImageShowsNoPreview()
    {
        PrinterEditorViewModel printer = await LoadOneAsync(new FakeCameraTester { Immediate = new CameraTestResult(CameraTestOutcome.Success, 1280, 720) });

        await printer.TestCameraCommand.ExecuteAsync(null);

        Assert.True(printer.CameraTestSucceeded);
        AssertNoPreview(printer);
        Assert.Empty(_renderer.Rendered);
    }

    [Fact]
    public async Task PreviewThatCannotBeDrawnKeepsTheSuccess()
    {
        _renderer.Throw = new InvalidOperationException("No graphics.");
        PrinterEditorViewModel printer = await LoadOneAsync();

        await TestWithFrameAsync(printer, 1);

        Assert.Equal("The camera works (64 × 36 px).", printer.CameraTestResultText);
        Assert.True(printer.CameraTestSucceeded);
        AssertNoPreview(printer);
    }
}
