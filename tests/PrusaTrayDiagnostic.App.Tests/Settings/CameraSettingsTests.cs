using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.App.Settings;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

using static PrusaTrayDiagnostic.App.Tests.Settings.SettingsTestData;

namespace PrusaTrayDiagnostic.App.Tests.Settings;

/// <summary>Settings files written by the tests, read back through the real <see cref="JsonSettingsStore"/>.</summary>
internal static class SettingsFiles
{
    /// <summary>A file whose camera address contains credentials (only the real loader can produce CredentialsRemoved).</summary>
    public static async Task<JsonSettingsStore> WithCameraCredentialsAsync(string directory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, JsonSettingsStore.FileName),
            """
            {
              "version": 1,
              "printers": [
                { "id": "a", "name": "Alpha", "connectionType": "PrusaLink", "address": "192.168.0.226",
                  "camera": { "enabled": true, "streamUrl": "rtsp://admin:secret@192.168.0.227/live" } },
                { "id": "b", "name": "Beta", "connectionType": "PrusaLink", "address": "192.168.0.228",
                  "camera": { "enabled": true, "streamUrl": "rtsp://192.168.0.229/live" } }
              ]
            }
            """,
            cancellationToken);
        return new JsonSettingsStore(directory);
    }
}

/// <summary>Camera section of the settings window (T-014).</summary>
public sealed class CameraSettingsTests : IDisposable
{
    private static readonly PasswordPolicy Supported = new(IsSupported: true, MaxBytes: 2560);

