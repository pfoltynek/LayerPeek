using Avalonia.Controls;

using LayerPeek.App.Cameras;
using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Printers;
using LayerPeek.App.Tests.Settings;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.UiTests.Settings;

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

    /// <summary>
    /// Opens and loads the window; new printers get the ids "p1", "p2"... in order. The Widgets tab lists the widgets of the
    /// app's registration (with the Cool-down panel, T-088) unless <paramref name="widgets"/> says otherwise; announced
    /// layouts are taken over right away.
    /// </summary>
    public static async Task<SettingsWindowHarness> OpenAsync(
        UiTestScope scope,
        ISettingsEditorStore store,
        IConnectionTester? tester = null,
        PasswordPolicy? policy = null,
        ICameraTester? cameraTester = null,
        IScreenCatalog? screens = null,
        ICameraWindowStateStore? cameraWindowStates = null,
        ICameraWindowPlacer? cameraWindowPlacer = null,
        IImageFilePicker? imagePicker = null,
        LayerPeek.App.Platform.IAutostart? autostart = null,
        bool autostartNeedsInstallation = false,
        IAuxiliaryWindowStateStore? windowStates = null,
        LayerPeek.App.Cameras.Overlay.OverlayWidgetRegistry? widgets = null,
        LayerPeek.App.Cameras.Layout.ICameraLayoutModeLauncher? layoutModeLauncher = null,
        ICameraOverlayLayoutWriter? layoutWriter = null,
        bool macTerms = false)
    {
        ArgumentNullException.ThrowIfNull(scope);
        AuxiliaryWindowSlot slot = new();
        Queue<string> ids = new(["p1", "p2", "p3"]);
        SettingsViewModel viewModel = new(store, tester ?? new FakeConnectionTester(), policy ?? Supported, ids.Dequeue, cameraTester ?? new FakeCameraTester(), screens: screens, cameraWindowStates: cameraWindowStates, cameraWindowPlacer: cameraWindowPlacer, autostart: autostartNeedsInstallation ? null : autostart ?? new MemoryAutostart(), autostartNeedsInstallation: autostartNeedsInstallation, productVersion: "0.1.0", widgets: widgets ?? LayerPeek.App.Composition.OverlayWidgets.CreateRegistry(), layoutModeLauncher: layoutModeLauncher ?? new FakeLayoutModeLauncher(), layoutWriter: layoutWriter, uiDispatcher: new InlineDispatcher())
        {
            // T-019: the texts of macOS ("Open at Login"), without the pre-release option.
            UsesMacTerms = macTerms,
            ShowsPrereleaseOption = !macTerms,
        };
        // Like SettingsWindowFactory: the window has a file dialog for custom printer images (T-039); by default the user cancels it.
        viewModel.ImageFilePicker = imagePicker ?? new FakeImagePicker { Content = null };
        scope.Defer(viewModel);
        SettingsWindow window = scope.Track(new SettingsWindow { DataContext = viewModel });
        if (windowStates is not null)
        {
            // Like AuxiliaryWindowFactory (T-094): the remembered size and maximized state.
            window.RememberSizeIn(windowStates);
        }

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

    /// <summary>Clicks Apply of the Camera window sub-tab (T-040) and waits for it to finish.</summary>
    public async Task ApplyCameraWindowAsync()
    {
        Click("PrinterCameraWindowApply");
        if (ViewModel.SelectedPrinter?.ApplyCameraWindowCommand.ExecutionTask is { } running)
        {
            await running;
        }

        Window.Settle();
    }

    /// <summary>The text of a text block, or <see langword="null"/> when it is not shown.</summary>
    public string? ShownText(string automationId) =>
        Window.IsShown<TextBlock>(automationId) ? Find<TextBlock>(automationId).Text : null;
}

/// <summary>Start after logon in memory (T-017): the option is shown like on Windows; nothing touches the Startup folder.</summary>
internal sealed class MemoryAutostart : LayerPeek.App.Platform.IAutostart
{
    public bool Enabled { get; set; }

    public Exception? Failure { get; set; }

    public bool IsEnabled() => Enabled;

    public void SetEnabled(bool enabled) => Enabled = Failure is null ? enabled : throw Failure;
}

/// <summary>
/// "Open at Login" the user allows in System Settings (macOS, T-019), in memory: opening System Settings is only counted,
/// the real ones are never opened.
/// </summary>
internal sealed class MemoryApprovalAutostart : LayerPeek.App.Platform.IAutostart, LayerPeek.App.Platform.IAutostartApproval
{
    public bool Enabled { get; set; }

    public Exception? Failure { get; set; }

    public int Opened { get; private set; }

    public bool IsEnabled() => Enabled;

    public void SetEnabled(bool enabled) => Enabled = Failure is null ? enabled : throw Failure;

    public void OpenApprovalSettings() => Opened++;
}

/// <summary>Runs posted work right away (the tests call from the UI thread).</summary>
internal sealed class InlineDispatcher : LayerPeek.App.Tray.IUiDispatcher
{
    public void Post(Action action) => action();
}
