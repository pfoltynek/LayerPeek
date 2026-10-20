using Avalonia.Controls;

using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.App.Settings;
using PrusaTrayDiagnostic.App.Tests.Printers;
using PrusaTrayDiagnostic.App.Tests.Settings;
using PrusaTrayDiagnostic.App.UiTests.Infrastructure;
using PrusaTrayDiagnostic.App.Windows;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.App.UiTests.Settings;

/// <summary>
/// The settings window opened the way the app opens it (<c>App.ShowSettings</c>): a <see cref="SettingsWindow"/> over a
/// <see cref="SettingsViewModel"/>, owned by an <see cref="AuxiliaryWindowSlot"/> that disposes the view model when the
/// window closes, then loaded. Only in-memory fakes: no settings file, no credential store, no network. UI thread only.
/// </summary>
internal sealed class SettingsWindowHarness
{
    public static readonly PasswordPolicy Supported = new(IsSupported: true, MaxBytes: 2560);

    private SettingsWindowHarness(SettingsWindow window, SettingsViewModel viewModel, AuxiliaryWindowSlot slot)
    {
        Window = window;
        ViewModel = viewModel;
        Slot = slot;
        window.Closed += (_, _) => ClosedCount++;
    }

    public SettingsWindow Window { get; }

    public SettingsViewModel ViewModel { get; }

    public AuxiliaryWindowSlot Slot { get; }

    public int ClosedCount { get; private set; }

    public bool IsOpen => Window.IsVisible;

    /// <summary>Opens and loads the window; new printers get the ids "p1", "p2"... in order.</summary>
    public static async Task<SettingsWindowHarness> OpenAsync(
        UiTestScope scope,
        ISettingsEditorStore store,
        IConnectionTester? tester = null,
        PasswordPolicy? policy = null,
        ICameraTester? cameraTester = null,
        IScreenCatalog? screens = null,
        ICameraWindowStateStore? cameraWindowStates = null,
        IImageFilePicker? imagePicker = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        AuxiliaryWindowSlot slot = new();
        Queue<string> ids = new(["p1", "p2", "p3"]);
        SettingsViewModel viewModel = new(store, tester ?? new FakeConnectionTester(), policy ?? Supported, ids.Dequeue, cameraTester ?? new FakeCameraTester(), screens: screens, cameraWindowStates: cameraWindowStates);
        // Like SettingsWindowFactory: the window has a file dialog for custom printer images (T-039); by default the user cancels it.
        viewModel.ImageFilePicker = imagePicker ?? new FakeImagePicker { Content = null };
        scope.Defer(viewModel);
        SettingsWindow window = scope.Track(new SettingsWindow { DataContext = viewModel });
        SettingsWindowHarness harness = new(window, viewModel, slot);
        slot.ShowOrActivate(() => (new AvaloniaAuxiliaryWindow(window), viewModel));
        await viewModel.LoadAsync();
        window.Settle();
        return harness;
    }

    public static FakeEditorStore StoreWith(params PrinterDefinition[] printers) =>
        new()
        {
            Data = new SettingsEditorData(
                AppSettings.Default with { Printers = printers },
                SettingsLoadStatus.Loaded,
                [],
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)),
        };

    public T Find<T>(string automationId)
        where T : Control => Window.Find<T>(automationId);

    public TextBox NameBox => Find<TextBox>("PrinterName");

    public TextBox AddressBox => Find<TextBox>("PrinterAddress");

    public TextBox UserNameBox => Find<TextBox>("PrinterUserName");

    public TextBox PasswordBox => Find<TextBox>("PrinterPassword");

    public void Click(string automationId) => Window.Click(Find<Control>(automationId));

    /// <summary>Clicks Save and waits for the save to finish (it may run on the thread pool and resume on the UI thread).</summary>
    public async Task SaveAsync()
    {
        Click("SettingsSave");
        if (ViewModel.SaveCommand.ExecutionTask is { } running)
        {
            await running;
        }

        Window.Settle();
    }

    /// <summary>The text of a text block, or <see langword="null"/> when it is not shown.</summary>
    public string? ShownText(string automationId) =>
        Window.IsShown<TextBlock>(automationId) ? Find<TextBlock>(automationId).Text : null;
}