    private readonly CultureScope _culture = new("en-US");
    private readonly FakeEditorStore _store = new();
    private readonly FakeConnectionTester _tester = new();
    private readonly FakeCameraTester _cameraTester = new();
    private readonly string _directory = TestDirectories.NewDirectoryPath();
    private SettingsViewModel? _viewModel;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _viewModel?.Dispose();
        _culture.Dispose();
        TestDirectories.Delete(_directory);
    }

    private static PrinterDefinition WithCamera(PrinterDefinition printer, CameraSettings camera) => printer with { Camera = camera };

    private static CameraSettings Camera(string url, bool enabled = true) => new(enabled, new Uri(url));

    private async Task<SettingsViewModel> LoadAsync(IReadOnlySet<string>? removedCredentials = null, params PrinterDefinition[] printers)
    {
        _store.Data = new SettingsEditorData(new AppSettings { Printers = printers }, SettingsLoadStatus.Loaded, [], Ids(), removedCredentials);
        SettingsViewModel vm = new(_store, _tester, Supported, cameraTester: _cameraTester);
        _viewModel = vm;
        await vm.LoadAsync();
        return vm;
    }

    private async Task<PrinterEditorViewModel> LoadOneAsync(CameraSettings? camera = null) =>
        (await LoadAsync(null, WithCamera(LinkPrinter("a", "Alpha"), camera ?? CameraSettings.Disabled))).Printers[0];

    private async Task<AppSettings> SaveAsync(SettingsViewModel vm)
    {
        await vm.SaveCommand.ExecuteAsync(null);
        return Assert.Single(_store.Saves).Updated;
    }

    // ---- Loading and saving ----

    [Fact]
    public async Task LoadedCameraIsShown()
    {
        PrinterEditorViewModel printer = await LoadOneAsync(Camera("rtsp://192.168.0.227/live"));

        Assert.True(printer.CameraEnabled);
        Assert.Equal("rtsp://192.168.0.227/live", printer.CameraUrl);
        Assert.Null(printer.CameraUrlError);
        Assert.False(printer.CameraCredentialsRemoved);
    }

    [Fact]
    public async Task SavingOtherChangesKeepsTheCamera()
    {
        SettingsViewModel vm = await LoadAsync(null, WithCamera(LinkPrinter("a", "Alpha"), Camera("rtsp://192.168.0.227/live")));
        Assert.False(vm.IsDirty);

        vm.Printers[0].Name = "Renamed";
        AppSettings saved = await SaveAsync(vm);

        Assert.Equal(Camera("rtsp://192.168.0.227/live"), saved.Printers[0].Camera);
    }

    [Fact]
    public async Task EnteredCameraIsSaved()
    {
        SettingsViewModel vm = await LoadAsync(null, LinkPrinter("a", "Alpha"));
        PrinterEditorViewModel printer = vm.Printers[0];

        printer.CameraUrl = "  rtsp://192.168.0.227/live ";
        printer.CameraEnabled = true;
        Assert.True(vm.IsDirty);
        AppSettings saved = await SaveAsync(vm);

        Assert.Equal(Camera("rtsp://192.168.0.227/live"), saved.Printers[0].Camera);
    }

    [Fact]
    public async Task DisabledCameraKeepsItsAddress()
    {
        SettingsViewModel vm = await LoadAsync(null, WithCamera(LinkPrinter("a", "Alpha"), Camera("rtsp://192.168.0.227/live")));

        vm.Printers[0].CameraEnabled = false;
        AppSettings saved = await SaveAsync(vm);

        Assert.Equal(Camera("rtsp://192.168.0.227/live", enabled: false), saved.Printers[0].Camera);
    }

    [Fact]
    public async Task EmptyDisabledCameraIsSavedAsNone()
    {
        SettingsViewModel vm = await LoadAsync(null, WithCamera(LinkPrinter("a", "Alpha"), Camera("rtsp://192.168.0.227/live", enabled: false)));

        vm.Printers[0].CameraUrl = "   ";
        AppSettings saved = await SaveAsync(vm);

        Assert.Same(CameraSettings.Disabled, saved.Printers[0].Camera);
        Assert.Null(vm.Printers[0].CameraUrlError);
    }

    [Fact]
    public async Task InvalidStoredAddressIsShownForCorrectionAndKeptUnchanged()
    {
        SettingsViewModel vm = await LoadAsync(null, WithCamera(LinkPrinter("a", "Alpha"), CameraSettings.FromInvalidStreamUrl("http://192.168.0.227/live")));
        PrinterEditorViewModel printer = vm.Printers[0];

        Assert.Equal("http://192.168.0.227/live", printer.CameraUrl);
        Assert.False(printer.CameraEnabled);
        Assert.Equal("Only rtsp:// addresses are supported.", printer.CameraUrlError);
        // A disabled camera may keep it: saving other changes is not blocked and the text stays as it was.
        Assert.False(printer.HasErrors);
        Assert.False(vm.IsDirty);

        printer.Name = "Renamed";
        AppSettings saved = await SaveAsync(vm);

        Assert.Equal("http://192.168.0.227/live", saved.Printers[0].Camera.InvalidStreamUrl);
    }

    [Theory]
    [InlineData("", "Enter the stream address, e.g. rtsp://192.168.0.227/live.")]
    [InlineData("192.168.0.227/live", "This is not a valid address. Use the form rtsp://address/path.")]
    [InlineData("http://192.168.0.227/live", "Only rtsp:// addresses are supported.")]
    [InlineData("rtsp:live", "The address has no host name or IP address.")]
    [InlineData("rtsp://admin:pw@192.168.0.227/live", "The address must not contain a user name, a password or the @ character.")]
    [InlineData("rtsp://admin:1234/x@192.168.0.227/live", "The address must not contain a user name, a password or the @ character.")]
    [InlineData("rtsp://192.168.0.227/live?user=a@b", "The address must not contain a user name, a password or the @ character.")]
    public async Task EnabledCameraNeedsAValidAddress(string url, string error)
    {
        SettingsViewModel vm = await LoadAsync(null, LinkPrinter("a", "Alpha"));
        PrinterEditorViewModel printer = vm.Printers[0];

        printer.CameraEnabled = true;
        printer.CameraUrl = url;

        Assert.Equal(error, printer.CameraUrlError);
        Assert.True(printer.HasErrors);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Empty(_store.Saves);
    }

    [Fact]
    public async Task CredentialsBlockSavingEvenForADisabledCamera()
    {
        SettingsViewModel vm = await LoadAsync(null, LinkPrinter("a", "Alpha"));
        PrinterEditorViewModel printer = vm.Printers[0];

        printer.CameraUrl = "rtsp://admin:pw@192.168.0.227/live";

        Assert.False(printer.CameraEnabled);
        Assert.Equal("The address must not contain a user name, a password or the @ character.", printer.CameraUrlError);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Empty(_store.Saves);
    }

    [Fact]
    public async Task UnparseableTextWithCredentialsIsNeverKept()
    {
        SettingsViewModel vm = await LoadAsync(null, LinkPrinter("a", "Alpha"));
        PrinterEditorViewModel printer = vm.Printers[0];

        printer.CameraUrl = "admin:pw@camera";

        Assert.Equal("The address must not contain a user name, a password or the @ character.", printer.CameraUrlError);
        Assert.True(printer.HasErrors);
    }

    [Fact]
    public async Task NewPrinterHidesCameraErrorsUntilEdited()
    {
        SettingsViewModel vm = await LoadAsync(null);
        vm.AddPrinterCommand.Execute(null);
        PrinterEditorViewModel printer = vm.Printers[0];

        Assert.False(printer.CameraEnabled);
        Assert.Equal(string.Empty, printer.CameraUrl);
        Assert.Null(printer.CameraUrlError);
    }

    // ---- Removed credentials ----

    [Fact]
    public async Task RemovedCredentialsShowANoticeUntilSaved()
    {
        SettingsViewModel vm = await LoadAsync(Ids("A"), WithCamera(LinkPrinter("a", "Alpha"), Camera("rtsp://192.168.0.227/live", enabled: false)), LinkPrinter("b", "Beta"));

        Assert.True(vm.Printers[0].CameraCredentialsRemoved);
        Assert.False(vm.Printers[1].CameraCredentialsRemoved);

        vm.Printers[0].CameraEnabled = true;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.False(vm.Printers[0].CameraCredentialsRemoved);
    }

    [Fact]
    public async Task CredentialsRemovedWhileLoadingAreReportedByTheEditorStore()
    {
        JsonSettingsStore file = await SettingsFiles.WithCameraCredentialsAsync(_directory, Ct);
        RemovedCameraCredentials atStartup = new();
        atStartup.Add(["b"]);
        SettingsEditorStore store = new(() => file, new FakeSecretStore(), Supported, new RecordingApplier(), new ListLogger<SettingsEditorStore>(), removedCameraCredentials: atStartup);

        SettingsEditorData data = await store.LoadAsync(Ct);

        Assert.Equal(["a", "b"], data.CamerasWithRemovedCredentials!.Order(StringComparer.Ordinal));
        CameraSettings camera = data.Settings.Printers[0].Camera;
        Assert.False(camera.Enabled);
        Assert.Equal("rtsp://192.168.0.227/live", camera.StreamUri!.OriginalString);
    }

    [Fact]
    public async Task SuccessfulSaveClearsTheStartupNotice()
    {
        MemorySettingsStore settings = new();
        RemovedCameraCredentials atStartup = new();
        atStartup.Add(["a"]);
        SettingsEditorStore store = new(() => settings, new FakeSecretStore(), Supported, new RecordingApplier(), new ListLogger<SettingsEditorStore>(), removedCameraCredentials: atStartup);

        SettingsSaveResult result = await store.SaveAsync(new SettingsChange(AppSettings.Default, AppSettings.Default, new Dictionary<string, string>()));

        Assert.Equal(SettingsSaveStatus.Saved, result.Status);
        Assert.Empty(atStartup.PrinterIds);
    }

    [Fact]
    public async Task FailedSaveKeepsTheStartupNotice()
    {
        MemorySettingsStore settings = new() { SaveFailure = new IOException("disk full") };
        RemovedCameraCredentials atStartup = new();
        atStartup.Add(["a"]);
        SettingsEditorStore store = new(() => settings, new FakeSecretStore(), Supported, new RecordingApplier(), new ListLogger<SettingsEditorStore>(), removedCameraCredentials: atStartup);

        await store.SaveAsync(new SettingsChange(AppSettings.Default, AppSettings.Default, new Dictionary<string, string>()));

        Assert.Equal(["a"], atStartup.PrinterIds);
    }

    // ---- Test button ----

    [Fact]
    public async Task TestReportsResolution()
    {
        PrinterEditorViewModel printer = await LoadOneAsync(Camera("rtsp://192.168.0.227/live"));
        _cameraTester.Immediate = new CameraTestResult(CameraTestOutcome.Success, 1280, 720);

        await printer.TestCameraCommand.ExecuteAsync(null);

        Assert.Equal(new Uri("rtsp://192.168.0.227/live"), Assert.Single(_cameraTester.Calls));
        Assert.Equal("The camera works (1280 × 720 px).", printer.CameraTestResultText);
        Assert.True(printer.CameraTestSucceeded);
        Assert.False(printer.IsTestingCamera);
    }

    [Theory]
    [InlineData(CameraTestOutcome.Timeout, null, "No image arrived from the camera within 15 s.")]
    [InlineData(CameraTestOutcome.Failed, "Connection refused.", "The camera stream could not be opened: Connection refused.")]
    [InlineData(CameraTestOutcome.Failed, null, "The camera stream could not be opened.")]
    public async Task TestReportsFailures(CameraTestOutcome outcome, string? reason, string expected)
    {
        PrinterEditorViewModel printer = await LoadOneAsync(Camera("rtsp://192.168.0.227/live"));
        _cameraTester.Immediate = new CameraTestResult(outcome, Reason: reason);

        await printer.TestCameraCommand.ExecuteAsync(null);

        Assert.Equal(expected, printer.CameraTestResultText);
        Assert.True(printer.CameraTestFailed);
    }

    [Fact]
    public async Task TestWorksForADisabledCameraAndTestsTheFormValue()
    {
        PrinterEditorViewModel printer = await LoadOneAsync();
        printer.CameraUrl = "rtsp://10.0.0.5/live";
        _cameraTester.Immediate = new CameraTestResult(CameraTestOutcome.Success, 640, 480);

        await printer.TestCameraCommand.ExecuteAsync(null);

        Assert.Equal(new Uri("rtsp://10.0.0.5/live"), Assert.Single(_cameraTester.Calls));
    }

    [Fact]
    public async Task InvalidAddressIsNotTested()
    {
        PrinterEditorViewModel printer = await LoadOneAsync();
        printer.CameraUrl = "http://10.0.0.5/live";

        await printer.TestCameraCommand.ExecuteAsync(null);

        Assert.Empty(_cameraTester.Calls);
        Assert.Equal("Only rtsp:// addresses are supported.", printer.CameraTestResultText);
        Assert.True(printer.CameraTestFailed);
    }

    [Fact]
    public async Task RunningTestCanBeCancelled()
    {
        PrinterEditorViewModel printer = await LoadOneAsync(Camera("rtsp://192.168.0.227/live"));

        Task test = printer.TestCameraCommand.ExecuteAsync(null);
        Assert.True(printer.IsTestingCamera);
        Assert.Equal("Waiting for an image from the camera…", printer.CameraTestResultText);
        Assert.False(printer.TestCameraCommand.CanExecute(null));

        printer.CancelCameraTestCommand.Execute(null);
        await test.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal("Test cancelled.", printer.CameraTestResultText);
        Assert.False(printer.IsTestingCamera);
    }

    [Fact]
    public async Task EditingTheAddressStopsTheTestAndClearsTheResult()
    {
        PrinterEditorViewModel printer = await LoadOneAsync(Camera("rtsp://192.168.0.227/live"));
        Task test = printer.TestCameraCommand.ExecuteAsync(null);

        printer.CameraUrl = "rtsp://192.168.0.228/live";
        await test.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.True(_cameraTester.LastToken.IsCancellationRequested);
        Assert.Null(printer.CameraTestResultText);
    }

    [Fact]
    public async Task ClosingTheWindowStopsTheTest()
    {
        PrinterEditorViewModel printer = await LoadOneAsync(Camera("rtsp://192.168.0.227/live"));
        Task test = printer.TestCameraCommand.ExecuteAsync(null);

        _viewModel!.Dispose();
        await test.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.True(_cameraTester.LastToken.IsCancellationRequested);
    }

    [Fact]
    public async Task UnexpectedTesterExceptionIsShownAsFailure()
    {
        PrinterEditorViewModel printer = await LoadOneAsync(Camera("rtsp://192.168.0.227/live"));
        _cameraTester.Throw = new InvalidOperationException("boom");

        await printer.TestCameraCommand.ExecuteAsync(null);

        Assert.Equal("The camera stream could not be opened.", printer.CameraTestResultText);
    }

    [Fact]
    public async Task CzechTextsAreUsed()
    {
        using CultureScope czech = new("cs-CZ");
        PrinterEditorViewModel printer = await LoadOneAsync();

        printer.CameraEnabled = true;

        Assert.Equal("Zadejte adresu streamu, např. rtsp://192.168.0.227/live.", printer.CameraUrlError);
    }

    /// <summary>Camera tester driven by the test: answers immediately, throws, or waits for cancellation.</summary>
    private sealed class FakeCameraTester : ICameraTester
    {
        public TimeSpan Timeout => TimeSpan.FromSeconds(15);

        public List<Uri> Calls { get; } = [];

        public CameraTestResult? Immediate { get; set; }

        public Exception? Throw { get; set; }

        public CancellationToken LastToken { get; private set; }

        public Task<CameraTestResult> TestAsync(Uri streamUri, CancellationToken cancellationToken)
        {
            Calls.Add(streamUri);
            LastToken = cancellationToken;
            if (Throw is not null)
            {
                return Task.FromException<CameraTestResult>(Throw);
            }

            if (Immediate is not null)
            {
                return Task.FromResult(Immediate);
            }

            return WaitForCancellationAsync(cancellationToken);
        }

        private static async Task<CameraTestResult> WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
            return new CameraTestResult(CameraTestOutcome.Failed);
        }
    }
}
