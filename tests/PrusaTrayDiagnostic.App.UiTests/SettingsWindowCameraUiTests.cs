using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using PrusaTrayDiagnostic.App.Settings;
using PrusaTrayDiagnostic.App.Tests.Cameras;
using PrusaTrayDiagnostic.App.Tests.Settings;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.App.Windows;
using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.App.UiTests;

/// <summary>The Camera section of the real settings window (T-014), driven by mouse and keyboard, on fakes.</summary>
public sealed class SettingsWindowCameraUiTests
{
    private const int TestTimeoutMs = 60_000;

    private sealed class Harness : IDisposable
    {
        private readonly CultureScope _culture = new("en-US");

        public Harness(PrinterDefinition printer, IReadOnlySet<string>? removedCredentials = null)
        {
            Store.Data = new SettingsEditorData(new AppSettings { Printers = [printer] }, SettingsLoadStatus.Loaded, [], new HashSet<string>(StringComparer.OrdinalIgnoreCase), removedCredentials);
            ViewModel = new SettingsViewModel(
                Store,
                new FakeConnectionTester(),
                new PasswordPolicy(IsSupported: true, MaxBytes: 2560),
                cameraTester: new CameraSnapshotTester(Factory, Time, NullLogger<CameraSnapshotTester>.Instance));
            // Tall enough that the whole printer form is on screen without scrolling.
            Window = new SettingsWindow { DataContext = ViewModel, Height = 1400 };
        }

        public FakeEditorStore Store { get; } = new();

        public FakeCameraFeedFactory Factory { get; } = new();

        public FakeTimeProvider Time { get; } = new();

        public SettingsViewModel ViewModel { get; }

        public SettingsWindow Window { get; }

        public async Task ShowAsync()
        {
            Window.Show();
            await ViewModel.LoadAsync();
            Dispatcher.UIThread.RunJobs();
        }

        public void Click(Control control)
        {
            Ui.Click(Window, Ui.CentreOf(control, Window));
            Dispatcher.UIThread.RunJobs();
        }

        public Button Button(string text) => Ui.Find<Button>(Window, button => button.Content as string == text && button.IsEffectivelyVisible);

        public TextBox UrlField => Ui.Find<TextBox>(Window, box => Avalonia.Automation.AutomationProperties.GetName(box) == "RTSP stream address");

        public void TypeUrl(string text)
        {
            Click(UrlField);
            Window.KeyTextInput(text);
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose()
        {
            Window.Close();
            ViewModel.Dispose();
            _culture.Dispose();
        }
    }

    private static PrinterDefinition Printer(CameraSettings? camera = null) =>
        TestPrinters.Definition("a", "Alpha") with { Camera = camera ?? CameraSettings.Disabled };

    [AvaloniaFact(Timeout = TestTimeoutMs)]
    public async Task InvalidAddressShowsItsMessage()
    {
        using Harness harness = new(Printer());
        await harness.ShowAsync();
        Assert.True(Ui.ShowsText(harness.Window, "Camera"));

        harness.TypeUrl("http://192.168.0.227/live");

        Assert.True(Ui.ShowsText(harness.Window, "Only rtsp:// addresses are supported."));
    }

    [AvaloniaFact(Timeout = TestTimeoutMs)]
    public async Task RemovedCredentialsShowTheNotice()
    {
        using Harness harness = new(Printer(new CameraSettings(enabled: false, CameraHarness.StreamUri)), new HashSet<string>(["a"], StringComparer.OrdinalIgnoreCase));
        await harness.ShowAsync();

        Assert.Contains(Ui.VisibleTexts(harness.Window), static text => text.StartsWith("The user name and password were removed", StringComparison.Ordinal));
    }

    [AvaloniaFact(Timeout = TestTimeoutMs)]
    public async Task TestButtonReportsTheResolution()
    {
        using Harness harness = new(Printer(new CameraSettings(enabled: true, CameraHarness.StreamUri)));
        await harness.ShowAsync();

        harness.Click(harness.Button("Test camera"));
        Assert.True(Ui.ShowsText(harness.Window, "Waiting for an image from the camera…"));
        FakeCameraFeed feed = await harness.Factory.WaitForFeedAsync(1);
        feed.Send(width: 1280, height: 720);

        await Ui.UntilAsync(() => Ui.ShowsText(harness.Window, "The camera works (1280 × 720 px)."), "success shown");
    }

    [AvaloniaFact(Timeout = TestTimeoutMs)]
    public async Task TestButtonReportsATimeoutAfterFifteenSeconds()
    {
        using Harness harness = new(Printer(new CameraSettings(enabled: true, CameraHarness.StreamUri)));
        await harness.ShowAsync();

        harness.Click(harness.Button("Test camera"));
        await harness.Factory.WaitForFeedAsync(1);
        harness.Time.Advance(TimeSpan.FromSeconds(15));

        await Ui.UntilAsync(() => Ui.ShowsText(harness.Window, "No image arrived from the camera within 15 s."), "timeout shown");
    }

    [AvaloniaFact(Timeout = TestTimeoutMs)]
    public async Task SaveCarriesTheEnteredCamera()
    {
        using Harness harness = new(Printer());
        await harness.ShowAsync();

        harness.TypeUrl("rtsp://192.168.0.227/live");
        harness.Click(Ui.Find<CheckBox>(harness.Window, box => box.Content as string == "Use the camera (Camera… item in the tray menu)"));
        harness.Click(harness.Button("Save"));
        await Ui.UntilAsync(() => harness.Store.Saves.Count == 1, "saved");

        Assert.Equal(new CameraSettings(enabled: true, CameraHarness.StreamUri), harness.Store.Saves[0].Updated.Printers[0].Camera);
    }
}
